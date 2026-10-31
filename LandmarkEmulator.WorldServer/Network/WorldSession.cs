using LandmarkEmulator.WorldServer.Storage;
using LandmarkEmulator.Shared.Configuration;
using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Gateway.Network;
using LandmarkEmulator.Gateway.Network.Message;
using LandmarkEmulator.Shared.Game.Entity.Static;
using LandmarkEmulator.Shared.GameTable;
using LandmarkEmulator.Shared.GameTable.Model;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using LandmarkEmulator.WorldServer.Network.Message.Model.Shared;
using LandmarkEmulator.WorldServer.Network.Packets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Numerics;
using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Shared.Game.Events;

namespace LandmarkEmulator.WorldServer.Network
{
    /// <summary>
    /// The zone side of one connected character: its game state, the handlers' view of the client, and the packets
    /// queued for it. The connection itself is the gateway's (<see cref="ClientSession"/> in this process today; its own
    /// process later, trace/architecture/gateway-service.md): this session talks to it only through
    /// <see cref="Link"/>, and gets the client's packets — as the 0.695 client sent them (the server speaks its
    /// numbering natively, docs/protocol.md §1.6), past the transport flood limits — through <see cref="FromClient"/>.
    /// </summary>
    public class WorldSession
    {
        private static readonly NLog.ILogger log = NLog.LogManager.GetCurrentClassLogger();

        /// <summary>The connection to the client (gateway side).</summary>
        public IClientLink Link { get; }
        public Database.Auth.Model.AccountModel Account { get; }
        public ClientProtocol ClientProtocol { get; }
        public string ClientBuild { get; }

        /// <summary>
        /// The server ticket this client logged in with. The login server starts it with "R" when the client signed in
        /// with a relogin key (back from character select in the same running client); see
        /// Game.Inventory.ItemDefinitionManager.SendLoginList.
        /// </summary>
        public string LoginTicket { get; }

        /// <summary>The connection has ended (closed by the client, timed out, or closed by the server).</summary>
        public bool Disconnected => closed || Link.Closed;
        private bool closed;

        /// <summary>
        /// A newer login of the same character took over (<see cref="Game.PlayerManager.ReplaceOlderSessions"/>): this
        /// session has left the world and is being closed; it never enters the world again (a late ClientIsReady from its
        /// client would otherwise kick the newer login).
        /// </summary>
        public bool Replaced { get; internal set; }

        // Other sessions enqueue here too (spawns, movement relay), hence concurrent.
        private readonly System.Collections.Concurrent.ConcurrentQueue<ZonePacket> outgoingPackets = new();

        private byte[] sendSelfPacket;
        public byte[] TerrainManifest { get; }
        public bool ReadySent { get; set; } = false;

        /// <summary>
        /// Set once the client has finished loading the world.
        /// </summary>
        public bool WorldReady { get; set; }
        /// <summary>
        /// The client's login loading screen has dropped: its PlayerUpdate ClientLoading {u8 0} (sent by GameStateLoading's
        /// exit, FUN_1436abd00, when HideLoadingscreen changes the game state, FUN_142ce8510) has arrived [P]. That is
        /// 15-25 s after ClientFinishedLoading, which only means WaitForWorldReady is done (FUN_142cc3050) while the
        /// screen stays up for the terrain (ShouldDrop FUN_142d54a00 in run state 0x12) [P, dev log 2026-10-18]. Other
        /// players and this one are shown to each other then (PlayerManager.LoadingScreenLeft). Set on a re-attach: that
        /// client is already in the world.
        /// </summary>
        public bool LoadingScreenDown { get; set; }
        /// <summary>The build mode HUD was put back for this login (MiscHandler.ClientFinishedLoading).</summary>
        public bool BuildHudReset { get; set; }
        /// <summary>The message of the day went out this login.</summary>
        public bool MotdSent { get; set; }

        // T-KIRMMIN-WIRE-TIMING (logging only): t=0 at first SendSelfToClient SOE-pack.
        private long? _wireT0Ms;
        private long? _wireZoneDoneMs;
        private long? _wireClientIsReadyMs;

        private static string GetResourcePath(params string[] segments)
        {
            var parts = new List<string> { AppContext.BaseDirectory, "Resources" };
            parts.AddRange(segments);
            return Path.GetFullPath(Path.Combine(parts.ToArray()));
        }

        public WorldSession(IClientLink link, Database.Auth.Model.AccountModel account, string clientBuild, ClientProtocol protocol,
            string loginTicket)
        {
            Link = link;
            Account = account;
            ClientBuild = clientBuild;
            ClientProtocol = protocol;
            LoginTicket = loginTicket ?? "";
            sendSelfPacket = File.ReadAllBytes(GetResourcePath("PacketDumps", "SendSelf.bin"));
            TerrainManifest = File.ReadAllBytes(GetResourcePath("PacketDumps", "TerrainCellManifest.bin"));
        }

        // Position persistence: the latest position from the PositionUpdate channel, saved periodically and on logout.
        private static readonly TimeSpan PositionSaveInterval = TimeSpan.FromSeconds(30);
        private CharacterModel currentCharacter;
        private Vector3? lastPosition;
        private Quaternion? lastRotation;

        /// <summary>Which way the player faces (latest movement update, else the saved rotation).</summary>
        public Quaternion Facing => lastRotation ?? (currentCharacter == null ? Quaternion.Identity
            : new Quaternion(currentCharacter.RotationX, currentCharacter.RotationY, currentCharacter.RotationZ, currentCharacter.RotationW));

        /// <summary>The movement state word of the newest type 0 / 1 body (<see cref="PositionUpdate.TryReadState"/>).</summary>
        public uint MovementState { get; private set; }

        /// <summary>
        /// Creatures this client has made full entities (LightweightToFullNpc sent, Creatures.SendFull): guid → the
        /// transient id it had then, so a respawn (new transient id) counts as lightweight again. Only full entities can
        /// speak a DisplayPropText bubble or a chat [*speaker*] line (OPEN-LEADS L96).
        /// </summary>
        public System.Collections.Concurrent.ConcurrentDictionary<ulong, uint> FullNpcs { get; } = new();

        /// <summary>
        /// The FreeBuild flag this player's client got with its SendSelf (+0x287 BypassClaimPermissions); null before
        /// that. The client keeps it until the next login (FreeBuild is a next-login setting), and with it treats every
        /// material as free, so the server's material rule follows this snapshot, not the live setting
        /// (Building.FreeMaterials).
        /// </summary>
        public bool? FreeBuildAtLogin { get; private set; }

        /// <summary>The newest full (type 0) movement body this client sent (dev: a template for NPC movement).</summary>
        public byte[] LastFullMovement { get; private set; }

        /// <summary>
        /// The velocity of the newest movement update that carried one (type 0 / 2, m/s, local frame) and when it came.
        /// A client sends movement only while it moves, so an old one means the player stands still.
        /// </summary>
        public Vector3 LastVelocity { get; private set; }
        public DateTime LastVelocityAt { get; private set; }

        /// <summary>
        /// Where the player is now (latest movement update, else the saved position).
        /// </summary>
        /// <summary>Where a re-zone (<see cref="Game.Teleport.Rezone"/>) is taking the player, until the client confirms it.</summary>
        public Vector3? RezoneTarget { get; set; }
        /// <summary>When the pending re-zone (<see cref="RezoneTarget"/>) was sent.</summary>
        public DateTime RezoneStarted { get; set; }

        /// <summary>
        /// Counts the re-zones sent to this client (Teleport.RezoneNow, one per ClientBeginZoning). An arrival hold
        /// (MiscHandler.ClientIsReady → Teleport.After) captures it and does nothing when it has changed meanwhile: the
        /// hold belonged to a re-zone the client has already left, and its zone-done flags would arrive in the middle of
        /// the next load (review 2026-10-18 #3).
        /// </summary>
        public int RezoneSerial { get; set; }

        /// <summary>
        /// A real re-zone (ClientBeginZoning) went out and its arrival (Teleport.FinishRezone) has not run yet; unlike
        /// <see cref="RezoneTarget"/> it is not set by a short move or by a re-zone still waiting to be sent. Another
        /// landscape's re-zone waits for it (Teleport.Rezone: one zone change at a time).
        /// </summary>
        public bool RezoneInFlight { get; set; }

        /// <summary>Where the re-zone in flight put the client (ClientBeginZoning's position), to see if it was retargeted.</summary>
        public Vector3 RezoneZonedTo { get; set; }

        /// <summary>This re-zone already sent the Spire's props ahead of the zone-done flags (MiscHandler.ClientIsReady).</summary>
        public bool SpireSentEarly { get; set; }

        /// <summary>The last re-zone moved the player to another landscape (false: same landscape; the client deletes its
        /// entities either way).</summary>
        public bool RezoneChangedLandscape { get; set; }

        /// <summary>
        /// The next move must be a real re-zone (ClientBeginZoning), never a TeleportToLocation in place: set while an arrival
        /// from another world places the player (<see cref="ReattachRezone"/>). The client still has the other world's
        /// terrain and entities then, and only ClientBeginZoning carries the new world id (+0xa8) that makes it change world
        /// (Teleport.RezoneNow; OPEN-LEADS L114) — a short move on the same landscape id would leave it on the old world's
        /// copy. <see cref="Game.Teleport.To"/> honours it while it is set.
        /// </summary>
        public bool FullRezoneNext { get; set; }

        /// <summary>The landscape the player is on (StaticZoneData zone id, <see cref="Game.Landscapes"/>).</summary>
        public uint ZoneId => currentCharacter?.ZoneId ?? Game.Terrain.TerrainStore.HomeContinent;

        /// <summary>Moves the player to another landscape (saved with the position).</summary>
        public void SetZone(uint zoneId)
        {
            if (currentCharacter == null)
                return;
            currentCharacter.ZoneId = zoneId;
            ulong characterId = currentCharacter.Id;
            DatabaseManager.Instance.CharacterDatabase.Save(c =>
            {
                var model = new CharacterModel { Id = characterId };
                c.Character.Attach(model);
                model.ZoneId = zoneId;
                c.MarkModified(model, nameof(model.ZoneId));
            });
        }

        /// <summary>True when the player is on the landscape whose terrain, trees, caves and sites the server keeps.</summary>
        public bool OnHomeContinent => ZoneId == Game.Terrain.TerrainStore.HomeContinent;

        public Vector3 Position => lastPosition ?? new Vector3(currentCharacter.PositionX, currentCharacter.PositionY, currentCharacter.PositionZ);

        /// <summary>
        /// Server-side move (teleport): the client is told separately; this keeps our copy in step. The player is then
        /// "arriving" until the client reports a position near the destination (or a minute passes): a long teleport
        /// makes the client reload its terrain engine, and entities streamed to it during that reload have hung it
        /// (its watchdog then kills it), so surface objects wait for the arrival.
        /// </summary>
        public void SetPosition(Vector3 position)
        {
            lastPosition = position;
            positionDirty = true;
            arrivalTarget = position;
            arrivingUntil = DateTime.UtcNow.AddSeconds(60);
            Game.Achievements.Moved(this, position);
        }

        private Vector3 arrivalTarget;
        private DateTime? arrivingUntil;
        private const float ArrivalRadius = 50f;

        /// <summary>True between a teleport and the client's first position update at the destination.</summary>
        public bool IsArriving => arrivingUntil is DateTime until && DateTime.UtcNow < until;

        /// <summary>
        /// World streams (creatures, gathering nodes, chests, outposts) wait while the player is arriving or a re-zone is
        /// still loading (<see cref="RezoneTarget"/> set until ClientIsReady). The client can report a position at the
        /// destination before it is ready, which ended the arrival; creatures streamed then were kept by the client and
        /// sent again by PlayerManager.Reintroduce at ClientIsReady, and the client refused every repeat
        /// (LightweightProxy.log "Failed to spawn ProxiedCharacter": an entity with that guid already exists,
        /// FUN_142eeb3c0), 107 in one session of cave trips (2026-10-14).
        /// Since 2026-10-21 also while the login's or re-zone's loading screen is still up (<see cref="LoadingHoldsStreams"/>,
        /// setting StreamHoldUntilLoaded): the asset burst then waits until the client's terrain has meshed (the 64
        /// allocator-slot race on 16+ thread CPUs, docs/terrain.md "Allocator slots").
        /// </summary>
        public bool StreamsOnHold => IsArriving
            || (RezoneTarget != null && DateTime.UtcNow - RezoneStarted < Game.Teleport.PendingRezoneLimit)
            || LoadingHoldsStreams;

        /// <summary>
        /// The loading-screen stream hold (setting StreamHoldUntilLoaded, Game.PlayerManager "Stream hold"): until the
        /// client's ClientLoading {0} for this login or re-zone, at most until this time (UTC ticks; 0 = no hold). Kept
        /// here, outside PlayerManager's lock, as every stream tick asks <see cref="StreamsOnHold"/>; PlayerManager sets
        /// and clears it under its lock and runs the held sends when it ends.
        /// </summary>
        private long streamHoldUntilTicks;

        /// <summary>The loading-screen stream hold is on (<see cref="streamHoldUntilTicks"/>).</summary>
        public bool LoadingHoldsStreams
        {
            get
            {
                long until = System.Threading.Interlocked.Read(ref streamHoldUntilTicks);
                return until != 0 && DateTime.UtcNow.Ticks < until;
            }
        }

