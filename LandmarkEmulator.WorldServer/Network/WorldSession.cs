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
using LandmarkEmulator.WorldServer.Network.Packets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Numerics;
using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Shared.Game.Events;

namespace LandmarkEmulator.WorldServer.Network
{
    public class WorldSession : GatewaySession
    {
        // Other sessions enqueue here too (spawns, movement relay), hence concurrent.
        private readonly System.Collections.Concurrent.ConcurrentQueue<ZonePacket> outgoingPackets = new();

        private byte[] sendSelfPacket;
        public byte[] TerrainManifest { get; }
        public bool ReadySent { get; set; } = false;

        /// <summary>
        /// Set once the client has finished loading the world.
        /// </summary>
        public bool WorldReady { get; set; }
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

        public WorldSession() : base(WorldServer.EncryptionKey)
        {
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

        /// <summary>The newest full (type 0) movement body this client sent (dev: a template for NPC movement).</summary>
        public byte[] LastFullMovement { get; private set; }

        /// <summary>
        /// Where the player is now (latest movement update, else the saved position).
        /// </summary>
        /// <summary>Where a re-zone (<see cref="Game.Teleport.Rezone"/>) is taking the player, until the client confirms it.</summary>
        public Vector3? RezoneTarget { get; set; }
        /// <summary>When the pending re-zone (<see cref="RezoneTarget"/>) was sent.</summary>
        public DateTime RezoneStarted { get; set; }

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

        public static readonly Vector3 RemoteFrameOffset = new(2558400f, 2000f, 2556450f);

        public string CharacterName => currentCharacter?.Name;

        public Game.Inventory.Inventory Inventory { get; private set; }

        /// <summary>Recipes learned from recipe items (default-learned ones are implied).</summary>
        public HashSet<uint> LearnedRecipes { get; private set; } = new();

        /// <summary>
        /// Gives the player items and tells the client about every stack that changed.
        /// </summary>
        public void GiveItem(uint definitionId, uint count)
        {
            foreach ((CharacterItemModel item, bool isNew) in Inventory.Add(definitionId, count))
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
            if (!Inventory.TryRemove(definitionId, count, changed))
                return false;
            foreach ((CharacterItemModel item, bool deleted) in changed)
            {
                if (deleted)
                    EnqueueMessage(new ClientUpdateItemDelete { Guid = item.Id });
                else
                    SendItem(item, replace: true);
            }
            return true;
        }

        /// <summary>
        /// Shows an item's current state on the client: ItemAdd for a new item, ItemUpdate (count and location) for
        /// one the client already has.
        /// </summary>
        public void SendItem(CharacterItemModel item, bool replace)
        {
            if (replace)
                EnqueueMessage(new ClientUpdateItemUpdate
                {
                    Guid = item.Id,
                    Count = item.Count,
                    Slot = item.Slot,
                    Container = item.Container,
                    Wear = item.Wear
                });
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
            CompositeEffect = Game.Inventory.ItemDefinitionManager.Get(item.ItemDefinitionId).AmbientEffect
        };

        /// <summary>
        /// Tells this client and everyone else in the world what changed in this character's visible equipment
        /// since <paramref name="before"/> (from <see cref="VisibleEquipment"/>). Only changed slots are sent.
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

            if (messages.Count == 0)
                return;
            // Tool stats (harvesting area = the mining ring, harvest damage, tier) follow the held tool.
            SendCalculatedStats();
            foreach (WorldSession session in Game.PlayerManager.Snapshot().Append(this).Distinct())
                foreach (IWritable message in messages)
                    session.EnqueueMessage(message);
        }

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
                Position = new Vector4(position + RemoteFrameOffset, 1f),
                Rotation = new Vector4(rotation.X, rotation.Y, rotation.Z, rotation.W)
            };
            message.Identity.FirstName = currentCharacter.Name;
            return message;
        }

        /// <summary>The character's race (CharCustomizationSlotCost RACE_ID: 1 humen, 5 large humen).</summary>
        public uint Race => currentCharacter?.Race ?? 1;

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
                Movement = lastFullMovement ?? PlayerUpdateLightweightToFullPc.EmptyMovement
            };
            message.Stats.AddRange(Game.Abilities.CalculatedStats(this));
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
        public override void OnCharacterLogin(CharacterModel model)
        {
            base.OnCharacterLogin(model);
            // A login during a shutdown (Play pressed just before the login server heard) loads as usual and is sent
            // back once in the world (Game.Shutdown.SendPlayersToCharacterSelect): a relogin session before the client
            // has the world crashes it as it starts the relogin (2026-10-14).
            currentCharacter = model;
            Game.Unlocks.Load(model.Id);
            Inventory = new Game.Inventory.Inventory(model);
            LearnedRecipes = Game.RecipeBook.LoadLearned(model.Id);
            Game.Achievements.LoadCharacter(model.Id);
            //EnqueueMessage((ZoneMessageOpcode)0x16015A04, File.ReadAllBytes("packets\\25-unknown").AsSpan().Slice(4).ToArray());
            Game.Pioneer.Settle(model);   // a brand-new character starts on its own site on a Pioneer land
            EnqueueMessage(ZoneMessageOpcode.SendZoneDetails, Game.Landscapes.ZoneDetails(model.ZoneId));

            // Setup Player, Send Packets, etc.
            EnqueueMessage(ZoneMessageOpcode.InitializationParameters, Convert.FromHexString("04-00-00-00-6C-69-76-65".Replace("-", "")));
            EnqueueMessage(ZoneMessageOpcode.ClientGameSettings, Convert.FromHexString("00-00-00-00-0A-00-00-00-00-00-00-00-00-00-00-80-3F-00-00-00-00-01-00-00-00-00-00-00-00-00-00-40-41-00-00-DC-42".Replace("-", "")));
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
        }

        /// <summary>
        /// SendZoneDetails body from a live capture: zone OpenBetaGeometry_406 (type 5), Sky.xml, continent 406 and its
        /// bitmaps, sky data. Also the source of the zone name and sky for <see cref="Game.Teleport"/>'s re-zoning.
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
        public Game.RateLimiter ChatLimiter { get; } = new(5, 1 / 1.5);
        public Game.RateLimiter ChatWarnLimiter { get; } = new(1, 1 / 10.0);

        /// <summary>
        /// Actions that are written to the database or disk (Links added or removed): bursts of 30, 10 a second — far
        /// above what the Links window sends, low enough that a modified client can't keep the database busy.
        /// </summary>
        public Game.RateLimiter StorageLimiter { get; } = new(30, 10);
        /// <summary>Contest entries (screenshots are written to disk): 2 at once, then one every 30 seconds.</summary>
        public Game.RateLimiter ContestLimiter { get; } = new(2, 1 / 30.0);
        /// <summary>Bug / build site reports (saved to disk): 2 at once, then one a minute.</summary>
        public Game.RateLimiter ReportLimiter { get; } = new(2, 1 / 60.0);

        /// <summary>
        /// Zone packets (not movement) a session may send: bursts of 300, 100 a second sustained — far above a normal
        /// client (building, a few dozen a second at most). Packets beyond it are dropped unread.
        /// </summary>
        private readonly Game.RateLimiter packetLimiter = new(300, 100);
        /// <summary>Terrain cell requests and build edits (see the flood check in the packet path).</summary>
        private readonly Game.RateLimiter terrainLimiter = new(200, 50);
        private readonly Game.RateLimiter packetWarnLimiter = new(1, 1 / 60.0);
        private long droppedPackets;

        /// <summary>When the player entered the world this session (for played time).</summary>
        private DateTime? enteredWorld;

        /// <summary>
        /// Adds the whole minutes spent in the world since the last call to the character's played minutes (on logout
        /// and with each periodic position save, so a server stop loses less than one save interval).
        /// </summary>
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

        /// <summary>Drops the saved choice for one slot (or every slot) so it shows its default again.</summary>
        public void ForgetActionSlots(uint? bar = null, uint? slot = null)
        {
            ulong characterId = currentCharacter.Id;
            DatabaseManager.Instance.CharacterDatabase.Save(c => c.CharacterActionSlot.RemoveRange(c.CharacterActionSlot.Where(s =>
                s.CharacterId == characterId && (bar == null || (s.BarId == bar && s.SlotId == slot)))));
            log.Debug($"{CharacterName} reset action slot{(bar == null ? "s" : $" {bar}/{slot}")} to default.");
        }

        private IEnumerable<SendSelfToClient.Hotbar> BuildHotbars()
        {
            const uint barCount = ActionBarCount;
            const uint slotsPerBar = ActionSlotsPerBar;

            var chosen = new Dictionary<(uint, uint), CharacterActionSlotModel>();
            foreach (CharacterActionSlotModel saved in DatabaseManager.Instance.CharacterDatabase.GetActionSlots(currentCharacter.Id))
                chosen[(saved.BarId, saved.SlotId)] = saved;
            var defaults = GameTableManager.Instance.ActionSlotDefinitions.Entries.ToDictionary(e => (e.BarId, e.SlotId));
            for (uint bar = 0; bar < barCount; bar++)
            {
                var hotbar = new SendSelfToClient.Hotbar { HotbarId = bar };
                for (uint slot = 0; slot < slotsPerBar; slot++)
                {
                    defaults.TryGetValue((bar, slot), out var entry);
                    if (chosen.TryGetValue((bar, slot), out CharacterActionSlotModel mine)
                        && (mine.Type != ActionSlotItem || Inventory.Get(mine.Value) != null))
                        hotbar.HotbarButtons.Add(new SendSelfToClient.Hotbar.HotbarButton
                        {
                            HotbarId = bar, SlotId = slot, ButtonType = mine.Type, ItemId = mine.Value
                        });
                    else
                    {
                        if (mine != null)
                            log.Debug($"{CharacterName}: saved action slot {bar}/{slot} names item {mine.Value:X}, which is gone; using the default.");
                        hotbar.HotbarButtons.Add(new SendSelfToClient.Hotbar.HotbarButton
                        {
                            HotbarId   = bar,
                            SlotId     = slot,
                            ButtonType = entry?.DefaultType ?? 0,
                            ItemId     = entry?.DefaultValue ?? 0
                        });
                    }
                }
                yield return hotbar;
            }
        }

        public void BuildSendToSelfPacket()
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
            message.Use589Layout = ZoneOpcodeTranslator.Uses589Opcodes(ClientBuild);
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
            // First-time help pages: the client's manager on (unless the player turned it off) and the ones already seen.
            message.SendFirstTimeEvents = Game.FirstTimeEvents.Enabled(Character.Id);
            message.FirstTimeEventsSeen = Game.FirstTimeEvents.Seen(Character.Id);
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
            
            message.Profiles.Add(new SendSelfToClient.Profile
            {
                Unknown0 = ProfileData.Id,
                NameId = new Shared.Game.LandmarkText(ProfileData.NameId),
                Unknown3 = ProfileData.ProfileType,
                Unknown15 = true,
                Unknown19 = ProfileData.StandCameraHeight, //1077097267,
                Unknown20 = ProfileData.LineOfSightHeight, //1068708659,
                Unknown21 = ProfileData.CapsuleRadius, //1076887552,
                Unknown22 = 1,
                Unknown23 = ProfileData.CrouchCameraHeight, //1053609165,
                Unknown24 = ProfileData.Mass, //1097859072,
                Unknown29 = ProfileData.CharSize, //3,
                Unknown30 = ProfileData.Armor, //3,
                Unknown31 = ProfileData.Feet, //3
            });
            message.CurrentProfile = ProfileData.Id;
            profileId = ProfileData.Id;
            message.ClassProgressions.Add((ProfileData.Id, ProfileData.Id, 0));
            message.Unknown30.Add((ProfileData.ProfileType, ProfileData.Id));
            message.Loadouts[0].Unknown1.Unknown1 = ProfileData.Id;


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
            // Known recipes: every default-learned one plus those learned from recipe items. Records go out for those
            // and for recipes of recipe items in the bag, so learning one this session shows it straight away.
            foreach (var recipe in GameTableManager.Instance.Recipes.Entries)
                if (recipe.DefaultLearned || LearnedRecipes.Contains(recipe.Id))
                    message.Recipes.Unknown0.Add(recipe.Id);
            message.Recipes.RecipeList.AddRange(Game.CraftingStations.BuildRecipeRecords(
                Game.RecipeBook.RecordsFor(this, message.Recipes.Unknown0).ToList()));
            var recipeBytes = new List<byte>();
            message.Recipes.Write(new GamePacketWriter(recipeBytes));
            log.Info($"SendSelf recipes: {message.Recipes.Unknown0.Count} known, {message.Recipes.RecipeList.Count} records, {recipeBytes.Count} bytes.");

            EnqueueMessage(message);

            PlayerGuid = message.Guid;
        }
        #endregion

        #region Packet Management
        // Other players' movement waiting to be sent, keyed by (transient id, update type): only the newest of each is
        // kept, so a burst of updates between ticks costs one packet per kind.
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
        /// Queues a raw zone packet (opcode bytes included), for developer experiments.
        /// </summary>
        public void SendRaw(byte[] packet) => rawPackets.Enqueue(packet);

        protected override void OnProcessZonePackets()
        {
            while (CanProcessPackets && rawPackets.TryDequeue(out byte[] raw))
                PackTunnelPacket(raw);

            if (CanProcessPackets)
            {
                lock (pendingMovement)
                {
                    foreach (((uint transientId, _), byte[] movement) in pendingMovement)
                        EnqueueMessage(new PlayerUpdatePosition { TransientId = transientId, Movement = movement });
                    pendingMovement.Clear();
                }
            }

            while (CanProcessPackets && outgoingPackets.TryDequeue(out ZonePacket packet))
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

        public override void OnDisconnect()
        {
            Game.PlayerManager.Leave(this);
            SavePosition();
            SavePlayedTime(leaving: true);
            base.OnDisconnect();
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

        private byte[] GetOpcodeBytes(ZoneMessageOpcode? opcode)
        {
            var hexOpcode = $"{opcode:X}";
            // Two-byte bases stored as their wire bytes (InstancedGameBase 0x0D01) print with an odd digit count.
            if (hexOpcode.Length % 2 != 0)
                hexOpcode = "0" + hexOpcode;
            int position = 0;
            List<byte> hexBytes = new();
            while (position < hexOpcode.Length)
            {
                var opByte = hexOpcode.Substring(position, 2);
                byte num = byte.Parse(opByte, System.Globalization.NumberStyles.AllowHexSpecifier);

                if (hexBytes.Count == 0 && num == 0)
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
        /// </summary>
        public static (ZoneMessageOpcode?, int) GetOpcode(byte[] data)
        {
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

            PackTunnelPacket(data.ToArray());
        }

        /// <summary>T-KIRMMIN-WIRE-TIMING: stamp ClientIsReady C→S relative to SendSelf t=0.</summary>
        public void MarkClientIsReadyWireTiming()
        {
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _wireClientIsReadyMs = nowMs;
            long sinceT0 = _wireT0Ms.HasValue ? nowMs - _wireT0Ms.Value : -1;
            log.Info($"WIRE-TIMING ClientIsReady(C→S) sinceSendSelfMs={sinceT0} utcMs={nowMs}");
        }

        public override void OnTunnelData(Channel channel, byte[] data)
        {
            if (channel == Channel.PositionUpdate)
            {
                // Movement packets aren't zone messages; only position and rotation are used so far.
                if (data.Length > 0 && data[0] == 0x00)
                    LastFullMovement = data;
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
                    }
                    if (rotation.HasValue)
                        lastRotation = rotation;
                    positionDirty = lastPosition.HasValue;
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

            // Abilities 0x27 sub 0x17 DelayedTargetInfo: while a power attack charges, the client sends its aim every frame
            // (ClientAbilityManager vtable slot 2 → FUN_1433ad3e0; seen 617 in two seconds of charging 733). The server
            // takes the aim from the cast, so these are dropped before the flood limiter, which they would otherwise
            // exhaust, dropping the requests that matter (the charge's release among them).
            if (data.Length >= 3 && data[0] == 0x27 && data[1] == 0x00 && data[2] == 0x17)
                return;

            // WorldEditingBase 0x10C: the build cursor, sent every frame while building (up to ~500 a second seen
            // 2026-10-13). Game.WorldEditing throttles its relay itself (100 ms per builder). Through the flood limiter
            // it used up the whole budget and the limiter then dropped terrain cell requests: never answered, the
            // client drew the original ground after its 6 s scene timer (removed terrain came back) and refused new
            // edits while the request stayed pending (the build tool hung).
            if (data.Length >= 2 && data[0] == 0x0C && data[1] == 0x01)
            {
                OpcodeStats.Received(data);
                Game.WorldEditing.Received(this, data);
                return;
            }

            // Terrain cell requests (0xEC) and build edits (0xF4) have their own allowance, so a flood of anything
            // else can't drop them.
            bool terrain = data.Length >= 2 && data[1] == 0x00 && (data[0] == 0xEC || data[0] == 0xF4);
            if (!(terrain ? terrainLimiter.TryTake() : packetLimiter.TryTake()))
            {
                long dropped = System.Threading.Interlocked.Increment(ref droppedPackets);
                if (packetWarnLimiter.TryTake())
                    log.Warn($"{CharacterName ?? "A session"} is sending packets too fast; {dropped} dropped so far.");
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
                // WarpNetwork 0x113 sub 4 ClientRequest {u32 1, u32 zone, u32 world, ...} at ClientFinishedLoading: asks for
                // the landscape's warp network (reply sub 2 WarpNetworkZoneNetwork, reader FUN_1434504c0: layers of
                // warp points): handled as WarpNetworkWire (MiscHandler → Game/WarpNetwork.cs, Spire Local Travel).
                // PlayerUpdate 0x6C Loading {u8}: the client's loading-screen state; informational.
                if (data.Length >= 3 && data[0] == 0x26 && data[1] == 0x00 && data[2] == 0x6C)
                    return;
                // SocialShare 0x104 (u16 sub; 1 GetProviderStatusRequest, 3 PublishRequest): sharing to social networks,
                // which this server doesn't offer; the client carries on without an answer.
                if (data.Length >= 2 && data[0] == 0x04 && data[1] == 0x01)
                    return;
                // MarketplaceImageBase 0x107 sub 1 UploadRequest: a design screenshot (large or small JPEG).
                if (data.Length >= 4 && data[0] == 0x07 && data[1] == 0x01 && data[2] == 0x01 && data[3] == 0x00)
                {
                    Game.Templates.StoreImage(this, data.AsSpan(4).ToArray());
                    return;
                }
                // ToolBase 0x117 sub 1 ToolPacketActivate (FUN_1433aa0c0): the client switched build tool. Informational,
                // no reply expected.
                if (data.Length >= 3 && data[0] == 0x17 && data[1] == 0x01 && data[2] == 0x01)
                    return;
                // HeroicMovement 0x105 (u8 sub, u64 entity, body): grappling hook fire (1), swimming (3), hook effects
                // end (4). The client sends its own entity as 0; others need the player's guid to show the rope and
                // swim pose (client handler FUN_142c42ab0 ignores packets about itself).
                if (data.Length >= 11 && data[0] == 0x05 && data[1] == 0x01)
                {
                    Game.PlayerManager.RelayHeroicMovement(this, data);
                    return;
                }
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