        /// <summary>Starts or moves the loading-screen stream hold's end (PlayerManager only).</summary>
        internal void HoldStreamsUntil(DateTime until) => System.Threading.Interlocked.Exchange(ref streamHoldUntilTicks, until.Ticks);

        /// <summary>Ends the loading-screen stream hold (PlayerManager only).</summary>
        internal void ReleaseStreamHold() => System.Threading.Interlocked.Exchange(ref streamHoldUntilTicks, 0);

        /// <summary>
        /// The client confirmed a re-zone (its repeat ClientIsReady, sent while the loading screen still waits for the
        /// server's confirmation packets): the arrival ends a few seconds later even without a position update. A
        /// player standing still after a teleport sends none, and until then nothing was streamed to them for a whole
        /// minute (2026-10-12: a boss spawned 45 s after a teleport never reached the idle test client).
        /// </summary>
        public void ArrivalConfirmed()
        {
            DateTime soon = DateTime.UtcNow.Add(ArrivalSettle);
            if (arrivingUntil is DateTime until && until > soon)
                arrivingUntil = soon;
        }

        private static readonly TimeSpan ArrivalSettle = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Entity guid of this session's player, as sent in SendSelfToClient.
        /// </summary>
        public ulong PlayerGuid { get; private set; }

        /// <summary>The client's preferred chat languages (PlayerUpdate 0x1A PreferredLanguages flags).</summary>
        public uint PreferredLanguages { get; set; }

        /// <summary>
        /// The Models.txt id this player is shown as instead of their own body, 0 for none (an admin's /setmodel,
        /// <see cref="Game.TemporaryAppearance"/>). Kept for this session only: a relog brings the normal look back. Sent
        /// to clients that meet the player later in LightweightToFullPc (<see cref="BuildLightweightToFullPc"/>).
        /// </summary>
        public uint TemporaryAppearanceModel { get; set; }

        /// <summary>
        /// SendSelf CastKeySeed (player +0x753f0, OPEN-LEADS L189): the client builds each CastAttempt's cast hash as
        /// ((cast counter &amp; 0xff) ^ seed) * 0x502c3f11 (FUN_142e0f810). Live sent a non-zero, random-looking value
        /// (0x5BB9EB0B, the one capture), so each session draws its own; it stays the same for every re-zone's SendSelf,
        /// because the client keeps its cast counter (+0x753f4) running. <see cref="Game.Abilities.CastKeyMatches"/>
        /// checks it.
        /// </summary>
        public uint CastKeySeed { get; } = (uint)System.Random.Shared.Next(1, int.MaxValue) | 0x80000000u;

        /// <summary>
        /// Short id other clients use for this player in movement packets.
        /// </summary>
        public uint TransientId { get; } = Game.PlayerManager.AllocateTransientId();

        /// <summary>
        /// Added to positions sent about other characters: clients subtract the zone origin / 10 from them, while
        /// positions from the PositionUpdate channel are already local. The origin is the one in the
        /// ZoneCoordinateInfo packet (25584000, 20000, 25564500).
        /// </summary>
        /// <summary>The entity the client last targeted (TargetBase RequestSetTarget), 0 when none.</summary>
        public ulong CurrentTarget { get; set; }

        /// <summary>The client's target slots as the server confirmed them: slot → entity (3 hostile, 8 ally, 9 harvest / neutral).</summary>
        public Dictionary<uint, ulong> TargetSlots { get; } = new();

        public static readonly Vector3 RemoteFrameOffset = new(2558400f, 2000f, 2556450f);

        public string CharacterName => currentCharacter?.Name;

        public Game.Inventory.Inventory Inventory { get; private set; }

        /// <summary>Recipes learned from recipe items (default-learned ones are implied).</summary>
        public HashSet<uint> LearnedRecipes { get; private set; } = new();

        /// <summary>The recipe ids the client was last told to hide (<see cref="Game.RecipeBook.RefreshHidden"/>; null before SendSelf).</summary>
        public HashSet<uint> HiddenRecipes { get; set; }
        /// <summary>Hidden unlocks whose SendSelf recipe record was left out at login (bandwidth R1); an unhide of one
        /// shows nothing until the next login.</summary>
        public HashSet<uint> RecipeRecordsWithheld { get; set; }

        /// <summary>
        /// Gives the player items and tells the client about every stack that changed.
        /// </summary>
        public void GiveItem(uint definitionId, uint count)
        {
            foreach ((CharacterItemModel item, bool isNew) in Inventory.Add(definitionId, count))
                SendItem(item, replace: !isNew);
        }

        /// <summary>
        /// Gives the player items that keep their instance state (a traded, mailed or stored item keeps its wear,
        /// <see cref="Game.Inventory.ItemState"/>) and tells the client about every stack that changed.
        /// </summary>
        public void GiveItem(uint definitionId, uint count, Game.Inventory.ItemState state)
        {
            foreach ((CharacterItemModel item, bool isNew) in Inventory.Add(definitionId, count, state))
                SendItem(item, replace: !isNew);
        }

        /// <summary>
        /// Takes up to <paramref name="count"/> from one stack and tells the client (count update, or delete when the
        /// stack runs out). Returns the stack (its definition is still readable) or null when there is no such stack.
        /// </summary>
        public CharacterItemModel TakeFromStack(ulong guid, uint count)
        {
            if (Inventory.RemoveFrom(guid, count) is not { } removed)
                return null;
            if (removed.Deleted)
                EnqueueMessage(new ClientUpdateItemDelete { Guid = guid });
            else
                SendItem(removed.Item, replace: true);
            return removed.Item;
        }

        /// <summary>
        /// Takes items away and tells the client (count update, or delete when a stack runs out). False, and
        /// nothing taken, when the player doesn't have enough.
        /// </summary>
        public bool TakeItem(uint definitionId, uint count)
        {
            var changed = new List<(CharacterItemModel Item, bool Deleted)>();
            // TryRemove takes worn stacks last, but takes them when nothing else is left (a recipe or a use that needs the
            // tool in hand, the last stack of a held item): what the character visibly holds is compared after.
            bool worn = Inventory.Items.Any(i => i.ItemDefinitionId == definitionId && i.Container == Game.Inventory.Inventory.EquippedContainer);
            Dictionary<uint, CharacterItemModel> before = worn ? VisibleEquipment() : null;
            if (!Inventory.TryRemove(definitionId, count, changed))
                return false;
            foreach ((CharacterItemModel item, bool deleted) in changed)
            {
                if (deleted)
                    EnqueueMessage(new ClientUpdateItemDelete { Guid = item.Id });
                else
                    SendItem(item, replace: true);
            }
            // A worn item used up: the slot is unset for the player and everyone on the landscape (SendEquipmentChanges),
            // or every client kept drawing the attachment its SetCharacterEquipmentSlot put on the character (the
            // ItemDelete only removes the inventory row) — the others until they met the player again (audit 2026-10-21).
            if (before != null && changed.Any(c => c.Deleted && c.Item.Container == Game.Inventory.Inventory.EquippedContainer))
                SendEquipmentChanges(before);
            return true;
        }

        /// <summary>
        /// Shows an item's current state on the client: ItemAdd for a new item, ItemUpdate (count and location) for
        /// one the client already has.
        /// </summary>
        public void SendItem(CharacterItemModel item, bool replace)
        {
            if (replace)
            {
                // The item's own random blocks (quality, tool stats), as its ItemAdd had them: zeros reset them.
                InventoryItem record = Game.Inventory.Inventory.ToPacket(item);
                EnqueueMessage(new ClientUpdateItemUpdate
                {
                    Guid = item.Id,
                    Count = item.Count,
                    Slot = item.Slot,
                    Container = item.Container,
                    Wear = item.Wear,
                    Random = record.Definition.Random,
                    Random2 = record.Definition.Random2
                });
            }
            else
                EnqueueMessage(new ClientUpdateItemAdd { Item = Game.Inventory.Inventory.ToPacket(item) });
            if (item.ItemDefinitionId == Game.Inventory.Inventory.CurrencyItem)
                SendCurrencies();
        }
        private uint modelId;
        private uint profileId;

        /// <summary>
        /// What this character visibly wields or wears from its equipped items, keyed by equipment slot id. Only
        /// items with a model of their own count (tools, weapons); others are shown through the character model.
        /// </summary>
        public Dictionary<uint, CharacterItemModel> VisibleEquipment()
        {
            var visible = new Dictionary<uint, CharacterItemModel>();
            foreach (CharacterItemModel item in Inventory.Items.Where(i => i.Container == Game.Inventory.Inventory.EquippedContainer))
            {
                var definition = Game.Inventory.ItemDefinitionManager.Get(item.ItemDefinitionId);
                if (definition != null && definition.EquipSlotId != 0 && !string.IsNullOrEmpty(definition.ModelFile))
                    visible[definition.EquipSlotId] = item;
            }
            return visible;
        }

        public EquipmentSetCharacterEquipmentSlot BuildEquipmentSlot(uint equipSlotId, CharacterItemModel item) => new()
        {
            ProfileId = profileId,
            CharacterGuid = PlayerGuid,
            EquipSlotId = equipSlotId,
            ItemGuid = item.Id,
            ModelName = Game.Inventory.ItemDefinitionManager.Get(item.ItemDefinitionId).ModelFile,
            // The item's colours: the same attachment layout as the character select payload, whose third string
            // (semantic tint alias) colours the outfit there. Left empty, armour showed in its untinted base colours.
            TintAlias = Game.Inventory.ItemDefinitionManager.Get(item.ItemDefinitionId).TintAlias ?? "",
            CompositeEffect = Game.Inventory.ItemDefinitionManager.Get(item.ItemDefinitionId).AmbientEffect
        };

        /// <summary>
        /// The off hand's attachment for a main-hand item with a second model (a sword-and-shield item's shield, a bow's
        /// quiver, the left gun / dagger of a pair), or null for any other item (OPEN-LEADS L192 (i), L95).
        /// <para>
        /// Why the server sends it [P, 0.695]: the item definition carries the second model (record tail +0x80 / +0xb0,
        /// <see cref="Game.Inventory.ItemDefinitionManager.ItemDefinition.SecondaryModelFile"/>), and the client's own
        /// item-to-actor builders attach it in the slot named by SecondaryWeaponSlotName ("Off Hand" = 11) whenever the
        /// item is in the PrimaryWeaponSlotName slot (FUN_142ebde20 / FUN_142cb1f60, the check FUN_143b7a520, the slot
        /// FUN_143b7a4a0) - but those run only for the client's previews. In the world, EquipmentSetCharacterEquipmentSlot
        /// (handler FUN_142e42da0) attaches only the one model in the packet (FUN_142ebd620, one record per slot, keyed by
        /// the slot +0x188) and never looks at the item's definition, for the player and for others alike. So a shield or
        /// a second gun shows in the world only when the server sends that model in slot 11 as well; before, every
        /// sword-and-shield and dual weapon showed one hand only [I strong: no other packet carries a second model].
        /// </para>
        /// The item guid stays 0: for the player's own character FUN_142ea49e0 would otherwise move the item record into
        /// slot 11 of the client's equipment model (it looks the guid up and re-slots it); an unknown guid does nothing.
        /// </summary>
        public EquipmentSetCharacterEquipmentSlot BuildSecondarySlot(CharacterItemModel item)
        {
            var definition = Game.Inventory.ItemDefinitionManager.Get(item.ItemDefinitionId);
            if (definition == null || definition.EquipSlotId != Game.Inventory.Inventory.MainHandSlot
                || string.IsNullOrEmpty(definition.SecondaryModelFile))
                return null;
            return new EquipmentSetCharacterEquipmentSlot
            {
                ProfileId = profileId,
                CharacterGuid = PlayerGuid,
                EquipSlotId = Game.Inventory.Inventory.OffHandSlot,
                ItemGuid = 0,
                ModelName = definition.SecondaryModelFile,
                TintAlias = definition.SecondaryTintAlias ?? ""
            };
        }

        /// <summary>
        /// Every slot packet that draws this character's visible equipment (<see cref="VisibleEquipment"/>), with the
        /// main-hand item's off-hand model after it (<see cref="BuildSecondarySlot"/>): for a viewer meeting the player and
        /// for the player's own zone-in.
        /// </summary>
        public List<EquipmentSetCharacterEquipmentSlot> EquipmentSlotPackets()
        {
            var packets = new List<EquipmentSetCharacterEquipmentSlot>();
            foreach ((uint slot, CharacterItemModel item) in VisibleEquipment())
            {
                packets.Add(BuildEquipmentSlot(slot, item));
                if (slot == Game.Inventory.Inventory.MainHandSlot && BuildSecondarySlot(item) is { } secondary)
                    packets.Add(secondary);
            }
            return packets;
        }

        /// <summary>
        /// The off hand follows the main hand: between two <see cref="VisibleEquipment"/> snapshots, the new main-hand item's
        /// second model (<see cref="BuildSecondarySlot"/>), or the off hand emptied when the old item had one and the new
        /// one (or none) has not — the client keeps a slot's model until told (FUN_142ebd620); null = nothing to send.
        /// </summary>
        public IWritable OffHandChange(Dictionary<uint, CharacterItemModel> before, Dictionary<uint, CharacterItemModel> after)
        {
            uint main = Game.Inventory.Inventory.MainHandSlot;
            before.TryGetValue(main, out CharacterItemModel oldMain);
            after.TryGetValue(main, out CharacterItemModel newMain);
            if (oldMain?.Id == newMain?.Id)
                return null;
            if (newMain != null && BuildSecondarySlot(newMain) is { } secondary)
                return secondary;
            if (oldMain != null && BuildSecondarySlot(oldMain) != null)
                return new EquipmentUnsetCharacterEquipmentSlot { ProfileId = profileId, CharacterGuid = PlayerGuid,
                    EquipSlotId = Game.Inventory.Inventory.OffHandSlot };
            return null;
        }

        /// <summary>
        /// Tells this client and everyone else in the world what changed in this character's visible equipment
        /// since <paramref name="before"/> (from <see cref="VisibleEquipment"/>). Only changed slots are sent; a main-hand
        /// change also sets or empties the off hand (<see cref="BuildSecondarySlot"/>).
        /// </summary>
        public void SendEquipmentChanges(Dictionary<uint, CharacterItemModel> before)
        {
            // A weapon change gives the attack buttons the new weapon's names, times and costs.
            Game.Combat.WeaponCombat.CheckWeapon(this);
            Dictionary<uint, CharacterItemModel> after = VisibleEquipment();
            // "Close build mode [Tab] and equip your weapon" (Cry Havoc!): a weapon newly in hand.
            if (Game.Abilities.HeldWeapon(this) != null)
                Game.Achievements.Progress(this, Game.Achievements.EquipWeapon);
            var messages = new List<IWritable>();
            foreach ((uint slot, CharacterItemModel item) in after)
                if (!before.TryGetValue(slot, out CharacterItemModel old) || old.Id != item.Id)
                {
                    messages.Add(BuildEquipmentSlot(slot, item));
                    Game.FirstTimeEvents.Equipped(this, item.ItemDefinitionId);
                }
            foreach (uint slot in before.Keys.Where(s => !after.ContainsKey(s)))
                messages.Add(new EquipmentUnsetCharacterEquipmentSlot { ProfileId = profileId, CharacterGuid = PlayerGuid, EquipSlotId = slot });
            if (OffHandChange(before, after) is IWritable offHand)
                messages.Add(offHand);

            if (messages.Count == 0)
                return;
            // Tool stats (harvesting area = the mining ring, harvest damage, tier) follow the held tool.
            SendCalculatedStats();
            foreach (WorldSession session in EquipmentAudience(Game.PlayerManager.Snapshot()))
                foreach (IWritable message in messages)
                    session.EnqueueMessage(message);
        }

        /// <summary>
        /// Who gets this character's equipment changes: the player and the players on the same landscape — every one of
        /// them has the character as an entity (PlayerManager.Introduce shows each player to the whole landscape, with
        /// its equipment), and the client's slot handler (0.695 FUN_142e42da0) only finds the character among its own
        /// entities (FUN_142cd1cb0). Until 2026-10-18 the change went to every player in the world, on every landscape,
        /// whose clients have no such entity and dropped it; a player who comes onto this landscape later gets the
        /// current equipment with the introduction.
        /// </summary>
        internal IEnumerable<WorldSession> EquipmentAudience(IEnumerable<WorldSession> players)
            => players.Where(p => p != this && p.ZoneId == ZoneId).Append(this);

        /// <summary>
        /// The packet that shows this player to other clients, at their latest known position.
        /// </summary>
        public PlayerUpdateAddLightweightPc BuildAddLightweightPc()
        {
            Vector3 position = lastPosition ?? new Vector3(currentCharacter.PositionX, currentCharacter.PositionY, currentCharacter.PositionZ);
            Quaternion rotation = lastRotation ?? new Quaternion(currentCharacter.RotationX, currentCharacter.RotationY,
                currentCharacter.RotationZ, currentCharacter.RotationW);
            if (rotation == default)
                rotation = Quaternion.Identity;

            var message = new PlayerUpdateAddLightweightPc
            {
                Guid = PlayerGuid,
                TransientId = TransientId,
                ModelId = modelId,
                // The character's current profile (entity +0x1474), as its own client got in SendSelfToClient.
                ProfileId = profileId,
                Position = new Vector4(position + RemoteFrameOffset, 1f),
                Rotation = new Vector4(rotation.X, rotation.Y, rotation.Z, rotation.W),
                // Other clients keep each player's membership on the entity (TASKS 3.12, Game.Membership).
                IsMember = Game.Membership.IsMember(this)
            };
            message.Identity.FirstName = currentCharacter.Name;
            return message;
        }

        /// <summary>The character's race (CharCustomizationSlotCost RACE_ID: 1 humen, 5 large humen).</summary>
        public uint Race => currentCharacter?.Race ?? 1;

        /// <summary>The character's gender (1 male, 2 female).</summary>
        public uint GenderId => currentCharacter?.Gender ?? 1;

        /// <summary>
        /// Changes the character's size (race) and gender (the 0.695 Crimson Parlor): saves race, gender and the profile
        /// that goes with them (character creation takes it from the same preset), and the model other clients are sent
        /// (the profile's male / female base actor). The caller re-introduces the player.
        /// </summary>
        public void SetBody(uint race, uint gender, uint profileId)
        {
            ulong characterId = currentCharacter.Id;
            currentCharacter.Race = (byte)race;
            currentCharacter.Gender = (byte)gender;
            currentCharacter.ProfileTypeId = profileId;
            modelInfo.Race = race;
            modelInfo.Gender = gender;
            if (GameTableManager.Instance.ProfileDefinitions.GetEntry(profileId) is ProfileDefinitionEntry profile)
                modelId = gender == (uint)Shared.Game.Entity.Static.Gender.Female ? profile.FemaleModelId : profile.MaleModelId;
            DatabaseManager.Instance.CharacterDatabase.Save(c =>
            {
                var model = new CharacterModel { Id = characterId };
                c.Character.Attach(model);
                model.Race = (byte)race;
                model.Gender = (byte)gender;
                model.ProfileTypeId = profileId;
                c.MarkModified(model, nameof(model.Race));
                c.MarkModified(model, nameof(model.Gender));
                c.MarkModified(model, nameof(model.ProfileTypeId));
            });
        }

        /// <summary>The character's appearance: customisations (slot → option, tint) and skin tint.</summary>
        public (IReadOnlyDictionary<uint, (uint Option, uint Tint)> Slots, uint SkinTint) Appearance
            => (currentCharacter.Customisation.ToDictionary(c => c.Slot, c => (c.Option, c.Tint)), currentCharacter.SkinTint);

        /// <summary>
        /// Replaces the character's appearance (the barbershop): saves the customisation rows and skin tint, and updates
        /// what other clients get as this player's full data (they are re-introduced by the caller).
        /// </summary>
        public void SetAppearance(IEnumerable<(uint Slot, uint Option, uint Tint)> slots, uint skinTint)
        {
            var list = slots.ToList();
            ulong characterId = currentCharacter.Id;
            currentCharacter.SkinTint = skinTint;
            currentCharacter.Customisation.Clear();
            foreach (var (slot, option, tint) in list)
                currentCharacter.Customisation.Add(new CharacterCustomisationModel { Id = characterId, Slot = slot, Option = option, Tint = tint });
            modelInfo.SkinId = skinTint;
            modelInfo.Customisations.Clear();
            foreach (var (slot, option, tint) in list)
                modelInfo.Customisations.Add((slot, option, tint));
            DatabaseManager.Instance.CharacterDatabase.Save(c =>
            {
                c.CharacterCustomisation.RemoveRange(c.CharacterCustomisation.Where(x => x.Id == characterId));
                foreach (var (slot, option, tint) in list)
                    c.Add(new CharacterCustomisationModel { Id = characterId, Slot = slot, Option = option, Tint = tint });
                var model = new CharacterModel { Id = characterId };
                c.Character.Attach(model);
                model.SkinTint = skinTint;
                c.MarkModified(model, nameof(model.SkinTint));
            });
        }

        // Appearance as sent in SendSelfToClient, and the newest full (type 0) movement body, for other clients.
        private Shared.Network.Message.Model.Shared.CharacterModelInfo modelInfo = new();

        /// <summary>The character's look as the client's customization data (slots, skin, race, gender).</summary>
        public Shared.Network.Message.Model.Shared.CharacterModelInfo ModelInfo => modelInfo;
        private byte[] lastFullMovement;

        /// <summary>
        /// The reply to another client's request for this player's full data.
        /// </summary>
        public PlayerUpdateLightweightToFullPc BuildLightweightToFullPc()
        {
            var message = new PlayerUpdateLightweightToFullPc
            {
                Guid = PlayerGuid,
                TransientId = TransientId,
                ModelId = modelId,
                Model = modelInfo,
                Movement = lastFullMovement ?? PlayerUpdateLightweightToFullPc.EmptyMovement,
                ChatBubble = ChatBubble,
                // The client applies both unconditionally when the character turns full, over the PlayerTitle / AFK
                // packets the introduction sent (PlayerManager.Introduce), so they must be the player's current ones.
                Title = Game.Titles.ShownTitle(this),
                Afk = Game.Afk.IsAway(this),
                // An admin's /setmodel look, which the client applies from this field once the actor exists.
                TemporaryAppearance = TemporaryAppearanceModel,
                GuildTag = Game.Social.GuildTags.TagOf(PlayerGuid)
            };
            // Without temporary buffs: no calculated stat is SEND_TO_OTHERS, so an observer gets no update when a buff ends
            // and a buffed snapshot (Bottled Moonlight's light radius → the character's self-light, FUN_142e5d990 →
            // FUN_142ec5a40) would stay on that observer (OPEN-LEADS L196 OL-3, Abilities.CalculatedStatsForOthers).
            message.Stats.AddRange(Game.Abilities.CalculatedStatsForOthers(this));
            // The movement acceleration times live copied to other players (once, with the character; L162).
            message.CharacterStats.AddRange(Game.CharacterStats.ForOthers());
            return message;
        }

        /// <summary>
        /// Sends the player's calculated stats. The client drops them unless its player entity already exists, so this
        /// must wait until the client reports it is ready.
        /// </summary>
        public void SendCalculatedStats()
        {
            var stats = new ClientUpdateUpdateCalculatedStats { Guid = PlayerGuid };
            stats.Stats.AddRange(Game.Abilities.CalculatedStats(this));
            EnqueueMessage(stats);
        }
        private bool positionDirty;
        private readonly Stopwatch sinceLastPositionSave = Stopwatch.StartNew();

        #region Session Handlers
        /// <summary>The gateway attached the connection with this character loaded (AttachConnection).</summary>
        public void OnCharacterLogin(CharacterModel model)
        {
            // A login during a shutdown (Play pressed just before the login server heard) loads as usual and is sent
            // back once in the world (Game.Shutdown.SendPlayersToCharacterSelect): a relogin session before the client
            // has the world crashes it as it starts the relogin (2026-10-14).
            currentCharacter = model;
            Game.Unlocks.Load(model.Id);
            Inventory = new Game.Inventory.Inventory(model);
            LearnedRecipes = Game.RecipeBook.LoadLearned(model.Id);
            Game.Achievements.LoadCharacter(model.Id);
            Game.Npc.WorldNpcs.LoadCharacter(model.Id);
            PreferenceXml = Game.Preferences.Load(model.Id);
            InventoryFilters = Game.InventoryFilters.Load(model.Id);
            //EnqueueMessage((ZoneMessageOpcode)0x16015A04, File.ReadAllBytes("packets\\25-unknown").AsSpan().Slice(4).ToArray());
            Game.Landscapes.LeaveClosedEventIsland(model);   // the Events world switched islands since the last login
            Game.Pioneer.LeaveOldLand(model);   // saved on an old Pioneer land (417-421): to their site or the new land
            Game.Pioneer.Settle(model);   // a brand-new character starts on its own site on a Pioneer land
            EnqueueMessage(ZoneMessageOpcode.SendZoneDetails, Game.Landscapes.ZoneDetails(model.ZoneId));

            // Setup Player, Send Packets, etc.
            EnqueueMessage(ZoneMessageOpcode.InitializationParameters, Convert.FromHexString("04-00-00-00-6C-69-76-65".Replace("-", "")));
            // The live capture's settings, as they are (fields named in the model).
            EnqueueMessage(ZoneMessageOpcode.ClientGameSettings, ClientGameSettings.LiveBody);
            EnqueueMessage(ZoneMessageOpcode.ReferenceDataItemClassDefinitions, Convert.FromHexString("00-00-00-00".Replace("-", "")));
            var catPath = GetResourcePath("PacketDumps", "39-ReferenceDataItemCategoryDefinitions");
            var profPath = GetResourcePath("PacketDumps", "41-ReferenceDataProfileDefinitions");
            EnqueueMessage(ZoneMessageOpcode.ReferenceDataItemCategoryDefinitions, File.ReadAllBytes(catPath).AsSpan().Slice(4).ToArray());
            EnqueueMessage(ZoneMessageOpcode.ReferenceDataProfileDefinitions, File.ReadAllBytes(profPath).AsSpan().Slice(4).ToArray());
            EnqueueMessage(ZoneMessageOpcode.SkillBase, Convert.FromHexString("05-02-00-00-00-00-00-00-00-04-00-00-00-01-00-00-00-01-00-00-00-44-82-08-00-44-82-08-00-00-00-00-00-01-00-00-00-00-00-00-00-01-00-00-00-00-00-00-00-01-00-00-00-00-00-00-00-05-00-00-00-05-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-03-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-02-00-00-00-02-00-00-00-44-82-08-00-44-82-08-00-00-00-00-00-01-00-00-00-00-00-00-00-01-00-00-00-00-00-00-00-01-00-00-00-01-00-00-00-05-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-04-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-03-00-00-00-03-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-01-00-00-00-00-00-00-00-01-00-00-00-00-00-00-00-01-00-00-00-02-00-00-00-0F-00-00-00-0A-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-05-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-04-00-00-00-04-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-01-00-00-00-00-00-00-00-01-00-00-00-00-00-00-00-01-00-00-00-03-00-00-00-1E-00-00-00-0F-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-06-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-01-00-00-00-01-00-00-00-01-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-01-00-00-00-00-00-00-00-01-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-01-00-00-00-01-00-00-00-01-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-01-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-01-00-00-00-01-00-00-00-01-00-00-00-44-82-08-00-44-82-08-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-08-00-00-00-13-00-00-00-13-00-00-00-01-00-00-00-00-00-00-00-14-00-00-00-14-00-00-00-01-00-00-00-00-00-00-00-1C-00-00-00-1C-00-00-00-01-00-00-00-00-00-00-00-1D-00-00-00-1D-00-00-00-01-00-00-00-00-00-00-00-1E-00-00-00-1E-00-00-00-01-00-00-00-00-00-00-00-1F-00-00-00-1F-00-00-00-01-00-00-00-00-00-00-00-20-00-00-00-20-00-00-00-01-00-00-00-00-00-00-00-21-00-00-00-21-00-00-00-01-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00".Replace("-", "")));
            Game.Inventory.ItemDefinitionManager.SendLoginList(this);
            //EnqueueMessage(ZoneMessageOpcode.SendSelfToClient, sendSelfPacket.AsSpan().Slice(2).ToArray());

            //var msgData = new Span<byte>(sendSelfPacket, 2, sendSelfPacket.Length - 2).ToArray();
            //var reader = new GamePacketReader(msgData);
            //var message = new SendSelfToClient();
            //message.Read(reader);

            BuildSendToSelfPacket();
            // An older session of this character still in the world (its client crashed, was killed or lost the network
            // and hasn't timed out yet) goes now, not when this client has finished loading: the others stop seeing the
            // dead one at once.
            Game.PlayerManager.ReplaceOlderSessions(this);
        }

        /// <summary>
        /// The gateway re-attached a connection it held while this zone was away (AttachConnection with isReattach,
        /// gateway-service.md §3.5): the client is still in the world with its own SendSelf, UI and inventory view, and
        /// the entities and terrain of the zone that went away. The character is loaded as at login, without the login
        /// packets; the session counts as past its first ClientIsReady; and as soon as the client reports where it is
        /// (or after <see cref="ReattachWait"/>, at the saved position) it is re-zoned there. ClientBeginZoning makes the
        /// client build a fresh terrain engine and drop every entity except the player (FUN_142ee4930, docs/terrain.md
        /// "Long teleports"), and its ClientIsReady then gets the zone's entities again (MiscHandler.ClientIsReady,
        /// re-zone path).
        /// </summary>
        public void OnReattach(CharacterModel model, bool transfer = false, ulong arriveNear = 0, ulong joinGame = 0,
            Internal.WorldSpot returnTo = default, Internal.WorldSpot arriveAt = default,
            Internal.CavernTripKind cavernTrip = Internal.CavernTripKind.None)
        {
            transferCavernTrip = transfer ? cavernTrip : Internal.CavernTripKind.None;
            transferArriveNear = transfer ? arriveNear : 0;
            transferJoinGame = transfer ? joinGame : 0;
            transferReturnTo = transfer ? returnTo : default;
            transferArriveAt = transfer ? arriveAt : default;
            currentCharacter = model;
            Game.Unlocks.Load(model.Id);
            Inventory = new Game.Inventory.Inventory(model);
            LearnedRecipes = Game.RecipeBook.LoadLearned(model.Id);
            Game.Achievements.LoadCharacter(model.Id);
            Game.Npc.WorldNpcs.LoadCharacter(model.Id);
            PreferenceXml = Game.Preferences.Load(model.Id);
            InventoryFilters = Game.InventoryFilters.Load(model.Id);
            BuildSendToSelfPacket(send: false);
            // An arrival from another world replaces any older session of the character here at once. Not a re-attach
            // after a zone restart: the gateway re-attaches every held connection, and of two of one character (a dead
            // client not yet timed out and its new login) only the live one will report ready — PlayerManager.Enter then
            // drops the other.
            if (transfer)
                Game.PlayerManager.ReplaceOlderSessions(this);
            ReadySent = true;
            MotdSent = true;
            BuildHudReset = true;
            WorldReady = true;
            LoadingScreenDown = true;
            transferArrival = transfer;
            if (transfer)
            {
                // From another world's zone (cross-world travel, B.10): straight to the landscape it saved, as the Landscapes
                // window's travel does here (its Spire), not where the client still thinks it is.
                reattachRezoneDue = DateTime.UtcNow;
                log.Info($"{model.Name} arrives from another world for landscape {model.ZoneId}.");
                return;
            }
            reattachRezoneDue = DateTime.UtcNow + ReattachWait;
            log.Info($"{model.Name} re-attached after a zone restart; re-zoning once the client reports its position.");
        }

        /// <summary>The re-attach is an arrival from another world (its re-zone goes to the saved landscape's arrival).</summary>
        private bool transferArrival;
        /// <summary>An arrival from another world by Travel to Friend: the friend to land beside (0 = none).</summary>
        private ulong transferArriveNear;
        /// <summary>An arrival from another world for a Game Finder match (L31): the site game to join (0 = none).</summary>
        private ulong transferJoinGame;
        /// <summary>That match's way back after the game (FinderReturnHome on the world it came from; unset = stays here).</summary>
        private Internal.WorldSpot transferReturnTo;
        /// <summary>An arrival back from a Game Finder trip: the spot it left from (unset = the landscape's usual arrival).</summary>
        private Internal.WorldSpot transferArriveAt;
        /// <summary>An arrival from another world on a Lost Crystal / Underground Adventure trip (L131): a cavern here.</summary>
        private Internal.CavernTripKind transferCavernTrip;

        /// <summary>
        /// Leaving for another world (cross-world travel, B.10): the target landscape and world (LastServerId, so the next
        /// login goes there too) are saved; the returned task finishes when they are written, before the hand-over.
        /// </summary>
        public System.Threading.Tasks.Task SaveForWorldMove(uint zoneId, ulong serverWorld)
        {
            if (currentCharacter == null)
                return System.Threading.Tasks.Task.CompletedTask;
            currentCharacter.ZoneId = zoneId;
            currentCharacter.LastServerId = serverWorld;
            ulong characterId = currentCharacter.Id;
            return DatabaseManager.Instance.CharacterDatabase.Save(c =>
            {
                var model = new CharacterModel { Id = characterId };
                c.Character.Attach(model);
                model.ZoneId = zoneId;
                model.LastServerId = serverWorld;
                c.MarkModified(model, nameof(model.ZoneId));
                c.MarkModified(model, nameof(model.LastServerId));
            });
        }

        /// <summary>How long a re-attached session waits for the client's position before re-zoning at the saved one.</summary>
        private static readonly TimeSpan ReattachWait = TimeSpan.FromSeconds(3);
        private DateTime? reattachRezoneDue;

        /// <summary>The re-zone that rebuilds a re-attached client's world, once.</summary>
        private void ReattachRezone()
        {
            if (reattachRezoneDue == null)
                return;
            reattachRezoneDue = null;
            if (transferArrival)
            {
                transferArrival = false;
                ulong near = transferArriveNear, join = transferJoinGame;
                Internal.WorldSpot returnTo = transferReturnTo, arriveAt = transferArriveAt;
                Internal.CavernTripKind trip = transferCavernTrip;
                transferArriveNear = 0;
                transferJoinGame = 0;
                transferReturnTo = default;
                transferArriveAt = default;
                transferCavernTrip = Internal.CavernTripKind.None;
                // Whatever places the player now must zone for real (FullRezoneNext): the client is still on the other
                // world, often on a landscape with this same id.
                FullRezoneNext = true;
                try
                {
                    PlaceTransferArrival(near, join, returnTo, arriveAt, trip);
                }
                finally
                {
                    FullRezoneNext = false;
                }
                return;
            }
            Game.Teleport.Rezone(this, Position);
        }

        /// <summary>Where an arrival from another world goes (<see cref="ReattachRezone"/>): the first that applies.</summary>
        private void PlaceTransferArrival(ulong near, ulong join, Internal.WorldSpot returnTo, Internal.WorldSpot arriveAt,
            Internal.CavernTripKind trip)
        {
            if (join != 0)
            {
                bool joining = Game.InstancedGames.ArriveToJoin(this, join);
                // Taken back home after the game (or at once when it's gone) when the world they came from asked for it.
                Game.InstancedGames.ExpectReturn(this, join, returnTo, joining);
                if (joining)
                    return;
            }
            if (arriveAt.IsSet && arriveAt.Zone == ZoneId)
            {
                // Back from a Game Finder trip on another world: to the spot they left from.
                Game.Teleport.Rezone(this, new Vector3(arriveAt.X, arriveAt.Y, arriveAt.Z), arriveAt.Zone);
                log.Info($"{CharacterName} is back from another world's game at the spot they left from.");
                return;
            }
            // A Lost Crystal / Underground Adventure trip from another world (L131): a cavern of this world (beside the
            // group member they follow, for an accepted group trip).
            if (trip != Internal.CavernTripKind.None && Game.CrossWorld.ArriveOnCavernTrip(this, trip, near))
                return;
            if (near != 0 && Game.Abilities.ArriveBesideFriend(this, near))
                return;
            Game.Landscapes.TravelTo(this, ZoneId);
        }

        /// <summary>
        /// SendZoneDetails body from a live capture: zone OpenBetaGeometry_406 (type 5), Sky.xml, world 2, and continent
        /// 406's settings and bitmaps (fields named in SendZoneDetails / ContinentDefinition). Also the source of the zone
        /// name and continent for <see cref="Game.Teleport"/>'s re-zoning.
        /// </summary>
        public static readonly byte[] ZoneDetails = Convert.FromHexString("14-00-00-00-4F-70-65-6E-42-65-74-61-47-65-6F-6D-65-74-72-79-5F-34-30-36-05-00-00-00-00-07-00-00-00-53-6B-79-2E-78-6D-6C-00-00-00-00-96-01-00-00-FF-FF-FF-FF-FF-FF-FF-FF-96-01-00-00-02-00-00-00-10-8F-08-00-96-01-00-00-05-00-00-00-4C-B8-08-00-15-00-00-00-4F-70-65-6E-42-65-74-61-43-6F-6E-74-69-6E-65-6E-74-5F-34-30-36-21-00-00-00-4F-70-65-6E-42-65-74-61-5F-44-6F-5F-4E-4F-54-5F-43-68-61-6E-67-65-54-68-69-73-54-65-78-74-5F-31-36-00-00-00-00-00-00-44-40-00-00-00-00-E0-84-43-41-00-00-00-00-00-40-9F-40-00-00-00-00-11-81-43-41-00-00-00-00-00-40-BF-40-00-00-00-00-00-40-AF-40-00-00-00-00-00-40-BF-40-01-00-1A-00-00-00-43-6F-6E-74-69-6E-65-6E-74-5F-42-69-6F-6D-65-49-64-73-5F-34-30-36-2E-62-6D-70-CD-CC-CC-40-17-00-00-00-43-6F-6E-74-69-6E-65-6E-74-5F-53-68-61-70-65-5F-34-30-36-2E-62-6D-70-CD-CC-CC-40-20-00-00-00-43-6F-6E-74-69-6E-65-6E-74-5F-48-65-69-67-68-74-4C-61-79-65-72-49-64-73-5F-34-30-36-2E-62-6D-70-00-00-C8-43-10-27-00-00-00-00-80-3E-00-00-80-3E-00-00-80-3E-00-00-40-3F-00-00-00-3F-00-00-00-40-03-00-00-00-0A-D7-23-3C-00-40-9C-44-00-C0-41-45-00-00-F0-41-01-00-00-00-00-00-00-00-00-6E-A7-40-DA-01-00-00-00-00-00-00-00-B5-36-00-00-01-00-00-80-3E-CD-CC-CC-3D-01-00-00-00-00-00-00-00-00".Replace("-", ""));

        /// <summary>
        /// The client sizes its action bars only from SendSelfToClient (a bar it is not sent there has no slots, and
        /// later slot updates for it are rejected). Bars come from ActionBarDefinitions.txt, slots per bar from
        /// StringHashToValue's ActionSlotCount, and defaults from ActionSlotDefinitions.txt; a default of type 1 (item
        /// definition) is bound by the client to the matching inventory item.
        /// </summary>
        private const uint ActionBarCount = 2, ActionSlotsPerBar = 10;
        public const uint ActionSlotEmpty = 0, ActionSlotItemDefinition = 1, ActionSlotItem = 2;

        /// <summary>Records that the character is (or just was) in the world: build site upkeep counts from this.</summary>
        public void MarkOnline()
        {
            if (currentCharacter == null)
                return;
            ulong characterId = currentCharacter.Id;
            DateTime now = DateTime.UtcNow;
            currentCharacter.LastOnline = now;
            uint logins = ++currentCharacter.LoginCount;
            enteredWorld = now;
            DatabaseManager.Instance.CharacterDatabase.Save(c =>
            {
                var model = new CharacterModel { Id = characterId };
                c.Character.Attach(model);
                model.LastOnline = now;
                model.LoginCount = logins;
            });
        }

        /// <summary>Currency.txt 1 "Gold Coin": the coin currency the UI shows as Lumens.</summary>
        private const byte CoinCurrency = 1;

        /// <summary>
        /// PlayerUpdate 0x49 PacketPlayerCurrencyChange (reader FUN_142bb0d50; dispatcher case 0x48): u64 player guid,
        /// u32 n × (u8 currency id, u32 amount). Fills the player's currency table, which backs the UI's
        /// "Character.Currencies" data source (the HUD's Lumen counter, the barbershop, store windows); the client
        /// raises OnCurrencyReceived / OnCurrencySpent when an amount changes. Sent at login and whenever the coin
        /// stack changes. The coin count packet (ClientUpdate CoinCount) alone does not reach those widgets.
        /// </summary>
        public void SendCurrencies()
        {
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write(PlayerGuid);
            writer.Write(1u);
            writer.Write(CoinCurrency);
            writer.Write(Coins);
            EnqueueMessage(ZoneMessageOpcode.PlayerUpdateCurrencyChangeWire, output.ToArray());
            // The commerce wallet carries the Lumens while the Player Studio listing fee is charged (the client checks
            // the fee against it): kept current with the coins.
            Game.Commerce.WalletChanged(this);
        }

        /// <summary>The character's coins: Lumens in the currency container (see Inventory.CurrencyItem).</summary>
        public uint Coins => Inventory?.CountOf(Game.Inventory.Inventory.CurrencyItem) ?? 0;

        /// <summary>
        /// Adds (positive) or takes (negative) coins; false, with nothing changed, if there aren't enough. The client
        /// hears of it as any item change (and through ClientUpdate 0x29 sub 0x13 CoinCount {u32}, player +0x72a38,
        /// Lua Player.GetPlayerCoinCount).
        /// </summary>
        public bool ChangeCoins(long delta)
        {
            if (Inventory == null || delta < int.MinValue || delta > int.MaxValue)
                return false;
            if (delta > 0)
                GiveItem(Game.Inventory.Inventory.CurrencyItem, (uint)delta);
            else if (delta < 0 && !TakeItem(Game.Inventory.Inventory.CurrencyItem, (uint)-delta))
                return false;
            EnqueueMessage(ZoneMessageOpcode.ClientUpdateCoinCount, BitConverter.GetBytes(Coins));
            return true;
        }

        /// <summary>Chat flood guard (see Game.Chat.Handle) and how often the player is told about it.</summary>
        public global::LandmarkEmulator.Shared.Game.RateLimiter ChatLimiter { get; } = new(5, 1 / 1.5);
        public global::LandmarkEmulator.Shared.Game.RateLimiter ChatWarnLimiter { get; } = new(1, 1 / 10.0);

        /// <summary>
        /// Actions that are written to the database or disk (Links added or removed): bursts of 30, 10 a second — far
        /// above what the Links window sends, low enough that a modified client can't keep the database busy.
        /// </summary>
        public global::LandmarkEmulator.Shared.Game.RateLimiter StorageLimiter { get; } = new(30, 10);
        /// <summary>Contest entries (screenshots are written to disk): 2 at once, then one every 30 seconds.</summary>
        public global::LandmarkEmulator.Shared.Game.RateLimiter ContestLimiter { get; } = new(2, 1 / 30.0);
        /// <summary>Bug / build site reports (saved to disk): 2 at once, then one a minute.</summary>
        public global::LandmarkEmulator.Shared.Game.RateLimiter ReportLimiter { get; } = new(2, 1 / 60.0);

        /// <summary>When the player entered the world this session (for played time).</summary>
        private DateTime? enteredWorld;

        /// <summary>
        /// Adds the whole minutes spent in the world since the last call to the character's played minutes (on logout
        /// and with each periodic position save, so a server stop loses less than one save interval).
        /// </summary>
        /// <summary>The character's profile text (0.695 Bio; "" for none).</summary>
        public string Bio => currentCharacter?.Bio ?? "";

        /// <summary>The character's id in the character database (0 before a character is loaded).</summary>
        public ulong CharacterId => currentCharacter?.Id ?? 0;

        /// <summary>
        /// The character's client preference document (Game.Preferences, zone opcode 0x03): as stored at login or as the
        /// client last sent it; null when none is stored (the live default is sent then).
        /// </summary>
        public byte[] PreferenceXml { get; set; }

        /// <summary>
        /// The character's saved bag filters (Game.InventoryFilters, PlayerFilterUpdatePacket 0xDD sub 4): loaded at login,
        /// changed as the client reports, sent in SendSelfToClient's InventoryFilterManager. Lock the list to use it.
        /// </summary>
        public List<CharacterInventoryFilterModel> InventoryFilters { get; set; }

        /// <summary>The client's Bio limit (BioLengthMax); its own bad-word filter runs before it sends.</summary>
        public const int BioLengthMax = 512;

        /// <summary>The player changed their bio (PlayerUpdate SetPlayerBio): saved on the character.</summary>
        public void SetBio(string bio)
        {
            if (currentCharacter == null)
                return;
            bio ??= "";
            if (bio.Length > BioLengthMax)
                bio = bio[..BioLengthMax];
            currentCharacter.Bio = bio;
            ulong characterId = currentCharacter.Id;
            DatabaseManager.Instance.CharacterDatabase.Save(c =>
            {
                var model = new CharacterModel { Id = characterId };
                c.Character.Attach(model);
                model.Bio = bio;
                c.MarkModified(model, nameof(model.Bio));
            });
        }

        /// <summary>The character's chat bubble colours and size (fg, bg, size; <see cref="Game.ChatBubbleColor"/>).</summary>
        public (uint Foreground, uint Background, uint Size) ChatBubble => currentCharacter == null
            ? (Game.ChatBubbleColor.DefaultForeground, Game.ChatBubbleColor.DefaultBackground, Game.ChatBubbleColor.DefaultSize)
            : (currentCharacter.ChatBubbleForeground, currentCharacter.ChatBubbleBackground, currentCharacter.ChatBubbleSize);

        /// <summary>The player chose new chat bubble colours (Command 0x15): kept on the character and saved.</summary>
        public void SetChatBubble(uint foreground, uint background, uint size)
        {
            if (currentCharacter == null)
                return;
            currentCharacter.ChatBubbleForeground = foreground;
            currentCharacter.ChatBubbleBackground = background;
            currentCharacter.ChatBubbleSize = size;
            ulong characterId = currentCharacter.Id;
            DatabaseManager.Instance.CharacterDatabase.Save(c =>
            {
                var model = new CharacterModel { Id = characterId };
                c.Character.Attach(model);
                model.ChatBubbleForeground = foreground;
                model.ChatBubbleBackground = background;
                model.ChatBubbleSize = size;
                c.MarkModified(model, nameof(model.ChatBubbleForeground));
                c.MarkModified(model, nameof(model.ChatBubbleBackground));
                c.MarkModified(model, nameof(model.ChatBubbleSize));
            });
        }

        private void SavePlayedTime(bool leaving = false)
        {
            if (currentCharacter == null || enteredWorld is not DateTime start)
                return;
            uint minutes = (uint)Math.Max(0, (DateTime.UtcNow - start).TotalMinutes);
            enteredWorld = leaving ? null : start.AddMinutes(minutes);   // keep the part-minute for next time
            if (minutes == 0)
                return;
            uint played = currentCharacter.PlayedMinutes += minutes;
            ulong characterId = currentCharacter.Id;
            DatabaseManager.Instance.CharacterDatabase.Save(c =>
            {
                var model = new CharacterModel { Id = characterId };
                c.Character.Attach(model);
                model.PlayedMinutes = played;
                c.MarkModified(model, nameof(model.PlayedMinutes));
            });
        }

        /// <summary>
        /// The action bars as this client has them: (bar, slot) → (type, value), filled when SendSelfToClient is built
        /// (<see cref="BuildHotbars"/>) and kept current by every slot change the server confirms. The server needs it
        /// to find a free slot on its own (<see cref="PlaceOnActionBar"/>) without reading the database back while
        /// earlier slot saves may still be on their way. Null before the bars are first sent.
        /// </summary>
        private Dictionary<(uint Bar, uint Slot), (uint Type, ulong Value)> actionSlots;
        private readonly object actionSlotsLock = new();

        /// <summary>ActionSlotDefinitions' default for one slot (empty when the table has no row for it).</summary>
        private static (uint Type, ulong Value) DefaultActionSlot(uint bar, uint slot)
        {
            ActionSlotDefinitionEntry entry = GameTableManager.Instance.ActionSlotDefinitions?.Entries
                .FirstOrDefault(e => e.BarId == bar && e.SlotId == slot);
            return entry == null ? (ActionSlotEmpty, 0ul) : (entry.DefaultType, entry.DefaultValue);
        }

        /// <summary>One slot as the client shows it (empty before the bars are sent).</summary>
        internal (uint Type, ulong Value) ActionSlot(uint bar, uint slot)
        {
            lock (actionSlotsLock)
                return actionSlots != null && actionSlots.TryGetValue((bar, slot), out var value) ? value : (ActionSlotEmpty, 0ul);
        }

        /// <summary>Sets the server's copy of the client's bars: what SendSelfToClient carried (tests seed it here).</summary>
        internal void SetActionSlotsSent(IEnumerable<((uint Bar, uint Slot) Key, (uint Type, ulong Value) Slot)> slots)
        {
            lock (actionSlotsLock)
                actionSlots = slots.ToDictionary(s => s.Key, s => s.Slot);
        }

        /// <summary>
        /// Sets an action bar slot the player chose (ActionManager sub 2): checked, saved, and confirmed to the client
        /// with ActionManagerUpdateSlot. An item slot must name one of the player's own items.
        /// </summary>
        public void SetActionSlot(uint bar, uint slot, uint type, ulong value)
        {
            bool valid = bar < ActionBarCount && slot < ActionSlotsPerBar && type switch
            {
                ActionSlotEmpty => true,
                ActionSlotItemDefinition => Game.Inventory.ItemDefinitionManager.Get((uint)value) != null,
                ActionSlotItem => Inventory.Get(value) != null,
                _ => false
            };
            if (!valid)
            {
                log.Debug($"{CharacterName} asked for action slot {bar}/{slot} = {type}:{value:X}, refused.");
                return;
            }
            lock (actionSlotsLock)
                if (actionSlots != null)
                    actionSlots[(bar, slot)] = (type, value);
            ulong characterId = currentCharacter.Id;
            DatabaseManager.Instance.CharacterDatabase.Save(c =>
            {
                var existing = c.CharacterActionSlot.Find(characterId, bar, slot);
                if (existing == null)
                    c.CharacterActionSlot.Add(new CharacterActionSlotModel
                    {
                        CharacterId = characterId, BarId = bar, SlotId = slot, Type = type, Value = value
                    });
                else
                {
                    existing.Type = type;
                    existing.Value = value;
                }
            });
            EnqueueMessage(new ActionManagerUpdateSlot { BarId = bar, SlotId = slot, Type = type, Value = value });
        }

        /// <summary>
        /// Puts an item on the action bar the way the 0.589 client did itself for a right-clicked tool (FUN_1433a0b80).
        /// The 0.695 copy of that routine (FUN_14344e300) is never called: re/tools/refscan.py finds no call, jump or
        /// pointer to it, and the 0.695 right-click sends only the equip (TransferItem bag → main hand), so the slot
        /// comes from the server. Same rule as the client's: nothing when a slot already holds the item (by guid, or by
        /// definition: a type-1 slot of a non-stacking item, which the client's bar builder FUN_14344daa0 shows as a guid
        /// slot of the player's copy — equipped first, then the bag, FUN_142f389f0 — when the definition's stack size
        /// (+0xe4) is 1, and that guid is what the routine compares), else the first empty slot, bar by bar, slot by slot.
        /// The client preferred its active bar (+0x78); the server is never told which bar is shown (Ui.SetActiveActionBar
        /// FUN_1435c8290 → FUN_14344eaa0 only stores +0x78; hotkey use FUN_14344e200 / FUN_14344c420 and every 0x9A request
        /// name their bar themselves), so bar 0 comes first — the bar every client shows until the player switches
        /// (ActionBar.lua OnWindowLoaded: SelectedBar 0) (trace/0695/phaseE2-building.md L193). The slot is saved and sent as
        /// a slot the player set (<see cref="SetActionSlot"/>; the client applies an unrequested ActionManagerUpdateSlot
        /// at once, FUN_142cd9040 → FUN_143458f00). Returns whether the item is on a bar afterwards.
        /// </summary>
        public bool PlaceOnActionBar(CharacterItemModel item)
        {
            if (item == null || Inventory?.Get(item.Id) == null)
                return false;
            (uint Bar, uint Slot)? free = null;
            // FUN_14344daa0 binds a definition slot to the player's item only for definitions that don't stack (+0xe4 == 1).
            bool bindsDefinition = Game.Inventory.ItemDefinitionManager.Get(item.ItemDefinitionId)?.MaxStack is null or <= 1;
            lock (actionSlotsLock)
            {
                if (actionSlots == null)
                    return false;
                for (uint bar = 0; bar < ActionBarCount; bar++)
                    for (uint slot = 0; slot < ActionSlotsPerBar; slot++)
                    {
                        (uint type, ulong value) = actionSlots.TryGetValue((bar, slot), out var held) ? held : (ActionSlotEmpty, 0ul);
                        if ((type == ActionSlotItem && value == item.Id) || (type == ActionSlotItemDefinition && value == item.ItemDefinitionId && bindsDefinition))
                            return true;
                        if (type == ActionSlotEmpty && free == null)
                            free = (bar, slot);
                    }
            }
            if (free == null)
            {
                log.Debug($"{CharacterName}: no free action slot for item {item.ItemDefinitionId}.");
                return false;
            }
            SetActionSlot(free.Value.Bar, free.Value.Slot, ActionSlotItem, item.Id);
            log.Debug($"{CharacterName}: item {item.ItemDefinitionId} put on action slot {free.Value.Bar}/{free.Value.Slot}.");
            return true;
        }

        /// <summary>Drops the saved choice for one slot (or every slot) so it shows its default again.</summary>
        public void ForgetActionSlots(uint? bar = null, uint? slot = null)
        {
            // The client already put the slot(s) back to the default (FUN_14339f280 / FUN_14339f180): so does our copy.
            lock (actionSlotsLock)
                if (actionSlots != null)
                    foreach ((uint Bar, uint Slot) key in actionSlots.Keys.ToList())
                        if (bar == null || (key.Bar == bar && key.Slot == slot))
                            actionSlots[key] = DefaultActionSlot(key.Bar, key.Slot);
            ulong characterId = currentCharacter.Id;
            DatabaseManager.Instance.CharacterDatabase.Save(c => c.CharacterActionSlot.RemoveRange(c.CharacterActionSlot.Where(s =>
                s.CharacterId == characterId && (bar == null || (s.BarId == bar && s.SlotId == slot)))));
            log.Debug($"{CharacterName} reset action slot{(bar == null ? "s" : $" {bar}/{slot}")} to default.");
        }

        /// <summary>
        /// The action bars for SendSelfToClient: each slot's saved choice (an item slot only while the item is still
        /// owned), else its ActionSlotDefinitions default. What is sent becomes the server's copy (<see cref="actionSlots"/>).
        /// </summary>
        private List<SendSelfToClient.Hotbar> BuildHotbars()
        {
            const uint barCount = ActionBarCount;
            const uint slotsPerBar = ActionSlotsPerBar;

            var chosen = new Dictionary<(uint, uint), CharacterActionSlotModel>();
            foreach (CharacterActionSlotModel saved in DatabaseManager.Instance.CharacterDatabase.GetActionSlots(currentCharacter.Id))
                chosen[(saved.BarId, saved.SlotId)] = saved;
            var defaults = GameTableManager.Instance.ActionSlotDefinitions.Entries.ToDictionary(e => (e.BarId, e.SlotId));
            var hotbars = new List<SendSelfToClient.Hotbar>();
            var sent = new List<((uint, uint), (uint, ulong))>();
            for (uint bar = 0; bar < barCount; bar++)
            {
                var hotbar = new SendSelfToClient.Hotbar { HotbarId = bar };
                for (uint slot = 0; slot < slotsPerBar; slot++)
                {
                    defaults.TryGetValue((bar, slot), out var entry);
                    uint type;
                    ulong value;
                    if (chosen.TryGetValue((bar, slot), out CharacterActionSlotModel mine)
                        && (mine.Type != ActionSlotItem || Inventory.Get(mine.Value) != null))
                        (type, value) = (mine.Type, mine.Value);
                    else
                    {
                        if (mine != null)
                            log.Debug($"{CharacterName}: saved action slot {bar}/{slot} names item {mine.Value:X}, which is gone; using the default.");
                        (type, value) = (entry?.DefaultType ?? 0, entry?.DefaultValue ?? 0);
                    }
                    hotbar.HotbarButtons.Add(new SendSelfToClient.Hotbar.HotbarButton
                    {
                        HotbarId = bar, SlotId = slot, ButtonType = type, ItemId = value
                    });
                    sent.Add(((bar, slot), (type, value)));
                }
                hotbars.Add(hotbar);
            }
            SetActionSlotsSent(sent);
            return hotbars;
        }

        /// <summary>
        /// Builds SendSelfToClient and queues it; with <paramref name="send"/> false only sets what it fixes for the
        /// session (player guid, model, profile) — a re-attached client still has its SendSelf.
        /// </summary>
        public void BuildSendToSelfPacket(bool send = true)
        {
            // TODO: Replace all this junk below with an actual Player entity.
            uint GetModelForGenderFromProfile(ProfileDefinitionEntry profile, Gender gender)
            {
                switch (gender)
                {
                    case Gender.Male:
                        return profile.MaleModelId;
                    case Gender.Female:
                        return profile.FemaleModelId;
                    default:
                        return 252u;
                }
            }

            var Character = currentCharacter;
            if (Character == null)
            {
                // TODO: Force Disconnect, not just drop the session.
                OnDisconnect();
                return;
            }

            var ProfileData = GameTableManager.Instance.ProfileDefinitions.GetEntry(Character.ProfileTypeId);

            var message = new SendSelfToClient();
            message.Bio = Character.Bio ?? "";
            // The client's admin-player flag (0.695 player +0x7b0d8; trace/architecture/admin-monitor.md "Admin player"):
            // unlocks its admin slash commands (/goto, /hideme, /runspeed, /gametime, /prop...), the Demo input action and
            // the admin options of the voxel paste and world map, and stops it sending HexPermissions (0x1E / 0x34).
            message.IsAdminMaybe = Game.Admin.IsAdmin(this);
            // Membership type (player +0xfc: 0 none, 1 trial, 2 member; Lua Ui.IsMember / IsTrialMember), the account's
            // (Game/Membership, TASKS 3.12). The nag-screen flag stays off: nothing on this server nags.
            message.MembershipStatus = Game.Membership.SendSelfType(this);
            // Unique per player so other clients can tell characters apart.
            message.Guid = Character.Id;
            message.CharacterId = Character.Id;
            message.TransientId = TransientId;
            // Clock and /played statistics: the client shows creation date, login count and played time (its
            // CharacterInfo lines) and keeps the server clock to convert server timestamps.
            message.ServerTime = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            // createTime is the database's current_timestamp(), i.e. the server's local time.
            message.CreationDate = (ulong)new DateTimeOffset(DateTime.SpecifyKind(Character.CreateTime, DateTimeKind.Local)).ToUnixTimeSeconds();
            message.LoginCount = Character.LoginCount + 1;   // this login: counted when the player enters the world (MarkOnline)
            message.PlayedMinutes = Character.PlayedMinutes;
            message.Coin = Inventory.CountOf(Game.Inventory.Inventory.CurrencyItem);
            message.Titles = Game.Titles.SelfData(Character.Id);
            message.InventoryFilters = Game.InventoryFilters.SelfData(InventoryFilters);
            // First-time help pages: the client's manager on (unless the player turned it off) and the ones already seen.
            message.SendFirstTimeEvents = Game.FirstTimeEvents.Enabled(Character.Id);
            message.FirstTimeEventsSeen = Game.FirstTimeEvents.Seen(Character.Id);
            // Grief points / damage points / level (player +0x76920; Game/Grief): the client's lock checks read the level.
            message.Grief = Game.Grief.SelfData(this);
            // The seed of the client's per-cast hash (L189): this session's own, like live's non-zero value.
            message.CastKeySeed = CastKeySeed;
            if (Character.PositionX != 0f || Character.PositionY != 0f || Character.PositionZ != 0f)
            {
                // Never log in inside a tree that grew where the character was saved.
                Vector3 saved = new(Character.PositionX, Character.PositionY, Character.PositionZ);
                Vector3 clear = Game.Gathering.ClearOfTrees(saved, Character.ZoneId);
                if (clear != saved)
                {
                    log.Info($"{Character.Name} logs in inside a tree; moved {Vector3.Distance(saved, clear):0.0} m.");
                    (Character.PositionX, Character.PositionY, Character.PositionZ) = (clear.X, clear.Y, clear.Z);
                }
                message.Position = new Vector4(Character.PositionX, Character.PositionY, Character.PositionZ, 1f);
            }
            if (Character.RotationX != 0f || Character.RotationY != 0f || Character.RotationZ != 0f || Character.RotationW != 0f)
                message.Rotation = new Vector4(Character.RotationX, Character.RotationY, Character.RotationZ, Character.RotationW);
            message.AccountName = Account.Username;
            message.GenderMaybe = (Gender)Character.Gender;

            message.SpeechSetId = Character.Race;
            message.ModelId = GetModelForGenderFromProfile(ProfileData, (Gender)Character.Gender);
            modelId = message.ModelId;
            //message.FacialAnimReceiver = true;
            // Client +0x287: claim permission checks always pass (FUN_142d15900).
            message.BypassClaimPermissions = ConfigurationManager<WorldServerConfiguration>.Instance.Config.FreeBuild;
            FreeBuildAtLogin = message.BypassClaimPermissions;

            message.Model = new Shared.Network.Message.Model.Shared.CharacterModelInfo
            {
                SkinId = Character.SkinTint,
                Race   = Character.Race,
                Gender = Character.Gender
            };
            foreach (var customisation in Character.Customisation)
                message.Model.Customisations.Add((customisation.Slot, customisation.Option, customisation.Tint));
            modelInfo = message.Model;
            message.Identity.FirstName = Character.Name;
            
            // The profile record as live sent it for profile 46 (SendSelfToClient.Profile; live 2.8 / 1.4 / 2.75 / 1 / 0.4 /
            // 15 and 3 / 3 / 3): until 2026-10-18 the floats went out in the table's column order (line of sight in the
            // crouch-camera slot +0x74, which the client reads, capsule radius in the line-of-sight slot, crouch height in
            // the capsule slot) and CHAR_SIZE / ARMOR / FEET one slot late (+0x9c / +0xa0 / +0x88), so the footstep and
            // foley sounds used char size 1 and feet = the armor value. NoJump / NoStrafe are the record's bits 0x08 / 0x04,
            // which act as those columns do (FUN_143325480, FUN_143010260); the columns are 0 for every player profile.
            message.Profiles.Add(new SendSelfToClient.Profile
            {
                ProfileId = ProfileData.Id,
                NameId = new Shared.Game.LandmarkText(ProfileData.NameId),
                ProfileType = ProfileData.ProfileType,
                FactionId = (byte)ProfileData.FactionId,
                NoJump = ProfileData.NoJump,
                NoStrafe = ProfileData.NoStrafe,
                StandCameraHeight = ProfileData.StandCameraHeight,
                CrouchCameraHeight = ProfileData.CrouchCameraHeight,
                LineOfSightHeight = ProfileData.LineOfSightHeight,
                UiModelCameraId = ProfileData.UiModelCameraId,
                CapsuleRadius = ProfileData.CapsuleRadius,
                Mass = ProfileData.Mass,
                CharSize = ProfileData.CharSize,
                Feet = ProfileData.Feet,
                Armor = ProfileData.Armor,
            });
            message.CurrentProfile = ProfileData.Id;
            profileId = ProfileData.Id;
            message.ClassProgressions.Add((ProfileData.Id, ProfileData.Id, 0));
            // LastPlayedProfileByType stays empty, as live sent it (SendSelfToClient.LastPlayedProfileByType).
            message.Loadouts[0].Record.ProfileId = ProfileData.Id;


            foreach (CharacterItemModel item in Inventory.Items)
                message.Inventory.Items.Add(Game.Inventory.Inventory.ToPacket(item));
            // The capture declares the currency container with room for nothing; Lumens go there.
            if (message.Inventory.Containers.Count > Game.Inventory.Inventory.CurrencyContainer)
                message.Inventory.Containers[(int)Game.Inventory.Inventory.CurrencyContainer] = (1, false);
            // Bag and vault sizes, raised by storage expansions.
            message.Inventory.Containers[(int)Game.Inventory.Inventory.BagContainer] = (Inventory.BagSize, false);
            message.Inventory.Containers[(int)Game.ItemUses.VaultContainer] = (Game.ItemUses.VaultSizeOf(Character.Id), false);

            message.Hotbars.AddRange(BuildHotbars());
            message.KnownPropIds.AddRange(Game.Unlocks.Values(Character.Id, Game.Unlocks.PropTaught));
            // Crimson Parlor knowledge (0.695 reader FUN_142c19bd0 → ClientKnownCharCustomizationManager at player
            // +0x7ae28): unlocked CharacterCreateOptions ids into set A (+0x7ae30), ArtTint ids into set B (+0x7ae48).
            // Without them the 0.695 Parlor hides every non-default option / tint (trace/0695/leads-L39.md, Game/Unlockers.cs).
            message.CustomizationKnowledge.AddRange(Game.Unlockers.KnownOptions(Character.Id));
            message.CustomizationKnowledge4.AddRange(Game.Unlockers.KnownTints(Character.Id));
            // Known recipes: every default-learned one plus those learned from recipe items. Records go out for those
            // and for recipes of recipe items in the bag, so learning one this session shows it straight away.
            foreach (var recipe in GameTableManager.Instance.Recipes.Entries)
                if (recipe.DefaultLearned || LearnedRecipes.Contains(recipe.Id))
                    message.Recipes.KnownRecipeIds.Add(recipe.Id);
            // Lumen Station unlocks the character already owns: the client leaves them out of the station's list.
            HiddenRecipes = Game.RecipeBook.HiddenUnlocks(this);
            // Bandwidth (audit-bandwidth-2026-10-18 R1): the full record (~124 B) of an owned unlock is never listed or
            // started by the client, so it is left out; its id stays in the known list (KnownRecipeIds) and the hidden list
            // (4 B each), which still guard the client's own start paths. Known ids without a record are normal (the
            // log shows 1,364 known vs 1,302 records) [P]. Up to ~72 KB per login for a player owning all 581 unlocks.
            // Owned unlocks never become unowned in play (taught props stay, expansions only grow), so no 0x20 unhide
            // should need the record mid-session; RecipeBook.RefreshHidden logs it if one ever does.
            message.Recipes.RecipeList.AddRange(Game.CraftingStations.BuildRecipeRecords(
                Game.RecipeBook.RecordsFor(this, message.Recipes.KnownRecipeIds).Where(r => !HiddenRecipes.Contains(r)).ToList()));
            RecipeRecordsWithheld = new HashSet<uint>(HiddenRecipes);
            message.Recipes.HiddenRecipeIds.AddRange(HiddenRecipes.OrderBy(r => r));
            var recipeBytes = new List<byte>();
            message.Recipes.Write(new GamePacketWriter(recipeBytes));
            log.Info($"SendSelf recipes: {message.Recipes.KnownRecipeIds.Count} known, {message.Recipes.HiddenRecipeIds.Count} hidden, {message.Recipes.RecipeList.Count} records, {recipeBytes.Count} bytes.");

            if (send)
                EnqueueMessage(message);

            PlayerGuid = message.Guid;
        }
        #endregion

        #region Packet Management
        // Other players' movement waiting to be sent, keyed by (transient id, update type): only the newest of each is
        // kept, so a burst of updates between ticks costs one packet per kind; sent in body-time order per mover (L179).
        private readonly Dictionary<(uint, byte), byte[]> pendingMovement = new();
        private readonly MovementTiming movementIn = new("received from");
        private readonly MovementTiming movementOut = new("sent to");

        public void QueueMovement(uint transientId, byte[] movement)
        {
            lock (pendingMovement)
                pendingMovement[(transientId, movement[0])] = movement;
        }

        private readonly System.Collections.Concurrent.ConcurrentQueue<byte[]> rawPackets = new();

        /// <summary>
        /// Queues a raw zone packet (opcode bytes included), for developer experiments. Sent exactly as given: not
        /// translated for 0.695 clients.
        /// </summary>
        public void SendRaw(byte[] packet) => rawPackets.Enqueue(packet);

        private readonly object eventLock = new();
        private readonly Queue<IEvent> events = new();

        /// <summary>Queues work to run on this session's next <see cref="Flush"/> once it can (e.g. after a database task).</summary>
        public void EnqueueEvent(IEvent gameEvent)
        {
            if (gameEvent == null)
                return;
            lock (eventLock)
                events.Enqueue(gameEvent);
        }

        private void RunEvents()
        {
            lock (eventLock)
            {
                while (events.TryPeek(out IEvent next))
                {
                    if (!next.CanExecute())
                        break;
                    events.Dequeue();
                    try
                    {
                        next.Execute();
                    }
                    catch (Exception exception)
                    {
                        log.Error(exception);
                    }
                }
            }
        }

        /// <summary>
        /// Once per tick (the gateway's update, while both halves share this process): runs queued events, sends the
        /// queued packets to the client (raw dev packets, the newest movement of each other player, then the queue) and
        /// saves the position now and then.
        /// </summary>
        public void Flush()
        {
            RunEvents();
            if (reattachRezoneDue is DateTime due && DateTime.UtcNow >= due)
                ReattachRezone();
            bool canSend = Link.CanSend && !Disconnected;
            while (canSend && rawPackets.TryDequeue(out byte[] raw))
                Link.Send(raw);

            if (canSend)
            {
                lock (pendingMovement)
                {
                    // Each mover's bodies oldest first (MovementFanout.InTimeOrder, L179).
                    foreach (((uint transientId, _), byte[] movement) in MovementFanout.InTimeOrder(pendingMovement, m => m))
                        EnqueueMessage(new PlayerUpdatePosition { TransientId = transientId, Movement = movement });
                    pendingMovement.Clear();
                }
            }

            while (canSend && outgoingPackets.TryDequeue(out ZonePacket packet))
                SendPacket(packet);

            if (sinceLastPositionSave.Elapsed >= PositionSaveInterval)
            {
                if (positionDirty)
                    SavePosition();   // restarts the interval
                else
                    sinceLastPositionSave.Restart();
                SavePlayedTime();
            }
        }

        /// <summary>Ends the connection (logout, kick, duplicate login): the gateway closes it and calls <see cref="LinkClosed"/>.</summary>
        public void OnDisconnect()
        {
            if (Link.Closed)
                LinkClosed();
            else
                Link.Close();
        }

        /// <summary>The connection is gone (ConnectionClosed): the player leaves the world and is saved. Runs once.</summary>
        public void LinkClosed()
        {
            if (closed)
                return;
            closed = true;
            ZoneSessions.Remove(this);
            Game.PlayerManager.Leave(this);
            SavePosition();
            SavePlayedTime(leaving: true);
        }

        private void SavePosition()
        {
            if (!positionDirty || currentCharacter == null || lastPosition is not Vector3 position)
                return;
            Quaternion rotation = lastRotation ?? new Quaternion(currentCharacter.RotationX, currentCharacter.RotationY,
                currentCharacter.RotationZ, currentCharacter.RotationW);

            positionDirty = false;
            sinceLastPositionSave.Restart();

            currentCharacter.PositionX = position.X;
            currentCharacter.PositionY = position.Y;
            currentCharacter.PositionZ = position.Z;
            currentCharacter.RotationX = rotation.X;
            currentCharacter.RotationY = rotation.Y;
            currentCharacter.RotationZ = rotation.Z;
            currentCharacter.RotationW = rotation.W;

            ulong characterId = currentCharacter.Id;
            DatabaseManager.Instance.CharacterDatabase.Save(c =>
            {
                var model = new CharacterModel { Id = characterId };
                c.Character.Attach(model);
                model.PositionX = position.X;
                model.PositionY = position.Y;
                model.PositionZ = position.Z;
                model.RotationX = rotation.X;
                model.RotationY = rotation.Y;
                model.RotationZ = rotation.Z;
                model.RotationW = rotation.W;
                c.MarkModified(model, nameof(model.PositionX), nameof(model.PositionY), nameof(model.PositionZ),
                    nameof(model.RotationX), nameof(model.RotationY), nameof(model.RotationZ), nameof(model.RotationW));
            }).ContinueWith(t => log.Error(t.Exception, $"Failed to save position for character {characterId}."),
                System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
        }

        private void HandleZonePacket(ZonePacket packet)
        {
            // Telemetry and external-service reports (Lua metrics, WallOfData, social share, …): recorded, never answered.
            if (Game.Telemetry.IsTelemetry(packet.Opcode))
            {
                Game.WorldChannel.WorldChannelServices.WorldChannel.ClientTelemetry(this, packet.Opcode, packet.Data);
                // CommerceSessionRequest 0x77: the client asks for a session ticket for a Spice REST payment request (fund
                // wallet, credit cards, redeem code …). Answer CommerceSessionResponse 0x78 {bool ok = false, string ""}
                // (reader FUN_142c4f100): the client then fails that request at once ("Failed to request commerce
                // session.") instead of waiting forever. Never a ticket: nothing is paid with real money here
                // (trace/0695/agent-leads2-d1.md L62).
                if (packet.Opcode == ZoneMessageOpcode.CommerceSessionRequest)
                    EnqueueMessage(ZoneMessageOpcode.CommerceSessionResponse, new byte[] { 0, 0, 0, 0, 0 });
                return;
            }
            IReadable message = ZoneMessageManager.Instance.GetZoneMessage(packet.Opcode, ClientProtocol);
            if (message == null)
            {
                log.Warn($"Received Zone packet that was unreadable {packet.Opcode}(0x{packet.Opcode:X}) : {BitConverter.ToString(packet.Data)}");
                return;
            }

            log.Debug($"Received Zone packet {packet.Opcode}(0x{packet.Opcode:X})");

            var reader = new GamePacketReader(packet.Data);
            try
            {
                message.Read(reader);
            }
            catch (Exception exception)
            {
                // A malformed packet is dropped; it must not end this tick's network update for every session.
                log.Warn($"Unreadable Zone packet {packet.Opcode}(0x{packet.Opcode:X}) from {CharacterName}: {exception.Message}");
                return;
            }
            if (reader.BytesRemaining > 0)
                log.Warn($"Failed to read entire contents of Zone packet {packet.Opcode} ({reader.BytesRemaining} bytes remaining)");

            ZoneMessageHandlerDelegate handlerInfo = ZoneMessageManager.Instance.GetZoneMessageHandler(packet.Opcode, ClientProtocol);
            if (handlerInfo == null)
            {
                Network.OpcodeStats.Unanswered($"no handler {packet.Opcode}", 0);
                log.Warn($"Unhandled Zone packet {packet.Opcode}(0x{packet.Opcode:X}).");
                return;
            }

            try
            {
                handlerInfo.Invoke(this, message);
            }
            catch (InvalidPacketValueException exception)
            {
                log.Error(exception);
                OnDisconnect();
            }
            catch (Exception exception)
            {
                log.Error(exception);
            }
        }

        /// <summary>
        /// The wire bytes of an opcode: the enum value's bytes without leading zeros (ChatBase 0x1300 → 13 00), except
        /// PlayerUpdate 0x26, whose sub-opcode the 0.695 client reads as a u32 [P]: header writer FUN_142c13350 writes u16
        /// base + 4 bytes (0.589 FUN_142b79bb0 wrote 1), the S→C dispatcher FUN_142cdfa60 reads <c>*(int *)</c> after the
        /// u16 (0.589 FUN_142c45fa0 read a <c>char</c>); trace/0695/packets-695.md "Changed layouts" A1. The enum keeps a
        /// PlayerUpdate sub as one byte (0x2600ss "…Wire" or 0x2600ss00), so 26 00 ss becomes 26 00 ss 00 00 00 here,
        /// and a 4-byte value (0x2600ss00) gives the same 6 bytes: nothing is written after the u32 sub. Before the
        /// wire audit (2026-10-17, trace/0695/audit-wire-a.md M3) the 4th enum byte was copied after it, which shifted
        /// CustomizationChange's guid by one byte.
        /// </summary>
        internal static byte[] GetOpcodeBytes(ZoneMessageOpcode? opcode)
        {
            byte[] bytes = EnumBytes(opcode);
            if (bytes.Length >= 3 && bytes[0] == PlayerUpdateBase0 && bytes[1] == 0x00)
            {
                byte[] wide = new byte[bytes.Length + 3];
                wide[0] = PlayerUpdateBase0;
                wide[2] = bytes[2];                     // u32 sub, little endian: ss 00 00 00
                return wide[..6];
            }
            return bytes;
        }

        /// <summary>PlayerUpdate's base (wire bytes 26 00), the one family whose sub width the opcode codec knows.</summary>
        private const byte PlayerUpdateBase0 = 0x26;

        /// <summary>
        /// The enum value's bytes, big-endian, leading zeros dropped (the opcode as the enum stores it), but never fewer than
        /// the u16 base: DebugDrawBase 0x0001 is wire bytes 00 01, and dropping its zero left a one-byte opcode the client
        /// cannot read (GetOpcode has no one-byte form either).
        /// </summary>
        private static byte[] EnumBytes(ZoneMessageOpcode? opcode)
        {
            var hexOpcode = $"{opcode:X}";
            // Two-byte bases stored as their wire bytes (InstancedGameBase 0x0D01) print with an odd digit count.
            if (hexOpcode.Length % 2 != 0)
                hexOpcode = "0" + hexOpcode;
            hexOpcode = hexOpcode.PadLeft(4, '0');
            int position = 0;
            List<byte> hexBytes = new();
            while (position < hexOpcode.Length)
            {
                var opByte = hexOpcode.Substring(position, 2);
                byte num = byte.Parse(opByte, System.Globalization.NumberStyles.AllowHexSpecifier);

                if (hexBytes.Count == 0 && num == 0 && hexOpcode.Length - position > 4)
                {
                    position += 2;
                    continue;
                }

                hexBytes.Add(num);
                position += 2;
            }

            return hexBytes.ToArray();
        }

        /// <summary>
        /// The opcode at the start of a zone packet, matched against the enum as 2, 3 or 4 wire bytes read big-endian
        /// (bases are u16 on the wire: ChatBase 0x1300 = bytes 13 00, KnownPropBase 0x1501 = bytes 15 01).
        /// There is no one-byte form: it used to be tried first and, once DebugDrawBase (wire bytes 00 01) was given
        /// the value 0x0001, it caught every packet starting with byte 01 (Notifications, bytes 01 00: the answers to
        /// group / guild popups were dropped as unreadable DebugDraw packets).
        /// PlayerUpdate 0x26 is read the 0.695 way (<see cref="GetOpcodeBytes"/>): u16 base, u32 sub, then the body; the
        /// sub is looked up as the enum's 0x2600ss, else 0x2600ss00, and the body starts after the 6 header bytes.
        /// </summary>
        public static (ZoneMessageOpcode?, int) GetOpcode(byte[] data)
        {
            if (data.Length >= 2 && data[0] == PlayerUpdateBase0 && data[1] == 0x00)
            {
                // Every PlayerUpdate sub of both clients fits a byte (0.695's highest is 0x80 SetPlayerBio); a larger one is
                // no packet the client builds.
                if (data.Length < 6 || data[3] != 0 || data[4] != 0 || data[5] != 0)
                    return (null, 0);
                uint sub = data[2];
                if (Enum.IsDefined(typeof(ZoneMessageOpcode), 0x260000u | sub))
                    return ((ZoneMessageOpcode)(0x260000u | sub), 6);
                if (Enum.IsDefined(typeof(ZoneMessageOpcode), 0x26000000u | (sub << 8)))
                    return ((ZoneMessageOpcode)(0x26000000u | (sub << 8)), 6);
                return (null, 0);
            }
            if (data.Length > 1)
            {
                uint opcode = (uint)((data[0] << 8) + data[1]);
                if (Enum.IsDefined(typeof(ZoneMessageOpcode), opcode))
                    return ((ZoneMessageOpcode)opcode, 2);

                if (data.Length > 2)
                {
                    opcode = (uint)((data[0] << 16) + (data[1] << 8) + data[2]);
                    if (Enum.IsDefined(typeof(ZoneMessageOpcode), opcode))
                        return ((ZoneMessageOpcode)opcode, 3);

                    if (data.Length > 3)
                    {
                        opcode = (uint)((data[0] << 24) + (data[1] << 16) + (data[2] << 8) + data[3]);
                        if (Enum.IsDefined(typeof(ZoneMessageOpcode), opcode))
                            return ((ZoneMessageOpcode)opcode, 4);
                    }
                }
            }

            return (null, 0);
        }

        public void EnqueueMessage(IWritable message)
        {
            if (!ZoneMessageManager.Instance.GetOpcode(message, out ZoneMessageOpcode opcode))
            {
                log.Warn("Failed to send message with no attribute!");
                return;
            }

            outgoingPackets.Enqueue(new ZonePacket(opcode, message, ZoneMessageManager.Instance.HasPrependedSize(opcode)));
        }

        public void EnqueueMessage(ZoneMessageOpcode opcode, byte[] data)
        {
            outgoingPackets.Enqueue(new ZonePacket(opcode, data));
        }

        private uint clientClockMs;
        private long clientClockAt = -1;

        /// <summary>
        /// Notes the client's own game clock from a KeepAlive (0x63): the client sends FUN_142cd2690, its timeGetTime
        /// clock plus the server sync offset (BaseClient +0x32be0 → +0x3c), the clock its timed HUD elements compare
        /// against (the KeepAlive sender in the 0.695 main loop, part_0065.c "PacketKeepAlive") [P].
        /// </summary>
        public void NoteClientClock(uint ms)
        {
            clientClockMs = ms;
            System.Threading.Interlocked.Exchange(ref clientClockAt, Environment.TickCount64);
        }

        /// <summary>The client's game clock now (ms), from the last KeepAlive plus the time since; null before the first.</summary>
        public uint? ClientClockNow
        {
            get
            {
                long at = System.Threading.Interlocked.Read(ref clientClockAt);
                return at < 0 ? null : unchecked(clientClockMs + (uint)(Environment.TickCount64 - at));
            }
        }

        private void SendPacket(ZonePacket packet)
        {
            List<byte> data = new();
            var writer = new GamePacketWriter(data);

            writer.WriteBytes(GetOpcodeBytes(packet.Opcode));
            writer.WriteBytes(packet.Data);
            OpcodeStats.Sent(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(data));

            // Movement relays are many per second per player; logging each one costs more than sending it.
            if (packet.Opcode == ZoneMessageOpcode.PlayerUpdatePosition)
                movementOut.Mark(CharacterName);
            else
            {
                log.Debug($"Sending packet {packet.Opcode}(0x{packet.Opcode:X})");
                log.Trace($"{BitConverter.ToString(packet.Data)}");
            }

            // #region agent log — T-KIRMMIN-WIRE-TIMING (enter-SOE / PackTunnel; not UDP socket)
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (packet.Opcode == ZoneMessageOpcode.SendSelfToClient && _wireT0Ms == null)
            {
                _wireT0Ms = nowMs;
                log.Info($"WIRE-TIMING SendSelfToClient t=0 utcMs={nowMs}");
            }
            else if (_wireT0Ms != null
                && (packet.Opcode == ZoneMessageOpcode.ZoneDoneSendingInitialData
                    || packet.Opcode == ZoneMessageOpcode.TerrainCellModifiedCellManifest
                    || packet.Opcode == ZoneMessageOpcode.ActivityBase))
            {
                long sinceT0 = nowMs - _wireT0Ms.Value;
                long sinceReady = _wireClientIsReadyMs.HasValue ? nowMs - _wireClientIsReadyMs.Value : -1;
                long sinceZoneDone = _wireZoneDoneMs.HasValue ? nowMs - _wireZoneDoneMs.Value : -1;
                if (packet.Opcode == ZoneMessageOpcode.ZoneDoneSendingInitialData)
                    _wireZoneDoneMs = nowMs;
                log.Info($"WIRE-TIMING {packet.Opcode}(0x{packet.Opcode:X}) enterSoeMs={sinceT0} sinceReadyMs={sinceReady} sinceZoneDoneMs={sinceZoneDone} utcMs={nowMs} liveReadyToZoneDone=1263 liveZoneDoneToA00=7286");
            }
            // #endregion

            Link.Send(data.ToArray());
        }

        /// <summary>T-KIRMMIN-WIRE-TIMING: stamp ClientIsReady C→S relative to SendSelf t=0.</summary>
        public void MarkClientIsReadyWireTiming()
        {
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _wireClientIsReadyMs = nowMs;
            long sinceT0 = _wireT0Ms.HasValue ? nowMs - _wireT0Ms.Value : -1;
            log.Info($"WIRE-TIMING ClientIsReady(C→S) sinceSendSelfMs={sinceT0} utcMs={nowMs}");
        }

        /// <summary>
        /// A client packet from the gateway (TunnelPacketFromExternalConnection): movement on the PositionUpdate channel,
        /// otherwise a zone packet as the 0.695 client sent it, past the gateway's flood limits.
        /// </summary>
        public void FromClient(Channel channel, byte[] data)
        {
            if (Disconnected)
                return;
            Receive(channel, data);
        }

        /// <summary>
        /// A client packet that arrived before the client's own SOE Disconnect but was still waiting for the world thread
        /// when the connection ended (the in-process gateway, <see cref="ClientSession.OnRemoved"/>). The link already
        /// reads as closed, so <see cref="FromClient"/> would drop it; it is handled as long as the zone session has not
        /// left yet. The client sends its last packets and its Disconnect back to back — e.g. Exit Game saves the
        /// preference document (PreferencePacketClientXml, sender FUN_142d59850 on the Exit path, Game/Preferences.cs)
        /// right before the process exits (review 2026-10-18b #1). Nothing is sent back (Flush sends nothing once the
        /// link is closed); <see cref="Game.PlayerManager.Enter"/> refuses a closed connection, so a last movement packet
        /// can't make a ghost.
        /// </summary>
        public void FromClientBeforeClose(Channel channel, byte[] data)
        {
            if (closed)
                return;
            Receive(channel, data);
        }

        private void Receive(Channel channel, byte[] data)
        {
            if (channel == Channel.PositionUpdate)
            {
                // Movement packets aren't zone messages; only position and rotation are used so far.
                if (data.Length > 0 && data[0] == 0x00)
                    LastFullMovement = data;
                if (PositionUpdate.TryReadVelocity(data, out Vector3 velocity))
                {
                    LastVelocity = velocity;
                    LastVelocityAt = DateTime.UtcNow;
                }
                // L121: log each change of the movement state word (which bits mean jumping / falling).
                if (PositionUpdate.TryReadState(data, out uint moveState) && moveState != MovementState)
                {
                    log.Debug($"{CharacterName}: movement state 0x{MovementState:X} -> 0x{moveState:X} (type {data[0]}, vy {LastVelocity.Y:F1})");
                    // The client's own /sit (and its end by moving / jumping / RequestAnimationSelection): relayed in
                    // this movement to the other clients, which sit / stand the character by it (Game.Emotes).
                    if (((moveState ^ MovementState) & PositionUpdate.SittingState) != 0)
                        log.Info($"{CharacterName}: {((moveState & PositionUpdate.SittingState) != 0 ? "sits down" : "stands up")} (movement state bit 0x{PositionUpdate.SittingState:X}).");
                    MovementState = moveState;
                }
                Game.SafeEject.Moved(this);   // a launched player who has landed: entity state bit 26 cleared
                if (PositionUpdate.TryRead(data, out Vector3? position, out Quaternion? rotation))
                {
                    // While arriving from a teleport, positions from before the client processed it are ignored; the
                    // first one at the destination ends the arrival.
                    if (position.HasValue && IsArriving)
                    {
                        if (Vector3.Distance(position.Value, arrivalTarget) > ArrivalRadius)
                            position = null;
                        else
                            arrivingUntil = null;
                    }
                    if (position.HasValue)
                    {
                        lastPosition = position;
                        Game.Achievements.Moved(this, position.Value);
                        Game.Emotes.Moved(this, position.Value);
                        Game.EmoteHolster.Moved(this, position.Value);
                        // The first seconds after a cavern trip: a fall through the world is brought back (bug 2026-10-18).
                        Game.CavernArrivalGuard.Moved(this, position.Value);
                    }
                    if (rotation.HasValue)
                        lastRotation = rotation;
                    positionDirty = lastPosition.HasValue;
                    if (reattachRezoneDue != null && lastPosition.HasValue)
                        ReattachRezone();
                }
                movementIn.Mark(CharacterName);
                // Converted once here rather than per recipient.
                byte[] remote = PositionUpdate.ToRemoteFrame(data, RemoteFrameOffset);
                if (remote.Length > 0 && remote[0] == 0x00)
                    lastFullMovement = remote;
                if (WorldReady && lastPosition.HasValue)
                    Game.PlayerManager.Enter(this);
                Game.PlayerManager.RelayMovement(this, remote);
                return;
            }

            // WorldEditingBase 0x10C: the build cursor, sent every frame while building (the gateway lets it past its
            // flood limiter). Game.WorldEditing throttles its relay itself (100 ms per builder).
            if (data.Length >= 2 && data[0] == 0x0C && data[1] == 0x01)
            {
                OpcodeStats.Received(data);
                Game.WorldEditing.Received(this, data);
                return;
            }

            OpcodeStats.Received(data);
            (ZoneMessageOpcode? opcode, int offset) = GetOpcode(data);
            data = new Span<byte>(data, offset, data.Length - offset).ToArray();

            if (opcode == null)
            {
                // WorldEditingBase 0x10C (build cursor box / shape / selection / preview updates, several a second while
                // building): relayed to the players nearby, whose clients draw them (Game/WorldEditing.cs).
                if (data.Length >= 2 && data[0] == 0x0C && data[1] == 0x01)
                {
                    Game.WorldEditing.Received(this, data);
                    return;
                }
                // The bases 0x104-0x117 that used to be read here as raw bytes (SocialShare 0x104, HeroicMovement 0x105,
                // MarketplaceImage 0x107, ToolBase 0x117) have enum values in their wire form since b653b73, so GetOpcode
                // finds them and they go through HandleZonePacket: telemetry (Game.Telemetry), Game.HeroicMovement,
                // Game.Templates.HandleImage and Building.Tools. WarpNetwork 0x113 is WarpNetworkWire (MiscHandler).
                Network.OpcodeStats.Unanswered("raw 0x" + (data.Length >= 2 ? $"{data[1]:X2}{data[0]:X2}" : "?"), data.Length > 2 ? data[2] : -1);
                log.Warn($"Unknown Zone Packet : {BitConverter.ToString(data)}");
                return;
            }

            var packet = new ZonePacket((ZoneMessageOpcode)opcode, data);
            HandleZonePacket(packet);
        }
        #endregion
    }
}
