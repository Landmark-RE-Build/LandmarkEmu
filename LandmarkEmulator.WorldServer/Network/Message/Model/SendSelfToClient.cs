using LandmarkEmulator.Shared.Game;
using LandmarkEmulator.Shared.Game.Entity.Static;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;
using LandmarkEmulator.Shared.Network.Message.Model.Shared;
using LandmarkEmulator.WorldServer.Network.Message.Model.Shared;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    [ZoneMessage(ZoneMessageOpcode.SendSelfToClient, prependSize: true)]
    public class SendSelfToClient : IReadable, IWritable
    {
        protected static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public class CharacterIdentity : IReadable, IWritable
        {
            // The client's name block (0.695 reader FUN_142c25b90, 0.589 FUN_142b8c3c0; SendSelf stores it at player +0x40)
            // [P]: u32 → +0xa0, u32 → +0xa4, u32 → +0xa8, string → +0x00 first name, string → +0x30 last name; FUN_143b87120
            // then builds +0x60 = "first last". The same block is in chat, friends, guild, mail and trade packets. Its one
            // consumer is FUN_142d8a970, which turns a name block into a text parameter ("player", "source") for the T4
            // text engine: +0x60 → the value, +0xa0 → parameter +0x30, +0xa4 → parameter +0x54, +0xa8 → parameter +0x3c.
            // FUN_142d8ac00 hands parameter +0x30 / +0x34 / +0x3c to T4 (FUN_14394b270 → FUN_143949d60), whose JSON log
            // (T4LoggingFormatterJSON slots 15 / 16 / 17: FUN_14396a920 "GENDER", FUN_14396b500 "TYPE", FUN_14396a9c0
            // "HONOR") names them; the entity version of the same parameter (FUN_142d88a70) fills +0x30 from the entity's
            // gender as (gender != 1) + 1. Live sent 0 / 0 / 0 (Resources/PacketDumps/SendSelf.bin, "Freldam"), so does
            // the server.

            /// <summary>+0xa0: the T4 GENDER attribute of the name in client text (1 male, 2 female; 0 = none) [P].</summary>
            public uint Gender { get; set; }
            /// <summary>
            /// +0xa4: client stores it, never read [P]: FUN_142d8a970 copies it to the text parameter's +0x54, which only
            /// FUN_142d8a970 and FUN_142d8a790 write and nothing reads (FUN_142d8ac00 does not pass it to T4).
            /// </summary>
            public uint UnusedA4 { get; set; }
            /// <summary>+0xa8: the T4 HONOR attribute of the name in client text [P] (0 = none).</summary>
            public uint Honor { get; set; }
            public string FirstName { get; set; } = "Player";
            public string LastName { get; set; } = "";

            public void Read(GamePacketReader reader)
            {
                Gender = reader.ReadUInt();
                UnusedA4 = reader.ReadUInt();
                Honor = reader.ReadUInt();
                FirstName = reader.ReadString();
                LastName = reader.ReadString();
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write(Gender);
                writer.Write(UnusedA4);
                writer.Write(Honor);
                writer.Write(FirstName);
                writer.Write(LastName);
            }
        }

        // Seems replicated here, or vey close at least: https://github.com/H1emu/h1z1-server/blob/master/src/packets/ClientProtocol/ClientProtocol_860/shared.ts#L1449-L1484
        public class Profile : IReadable, IWritable
        {
            // A profile record (0.695 reader FUN_142c1ac40, 0.589 FUN_142b81480, same layout; 200-byte object built by
            // FUN_142e5e640, kept in the player's profile hash at player +0x190 keyed by +0xb0 = +0x04, list +0x178 / next
            // +0xc0) [P]. Wire order → record offset: u32 +0x04, u32 +0x08, u32 +0x0c, u32 +0x14, u32 +0x1c, u32 +0x28,
            // u32 +0x20, u32 +0x24, bit 0x80 of +0x00, u32 +0x18, item classes +0x40, bit 0x40, u32 +0x2c, u32 +0x30,
            // bit 0x20, i8 +0x38, bit 0x10, bit 0x08, bit 0x04, then +0x70 +0x74 +0x78 +0x80 +0x7c +0x84 +0x8c +0x90 +0x94
            // +0x98 +0x9c +0xa0 +0x88 +0x3c +0x34. Every reader of a record was searched (the player +0x190 lookups:
            // FUN_142ccaeb0, FUN_142ea21d0, FUN_142fcb160, FUN_142fcda70, FUN_142fcdd70, FUN_142fce2c0, FUN_142fd33a0,
            // FUN_142d88a70, FUN_143325380 / FUN_143325480 / FUN_1433264c0 (LocalProxiedCharacter slots 83 / 87 / 164),
            // FUN_142ec04a0, FUN_14347d910, FUN_14347dca0, FUN_143008ee0, FUN_143010260, the "PID=%d" /profile list
            // FUN_1433b0810, FUN_143119140): the client reads +0x04, +0x08, +0x14, +0x38, +0x40 list, +0x74, +0x84,
            // +0x98..+0xa0 and bits 0x10 / 0x08 / 0x04. Most other values it takes from its own ProfileDefinitions by the
            // id (FUN_143be1060), e.g. the camera heights and capsule (FUN_142e9e8b0) and the switch animation. The
            // unread ones line up with ProfileDefinitions columns [I] (same order, same offsets up to +0x34; loader
            // FUN_143cb57d0) and are named by offset. Live values: the SendSelf capture (profile 46 "Human"):
            // 46, 571666, 0, 3, 0, 0, 0, 0, 0, 0, [], 0, 0, 0, 0, 1, 0, 0, 0, 2.8, 1.4, 2.75, 1, 0.4, 15, 0, 0, 0, 3, 3, 3,
            // 0, 0, 0 — WorldSession.BuildSendToSelfPacket fills them from ProfileDefinitions.

            /// <summary>+0x04: the profile id, the hash key (ProfileDefinitions ID) [P].</summary>
            public uint ProfileId { get; set; }
            /// <summary>+0x08: name text (FUN_142e5e8e0 looks it up; the "PID=%d (%s)" profile list) [P].</summary>
            public LandmarkText NameId { get; set; } = new();
            /// <summary>+0x0c: client stores it, never read [P] (ProfileDefinitions DESCRIPTION_ID is at the same offset [I]).</summary>
            public uint Unused0C { get; set; }
            /// <summary>+0x14: PROFILE_TYPE: FUN_142fd33a0 keys the type → profile map (player +0x168) by it (type &lt; 2 skipped) [P].</summary>
            public uint ProfileType { get; set; }
            /// <summary>+0x1c: client stores it, never read [P] (ProfileDefinitions ICON_ID [I]).</summary>
            public uint Unused1C { get; set; }
            /// <summary>+0x28: client stores it, never read [P] (the definition keeps its category's BADGE_IMAGE_SET_ID here [I]).</summary>
            public uint Unused28 { get; set; }
            /// <summary>+0x20: client stores it, never read [P] (ProfileDefinitions CATEGORY [I]).</summary>
            public uint Unused20 { get; set; }
            /// <summary>+0x24: client stores it, never read [P] (ProfileDefinitions ABILITY_BG_IMAGE_SET_ID [I]).</summary>
            public uint Unused24 { get; set; }
            /// <summary>Bit 0x80 of +0x00: client stores it, never read [P] (the definition's MEMBERS_ONLY bit [I]).</summary>
            public bool UnusedFlag80 { get; set; }
            /// <summary>+0x18: client stores it, never read [P] (ProfileDefinitions STARTING_EQUIPMENT_SET_ID [I]).</summary>
            public uint Unused18 { get; set; }
            /// <summary>
            /// +0x40 <c>HashList&lt;ProfileItemClassData&gt;</c> (FUN_142c35d20): u32 n × {u32 key → node +0x18, u32 → +0x00,
            /// u32 → +0x04} [P]. The client loads the same type from ProfileDefinitions ITEM_CLASS / PROFILE_RELEVANCE_RATING
            /// rows (FUN_143bdfc00: +0x00 item class, +0x04 rating, key = item class). The key is read: an item fits the
            /// profile when its item class (+0x2c) is a key here, or its profile (+0x30) is this one (FUN_142e5e820,
            /// FUN_142fcdd70); the two values are never read [P]. Live sent none.
            /// </summary>
            public List<(uint ItemClassKey, uint ItemClass, uint RelevanceRating)> ItemClasses { get; set; } = new();
            /// <summary>Bit 0x40 of +0x00: client stores it, never read [P] (the definition's SHOW_PREVIEW bit [I]).</summary>
            public bool UnusedFlag40 { get; set; }
            /// <summary>+0x2c: client stores it, never read [P] (the definition keeps its category's BADGE_BUTTON_IMAGE_SET_ID here [I]).</summary>
            public uint Unused2C { get; set; }
            /// <summary>+0x30: client stores it, never read [P] (ProfileDefinitions SWITCH_PROFILE_ANIMATION [I]).</summary>
            public uint Unused30 { get; set; }
            /// <summary>Bit 0x20 of +0x00: client stores it, never read [P] (the definition's TRIAL_ENABLED bit [I]).</summary>
            public bool UnusedFlag20 { get; set; }
            /// <summary>
            /// +0x38 (an i8 on the wire, sign-extended): the profile's faction. FUN_142fcda70 returns the active profile's
            /// (default DAT_1455fe51c, the constructor's) and FUN_142f51c30 … FUN_142f532c0 key per-faction counts by it [P];
            /// live 1 = ProfileDefinitions FACTION_ID of profile 46 [I by value].
            /// </summary>
            public byte FactionId { get; set; }
            /// <summary>
            /// Bit 0x10 of +0x00: while set, LocalProxiedCharacter::CanCrouch (vtable slot 83, FUN_143325380) skips its
            /// "movement speed modifier (+0x4cc0) ≤ 1" test, so the player can crouch while sped up [P]. Live 0.
            /// </summary>
            public bool CrouchAtAnySpeed { get; set; }
            /// <summary>Bit 0x08 of +0x00: LocalProxiedCharacter::CanJump (slot 87, FUN_143325480) returns false [P]. Live 0.</summary>
            public bool NoJump { get; set; }
            /// <summary>Bit 0x04 of +0x00: the movement input leaves out the strafe actions (FUN_143010260, FUN_143008ee0) [P]. Live 0.</summary>
            public bool NoStrafe { get; set; }
            /// <summary>+0x70 f32: STAND_CAMERA_HEIGHT (live 2.8 = the column) [P by value]; never read on the record (the client uses its definition's) [P].</summary>
            public float StandCameraHeight { get; set; }
            /// <summary>+0x74 f32: CROUCH_CAMERA_HEIGHT (live 1.4); read by LocalProxiedCharacter slot 164 (FUN_1433264c0, default 0.95) [P].</summary>
            public float CrouchCameraHeight { get; set; }
            /// <summary>+0x78 f32: LINE_OF_SIGHT_HEIGHT (live 2.75) [P by value]; never read on the record [P].</summary>
            public float LineOfSightHeight { get; set; }
            /// <summary>+0x80: UI_MODEL_CAMERA_ID (live 1) [P by value, the definition loader's order]; never read on the record [P].</summary>
            public uint UiModelCameraId { get; set; }
            /// <summary>+0x7c f32: CAPSULE_RADIUS (live 0.4) [P by value]; never read on the record [P].</summary>
            public float CapsuleRadius { get; set; }
            /// <summary>+0x84 f32: MASS (live 15): set on the character's physics when &gt; 0 (FUN_142ec04a0, virtual +0x158) [P].</summary>
            public float Mass { get; set; }
            /// <summary>+0x8c: client stores it, never read [P]. Live 0.</summary>
            public uint Unused8C { get; set; }
            /// <summary>+0x90: client stores it, never read [P]. Live 0.</summary>
            public uint Unused90 { get; set; }
            /// <summary>+0x94: client stores it, never read [P]. Live 0.</summary>
            public uint Unused94 { get; set; }
            /// <summary>+0x98: CHAR_SIZE (1..4): the footstep sound row (FUN_14347d910, FUN_14347dca0 → Footstep.txt CHAR_SIZE) [P]. Live 3.</summary>
            public uint CharSize { get; set; } = 1;
            /// <summary>+0x9c: FEET (1..6): the footstep sound row (FUN_14347dca0 → Footstep.txt FEET) [P]. Live 3.</summary>
            public uint Feet { get; set; } = 1;
            /// <summary>+0xa0: ARMOR (1..5): the foley sound row (FUN_14347d910, FUN_14347dca0 → Footstep.txt ARMOR) [P]. Live 3.</summary>
            public uint Armor { get; set; } = 1;
            /// <summary>+0x88: client stores it, never read [P]. Live 0.</summary>
            public uint Unused88 { get; set; }
            /// <summary>+0x3c: client stores it, never read [P]. Live 0.</summary>
            public uint Unused3C { get; set; }
            /// <summary>+0x34: client stores it, never read [P]. Live 0 (not the definition's SWITCH_PROFILE_EFFECT_ID 2514).</summary>
            public uint Unused34 { get; set; }

            public void Read(GamePacketReader reader)
            {
                ProfileId = reader.ReadUInt();
                NameId = new LandmarkText(reader.ReadUInt());
                Unused0C = reader.ReadUInt();
                ProfileType = reader.ReadUInt();
                Unused1C = reader.ReadUInt();
                Unused28 = reader.ReadUInt();
                Unused20 = reader.ReadUInt();
                Unused24 = reader.ReadUInt();
                UnusedFlag80 = reader.ReadBool();
                Unused18 = reader.ReadUInt();

                var itemClassCount = reader.ReadUInt();
                for (int i = 0; i < itemClassCount; i++)
                    ItemClasses.Add((reader.ReadUInt(), reader.ReadUInt(), reader.ReadUInt()));

                UnusedFlag40 = reader.ReadBool();
                Unused2C = reader.ReadUInt();
                Unused30 = reader.ReadUInt();
                UnusedFlag20 = reader.ReadBool();
                FactionId = reader.ReadByte();
                CrouchAtAnySpeed = reader.ReadBool();
                NoJump = reader.ReadBool();
                NoStrafe = reader.ReadBool();

                StandCameraHeight = reader.ReadSingle();
                CrouchCameraHeight = reader.ReadSingle();
                LineOfSightHeight = reader.ReadSingle();
                UiModelCameraId = reader.ReadUInt();
                CapsuleRadius = reader.ReadSingle();
                Mass = reader.ReadSingle();
                Unused8C = reader.ReadUInt();
                Unused90 = reader.ReadUInt();
                Unused94 = reader.ReadUInt();
                CharSize = reader.ReadUInt();
                Feet = reader.ReadUInt();
                Armor = reader.ReadUInt();
                Unused88 = reader.ReadUInt();
                Unused3C = reader.ReadUInt();
                Unused34 = reader.ReadUInt();
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write(ProfileId);
                NameId.Write(writer);
                writer.Write(Unused0C);
                writer.Write(ProfileType);
                writer.Write(Unused1C);
                writer.Write(Unused28);
                writer.Write(Unused20);
                writer.Write(Unused24);
                writer.Write(UnusedFlag80);
                writer.Write(Unused18);

                writer.Write((uint)ItemClasses.Count);
                foreach (var item in ItemClasses)
                {
                    writer.Write(item.ItemClassKey);
                    writer.Write(item.ItemClass);
                    writer.Write(item.RelevanceRating);
                }

                writer.Write(UnusedFlag40);
                writer.Write(Unused2C);
                writer.Write(Unused30);
                writer.Write(UnusedFlag20);
                writer.Write(FactionId);
                writer.Write(CrouchAtAnySpeed);
                writer.Write(NoJump);
                writer.Write(NoStrafe);

                writer.Write(StandCameraHeight);
                writer.Write(CrouchCameraHeight);
                writer.Write(LineOfSightHeight);
                writer.Write(UiModelCameraId);
                writer.Write(CapsuleRadius);
                writer.Write(Mass);
                writer.Write(Unused8C);
                writer.Write(Unused90);
                writer.Write(Unused94);
                writer.Write(CharSize);
                writer.Write(Feet);
                writer.Write(Armor);
                writer.Write(Unused88);
                writer.Write(Unused3C);
                writer.Write(Unused34);
            }
        }

        public class Collection : IReadable, IWritable
        {
            // A collection (0.695 reader FUN_142c18f50, 0.589 FUN_142b7f7a0, same layout; 0x22c8-byte object FUN_142c5f5f0,
            // added to the player's collection manager at player +0x75220 keyed by +0x138, FUN_142f14dc0) [P]: 7 × u32 →
            // +0x138 +0x13c +0x140 +0x144 +0x150 +0x154 +0x158, the reward bundle → +0x160 (FUN_142c2f040), then its
            // entries (FUN_142c3d290). ClientUpdate 0x29 sub 7 CollectionStart carries the same record and sub 9
            // CollectionAddEntry one entry (handler FUN_142cd9980): they raise the StartCollection / UpdateCollection /
            // CompleteCollection notifications (FUN_1433f8e80, FUN_1433f92c0, FUN_1433f8a40 → NotificationManager, texts
            // CollectionStarted / CollectionCompleted). The entries were missing here, so a sent collection would have
            // shifted the rest of SendSelf; live sent none (the capture), nor does the server.

            /// <summary>
            /// An entry (FUN_142c190d0 into a 0x50-byte node, <c>IconData</c> at +0x08) [P]: u32 → +0x00, +0x20, +0x1c,
            /// +0x18, +0x10, +0x14, +0x24, bool → +0x28.
            /// </summary>
            public class CollectionEntry : IReadable, IWritable
            {
                /// <summary>+0x00: the entry id; the collection keys its entries by it (FUN_142f14fa0) [P].</summary>
                public uint EntryId { get; set; }
                /// <summary>+0x20: client stores it, never read [P].</summary>
                public uint Unused20 { get; set; }
                /// <summary>+0x1c: the collection it belongs to (CollectionAddEntry finds the collection by it, FUN_142f14ec0) [P].</summary>
                public uint CollectionId { get; set; }
                /// <summary>+0x18: name text, the UpdateCollection notification's third text [P FUN_1433f92c0].</summary>
                public LandmarkText NameId { get; set; } = new();
                /// <summary>+0x10: the icon's image set (IconData +0x08), passed to UpdateCollection [P FUN_1433f92c0].</summary>
                public uint ImageSetId { get; set; }
                /// <summary>+0x14: the icon's tint (IconData +0x0c) [I: IconData layout, FUN_142f38e80].</summary>
                public uint TintId { get; set; }
                /// <summary>+0x24: the UpdateCollection notification's first text [P FUN_1433f92c0].</summary>
                public LandmarkText MessageId { get; set; } = new();
                /// <summary>+0x28: collected. All collected = complete (FUN_142f15860); any collected starts a StartMode 1 collection (FUN_142f15890) [P].</summary>
                public bool Collected { get; set; }

                public void Read(GamePacketReader reader)
                {
                    EntryId = reader.ReadUInt();
                    Unused20 = reader.ReadUInt();
                    CollectionId = reader.ReadUInt();
                    NameId = new LandmarkText(reader.ReadUInt());
                    ImageSetId = reader.ReadUInt();
                    TintId = reader.ReadUInt();
                    MessageId = new LandmarkText(reader.ReadUInt());
                    Collected = reader.ReadBool();
                }

                public void Write(GamePacketWriter writer)
                {
                    writer.Write(EntryId);
                    writer.Write(Unused20);
                    writer.Write(CollectionId);
                    NameId.Write(writer);
                    writer.Write(ImageSetId);
                    writer.Write(TintId);
                    MessageId.Write(writer);
                    writer.Write(Collected);
                }
            }

            /// <summary>+0x138: the collection id, the manager's key [P FUN_142f14dc0, FUN_142f15330].</summary>
            public uint CollectionId { get; set; }
            /// <summary>+0x13c: name text, shown by all three notifications [P FUN_1433f8e80].</summary>
            public LandmarkText NameId { get; set; } = new();
            /// <summary>+0x140: client stores it, never read [P] (every collection-manager function FUN_142f14da0…FUN_142f15e10 and the notifications checked).</summary>
            public uint Unused140 { get; set; }
            /// <summary>+0x144: client stores it, never read [P] (as <see cref="Unused140"/>).</summary>
            public uint Unused144 { get; set; }
            /// <summary>+0x150: the notifications' icon (third parameter of StartCollection / UpdateCollection / CompleteCollection) [P].</summary>
            public uint IconId { get; set; }
            /// <summary>+0x154: the icon's tint: FUN_142f15410 turns it into a colour (FUN_143d50e40) for the notifications [P].</summary>
            public uint IconTintId { get; set; }
            /// <summary>
            /// +0x158: when the collection counts as started (FUN_142f15890) [P]: 0 at once, 1 once any entry is collected,
            /// anything else never; CollectionStart / AddEntry show "Collection started." when it becomes true.
            /// </summary>
            public uint StartMode { get; set; }
            public RewardBundle RewardBundleData { get; set; } = new();
            /// <summary>The entries (FUN_142c3d290): u32 n × {u32 key → node +0x38, entry} [P].</summary>
            public List<(uint Key, CollectionEntry Entry)> Entries { get; set; } = new();

            public void Read(GamePacketReader reader)
            {
                CollectionId = reader.ReadUInt();
                NameId = new LandmarkText(reader.ReadUInt());
                Unused140 = reader.ReadUInt();
                Unused144 = reader.ReadUInt();
                IconId = reader.ReadUInt();
                IconTintId = reader.ReadUInt();
                StartMode = reader.ReadUInt();
                RewardBundleData.Read(reader);

                uint entryCount = reader.ReadUInt();
                for (int i = 0; i < entryCount; i++)
                {
                    uint key = reader.ReadUInt();
                    CollectionEntry entry = new();
                    entry.Read(reader);
                    Entries.Add((key, entry));
                }
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write(CollectionId);
                NameId.Write(writer);
                writer.Write(Unused140);
                writer.Write(Unused144);
                writer.Write(IconId);
                writer.Write(IconTintId);
                writer.Write(StartMode);
                RewardBundleData.Write(writer);

                writer.Write((uint)Entries.Count);
                foreach ((uint key, CollectionEntry entry) in Entries)
                {
                    writer.Write(key);
                    (entry ?? new CollectionEntry()).Write(writer);
                }
            }
        }

        public class Buff : IReadable, IWritable
        {
            // An active effect: <c>Abilities::ClientEffectData</c> (0.695 reader FUN_142c19230, 0.589 FUN_142b7fa80, same
            // layout; map FUN_142c3fa30 into player +0x75638, <c>HashListMap&lt;__int64,Abilities::ClientEffectData&gt;</c>,
            // keyed by (+0x10, +0x18) with +0x08) [P]. Wire order → offset: u64 +0x08, u32 +0x10, +0x18, +0x1c, +0x20,
            // +0x24, +0x28, +0x2c, u64 +0x30, u64 +0x38, u32 +0x40, u64 +0x48, u32 +0x50, +0x54, 4 × u32 +0x80, u32 +0xa0,
            // +0xa4, +0xa8, +0xac, +0xb0, 4 × u32 +0x90 (FUN_142c02830), u32 +0x58, +0x5c, +0x60, +0x68, bool +0x6c,
            // u32 +0x70, +0x78, +0x64, +0x74, +0x00: 161 bytes. The model had an extra u64 after the first one (169
            // bytes), so a sent effect would have shifted the rest of SendSelf; live sent none and so does the server.
            // Every character's effect list holds the same type (copy FUN_142e876c0; LocalProxiedCharacter slots 68 / 70 /
            // 72 = first / find / add, FUN_143326720 / FUN_143325da0 / FUN_1433246a0). In play the client builds records
            // from Abilities 0x27 sub 0x1C EffectInitialized (FUN_142e194c0, record +0x00 = 1) and sub 0x0C EffectStart
            // (FUN_142e19660, +0x00 = 2); the field names below come from those builders and the readers: the buff bar's
            // data source Character.AbilityEffects (FUN_142e25fb0: nameId, descriptionId, imageSetId, secondsTotal,
            // stackCount, enemyCaster, isGood), the combat log on EffectEnd (FUN_142e18c70 → CombatSpam "Buff off" /
            // "Debuff off" with SourcePlayer / TargetPlayer / Ability) and the visual re-apply FUN_142e1c8c0.

            /// <summary>+0x08: first part of the key (EffectInitialized / EffectStart u64; the server sends the carrier's guid) [P].</summary>
            public ulong KeyGuid { get; set; }
            /// <summary>+0x10: second part of the key [P FUN_142fd2100, FUN_143325da0].</summary>
            public uint KeyId { get; set; }
            /// <summary>+0x18: the effect id, third part of the key; the buff bar row id and the cancel button's value [P FUN_142e25fb0].</summary>
            public uint EffectId { get; set; }
            /// <summary>+0x1c: the buff bar shows one row per value, the one with the latest <see cref="StartTime"/> (EnteredEffectInfo, FUN_142e25fb0) [P].</summary>
            public uint GroupId { get; set; }
            /// <summary>
            /// +0x20: the effect type, the hash of its type name (EffectStart +0x38): Block 0xc5838838 (FUN_142fd09c0),
            /// DetectCave 0x6eb699a5 and 0x702f2f26 (FUN_14332abd0), 0x9201d31 / 0x6ab84593 (EffectEnd), 0x7ad1a80e
            /// (FUN_142e0ec90) [P].
            /// </summary>
            public uint EffectType { get; set; }
            /// <summary>+0x24: animation played on the target when its effects are re-applied (FUN_142e1c8c0 → FUN_142dfc010) [P].</summary>
            public uint AnimationId { get; set; }
            /// <summary>+0x28: composite effect played on the target on re-apply, for <see cref="VisualEffectDuration"/> less the time since <see cref="StartTime"/> (FUN_142e1c8c0, virtual +0x4a8) [P].</summary>
            public uint VisualEffectId { get; set; }
            /// <summary>+0x2c f32: seconds the <see cref="VisualEffectId"/> lasts (FUN_142e1c8c0) [P].</summary>
            public float VisualEffectDuration { get; set; }
            /// <summary>+0x30: the caster (combat log SourcePlayer; enemyCaster is worked out from it) [P].</summary>
            public ulong CasterGuid { get; set; }
            /// <summary>+0x38: the character carrying it (combat log TargetPlayer; FUN_142e1c8c0) [P].</summary>
            public ulong TargetGuid { get; set; }
            /// <summary>+0x40: start, server clock in ms: elapsed = now − this (FUN_142e167d0) [P].</summary>
            public uint StartTime { get; set; }
            /// <summary>+0x48: client time it was added; a newer record replaces an older one with the same key (FUN_142fca720) [P].</summary>
            public ulong AddedTime { get; set; }
            /// <summary>+0x50: client stores it, never read [P] (both in-play builders leave it 0).</summary>
            public uint Unused50 { get; set; }
            /// <summary>+0x54: client stores it, never read [P] (both builders leave it 0).</summary>
            public uint Unused54 { get; set; }
            /// <summary>+0x80: 4 × u32, client stores them, never read [P] (EffectStart copies its world position here, FUN_142e19660).</summary>
            public Vector4 Unused80 { get; set; } = new();
            /// <summary>
            /// +0xa0..+0xac f32: the type's parameters (EffectStart +0x110..+0x11c): Block arc and height (FUN_14354dc10),
            /// DetectCave strength +0xa4 / fade +0xa8 (FUN_1433246a0), type 0x7ad1a80e (FUN_142e0ec90) [P].
            /// </summary>
            public Vector4 TypeParameters { get; set; } = new();
            /// <summary>+0xb0 f32: the icon's seconds (EffectInitialized duration); the buff bar's secondsTotal when <see cref="Duration"/> ≤ 0 [P].</summary>
            public float IconDuration { get; set; }
            /// <summary>+0x90: 4 × u32, client stores them, never read [P] (EffectStart copies a direction here, FUN_142e19660).</summary>
            public Vector4 Unused90 { get; set; } = new();
            /// <summary>+0x58: name text (buff bar nameId; the combat log's Ability) [P].</summary>
            public LandmarkText NameId { get; set; } = new();
            /// <summary>+0x5c: description text (buff bar descriptionId) [P].</summary>
            public LandmarkText DescriptionId { get; set; } = new();
            /// <summary>+0x60: icon image set (buff bar imageSetId) [P].</summary>
            public uint IconId { get; set; }
            /// <summary>+0x68 f32: duration in seconds (buff bar secondsTotal; EffectStart +0xdc) [P].</summary>
            public float Duration { get; set; }
            /// <summary>+0x6c: the caster is an enemy (buff bar enemyCaster when the caster is not around) [P].</summary>
            public bool EnemyCaster { get; set; }
            /// <summary>+0x70: the stat it changes; when non-zero EffectEnd logs "Buff off" / "Debuff off" (FUN_142e18c70) [P].</summary>
            public uint StatId { get; set; }
            /// <summary>+0x78 f32: the stat change: &gt; 0 logs "Buff off", &lt; 0 "Debuff off" [P].</summary>
            public float StatValue { get; set; }
            /// <summary>+0x64: buff bar isGood: 2 always good, 1 good unless the caster is an enemy [P].</summary>
            public uint GoodType { get; set; }
            /// <summary>+0x74: buff bar stackCount [P].</summary>
            public uint StackCount { get; set; }
            /// <summary>+0x00: client stores it, never read [P] (EffectInitialized records get 1, EffectStart records 2).</summary>
            public uint Unused00 { get; set; }

            public void Read(GamePacketReader reader)
            {
                KeyGuid = reader.ReadULong();
                KeyId = reader.ReadUInt();
                EffectId = reader.ReadUInt();
                GroupId = reader.ReadUInt();
                EffectType = reader.ReadUInt();
                AnimationId = reader.ReadUInt();
                VisualEffectId = reader.ReadUInt();
                VisualEffectDuration = reader.ReadSingle();
                CasterGuid = reader.ReadULong();
                TargetGuid = reader.ReadULong();
                StartTime = reader.ReadUInt();
                AddedTime = reader.ReadULong();
                Unused50 = reader.ReadUInt();
                Unused54 = reader.ReadUInt();

                Unused80 = new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                TypeParameters = new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                IconDuration = reader.ReadSingle();
                Unused90 = new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

                NameId = new LandmarkText(reader.ReadUInt());
                DescriptionId = new LandmarkText(reader.ReadUInt());
                IconId = reader.ReadUInt();
                Duration = reader.ReadSingle();
                EnemyCaster = reader.ReadBool();
                StatId = reader.ReadUInt();
                StatValue = reader.ReadSingle();
                GoodType = reader.ReadUInt();
                StackCount = reader.ReadUInt();
                Unused00 = reader.ReadUInt();
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write(KeyGuid);
                writer.Write(KeyId);
                writer.Write(EffectId);
                writer.Write(GroupId);
                writer.Write(EffectType);
                writer.Write(AnimationId);
                writer.Write(VisualEffectId);
                writer.Write(VisualEffectDuration);
                writer.Write(CasterGuid);
                writer.Write(TargetGuid);
                writer.Write(StartTime);
                writer.Write(AddedTime);
                writer.Write(Unused50);
                writer.Write(Unused54);

                writer.Write(Unused80);
                writer.Write(TypeParameters);
                writer.Write(IconDuration);
                writer.Write(Unused90);

                NameId.Write(writer);
                DescriptionId.Write(writer);
                writer.Write(IconId);
                writer.Write(Duration);
                writer.Write(EnemyCaster);
                writer.Write(StatId);
                writer.Write(StatValue);
                writer.Write(GoodType);
                writer.Write(StackCount);
                writer.Write(Unused00);
            }
        }

        public class InventoryData : IReadable, IWritable
        {
            /// <summary>
            /// Seems to default to current number of items in container 0 for maxxSize on Containers 7+. Fized size of 10 containers.
            /// </summary>
            public List<(uint /*maxSize*/, bool /*visible?*/)> Containers { get; set; } = new()
            {
                ( 40, false ),
                ( 60, false ),
                ( 10, false ),
                ( 10, false ),
                ( 10, false ),
                ( 250, false ),
                ( 250, false ),
                ( 0, true),
                ( 0, true),
                ( 0, true),
                ( 0, true),
                ( 0, true)
            };
            public List<InventoryItem> Items { get; set; } = new();

            public void Read(GamePacketReader reader)
            {
                Containers = new();
                for (int i = 0; i < 12; i++)
                    Containers.Add((reader.ReadUInt(), reader.ReadBool()));

                uint itemCount = reader.ReadUInt();
                for (int i = 0; i < itemCount; i++)
                {
                    InventoryItem item = new();
                    item.Read(reader);
                    Items.Add(item);
                }
            }

            public void Write(GamePacketWriter writer)
            {
                log.Info($"Writing {Containers.Count} containers");
                foreach (var item in Containers)
                {
                    writer.Write(item.Item1);
                    writer.Write(item.Item2);
                }

                writer.Write((uint)Items.Count);
                foreach (InventoryItem item in Items)
                    item.Write(writer);
            }
        }

        /// <summary>Reads a u32 count, then that many elements (no capacity is reserved from the forged count).</summary>
        private static List<T> ReadList<T>(GamePacketReader reader, Func<GamePacketReader, T> element)
        {
            uint count = reader.ReadUInt();
            var list = new List<T>();
            for (uint i = 0; i < count; i++)
                list.Add(element(reader));
            return list;
        }

        private static T ReadModel<T>(GamePacketReader reader) where T : IReadable, new()
        {
            var model = new T();
            model.Read(reader);
            return model;
        }

        private static void WriteList<T>(GamePacketWriter writer, List<T> list) where T : IWritable
        {
            writer.Write((uint)list.Count);
            foreach (T item in list)
                item.Write(writer);
        }

        /// <summary>
        /// One of the player's palettes (the list after the inventory: 0.695 reader FUN_142c3fc60 into the object at player
        /// +0x753e8, nodes from FUN_142cf00c0, which also sets up a <c>SoeUtil::HashList&lt;PaletteManager::PlayerPaletteEntry&gt;</c>)
        /// [P]: u64 key (the node's +0x68), then record FUN_142c25c60: string +0x08, u64 +0x00, the entries (FUN_142c35af0),
        /// bool +0x54, u32 +0x50. The server sends none (a new character has none) [I: what live sent is not captured].
        /// <para>
        /// The same record is PaletteUpdate's body (PaletteBase 0xF5 sub 5, 0.695 handler FUN_142f13390 → the
        /// <c>ClientPaletteManager</c> at player +0x753e8; Game/Palettes.cs sends it). Readers (0.695) [P]: the build-mode
        /// radial menu lists the palettes by name (FUN_14343f0b0) and opens one into its slots (FUN_1434411a0);
        /// FUN_142f13a70 re-packs the palettes into the menu positions (<see cref="MenuSlot"/>) and sends
        /// PalettePacketSlotUpdates (sub 7) for those that moved; the edit requests (AddEntry 3, Delete 2, DeleteEntry 4:
        /// FUN_142f14850 / FUN_142f14a10 / FUN_142f14aa0) check <see cref="ReadOnly"/>.
        /// </para>
        /// </summary>
        public class PlayerPalette : IReadable, IWritable
        {
            /// <summary>
            /// One palette entry (FUN_142c35af0: u32 key → node +0x20, then FUN_142c25d30: u32 +0x0, u32 +0x4, u64 +0x8)
            /// [P]. Opening the palette (FUN_1434411a0) turns each entry into a radial menu item: position
            /// <see cref="Slot"/>, icon and name looked up by <see cref="Type"/> and <see cref="Value"/> (FUN_142f131e0).
            /// The same three fields are what AddEntry (sub 3) sends: {u64 palette, u32 slot, u32 type, u64 value}.
            /// </summary>
            public class PlayerPaletteEntry : IReadable, IWritable
            {
                /// <summary>The hash list key (node +0x20; the client only files the entry under it) [P]. Game/Palettes.cs sends the slot.</summary>
                public uint Key { get; set; }
                /// <summary>+0x0 the radial menu position of the entry (FUN_1434411a0) [P].</summary>
                public uint Slot { get; set; }
                /// <summary>
                /// +0x4 what the entry holds, picking the data source its icon ("ImageSetId") and name come from
                /// (FUN_142f131e0) [P]: 1 Design.Props, 2 Voxel.Materials, 3 Voxel.Shapes (Voxel.SelectShape sends 3),
                /// 4 Character.Templates; anything else shows "Invalid".
                /// </summary>
                public uint Type { get; set; }
                /// <summary>+0x8 the row id in that data source (FUN_142f131e0) [P].</summary>
                public ulong Value { get; set; }

                public void Read(GamePacketReader reader)
                {
                    Key = reader.ReadUInt();
                    Slot = reader.ReadUInt();
                    Type = reader.ReadUInt();
                    Value = reader.ReadULong();
                }

                public void Write(GamePacketWriter writer)
                {
                    writer.Write(Key);
                    writer.Write(Slot);
                    writer.Write(Type);
                    writer.Write(Value);
                }
            }

            /// <summary>The palette's key (u64, read before the record; the manager files and finds palettes by it) [P].</summary>
            public ulong PaletteId { get; set; }
            /// <summary>+0x08 the palette's name, listed in the radial menu (FUN_14343f0b0) [P].</summary>
            public string Name { get; set; } = "";
            /// <summary>
            /// +0x00 the record's own id: the radial menu's palette items carry it (FUN_14343f0b0) and SlotUpdates sends it
            /// as the palette id (FUN_142f13a70) [P]. Sent equal to <see cref="PaletteId"/>.
            /// </summary>
            public ulong Id { get; set; }
            /// <summary>+0x20 the entries (<see cref="PlayerPaletteEntry"/>).</summary>
            public List<PlayerPaletteEntry> Entries { get; set; } = new();
            /// <summary>
            /// +0x54 read-only: the client sends no AddEntry / Delete / DeleteEntry for such a palette (FUN_142f14850 /
            /// FUN_142f14a10 / FUN_142f14aa0 return when it is set); assigning a slot on it keeps a local draft instead
            /// (manager +0xf0, FUN_143447340), and while a draft exists the radial list leaves read-only palettes out
            /// (FUN_14343f0b0 / FUN_143440b80) [P].
            /// </summary>
            public bool ReadOnly { get; set; }
            /// <summary>
            /// +0x50 the palette's position in the radial menu: FUN_142f13a70 places palettes with a value below 11 at it
            /// and renumbers the rest, sending SlotUpdates (sub 7, PaletteIdToSlot {u64 id, u32 slot}) for each one whose
            /// value changes [P].
            /// </summary>
            public uint MenuSlot { get; set; }

            public void Read(GamePacketReader reader)
            {
                PaletteId = reader.ReadULong();
                Name = reader.ReadString();
                Id = reader.ReadULong();
                Entries = ReadList(reader, ReadModel<PlayerPaletteEntry>);
                ReadOnly = reader.ReadBool();
                MenuSlot = reader.ReadUInt();
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write(PaletteId);
                writer.Write(Name ?? "");
                writer.Write(Id);
                WriteList(writer, Entries);
                writer.Write(ReadOnly);
                writer.Write(MenuSlot);
            }
        }

        /// <summary>
        /// One acquaintance (the list after the achievements: 0.695 FUN_142c19bd0 reads u32 n, then n × FUN_142c2e960 into
        /// the player's list at +0x74aa0, kept newest first by FUN_142fcfdc0) [P]: u64 +0x00, string +0x08, u32 +0x20,
        /// u64 +0x28, bool +0x30. The live AddAcquaintance packet (0x4B sub 1, FUN_142fc4230 family handler in part_0080)
        /// reads the same record; RemoveAcquaintance (sub 2) finds it by the name, AcquaintanceOnline (sub 3) by the u64 and
        /// sets the bool; at most 20 are kept. Sent empty (the server sends no acquaintances).
        /// </summary>
        public class Acquaintance : IReadable, IWritable
        {
            /// <summary>+0x00: matched by AcquaintanceOnline [P] (the character guid [I]).</summary>
            public ulong CharacterGuid { get; set; }
            /// <summary>+0x08: matched by RemoveAcquaintance [P].</summary>
            public string Name { get; set; } = "";
            /// <summary>
            /// +0x20: client stores it, never read [P]: the only users of the list (player +0x74aa0) are the
            /// AcquaintanceBase 0x4B handler (sub 1 Add matches +0x00, sub 2 Remove the name, sub 3 Online sets +0x30) and
            /// the sorted insert FUN_142fcfdc0 (+0x28); AddAcquaintance's own record starts it at 0. Sent 0.
            /// </summary>
            public uint Unused20 { get; set; }
            /// <summary>+0x28: the list is kept in descending order of it [P FUN_142fcfdc0] (a time [I]).</summary>
            public ulong SortKey { get; set; }
            /// <summary>+0x30: set by AcquaintanceOnline [P].</summary>
            public bool Online { get; set; }

            public void Read(GamePacketReader reader)
            {
                CharacterGuid = reader.ReadULong();
                Name = reader.ReadString();
                Unused20 = reader.ReadUInt();
                SortKey = reader.ReadULong();
                Online = reader.ReadBool();
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write(CharacterGuid);
                writer.Write(Name ?? "");
                writer.Write(Unused20);
                writer.Write(SortKey);
                writer.Write(Online);
            }
        }

        /// <summary>
        /// The quest manager (player +0x268b0; 0.695 reader FUN_142c1b3d0, 0.589 FUN_142b81c10) [P]: u32 n quests (each
        /// FUN_142c2e280 into a <c>HashListMap</c> keyed by its first u32), then u32 → +0x11d98 CompletedQuestCount (what
        /// CompletedQuestCountUpdatePacket 0x34/0xC sets, FUN_142f2cd80), u32 → +0x11d90 SelectedQuestId (Lua
        /// QuestScreen.GetSelectedQuest, FUN_142fce5b0), bool → +0x11d94 SelectedQuestLocked (SetAutoSwitchSelectedQuest
        /// → FUN_142f2e4a0 sends SelectedQuestLockedPacket when it changes), u32 → +0x11d9c QuestLimit (non-members) and
        /// u32 → +0x11da0 MemberQuestLimit (Lua GetQuestLimit FUN_142fce420 picks by membership; the client text keys
        /// QuestLimitNonmember / QuestLimitMember). Live sent no quests (the live SendSelf capture); the server does the
        /// same.
        /// </summary>
        public class QuestData : IReadable, IWritable
        {
            /// <summary>
            /// One quest objective (<c>HashListMap&lt;int,ObjectiveData,16&gt;</c>: u32 key → node +0x2190, then record
            /// FUN_142c22ee0) [P]: u32 +0x0, u32 +0x4, u32 +0x8, a reward bundle (FUN_142c2f040 → +0x10), u32 +0x2150,
            /// u32 +0x2154, u32 +0x2158, u32 +0x2170, u32 +0x215c, u32 +0xc, 4 × u32 (FUN_142c22e30 → +0x2160), u8
            /// +0x2178, u32 +0x2174.
            /// <para>
            /// The record is the client's ObjectiveData, the one achievements use (<see cref="Objective"/>, same reader;
            /// the names below match it). What the quest code does with it (0.695) [P]: the QuestPacket family handler
            /// FUN_142f2cd80 finds an objective by quest id and <see cref="Key"/> and sets <see cref="Status"/> — 1 by
            /// ObjectiveActivated (sub 8, FUN_142f2bcb0, which also sets <see cref="Total"/> and starts the
            /// <see cref="EventBar"/>), 2 by ObjectiveComplete (sub 0xa, FUN_142f2c0f0), 3 by ObjectiveFailed (sub 0xb,
            /// FUN_142f2c4c0) — and <see cref="Count"/> (ObjectiveUpdate sub 9, FUN_142f2e5a0). The notices
            /// (FUN_1433f7160 / FUN_1433f7ec0 / FUN_1433f75e0 / FUN_1433f7a50: "QuestNewObjective", "QuestObjective
            /// Update" "%s %s: %s", "(Completed) %s", "(Failed) %s") read <see cref="ObjectiveId"/> (Lua
            /// QuestHandler:UpdateObjective), <see cref="NameId"/>, <see cref="Count"/> and <see cref="Total"/>. Sent
            /// empty (live sent no quests).
            /// </para>
            /// </summary>
            public class QuestObjective : IReadable, IWritable
            {
                /// <summary>The map key (node +0x2190): the quest packets look an objective up by it [P FUN_142f2bcb0].</summary>
                public uint Key { get; set; }
                /// <summary>+0x0 objective id: Lua QuestHandler:UpdateObjective gets it (FUN_143400580) [P].</summary>
                public uint ObjectiveId { get; set; }
                /// <summary>+0x4 name text: resolved for the objective notices ("%s %s: %s", FUN_1433f7160) [P].</summary>
                public LandmarkText NameId { get; set; } = new();
                /// <summary>
                /// +0x8 description text (the Journal's "Description" for achievement objectives, FUN_143556ff0); the quest
                /// code never reads it [P].
                /// </summary>
                public LandmarkText DescriptionId { get; set; } = new();
                /// <summary>+0x10 the objective's reward bundle (the Journal's "ObjectiveRewards"); the quest code never reads it [P].</summary>
                public RewardBundle Reward { get; set; } = new();
                /// <summary>+0x2150 status: 1 active, 2 complete, 3 failed (set by the quest packets, see above) [P].</summary>
                public uint Status { get; set; }
                /// <summary>+0x2154 current count: set by ObjectiveUpdate, shown in "QuestObjectiveUpdate" (FUN_1433f7ec0) [P].</summary>
                public uint Count { get; set; }
                /// <summary>+0x2158 required count: set by ObjectiveActivated, shown next to <see cref="Count"/> [P].</summary>
                public uint Total { get; set; }
                /// <summary>
                /// +0x2170 sort order (the achievement objective sorter FUN_142e26d90, called from FUN_142e33b40, compares
                /// it); the quest code never reads it: quest objectives are filed by FUN_142e26b00, which does not sort [P].
                /// </summary>
                public uint SortOrder { get; set; }
                /// <summary>+0x215c u32. Never read [P] (as <see cref="Objective.Slot215C"/>).</summary>
                public uint Slot215C { get; set; }
                /// <summary>
                /// +0xc a third text id (<see cref="Objective.HintTextId"/>). Never read for quest objectives [P]: the
                /// quest wrapper FUN_142f28c90 resolves the QUEST's +0xc (<see cref="Quest.HintTextId"/>), not this one;
                /// the objective copy FUN_142e26b00 only copies it.
                /// </summary>
                public LandmarkText HintTextId { get; set; } = new();
                /// <summary>
                /// +0x2160 the event bar (FUN_142c22e30): an active objective (status 1) hands it to the event bar
                /// (FUN_1431d81f0 → FUN_1431d7f00 "EventBar:SetupBar") when it activates, when its quest is added
                /// (FUN_142f2beb0) and when the quest is selected (FUN_142f2de30) [P].
                /// </summary>
                public Objective.EventBarData EventBar { get; set; } = new();
                /// <summary>+0x2178 flags byte (bit 0x80 picks the Journal's other status icons, FUN_143556ff0); the quest code never reads it [P].</summary>
                public byte Flags { get; set; }
                /// <summary>+0x2174 u32. Never read [P] (as <see cref="Objective.Slot2174"/>).</summary>
                public uint Slot2174 { get; set; }

                public void Read(GamePacketReader reader)
                {
                    Key = reader.ReadUInt();
                    ObjectiveId = reader.ReadUInt();
                    NameId = new LandmarkText(reader.ReadUInt());
                    DescriptionId = new LandmarkText(reader.ReadUInt());
                    Reward = ReadModel<RewardBundle>(reader);
                    Status = reader.ReadUInt();
                    Count = reader.ReadUInt();
                    Total = reader.ReadUInt();
                    SortOrder = reader.ReadUInt();
                    Slot215C = reader.ReadUInt();
                    HintTextId = new LandmarkText(reader.ReadUInt());
                    EventBar = ReadModel<Objective.EventBarData>(reader);
                    Flags = reader.ReadByte();
                    Slot2174 = reader.ReadUInt();
                }

                public void Write(GamePacketWriter writer)
                {
                    writer.Write(Key);
                    writer.Write(ObjectiveId);
                    NameId.Write(writer);
                    DescriptionId.Write(writer);
                    Reward.Write(writer);
                    writer.Write(Status);
                    writer.Write(Count);
                    writer.Write(Total);
                    writer.Write(SortOrder);
                    writer.Write(Slot215C);
                    HintTextId.Write(writer);
                    EventBar.Write(writer);
                    writer.Write(Flags);
                    writer.Write(Slot2174);
                }
            }

            /// <summary>
            /// One quest (record FUN_142c2e280) [P]: u32 +0x0 QuestId (the map key), u32 +0x4, u32 +0x8, u32 +0xc, bool
            /// +0x10, u64 +0x18, u32 +0x20, bool +0x24, u32 +0x28, a reward bundle (FUN_142c2f040 → +0x30, the
            /// <see cref="RewardBundle"/> layout: "CreateRewardBundleEntry of type %d"), the objectives (FUN_142c35990 →
            /// +0x2170), u32 +0x2238, bool +0x223c, bool +0x11.
            /// <para>
            /// The client keeps each quest as a ClientQuestData (RTTI <c>HashListMap&lt;int,ClientQuestData,256,8&gt;</c>,
            /// node key +0x2298): FUN_142f28c90 copies the record (FUN_142f28d60) and resolves its three text ids into
            /// strings at +0x2240, +0x2258 and +0x2270. Everything that reads a quest afterwards (0.695: the QuestPacket
            /// handler FUN_142f2cd80 and the quest manager methods FUN_142f2bb90 .. FUN_142f2e5a0, the only code that
            /// walks the map) reads <see cref="QuestId"/>, the +0x2240 name, <see cref="Silent"/>,
            /// <see cref="LastUpdate"/> and the objectives. Only the constructor (FUN_142c69a20), the reader and the copy
            /// touch the rest: "never read [P]". Landmark has no quest window: the Lua QuestHandler the notices call is not
            /// in the 0.695 UI. Sent empty, as live.
            /// </para>
            /// </summary>
            public class Quest : IReadable, IWritable
            {
                /// <summary>+0x0 quest id, the map key (constructor default 0xFFFFFFFF); passed to every notice [P].</summary>
                public uint QuestId { get; set; }
                /// <summary>
                /// +0x4 name text: resolved into the quest's name (+0x2240), which the notices print: "QuestStarted",
                /// "QuestCompleted", "QuestFailed", "QuestAbandoned" ("%s %s", FUN_1433f84c0 / FUN_1433f6a50 / FUN_1433f6dd0
                /// / FUN_1433f66d0, also handed to Lua StartQuest / CompleteQuest / FailQuest) and the objective notices'
                /// quest name ("%s %s: %s", "questName") [P].
                /// </summary>
                public LandmarkText NameId { get; set; } = new();
                /// <summary>
                /// +0x8 a text id, resolved into the string at +0x2258 (FUN_142f28c90), which nothing reads [P]. Named as the
                /// description after the ObjectiveData layout (+0x4 name, +0x8 description) [I].
                /// </summary>
                public LandmarkText DescriptionId { get; set; } = new();
                /// <summary>
                /// +0xc a third text id, resolved into the string at +0x2270 (FUN_142f28c90), which nothing reads [P]. Named
                /// after ObjectiveData's third text (<see cref="Objective.HintTextId"/>) [I].
                /// </summary>
                public LandmarkText HintTextId { get; set; } = new();
                /// <summary>+0x10 bool. Never read [P].</summary>
                public bool Slot10 { get; set; }
                /// <summary>
                /// +0x18 u64; the constructor fills it with the client's "no guid" value (DAT_145430dc8, also the default of
                /// player +0x290 and the other guid fields), so a guid [I]. Never read [P].
                /// </summary>
                public ulong Slot18 { get; set; }
                /// <summary>+0x20 u32. Never read [P].</summary>
                public uint Slot20 { get; set; }
                /// <summary>
                /// +0x24 silent: when set the client shows no notice for the quest: no "QuestStarted" / "QuestCompleted" /
                /// "QuestFailed" line or Lua call (FUN_1433f84c0 / FUN_1433f6a50 / FUN_1433f6dd0 skip them) and no objective
                /// notices (the handler passes it as their "quiet" argument, ORed with the packet's own) [P].
                /// </summary>
                public bool Silent { get; set; }
                /// <summary>
                /// +0x28: ObjectiveUpdate (sub 9, FUN_142f2e5a0) and ObjectiveComplete (sub 0xa, FUN_142f2c0f0) overwrite it
                /// with a packet value; when the selected quest ends (FUN_142f2bb90 / FUN_142f2c1b0 / FUN_142f2c560) the quest
                /// with the highest value goes to the helper (QuestHandler:PopulateHelper) [P]. So the time or sequence of the
                /// quest's last update [I].
                /// </summary>
                public uint LastUpdate { get; set; }
                /// <summary>+0x30 the quest's reward bundle. The quest code never reads it [P].</summary>
                public RewardBundle Reward { get; set; } = new();
                /// <summary>+0x2170 the objectives (see <see cref="QuestObjective"/>).</summary>
                public List<QuestObjective> Objectives { get; set; } = new();
                /// <summary>+0x2238 u32. Never read [P].</summary>
                public uint Slot2238 { get; set; }
                /// <summary>+0x223c bool. Never read [P].</summary>
                public bool Slot223C { get; set; }
                /// <summary>+0x11 bool (read last). Never read [P].</summary>
                public bool Slot11 { get; set; }

                public void Read(GamePacketReader reader)
                {
                    QuestId = reader.ReadUInt();
                    NameId = new LandmarkText(reader.ReadUInt());
                    DescriptionId = new LandmarkText(reader.ReadUInt());
                    HintTextId = new LandmarkText(reader.ReadUInt());
                    Slot10 = reader.ReadBool();
                    Slot18 = reader.ReadULong();
                    Slot20 = reader.ReadUInt();
                    Silent = reader.ReadBool();
                    LastUpdate = reader.ReadUInt();
                    Reward = ReadModel<RewardBundle>(reader);
                    Objectives = ReadList(reader, ReadModel<QuestObjective>);
                    Slot2238 = reader.ReadUInt();
                    Slot223C = reader.ReadBool();
                    Slot11 = reader.ReadBool();
                }

                public void Write(GamePacketWriter writer)
                {
                    writer.Write(QuestId);
                    NameId.Write(writer);
                    DescriptionId.Write(writer);
                    HintTextId.Write(writer);
                    writer.Write(Slot10);
                    writer.Write(Slot18);
                    writer.Write(Slot20);
                    writer.Write(Silent);
                    writer.Write(LastUpdate);
                    Reward.Write(writer);
                    WriteList(writer, Objectives);
                    writer.Write(Slot2238);
                    writer.Write(Slot223C);
                    writer.Write(Slot11);
                }
            }

            public List<Quest> QuestList { get; set; } = new();
            public uint CompletedQuestCount { get; set; }
            public uint SelectedQuestId { get; set; }
            public bool SelectedQuestLocked { get; set; }
            public uint QuestLimit { get; set; }
            public uint MemberQuestLimit { get; set; }

            public void Read(GamePacketReader reader)
            {
                QuestList = ReadList(reader, ReadModel<Quest>);
                CompletedQuestCount = reader.ReadUInt();
                SelectedQuestId = reader.ReadUInt();
                SelectedQuestLocked = reader.ReadBool();
                QuestLimit = reader.ReadUInt();
                MemberQuestLimit = reader.ReadUInt();
            }

            public void Write(GamePacketWriter writer)
            {
                WriteList(writer, QuestList);
                writer.Write(CompletedQuestCount);
                writer.Write(SelectedQuestId);
                writer.Write(SelectedQuestLocked);
                writer.Write(QuestLimit);
                writer.Write(MemberQuestLimit);
            }
        }

        public class RecipeData : IReadable, IWritable
        {
            /// <summary>
            /// A recipe's cooldown, <c>Crafting::PlayerRecipeData</c> (the class of RecipeBase 0x42 sub 0x1E
            /// PlayerRecipeDataUpdatePacket, which carries {u32 recipe, u64 time}): the recipe manager's map at +0x38, read
            /// by FUN_142c35bd0 (was named after the block reader FUN_142c1b810) as u32 key → node +0x20, u32 → +0x00,
            /// u64 → +0x08 (default DAT_145430dc8, "no time") [P]. Live sent one: {2389, 2389, 1487449669}.
            /// </summary>
            public class PlayerRecipeData : IReadable, IWritable
            {
                /// <summary>Node +0x20: the map key, the recipe id (FUN_142f39230 finds a recipe's entry by it) [P].</summary>
                public uint RecipeKey { get; set; }
                /// <summary>+0x00: the recipe id again (the update packet's u32; live = the key) [P]; client stores it, never read on its own.</summary>
                public uint RecipeId { get; set; }
                /// <summary>
                /// +0x08: when the cooldown started, server seconds (live 1487449669 = 2017-02-18): remaining = the record's
                /// <see cref="Recipe.CooldownTotal"/> − (now − this) (FUN_143613b30, the window's CooldownRemaining), the
                /// recipe leaves the cooling set once that passes (FUN_142f40400) [P].
                /// </summary>
                public ulong CooldownStart { get; set; }

                public void Read(GamePacketReader reader)
                {
                    RecipeKey = reader.ReadUInt();
                    RecipeId = reader.ReadUInt();
                    CooldownStart = reader.ReadULong();
                }

                public void Write(GamePacketWriter writer)
                {
                    writer.Write(RecipeKey);
                    writer.Write(RecipeId);
                    writer.Write(CooldownStart);
                }
            }

            // A recipe record (0.695 reader FUN_142c2e640, 0.589 FUN_142b94b50) [P]: u32 → +0x04, +0x08, +0x00, +0x0c, +0x10,
            // +0x14, +0x18, +0x28, +0x2c, +0x30, components → +0x70, bool → +0x34, u32 → +0x38, +0x3c, +0x40, +0x44, list →
            // +0x50, u32 → +0x48. Columns from the window's row builder FUN_14360e950 (RecipeId, RecipeType, AllowCatalyst,
            // ItemDefinitionId, ItemCountAwarded, MaxQueue, name, Description, the icon FUN_14360c330 = IconData {+0x28,
            // +0x2c}, AchievementId, CooldownTotal, QueueSlots = station queue (+0x194) / +0x3c, FuelNeeded = queue × +0x30)
            // and trace/0695/agent-l43.md §4. All 2,363 records of the live capture match Recipes.txt exactly for RecipeType,
            // SORT_ORDER, CASTING_TIME (+0x30), STACK_SIZE (+0x3c) and MAX_QUEUE (+0x48) [P by value].
            public class Recipe : IReadable, IWritable
            {
                // A component (0.695 reader FUN_142c2e4e0, 0.589 FUN_142b949f0) [P]: u32 → +0x00, +0x04, +0x08, +0x0c, +0x10,
                // +0x20, +0x24, +0x28. Columns from the window's component row (FUN_14360e6e0: ComponentId +0x08,
                // ComponentType +0x04, CountRequired +0x0c, name +0x10, icon FUN_142f38e80 = IconData {+0x20, +0x24}).
                public class RecipeComponent : IReadable, IWritable
                {
                    public uint Index { get; set; }
                    /// <summary>+0x04: COMPONENT_TYPE (1 item, 2 currency, 4, 5, 7; RecipeComponents.txt) [P row builder "ComponentType"].</summary>
                    public uint ComponentType { get; set; }
                    public uint ItemId { get; set; }
                    public uint Quantity { get; set; }
                    public LandmarkText NameId { get; set; } = new();
                    public uint IconId { get; set; }
                    /// <summary>+0x24: the icon's tint (IconData +0x0c, FUN_142f38e80) [P]. Live 0 on every component.</summary>
                    public uint IconTintId { get; set; }
                    /// <summary>
                    /// +0x28: DESCRIPTION_ID of the RecipeComponents row (live: 568129, 565535 … exactly that column) [P by
                    /// value]; client stores it, never read [P] (the component row FUN_14360e6e0 does not use it).
                    /// </summary>
                    public LandmarkText DescriptionId { get; set; } = new();

                    public void Read(GamePacketReader reader)
                    {
                        Index = reader.ReadUInt();
                        ComponentType = reader.ReadUInt();
                        ItemId = reader.ReadUInt();
                        Quantity = reader.ReadUInt();
                        NameId = new LandmarkText(reader.ReadUInt());
                        IconId = reader.ReadUInt();
                        IconTintId = reader.ReadUInt();
                        DescriptionId = new LandmarkText(reader.ReadUInt());
                    }

                    public void Write(GamePacketWriter writer)
                    {
                        writer.Write(Index);
                        writer.Write(ComponentType);
                        writer.Write(ItemId);
                        writer.Write(Quantity);
                        NameId.Write(writer);
                        writer.Write(IconId);
                        writer.Write(IconTintId);
                        DescriptionId.Write(writer);
                    }
                }

                public uint RecipeId { get; set; }
                /// <summary>+0x08: RECIPE_TYPE (row builder "RecipeType") [P].</summary>
                public uint RecipeType { get; set; }
                /// <summary>+0x00: SORT_ORDER (live = the column on every record) [P by value]; the window's sort key [I, agent-l43.md §4].</summary>
                public uint SortOrder { get; set; }
                public uint Itemid { get; set; }
                public uint Quantity { get; set; }
                public LandmarkText NameId { get; set; } = new();
                public LandmarkText DescriptionId { get; set; } = new();
                public uint IconId { get; set; }
                /// <summary>+0x2c: the icon's tint (IconData +0x0c, FUN_14360c330) [P]. Live 0 on every record.</summary>
                public uint IconTintId { get; set; }
                /// <summary>
                /// +0x30 f32: CASTING_TIME (live 0.25 / 1.0 = the column) [P by value]; the refining window's FuelNeeded is the
                /// station queue × this (FUN_14360e950) [P]. Was sent as 0.
                /// </summary>
                public float CastingTime { get; set; }
                public List<RecipeComponent> Components { get; set; } = new();
                /// <summary>Record +0x34: ALLOW_CATALYST (reader FUN_142c2e640; was named MemberOnly) [P].</summary>
                public bool AllowCatalyst { get; set; }
                /// <summary>
                /// +0x38: client stores it, never read [P] (row builder FUN_14360e950, the recipe manager FUN_142f36130 …
                /// FUN_142f40f90 and the crafting UI 0x143550000 … 0x143640000 searched). Live sent item-like ids (108 on the
                /// Hewer's Pick, 42 on Smelted Copper) that no Recipes.txt column holds; the server sends 0.
                /// </summary>
                public uint Unused38 { get; set; }
                /// <summary>+0x3c: STACK_SIZE (live = the column): the refining window's QueueSlots = station queue / this (FUN_14360e950) [P]. Was sent as 0.</summary>
                public uint StackSize { get; set; }
                /// <summary>
                /// Record +0x40: TRACKER_ACHIEVEMENT_ID — the window's IsTracking column is "the player's tracked goals
                /// (player +0x1e628) hold it" (row builder FUN_14360e950) [P].
                /// </summary>
                public uint TrackerAchievementId { get; set; }
                /// <summary>+0x44: COOLDOWN_TIME in seconds: the window's CooldownTotal, and with <see cref="PlayerRecipeData"/> its CooldownRemaining (FUN_14360e950, FUN_143613b30) [P].</summary>
                public uint CooldownTotal { get; set; }
                /// <summary>+0x50 list: the output item's categories; the window adds one row per entry (FUN_14355de30) [P].</summary>
                public List<uint> Categories { get; set; } = new();
                /// <summary>+0x48: MAX_QUEUE (row builder "MaxQueue") [P].</summary>
                public uint MaxQueue { get; set; }

                public void Read(GamePacketReader reader)
                {
                    RecipeId = reader.ReadUInt();
                    RecipeType = reader.ReadUInt();
                    SortOrder = reader.ReadUInt();
                    Itemid = reader.ReadUInt();
                    Quantity = reader.ReadUInt();
                    NameId = new LandmarkText(reader.ReadUInt());
                    DescriptionId = new LandmarkText(reader.ReadUInt());
                    IconId = reader.ReadUInt();
                    IconTintId = reader.ReadUInt();
                    CastingTime = reader.ReadSingle();

                    uint recipeComponentsCount = reader.ReadUInt();
                    for (int i = 0; i < recipeComponentsCount; i++)
                    {
                        RecipeComponent component = new();
                        component.Read(reader);
                        Components.Add(component);
                    }

                    AllowCatalyst = reader.ReadBool();
                    Unused38 = reader.ReadUInt();
                    StackSize = reader.ReadUInt();
                    TrackerAchievementId = reader.ReadUInt();
                    CooldownTotal = reader.ReadUInt();

                    uint categoryCount = reader.ReadUInt();
                    for (int i = 0; i < categoryCount; i++)
                        Categories.Add(reader.ReadUInt());

                    MaxQueue = reader.ReadUInt();
                }

                public void Write(GamePacketWriter writer)
                {
                    writer.Write(RecipeId);
                    writer.Write(RecipeType);
                    writer.Write(SortOrder);
                    writer.Write(Itemid);
                    writer.Write(Quantity);
                    NameId.Write(writer);
                    DescriptionId.Write(writer);
                    writer.Write(IconId);
                    writer.Write(IconTintId);
                    writer.Write(CastingTime);

                    writer.Write((uint)Components.Count);
                    foreach (RecipeComponent component in Components)
                        component.Write(writer);

                    writer.Write(AllowCatalyst);
                    writer.Write(Unused38);
                    writer.Write(StackSize);
                    writer.Write(TrackerAchievementId);
                    writer.Write(CooldownTotal);

                    writer.Write((uint)Categories.Count);
                    foreach (uint category in Categories)
                        writer.Write(category);

                    writer.Write(MaxQueue);
                }
            }

            /// <summary>
            /// Known recipe ids: the recipe manager's set at +0x08 (FUN_142c34a10, cleared first) [P]; the window's
            /// IsLearned column (FUN_142f38860) [P]. The server sends the default-learned recipes and those learned from
            /// recipe items.
            /// </summary>
            public List<uint> KnownRecipeIds { get; set; } = new();
            /// <summary>Cooldowns: the recipe manager's <see cref="PlayerRecipeData"/> map at +0x38 (FUN_142c35bd0) [P]. Sent empty (no recipe has a COOLDOWN_TIME).</summary>
            public List<PlayerRecipeData> Cooldowns { get; set; } = new();
            /// <summary>
            /// Hidden recipe ids: the 0.695 reader FUN_142c1b810 clears the recipe manager's hidden set (player +0x7aa00,
            /// +0x20) and fills it from this list; the crafting window leaves those recipes out (FUN_143611630) and the
            /// client's own start paths refuse them (FUN_142f37f30) [P] (trace/0695/agent-l43.md). Changed in play by
            /// RecipeBase sub 0x1F / 0x20 (<see cref="Game.RecipeBook.RefreshHidden"/>).
            /// </summary>
            public List<uint> HiddenRecipeIds { get; set; } = new();
            public List<Recipe> RecipeList { get; set; } = new();

            public void Read(GamePacketReader reader)
            {
                uint knownCount = reader.ReadUInt();
                for (int i = 0; i < knownCount; i++)
                    KnownRecipeIds.Add(reader.ReadUInt());

                uint cooldownCount = reader.ReadUInt();
                for (int i = 0; i < cooldownCount; i++)
                {
                    PlayerRecipeData cooldown = new();
                    cooldown.Read(reader);
                    Cooldowns.Add(cooldown);
                }

                uint hiddenCount = reader.ReadUInt();
                for (int i = 0; i < hiddenCount; i++)
                    HiddenRecipeIds.Add(reader.ReadUInt());

                uint recipeCount = reader.ReadUInt();
                for (int i = 0; i < recipeCount; i++)
                {
                    Recipe recipe = new();
                    recipe.Read(reader);
                    RecipeList.Add(recipe);
                }
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write((uint)KnownRecipeIds.Count);
                foreach (uint known in KnownRecipeIds)
                    writer.Write(known);

                writer.Write((uint)Cooldowns.Count);
                foreach (PlayerRecipeData cooldown in Cooldowns)
                    cooldown.Write(writer);

                writer.Write((uint)HiddenRecipeIds.Count);
                foreach (uint hidden in HiddenRecipeIds)
                    writer.Write(hidden);

                writer.Write((uint)RecipeList.Count);
                foreach (Recipe recipe in RecipeList)
                    recipe.Write(writer);
            }
        }

        public class Hotbar : IReadable, IWritable
        {
            // sub_142C16C60
            public class HotbarButton : IReadable, IWritable
            {
                public uint HotbarId { get; set; }
                public uint SlotId { get; set; }
                /// <summary>
                /// 0 = None, 1 = ItemId, 2 = ItemGuid
                /// </summary>
                public uint ButtonType { get; set; }
                public ulong ItemId { get; set; }

                public void Read(GamePacketReader reader)
                {
                    HotbarId   = reader.ReadUInt();
                    SlotId     = reader.ReadUInt();
                    ButtonType = reader.ReadUInt();
                    ItemId     = reader.ReadULong();
                }

                public void Write(GamePacketWriter writer)
                {
                    writer.Write(HotbarId);
                    writer.Write(SlotId);
                    writer.Write(ButtonType);
                    writer.Write(ItemId);
                }
            }

            public uint HotbarId { get; set; }
            public List<HotbarButton> HotbarButtons { get; private set; } = new();

            public void Read(GamePacketReader reader)
            {
                HotbarId = reader.ReadUInt();

                uint hotbarButtonsCount = reader.ReadUInt();
                for (int i = 0; i < hotbarButtonsCount; i++)
                {
                    HotbarButton hotbarButton = new();
                    hotbarButton.Read(reader);
                    HotbarButtons.Add(hotbarButton);
                }
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write(HotbarId);

                writer.Write((uint)HotbarButtons.Count);
                foreach (HotbarButton button in HotbarButtons)
                    button.Write(writer);
            }
        }

        public class TitleData : IReadable, IWritable
        {
            public class Title : IReadable, IWritable
            {
                public uint TitleId { get; set; }
                public uint TitleType { get; set; }
                /// <summary>
                /// This is an ID reference to a string in the localisation files
                /// </summary>
                public LandmarkText NameId { get; set; } = new();

                public void Read(GamePacketReader reader)
                {
                    TitleId = reader.ReadUInt();
                    TitleType = reader.ReadUInt();
                    NameId = new LandmarkText(reader.ReadUInt());
                }

                public void Write(GamePacketWriter writer)
                {
                    writer.Write(TitleId);
                    writer.Write(TitleType);
                    NameId.Write(writer);
                }
            }

            public List<Title> TitleList { get; set; } = new();
            public uint CurrentTitleId { get; set; }

            public void Read(GamePacketReader reader)
            {
                uint titleListCount = reader.ReadUInt();
                for (int i = 0; i < titleListCount; i++)
                {
                    Title title = new();
                    title.Read(reader);
                    TitleList.Add(title);
                }

                CurrentTitleId = reader.ReadUInt();
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write((uint)TitleList.Count);
                foreach (Title title in TitleList)
                    title.Write(writer);

                writer.Write(CurrentTitleId);
            }
        }

        /// <summary>
        /// One entry of ClientPcData +0x2b0, a SoeUtil::HashListMap&lt;int,int&gt; (type from the ClientPcData destructor
        /// FUN_142fc8c40; 0.695 reader FUN_142c34f90: u32 key → node +0x18, u32 value → node +0). Never read [P]: apart
        /// from that reader only the constructor FUN_142fc7460 and the destructor touch +0x2b0..+0x2df (exe scan of every
        /// [reg+0x2b0..0x2d0] in ClientPcData's methods 0x142fc0000-0x142fe0000 and of every load of the player global
        /// DAT_145431a20 followed by those offsets; the hits elsewhere are other objects). Live: empty, as sent.
        /// </summary>
        public class IntMap2b0Entry : IReadable, IWritable
        {
            /// <summary>Map key (node +0x18).</summary>
            public uint Key { get; set; }
            /// <summary>Map value (node +0).</summary>
            public uint Value { get; set; }

            public void Read(GamePacketReader reader)
            {
                Key = reader.ReadUInt();
                Value = reader.ReadUInt();
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write(Key);
                writer.Write(Value);
            }
        }

        /// <summary>
        /// The client's seconds progress timer (0.695 reader FUN_142c251e0, 16 bytes): the value of the profile / vehicle
        /// acquire timers, the item trial lock timer and the item line trial timers. Each tick the client adds the seconds
        /// since <see cref="LastUpdateTime"/> to <see cref="Progress"/> (acquire timers scaled by a rate), caps it at
        /// <see cref="ExpireSec"/> and stamps LastUpdateTime (FUN_142e36a60 / FUN_142e36b00, items FUN_142f014d0); the
        /// percentage is Progress × 100 / ExpireSec, 100 when ExpireSec ≤ 0. Names from the client's own dumps
        /// "expireSec=%d, progress=%d, progressPcnt=%d" (FUN_142e36780, FUN_142f02740) [P]. A LastUpdateTime of 0 makes
        /// the first tick fill the timer at once (now − 0 seconds).
        /// </summary>
        public class ProgressTimer : IReadable, IWritable
        {
            /// <summary>+0: the timer's length in seconds.</summary>
            public uint ExpireSec { get; set; }
            /// <summary>+4: seconds done so far (at most <see cref="ExpireSec"/>).</summary>
            public uint Progress { get; set; }
            /// <summary>+8: the server time (seconds) <see cref="Progress"/> was last advanced to.</summary>
            public ulong LastUpdateTime { get; set; }

            public void Read(GamePacketReader reader)
            {
                ExpireSec = reader.ReadUInt();
                Progress = reader.ReadUInt();
                LastUpdateTime = reader.ReadULong();
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write(ExpireSec);
                writer.Write(Progress);
                writer.Write(LastUpdateTime);
            }
        }

        /// <summary>
        /// The client's millisecond progress timer (0.695 reader FUN_142c25130, 24 bytes): the item lock timer and the item
        /// line timers. Ticked by FUN_142f014d0 on the millisecond clock: Progress += ms since <see cref="LastUpdateMsec"/>,
        /// capped at <see cref="ExpireMsec"/>; both clocks are stamped. Names from the item timer dump FUN_142f00460 /
        /// FUN_142f025b0 ("expireMsec=%d, progress=%d, progressPcnt=%d") [P].
        /// </summary>
        public class ProgressTimerMsec : IReadable, IWritable
        {
            /// <summary>+0: the timer's length in milliseconds.</summary>
            public uint ExpireMsec { get; set; }
            /// <summary>+4: milliseconds done so far.</summary>
            public uint Progress { get; set; }
            /// <summary>+8: the server millisecond clock at the last advance.</summary>
            public ulong LastUpdateMsec { get; set; }
            /// <summary>+0x10: the server seconds clock at the last advance (stamped with +8, FUN_142f014d0).</summary>
            public ulong LastUpdateTime { get; set; }

            public void Read(GamePacketReader reader)
            {
                ExpireMsec = reader.ReadUInt();
                Progress = reader.ReadUInt();
                LastUpdateMsec = reader.ReadULong();
                LastUpdateTime = reader.ReadULong();
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write(ExpireMsec);
                writer.Write(Progress);
                writer.Write(LastUpdateMsec);
                writer.Write(LastUpdateTime);
            }
        }

        /// <summary>
        /// AcquireTimers::ClientAcquireTimerManager (player +0x75be8; 0.695 reader FUN_142c169d0 into +0x75bf0): how long
        /// until a profile or vehicle may be acquired. AcquireTimersBase 0xDE SetAcquireTimerManager (FUN_142e34f70) reads
        /// the same block. The client only ticks the timers (ClientPcData::GiveTime FUN_142fce9a0 → FUN_142e36150) and
        /// prints them for the admin list (FUN_142e35840 "Profile acquire timers:" / "Vehicle acquire timers:"). Sent empty;
        /// live empty too.
        /// </summary>
        public class AcquireTimerManager : IReadable, IWritable
        {
            /// <summary>
            /// HashListMap&lt;StrongType&lt;int,SignatureProfileId&gt;, AcquireTimers::ProfileAcquireTimer&gt; element
            /// (FUN_142c3bf00; manager +8).
            /// </summary>
            public class ProfileAcquireTimer : IReadable, IWritable
            {
                /// <summary>Map key, the profile id (node +0x28).</summary>
                public uint Key { get; set; }
                /// <summary>The timer (node +0).</summary>
                public ProgressTimer Timer { get; set; } = new();
                /// <summary>Node +0x10: the profile id the admin dump prints ("profileId=%d").</summary>
                public uint ProfileId { get; set; }

                public void Read(GamePacketReader reader)
                {
                    Key = reader.ReadUInt();
                    Timer.Read(reader);
                    ProfileId = reader.ReadUInt();
                }

                public void Write(GamePacketWriter writer)
                {
                    writer.Write(Key);
                    Timer.Write(writer);
                    writer.Write(ProfileId);
                }
            }

            /// <summary>
            /// HashListMap&lt;Vehicles::VehicleFactionKey, AcquireTimers::VehicleAcquireTimer&gt; element (FUN_142c3f7f0, value
            /// FUN_142c301a0; manager +0xb0). Landmark has no vehicles.
            /// </summary>
            public class VehicleAcquireTimer : IReadable, IWritable
            {
                /// <summary>VehicleFactionKey vehicle id (node +0x28).</summary>
                public uint KeyVehicleId { get; set; }
                /// <summary>VehicleFactionKey faction id, one signed byte (node +0x2c).</summary>
                public byte KeyFactionId { get; set; }
                /// <summary>The timer (node +0).</summary>
                public ProgressTimer Timer { get; set; } = new();
                /// <summary>Node +0x10: "vehicleId=%d" in the admin dump.</summary>
                public uint VehicleId { get; set; }
                /// <summary>Node +0x14, one signed byte: "factionId=%d" in the admin dump.</summary>
                public byte FactionId { get; set; }

                public void Read(GamePacketReader reader)
                {
                    KeyVehicleId = reader.ReadUInt();
                    KeyFactionId = reader.ReadByte();
                    Timer.Read(reader);
                    VehicleId = reader.ReadUInt();
                    FactionId = reader.ReadByte();
                }

                public void Write(GamePacketWriter writer)
                {
                    writer.Write(KeyVehicleId);
                    writer.Write(KeyFactionId);
                    Timer.Write(writer);
                    writer.Write(VehicleId);
                    writer.Write(FactionId);
                }
            }

            public List<ProfileAcquireTimer> ProfileTimers { get; set; } = new();
            public List<VehicleAcquireTimer> VehicleTimers { get; set; } = new();
            /// <summary>
            /// Manager +0x160 (player +0x75d48), one byte. Never read [P]: only the readers (FUN_142c169d0, FUN_142e34f70)
            /// and the constructor FUN_142e353d0 (sets 0) touch it (exe scan of the acquire timer code 0x142e34000-0x142e37000
            /// and of every player-global load). Live 0x80, kept.
            /// </summary>
            public byte Flags160 { get; set; } = 128;

            public void Read(GamePacketReader reader)
            {
                uint profileTimersCount = reader.ReadUInt();
                for (int i = 0; i < profileTimersCount; i++)
                {
                    ProfileAcquireTimer timer = new();
                    timer.Read(reader);
                    ProfileTimers.Add(timer);
                }

                uint vehicleTimersCount = reader.ReadUInt();
                for (int i = 0; i < vehicleTimersCount; i++)
                {
                    VehicleAcquireTimer timer = new();
                    timer.Read(reader);
                    VehicleTimers.Add(timer);
                }

                Flags160 = reader.ReadByte();
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write((uint)ProfileTimers.Count);
                foreach (ProfileAcquireTimer timer in ProfileTimers)
                    timer.Write(writer);

                writer.Write((uint)VehicleTimers.Count);
                foreach (VehicleAcquireTimer timer in VehicleTimers)
                    timer.Write(writer);

                writer.Write(Flags160);
            }
        }

        /// <summary>
        /// One currency discount: the currency manager's HashListMap&lt;int,float,8&gt; at player +0x75d68 (0.695 reader
        /// FUN_142c35460: u32 key → node +0x18, f32 → node +0), also set by CurrencyBase 0xE5 sub 1 SetCurrencyDiscount.
        /// Prices in a currency are price × (1 − discount): ≤ 0 no discount, ≥ 1 free (FUN_142f1d000, used by the item price
        /// code FUN_142f1d060 and FUN_1435655e0). Names from the client's dump "currencyType=%d, discount=%f"
        /// (FUN_142e4f9c0) [P]. Sent empty; live empty.
        /// </summary>
        public class CurrencyDiscount : IReadable, IWritable
        {
            /// <summary>Currency.txt id.</summary>
            public uint CurrencyType { get; set; }
            /// <summary>Fraction off, 0..1.</summary>
            public float Discount { get; set; }

            public void Read(GamePacketReader reader)
            {
                CurrencyType = reader.ReadUInt();
                Discount = reader.ReadSingle();
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write(CurrencyType);
                writer.Write(Discount);
            }
        }

        /// <summary>
        /// A rate tier, 5 × u32: the currency rate tier (player +0x75dd0 = currency manager +0x68; 0.695 reader
        /// FUN_142c3d710, the same 20 bytes as CurrencyBase 0xE5 sub 2 SetCurrencyRateTier FUN_142e4ff80 / FUN_142e4f3b0)
        /// and the experience rate tier (+0x75fc0 = experience manager +0xa8; FUN_142c3de40, as ExperienceBase 0xBE sub 0
        /// SetExperienceRateTier FUN_142ed7ed0 / FUN_142ed5240). The shape is the client's SkillPointRateTier record
        /// (loader FUN_143cba540 / FUN_143cba680 "tierValueMin", "tierMappedValue", "descriptor"; descriptor FUN_143cba8a0
        /// NAME_ID, DESCRIPTION_ID, ICON_ID; table SkillPointRates.txt TIER_VALUE_MIN^SEC_PER_SKILL_POINT^NAME_ID^
        /// DESCRIPTION_ID^ICON_ID) [P]. Never read [P]: after either packet the client only calls FUN_142fd4280, which is
        /// empty, and nothing else reads the offsets (exe displacement scans for +0x75dd0..+0x75de0 and +0x75fc0..+0x75fd0:
        /// only the reader and the constructor FUN_142fc7460). Live: all zero, as sent.
        /// </summary>
        public class RateTier : IReadable, IWritable
        {
            /// <summary>+0 "tierValueMin": the lowest value this tier applies from.</summary>
            public uint TierValueMin { get; set; }
            /// <summary>+4 "tierMappedValue": the tier's rate (SEC_PER_SKILL_POINT for skill point tiers).</summary>
            public uint TierMappedValue { get; set; }
            /// <summary>+8 descriptor NAME_ID (text id).</summary>
            public uint NameId { get; set; }
            /// <summary>+0xc descriptor DESCRIPTION_ID (text id).</summary>
            public uint DescriptionId { get; set; }
            /// <summary>+0x10 descriptor ICON_ID.</summary>
            public uint IconId { get; set; }

            public void Read(GamePacketReader reader)
            {
                TierValueMin = reader.ReadUInt();
                TierMappedValue = reader.ReadUInt();
                NameId = reader.ReadUInt();
                DescriptionId = reader.ReadUInt();
                IconId = reader.ReadUInt();
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write(TierValueMin);
                writer.Write(TierMappedValue);
                writer.Write(NameId);
                writer.Write(DescriptionId);
                writer.Write(IconId);
            }
        }

        /// <summary>
        /// DefinitionFilters::ClientDefinitionVariableManager (player +0x75df0), first map (+0x75e00):
        /// HashListMap&lt;DefinitionVariableKey, DefinitionVariable&gt; (RTTI; 0.695 reader FUN_142c3dae0: key u32 → node +0x18,
        /// u64 → +0x20; value f32 → +0, f32 → +4). A server-side override of one definition's value, e.g. an item's stack
        /// size: the client's lookups (FUN_143c2caa0, FUN_143c2cb40, FUN_143c2d900 from the item code FUN_142f1d060,
        /// FUN_142f1d360, FUN_1433cd1e0, FUN_142eb4800) return Base + Addend, rounded, when an entry exists and the
        /// definition's own value otherwise. Key names from the manager's logs "variableId=%d, definitionId=%I64d"
        /// (FUN_143c2cdd0 and the other DefinitionVariableManager functions) [P]. Sent empty; live empty.
        /// </summary>
        public class DefinitionVariable : IReadable, IWritable
        {
            /// <summary>The variable (a string-hash id, e.g. 0x9d31da22 in FUN_143c2caa0).</summary>
            public uint VariableId { get; set; }
            /// <summary>The definition it overrides (e.g. an item definition id).</summary>
            public ulong DefinitionId { get; set; }
            /// <summary>
            /// Node +0: the base value. Base and addend are only ever added together by the client; that +0 is the base
            /// and +4 the addend follows SetDefinitionVariableBase / SetDefinitionVariableAddend / ...BaseAndAddend
            /// (FUN_143c2e220, FUN_143c2e130, FUN_143c2e310) [I].
            /// </summary>
            public float Base { get; set; }
            /// <summary>Node +4: the addend (see <see cref="Base"/>).</summary>
            public float Addend { get; set; }

            public void Read(GamePacketReader reader)
            {
                VariableId = reader.ReadUInt();
                DefinitionId = reader.ReadULong();
                Base = reader.ReadSingle();
                Addend = reader.ReadSingle();
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write(VariableId);
                writer.Write(DefinitionId);
                writer.Write(Base);
                writer.Write(Addend);
            }
        }

        /// <summary>
        /// ClientDefinitionVariableManager second map (+0x75e30): HashListMap&lt;DefinitionVariableKey,
        /// HashListMap&lt;int,int,4&gt;&gt; (RTTI; 0.695 reader FUN_142c3d900, inner pairs FUN_142c34e50) — the int sets of the
        /// int-list variables, changed by DefinitionVariableManager::AddDefinitionInt(s) / RemoveDefinitionInt
        /// (FUN_143c2cdd0, FUN_143c2cea0, FUN_143c2de40, entry lookup FUN_143c2d3e0), which drop the entry when its map is
        /// empty [P]. Sent empty; live empty.
        /// </summary>
        public class DefinitionInts : IReadable, IWritable
        {
            /// <summary>One pair of the inner HashListMap&lt;int,int&gt;. Only the add / remove virtuals of the variable's
            /// definition use it, so which int is a key and which a count is not shown by any reader yet [I].</summary>
            public class DefinitionInt : IReadable, IWritable
            {
                /// <summary>Inner map key.</summary>
                public uint Key { get; set; }
                /// <summary>Inner map value.</summary>
                public uint Value { get; set; }

                public void Read(GamePacketReader reader)
                {
                    Key = reader.ReadUInt();
                    Value = reader.ReadUInt();
                }

                public void Write(GamePacketWriter writer)
                {
                    writer.Write(Key);
                    writer.Write(Value);
                }
            }

            /// <summary>DefinitionVariableKey variable id (node +0x58).</summary>
            public uint VariableId { get; set; }
            /// <summary>DefinitionVariableKey definition id (node +0x60).</summary>
            public ulong DefinitionId { get; set; }
            public List<DefinitionInt> Ints { get; set; } = new();

            public void Read(GamePacketReader reader)
            {
                VariableId = reader.ReadUInt();
                DefinitionId = reader.ReadULong();

                uint intsCount = reader.ReadUInt();
                for (int i = 0; i < intsCount; i++)
                {
                    DefinitionInt entry = new();
                    entry.Read(reader);
                    Ints.Add(entry);
                }
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write(VariableId);
                writer.Write(DefinitionId);

                writer.Write((uint)Ints.Count);
                foreach (DefinitionInt entry in Ints)
                    entry.Write(writer);
            }
        }

        /// <summary>
        /// One element of the equipment records, player +0x75e70 HashListMap&lt;int, Equipment::CharacterEquipmentRecord,16&gt;
        /// (RTTI; 0.695 reader FUN_142c36ad0: u32 key → node +0x1e0, then the record FUN_142c187f0; 0.589 FUN_142b9cf30 /
        /// FUN_142b7f040 the same). The record per profile of what is worn in each equipment slot; EquipmentBase 0xCB sub 2
        /// SetCharacterEquipmentSlot (FUN_142e42da0) creates / updates it, and the bag / repair code finds the worn item
        /// through it (FUN_142fcd5d0, FUN_142fd0a70, FUN_142fd33a0). Until 2026-10-18 this model had a second record list
        /// after the record that neither client reads (never on the wire: the list is sent empty). Sent empty; live empty.
        /// </summary>
        public class CharacterEquipmentRecord : IReadable, IWritable
        {
            /// <summary>
            /// Equipment::EquipmentSlotRecord, element of the record's HashListMap&lt;StrongType&lt;int,EquipSlotId&gt;,
            /// EquipmentSlotRecord,16&gt; at +0x88 (RTTI in the record constructor FUN_142e41f60; reader FUN_142c3bb80 →
            /// FUN_142c1f840). The same fields EquipmentPacketSetCharacterEquipmentSlot carries (FUN_142e41b40 → copied by
            /// FUN_142e425e0).
            /// </summary>
            public class EquipmentSlotRecord : IReadable, IWritable
            {
                /// <summary>Map key, the EquipmentSlotDefinitions id (node +0xc0).</summary>
                public uint Key { get; set; }
                /// <summary>Node +0x20: the slot id again (FUN_142fd33a0 reads it).</summary>
                public uint EquipSlotId { get; set; }
                /// <summary>Node +0x28: the worn item's guid; FUN_142d08300 indexes the slots by it, the item lookups use it.</summary>
                public ulong ItemGuid { get; set; }
                /// <summary>Node +0x30, a StringFixed&lt;32&gt;: no reader found yet besides the copies (open lead).</summary>
                public string String30 { get; set; } = "";
                /// <summary>Node +0x70, a StringFixed&lt;32&gt;: no reader found yet besides the copies (open lead).</summary>
                public string String70 { get; set; } = "";

                public void Read(GamePacketReader reader)
                {
                    Key = reader.ReadUInt();
                    EquipSlotId = reader.ReadUInt();
                    ItemGuid = reader.ReadULong();
                    String30 = reader.ReadString();
                    String70 = reader.ReadString();
                }

                public void Write(GamePacketWriter writer)
                {
                    writer.Write(Key);
                    writer.Write(EquipSlotId);
                    writer.Write(ItemGuid);
                    writer.Write(String30);
                    writer.Write(String70);
                }
            }

            /// <summary>Map key, the profile id (node +0x1e0; FUN_142e42da0 looks the record up by the slot packet's profile).</summary>
            public uint Key { get; set; }
            /// <summary>Record +0: the profile id (FUN_142e41f60 stores the profile here when it creates a record).</summary>
            public uint ProfileId { get; set; }
            /// <summary>Record +8, a StringFixed&lt;32&gt;: no reader found yet (open lead).</summary>
            public string String08 { get; set; } = "";
            /// <summary>Record +0x48, a StringFixed&lt;32&gt;: no reader found yet (open lead).</summary>
            public string String48 { get; set; } = "";
            public List<EquipmentSlotRecord> Slots { get; set; } = new();

            public void Read(GamePacketReader reader)
            {
                Key = reader.ReadUInt();
                ProfileId = reader.ReadUInt();
                String08 = reader.ReadString();
                String48 = reader.ReadString();

                uint slotsCount = reader.ReadUInt();
                for (int i = 0; i < slotsCount; i++)
                {
                    EquipmentSlotRecord slot = new();
                    slot.Read(reader);
                    Slots.Add(slot);
                }
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write(Key);
                writer.Write(ProfileId);
                writer.Write(String08);
                writer.Write(String48);

                writer.Write((uint)Slots.Count);
                foreach (EquipmentSlotRecord slot in Slots)
                    slot.Write(writer);
            }
        }

        /// <summary>
        /// One element of the experience records, player +0x75f18 HashListMap&lt;int, Experience::ExperienceRecord,16&gt; (RTTI
        /// in the manager constructor FUN_142ed5770; 0.695 reader FUN_142c373e0: u32 key → node +0x28, record FUN_142c1fa30
        /// 5 × u32). ExperienceBase 0xBE sub 1 SetExperience (FUN_142ed77a0) replaces a record whole. Live: one record,
        /// key 0, level 1; the client treats a missing record as level 1 too (FUN_142cd2150), so the server sends none.
        /// </summary>
        public class ExperienceRecord : IReadable, IWritable
        {
            /// <summary>
            /// Map key (node +0x28). With ClientGameSettings.SharedExperienceRecord set (live 1) the client uses the first
            /// record whatever its key (FUN_142cd2150, FUN_14370f7d0).
            /// </summary>
            public uint Key { get; set; }
            /// <summary>Record +0: the record's id; SetExperience finds the record to replace by it (FUN_142ed77a0).</summary>
            public uint RecordId { get; set; }
            /// <summary>Record +4: total experience ("Bad xp message, prevXp=%d, newXp=%d", "You have received %d xp.", OnXPReceived).</summary>
            public uint Xp { get; set; }
            /// <summary>Record +8: never read [P] — the record's readers (FUN_142cd2150, FUN_14370f7d0, FUN_14370f8c0, FUN_14370f9b0, FUN_143119140) read only +0xc, SetExperience only copies it.</summary>
            public uint Slot08 { get; set; }
            /// <summary>
            /// Record +0xc: the level (rank). FUN_142cd2150 returns it (1 when there is no record), the level
            /// requirements read it (FUN_14370f7d0, FUN_14370f8c0, FUN_14370f9b0), a profile row shows it as "JobLevel"
            /// (FUN_143119140) and OnPlayerLevelUp gets it (FUN_142ed6ee0).
            /// </summary>
            public uint Level { get; set; } = 1;
            /// <summary>Record +0x10: never read [P] — the record's readers (FUN_142cd2150, FUN_14370f7d0, FUN_14370f8c0, FUN_14370f9b0, FUN_143119140) read only +0xc, SetExperience only copies it.</summary>
            public uint Slot10 { get; set; }

            public void Read(GamePacketReader reader)
            {
                Key = reader.ReadUInt();
                RecordId = reader.ReadUInt();
                Xp = reader.ReadUInt();
                Slot08 = reader.ReadUInt();
                Level = reader.ReadUInt();
                Slot10 = reader.ReadUInt();
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write(Key);
                writer.Write(RecordId);
                writer.Write(Xp);
                writer.Write(Slot08);
                writer.Write(Level);
                writer.Write(Slot10);
            }
        }

        /// <summary>
        /// The player's grief state (Game/Grief.cs): read by FUN_142c324d0 (0.695; 0.589 FUN_142b98950) into player
        /// +0x76920 as three i32 — grief points (+0x76920), damage points (+0x76924) and grief level (+0x76928, the value
        /// the client's equipment-lock checks read: level ≥ 4 locks) — the same object GriefBase 0xC0 updates later. The
        /// client's own dump of it (0.589 FUN_142e43250) names them "griefPoints", "damagePoints", "griefLevel" [P].
        /// </summary>
        public class GriefData : IReadable, IWritable
        {
            public int Points { get; set; }
            public int DamagePoints { get; set; }
            public int Level { get; set; }

            public void Read(GamePacketReader reader)
            {
                Points = (int)reader.ReadUInt();
                DamagePoints = (int)reader.ReadUInt();
                Level = (int)reader.ReadUInt();
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write((uint)Points);
                writer.Write((uint)DamagePoints);
                writer.Write((uint)Level);
            }
        }

        /// <summary>
        /// The item timer manager (player +0x76938; 0.695 reader FUN_142c21030), the block ItemsBase 0xE6
        /// SetItemTimerManager also carries (FUN_142f02070 → FUN_142eff600). Map types from its constructor FUN_142effc20
        /// (HashListMap&lt;StrongType&lt;int,ItemLineId&gt;, Items::ItemLineTimer,8&gt; and ...Items::ItemLineTrialTimer,8&gt;);
        /// field names from the client's dump FUN_142f00460 ("Item lock timer", "Item line timers", "Item trial lock
        /// timer", "Item trial timers") [P]. Ticked by ClientPcData::GiveTime (FUN_142fce9a0 → FUN_142f014d0). Sent
        /// empty / zero; live the same.
        /// </summary>
        public class ItemTimerManager : IReadable, IWritable
        {
            /// <summary>Items::ItemLineTimer element (FUN_142c3bc60; map at +0x18); added in play by ItemPacketAddItemLineTimer (FUN_142f002a0).</summary>
            public class ItemLineTimer : IReadable, IWritable
            {
                /// <summary>Map key, the item line id (node +0x30).</summary>
                public uint Key { get; set; }
                /// <summary>The timer (node +0).</summary>
                public ProgressTimerMsec Timer { get; set; } = new();
                /// <summary>Node +0x18: "itemLineId=%d" in the dump (FUN_142f025b0).</summary>
                public uint ItemLineId { get; set; }

                public void Read(GamePacketReader reader)
                {
                    Key = reader.ReadUInt();
                    Timer.Read(reader);
                    ItemLineId = reader.ReadUInt();
                }

                public void Write(GamePacketWriter writer)
                {
                    writer.Write(Key);
                    Timer.Write(writer);
                    writer.Write(ItemLineId);
                }
            }

            /// <summary>Items::ItemLineTrialTimer element (FUN_142c3bdd0, value FUN_142c20dd0; map at +0x90).</summary>
            public class ItemLineTrialTimer : IReadable, IWritable
            {
                /// <summary>Map key, the item line id (node +0x28).</summary>
                public uint Key { get; set; }
                /// <summary>The timer (node +0).</summary>
                public ProgressTimer Timer { get; set; } = new();
                /// <summary>Node +0x10: "itemLineId=%d" in the dump (FUN_142f02740).</summary>
                public uint ItemLineId { get; set; }
                /// <summary>Node +0x14: "type=%d" in the dump (FUN_142f02740).</summary>
                public uint Type { get; set; }

                public void Read(GamePacketReader reader)
                {
                    Key = reader.ReadUInt();
                    Timer.Read(reader);
                    ItemLineId = reader.ReadUInt();
                    Type = reader.ReadUInt();
                }

                public void Write(GamePacketWriter writer)
                {
                    writer.Write(Key);
                    Timer.Write(writer);
                    writer.Write(ItemLineId);
                    writer.Write(Type);
                }
            }

            /// <summary>+0: the item lock timer (set in play by FUN_142f01f40).</summary>
            public ProgressTimerMsec ItemLockTimer { get; set; } = new();
            public List<ItemLineTimer> ItemLineTimers { get; set; } = new();
            /// <summary>+0x80: the item trial lock timer (ItemPacketSetItemTrialLockTimer, FUN_142f020f0).</summary>
            public ProgressTimer ItemTrialLockTimer { get; set; } = new();
            public List<ItemLineTrialTimer> ItemLineTrialTimers { get; set; } = new();
            /// <summary>
            /// +0x100, flag bits: 0x40 stops the manager calling its change listener at +0xf8 (FUN_142f00220, FUN_142f002a0,
            /// FUN_142f01f40, FUN_142f020f0) [P]. Only the constructor FUN_142effc20 writes that listener pointer (null; exe
            /// scan of the item timer code 0x142eff000-0x142f03500 and of the player offsets), so the byte has no visible
            /// effect. Live 0x80, kept.
            /// </summary>
            public byte ListenerFlags { get; set; } = 128;

            public void Read(GamePacketReader reader)
            {
                ItemLockTimer.Read(reader);

                uint itemLineTimersCount = reader.ReadUInt();
                for (int i = 0; i < itemLineTimersCount; i++)
                {
                    ItemLineTimer timer = new();
                    timer.Read(reader);
                    ItemLineTimers.Add(timer);
                }

                ItemTrialLockTimer.Read(reader);

                uint itemLineTrialTimersCount = reader.ReadUInt();
                for (int i = 0; i < itemLineTrialTimersCount; i++)
                {
                    ItemLineTrialTimer timer = new();
                    timer.Read(reader);
                    ItemLineTrialTimers.Add(timer);
                }

                ListenerFlags = reader.ReadByte();
            }

            public void Write(GamePacketWriter writer)
            {
                ItemLockTimer.Write(writer);

                writer.Write((uint)ItemLineTimers.Count);
                foreach (ItemLineTimer timer in ItemLineTimers)
                    timer.Write(writer);

                ItemTrialLockTimer.Write(writer);

                writer.Write((uint)ItemLineTrialTimers.Count);
                foreach (ItemLineTrialTimer timer in ItemLineTrialTimers)
                    timer.Write(writer);

                writer.Write(ListenerFlags);
            }
        }

        public class Loadout : IReadable, IWritable
        {
            // sub_142C217F0. Field names from the 0.695 client record FUN_142f06ef0 (Character.ClassLoadouts data source):
            // +0x20 LoadoutId, +0x24 ProfileId, +0x28 name, +0x40 ActiveWeaponSet, ActiveAbility1-4, PassiveAbility1-5,
            // EquipmentSlot1-n (trace/0695/agent-l138-a.md §3). Live sent loadout 1, profile 46, "Default Loadout".
            public class LoadoutRecord : IReadable, IWritable
            {
                public uint LoadoutId { get; set; } = 1;
                public uint ProfileId { get; set; } = 46;
                public string Name { get; set; } = "Default Loadout";
                public uint ActiveWeaponSet { get; set; }
                public List<uint> ActiveAbilities { get; set; } = new uint[4].ToList();
                public List<uint> PassiveAbilities { get; set; } = new uint[5].ToList();
                public List<ulong> EquipmentSlots { get; set; } = new ulong[16].ToList();

                public void Read(GamePacketReader reader)
                {
                    LoadoutId = reader.ReadUInt();
                    ProfileId = reader.ReadUInt();
                    Name = reader.ReadString();
                    ActiveWeaponSet = reader.ReadUInt();

                    ActiveAbilities = new List<uint>();
                    uint activeCount = reader.ReadUInt();
                    for (int i = 0; i < activeCount; i++)
                        ActiveAbilities.Add(reader.ReadUInt());

                    PassiveAbilities = new List<uint>();
                    uint passiveCount = reader.ReadUInt();
                    for (int i = 0; i < passiveCount; i++)
                        PassiveAbilities.Add(reader.ReadUInt());

                    EquipmentSlots = new List<ulong>();
                    uint equipmentCount = reader.ReadUInt();
                    for (int i = 0; i < equipmentCount; i++)
                        EquipmentSlots.Add(reader.ReadULong());
                }

                public void Write(GamePacketWriter writer)
                {
                    writer.Write(LoadoutId);
                    writer.Write(ProfileId);
                    writer.Write(Name);
                    writer.Write(ActiveWeaponSet);

                    writer.Write((uint)ActiveAbilities.Count);
                    foreach (uint ability in ActiveAbilities)
                        writer.Write(ability);

                    writer.Write((uint)PassiveAbilities.Count);
                    foreach (uint passive in PassiveAbilities)
                        writer.Write(passive);

                    writer.Write((uint)EquipmentSlots.Count);
                    foreach (ulong equipment in EquipmentSlots)
                        writer.Write(equipment);
                }
            }

            // One entry of the class loadouts map (0.695 FUN_142c3e380 into player +0x76a58, the object at +0x76a50 that owns
            // the "Character.ClassLoadouts" data source) [P]: u32 key → node +0x08 (hash key, & 7 buckets), then the record
            // (FUN_142c217f0) into the 0x148-byte node.

            /// <summary>
            /// Node +0x08: the map key, the loadout id: the weapon-set swap finds the active loadout by it (FUN_1435f05b0:
            /// key == FUN_143c50460's active loadout id, else "No active loadout!  Weapon swap failed.") [P]. Live 1 =
            /// <see cref="LoadoutRecord.LoadoutId"/>.
            /// </summary>
            public uint LoadoutId { get; set; } = 1;
            /// <summary>The loadout itself (<see cref="LoadoutRecord"/>).</summary>
            public LoadoutRecord Record { get; set; } = new();

            public void Read(GamePacketReader reader)
            {
                LoadoutId = reader.ReadUInt();

                Record.Read(reader);
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write(LoadoutId);

                Record.Write(writer);
            }
        }

        /// <summary>
        /// The mission instances (<c>Missions::ClientMissionInstanceManager</c>, player +0x76db8; 0.695 reader FUN_142c32400,
        /// 0.589 FUN_142b98880) [P]: five maps, read in this order —
        /// <list type="number">
        /// <item>+0x08 <c>HashListMap&lt;Missions::MissionComponentDataKey,int,8&gt;</c> (FUN_142c3e7c0): key, i32 value;</item>
        /// <item>+0x70 the same with a float value (FUN_142c3e930);</item>
        /// <item>+0xd8 the same with an __int64 value (FUN_142c3eaa0);</item>
        /// <item>+0x140 <c>HashListMap&lt;Missions::MissionInstanceKey,Missions::MissionInstance,64&gt;</c> (FUN_142c3ee10):
        /// the available missions (MissionPacketAddAvailableMission → FUN_143c51260 adds at manager +0x140);</item>
        /// <item>+0x368 the same with 8 buckets (FUN_142c3ed40): the active missions
        /// (MissionPacketAddActiveMission → "MissionInstanceManager::AddOrUpdateActiveMission", FUN_143c510b0 at +0x368).</item>
        /// </list>
        /// Live sent all five empty (the live SendSelf capture); the server does the same.
        /// </summary>
        public class MissionInstanceManager : IReadable, IWritable
        {
            /// <summary>
            /// <c>Missions::MissionInstanceKey</c> (FUN_142c22260) [P]: u32 MissionId (the "missing definition for
            /// missionId=%d" check reads it), u64, i8 (read as a signed char into an int). The maps compare all three
            /// (FUN_143c510b0 / FUN_143c51260). Names from the client's mission dump ("Client mission info", FUN_142f0b8c0 →
            /// FUN_143c50980: "missionId=%d, missionInstanceId=%I64d, factionId=%d, ...", printing an instance's own key).
            /// </summary>
            public class MissionInstanceKey : IReadable, IWritable
            {
                /// <summary>+0x0 mission id (Missions.txt MISSION_ID; "missionId=%d") [P].</summary>
                public uint MissionId { get; set; }
                /// <summary>+0x8 the instance's id ("missionInstanceId=%I64d") [P].</summary>
                public ulong MissionInstanceId { get; set; }
                /// <summary>
                /// +0x10 faction id ("factionId=%d"; the default is the client's "no faction" value DAT_1455fe51c, the one
                /// the actor faction +0x1448 is compared with) [P].
                /// </summary>
                public sbyte FactionId { get; set; }

                public void Read(GamePacketReader reader)
                {
                    MissionId = reader.ReadUInt();
                    MissionInstanceId = reader.ReadULong();
                    FactionId = (sbyte)reader.ReadByte();
                }

                public void Write(GamePacketWriter writer)
                {
                    writer.Write(MissionId);
                    writer.Write(MissionInstanceId);
                    writer.Write((byte)FactionId);
                }
            }

            /// <summary>
            /// <c>Missions::MissionComponentDataKey</c> [P]: a <see cref="MissionInstanceKey"/>, then u32, u32 (readers
            /// FUN_142c3e7c0 / FUN_142c3e930 / FUN_142c3eaa0, which are also the only code touching the three component maps
            /// besides their containers: no 0.695 code looks a value up).
            /// </summary>
            public class MissionComponentDataKey : IReadable, IWritable
            {
                public MissionInstanceKey Instance { get; set; } = new();
                /// <summary>
                /// +0x18 mission component id: its default is the "no component" value DAT_1455fe58c, the one
                /// MissionComponents.txt / MissionComponentMap.txt (MISSION_COMP_ID) definitions start from
                /// (FUN_143bd7f30 / FUN_143bd8010) [P]. Never read [P].
                /// </summary>
                public uint MissionComponentId { get; set; }
                /// <summary>+0x1c u32 (default 0). Never read [P].</summary>
                public uint Slot1C { get; set; }

                public void Read(GamePacketReader reader)
                {
                    Instance.Read(reader);
                    MissionComponentId = reader.ReadUInt();
                    Slot1C = reader.ReadUInt();
                }

                public void Write(GamePacketWriter writer)
                {
                    Instance.Write(writer);
                    writer.Write(MissionComponentId);
                    writer.Write(Slot1C);
                }
            }

            /// <summary>An int mission component value.</summary>
            public class ComponentInt : IReadable, IWritable
            {
                public MissionComponentDataKey Key { get; set; } = new();
                public int Value { get; set; }

                public void Read(GamePacketReader reader) { Key.Read(reader); Value = reader.ReadInt(); }
                public void Write(GamePacketWriter writer) { Key.Write(writer); writer.Write(Value); }
            }

            /// <summary>A float mission component value.</summary>
            public class ComponentFloat : IReadable, IWritable
            {
                public MissionComponentDataKey Key { get; set; } = new();
                public float Value { get; set; }

                public void Read(GamePacketReader reader) { Key.Read(reader); Value = reader.ReadSingle(); }
                public void Write(GamePacketWriter writer) { Key.Write(writer); writer.Write(Value); }
            }

            /// <summary>An __int64 mission component value.</summary>
            public class ComponentInt64 : IReadable, IWritable
            {
                public MissionComponentDataKey Key { get; set; } = new();
                public long Value { get; set; }

                public void Read(GamePacketReader reader) { Key.Read(reader); Value = reader.ReadLong(); }
                public void Write(GamePacketWriter writer) { Key.Write(writer); writer.Write(Value); }
            }

            /// <summary>
            /// A <c>Missions::MissionInstance</c> with its map key [P]: the key (FUN_142c22260), then FUN_142c3ecb0: the
            /// instance's own key (+0x30), 3 × u32 (FUN_142c221d0 → +0x48), u32 +0x54 (the value AddOrUpdate*Mission
            /// compares and replaces), 6 × u32 (FUN_142c222f0 → +0x60, +0x64, +0x70..+0x7c).
            /// <para>
            /// What reads an instance (0.695) [P]: the add / update paths (FUN_143c510b0 / FUN_143c51260, also behind
            /// MissionPacketAddActiveMission / AddAvailableMission, FUN_142f0afc0 / FUN_142f0b0b0, which read the same
            /// fields) compare <see cref="Priority"/>; the list order (FUN_143c50b90 / FUN_143c50c60) is the definition's
            /// priority bracket, then <see cref="Priority"/>, then the mission id; the mission dump FUN_143c50980 prints the
            /// key, priority, zone, region and location x / y / z. The manager's change callbacks
            /// (<c>Missions::ClientMissionInstanceManager</c> vtable 1446ded20) are all empty, so nothing else does.
            /// </para>
            /// </summary>
            public class MissionInstance : IReadable, IWritable
            {
                /// <summary>The map key (node +0x90 .. +0xa0).</summary>
                public MissionInstanceKey Key { get; set; } = new();
                /// <summary>+0x30 the instance's own key, the one the dump prints [P].</summary>
                public MissionInstanceKey Instance { get; set; } = new();
                /// <summary>+0x48 u32 (FUN_142c221d0's first). Never read [P]: only the reader and the node copy (FUN_143c50d30) touch +0x48 .. +0x50.</summary>
                public uint Slot48 { get; set; }
                /// <summary>+0x4c u32. Never read [P].</summary>
                public uint Slot4C { get; set; }
                /// <summary>+0x50 u32. Never read [P].</summary>
                public uint Slot50 { get; set; }
                /// <summary>
                /// +0x54 priority ("priority=%d"): AddOrUpdate*Mission replace it when it differs (a negative value leaves the
                /// instance alone) and re-sort the list by it [P].
                /// </summary>
                public uint Priority { get; set; }
                /// <summary>+0x60 zone id ("zoneId=%d") [P].</summary>
                public uint ZoneId { get; set; }
                /// <summary>+0x64 region id ("regionId=%d") [P].</summary>
                public uint RegionId { get; set; }
                /// <summary>
                /// +0x70 location (4 × f32; the packet default is the vec4 constant DAT_1455f0740): x / y / z are printed
                /// ("locationX=%f, locationY=%f, locationZ=%f"), w is never read [P]. +0x68 / +0x6c are not on the wire.
                /// </summary>
                public Vector4 Location { get; set; }

                public void Read(GamePacketReader reader)
                {
                    Key.Read(reader);
                    Instance.Read(reader);
                    Slot48 = reader.ReadUInt();
                    Slot4C = reader.ReadUInt();
                    Slot50 = reader.ReadUInt();
                    Priority = reader.ReadUInt();
                    ZoneId = reader.ReadUInt();
                    RegionId = reader.ReadUInt();
                    Location = new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                }

                public void Write(GamePacketWriter writer)
                {
                    Key.Write(writer);
                    Instance.Write(writer);
                    writer.Write(Slot48);
                    writer.Write(Slot4C);
                    writer.Write(Slot50);
                    writer.Write(Priority);
                    writer.Write(ZoneId);
                    writer.Write(RegionId);
                    writer.Write(Location.X);
                    writer.Write(Location.Y);
                    writer.Write(Location.Z);
                    writer.Write(Location.W);
                }
            }

            public List<ComponentInt> ComponentInts { get; set; } = new();
            public List<ComponentFloat> ComponentFloats { get; set; } = new();
            public List<ComponentInt64> ComponentInt64s { get; set; } = new();
            public List<MissionInstance> AvailableMissions { get; set; } = new();
            public List<MissionInstance> ActiveMissions { get; set; } = new();

            public void Read(GamePacketReader reader)
            {
                ComponentInts = ReadList(reader, ReadModel<ComponentInt>);
                ComponentFloats = ReadList(reader, ReadModel<ComponentFloat>);
                ComponentInt64s = ReadList(reader, ReadModel<ComponentInt64>);
                AvailableMissions = ReadList(reader, ReadModel<MissionInstance>);
                ActiveMissions = ReadList(reader, ReadModel<MissionInstance>);
            }

            public void Write(GamePacketWriter writer)
            {
                WriteList(writer, ComponentInts);
                WriteList(writer, ComponentFloats);
                WriteList(writer, ComponentInt64s);
                WriteList(writer, AvailableMissions);
                WriteList(writer, ActiveMissions);
            }
        }

        /// <summary>
        /// The skill point manager inside Skills::ClientSkillInstanceManager (player +0x77198, block at +0x771a0; 0.695
        /// reader FUN_142c2f750), the same 36 bytes SkillBase 0xBC sub 6 SetSkillPointManager carries (FUN_142f56c40).
        /// Names from the client's skill dump FUN_142f509b0 ("Earned skill points", "Spent skill points", "Skill point
        /// progress", "Skill point rate tier value") [P]; Landmark shows them as class points (Character.PlayerData
        /// "SpentClassPointsTotal", "AvailableClassPoints" = earned − spent, FUN_142dd3c40).
        /// </summary>
        public class SkillPointManager : IReadable, IWritable
        {
            /// <summary>+0x771a0: skill (class) points earned.</summary>
            public ulong EarnedSkillPoints { get; set; }
            /// <summary>+0x771a8: skill (class) points spent.</summary>
            public ulong SpentSkillPoints { get; set; }
            /// <summary>+0x771b0, a double: progress towards the next point, 0..1 (the dump prints it × 100 as a percentage).</summary>
            public double SkillPointProgress { get; set; }
            /// <summary>
            /// +0x771b8, u64: no reader found — the dump prints every other field of the block and skips this one, and
            /// the exe scans for +0x771b0..+0x771c0 (displacements and player-global loads) find only the reader (open lead
            /// for reads through the manager pointer). Live 1487581831, a unix time 8,112 s before the capture's server time
            /// [I: looks like a time stamp]; kept.
            /// </summary>
            public ulong Slot771b8 { get; set; } = 1487581831;
            /// <summary>+0x771c0: the skill point rate tier value (admin SkillAdminPacketSetSkillPointRateTierValue).</summary>
            public uint SkillPointRateTierValue { get; set; }

            public void Read(GamePacketReader reader)
            {
                EarnedSkillPoints = reader.ReadULong();
                SpentSkillPoints = reader.ReadULong();
                SkillPointProgress = reader.ReadDouble();
                Slot771b8 = reader.ReadULong();
                SkillPointRateTierValue = reader.ReadUInt();
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write(EarnedSkillPoints);
                writer.Write(SpentSkillPoints);
                writer.Write(SkillPointProgress);
                writer.Write(Slot771b8);
                writer.Write(SkillPointRateTierValue);
            }
        }

        /// <summary>
        /// InventoryFilters::ClientInventoryFilterManager (player +0x7ae60; 0.695 reader FUN_142c3d470): the player's saved
        /// bag filters, shown through the data source "Character.InventoryFilters" (FUN_142efd930: FilterId, FilterName,
        /// FilterString). Lua AddFilter (FUN_142efd5e0) gives a new filter <see cref="NextFilterId"/> and counts it up,
        /// RemoveFilter (FUN_142efd330) ignores id 0, ResetFilters (FUN_142efd4f0); each tells the server with
        /// PlayerFilterUpdatePacket 0xDD (FUN_142fd2d40 → FUN_142fc8110: op 0 add, 1 remove, 2 reset) [P]. The server keeps
        /// those changes and fills this block from them at login (<see cref="Game.InventoryFilters.SelfData"/>).
        /// </summary>
        public class InventoryFilterManager : IReadable, IWritable
        {
            /// <summary>InventoryFilters::PlayerInventoryFilter, element of the HashListMap&lt;int,PlayerInventoryFilter&gt; at +0x7ae70 (FUN_142c37990).</summary>
            public class PlayerInventoryFilter : IReadable, IWritable
            {
                /// <summary>Map key, the filter id (node +0x50).</summary>
                public uint Key { get; set; }
                /// <summary>Node +8: "FilterId".</summary>
                public uint FilterId { get; set; }
                /// <summary>Node +0x10: "FilterName".</summary>
                public string FilterName { get; set; } = "";
                /// <summary>Node +0x28: "FilterString".</summary>
                public string FilterString { get; set; } = "";

                public void Read(GamePacketReader reader)
                {
                    Key = reader.ReadUInt();
                    FilterId = reader.ReadUInt();
                    FilterName = reader.ReadString();
                    FilterString = reader.ReadString();
                }

                public void Write(GamePacketWriter writer)
                {
                    writer.Write(Key);
                    writer.Write(FilterId);
                    writer.Write(FilterName);
                    writer.Write(FilterString);
                }
            }

            public List<PlayerInventoryFilter> Filters { get; set; } = new();
            /// <summary>
            /// +0x7ae68: the id the next new filter gets. Live 1 (the capture read with the 0.695 reader) and the client's
            /// own default (constructor FUN_142efcea0). Was 0 here until 2026-10-18: the first filter then got id 0, which
            /// RemoveFilter refuses.
            /// </summary>
            public uint NextFilterId { get; set; } = 1;

            public void Read(GamePacketReader reader)
            {
                uint filtersCount = reader.ReadUInt();
                for (int i = 0; i < filtersCount; i++)
                {
                    PlayerInventoryFilter filter = new();
                    filter.Read(reader);
                    Filters.Add(filter);
                }

                NextFilterId = reader.ReadUInt();
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write((uint)Filters.Count);
                foreach (PlayerInventoryFilter filter in Filters)
                    filter.Write(writer);

                writer.Write(NextFilterId);
            }
        }

        /// <summary>The size prefix the framework adds (prependSize); not part of the client's reader.</summary>
        public uint SizePrefix { get; set; } = 655356;
        public ulong Guid { get; set; } = 1000000;
        public string AccountName { get; set; } = "TestAccount";
        public ulong CharacterId { get; set; }
        /// <summary>Entity transient id, 2-bit-length encoded (+0x38).</summary>
        public uint TransientId { get; set; }
        /// <summary>The server clock, unix seconds. The client keeps (its clock - this) to move +0x290 into local time.</summary>
        public ulong ServerTime { get; set; }
        /// <summary><see cref="ServerTime"/> as a date (read side only).</summary>
        public DateTime ServerDateTime { get; set; }
        public uint ModelId { get; set; }
        public CharacterModelInfo Model { get; set; } = new();
        /// <summary>Position x, y, z, w (+0x260).</summary>
        public Vector4 Position { get; set; }
        /// <summary>Rotation quaternion (+0x270); the entity's heading comes from it.</summary>
        public Vector4 Rotation { get; set; }
        public CharacterIdentity Identity { get; set; } = new CharacterIdentity();
        /// <summary>Coin balance (+0x72a38): Lua Player.GetPlayerCoinCount, HandlerMerchant.SetPlayerCoin.</summary>
        public uint Coin { get; set; }
        /// <summary>Currency balances (currency id, amount) (+0x72a40); each entry goes to the currency setter FUN_142f33fd0.</summary>
        public List<(uint, uint)> Currencies { get; set; } = new();
        /// <summary>Character creation date, unix seconds (+0x74a70): /played "Character Created".</summary>
        public ulong CreationDate { get; set; }
        /// <summary>Stored at +0x74a78; nothing in the 0.589 client reads it.</summary>
        public uint Unused74a78 { get; set; }
        /// <summary>Total played time in minutes (+0x74a7c): /played adds the current session to it.</summary>
        public uint PlayedMinutes { get; set; }
        /// <summary>Lua IsUnderage (+0xf8); also sets the local entity's flag 0x08 at +0x1453.</summary>
        public bool IsUnderage { get; set; }
        /// <summary>Stored at +0xfa; nothing in the 0.589 client reads it.</summary>
        public bool UnusedFa { get; set; }
        /// <summary>Dead at login (+0xf9): the client opens the respawn window (RespawnHandler:Show).</summary>
        public bool IsDead { get; set; }
        /// <summary>Membership (+0xfc): 0 none, 1 trial (Lua IsTrialMember), 2 member; non-zero = IsMember.</summary>
        public uint MembershipStatus { get; set; }
        /// <summary>Lua IsPlayerStudioRegistered (+0x100).</summary>
        public bool IsPlayerStudioRegistered { get; set; }
        /// <summary>Lua ShouldShowMemberNagScreen (+0x108), ignored for full members.</summary>
        public bool ShowMemberNagScreen { get; set; }
        /// <summary>Country (+0x110), checked by ClientRequirementCheckCountry.</summary>
        public uint CountryId { get; set; }
        /// <summary>Client language (+0x10c): ClientRequirementCheckLanguage and localised shop country names (default 2).</summary>
        public uint LanguageId { get; set; }
        /// <summary>Bit mask of preferred chat languages (+0x74a98); the client sends PlayerUpdatePreferredLanguages when one changes.</summary>
        public uint PreferredLanguages { get; set; }
        /// <summary>Stored at +0x74a9c; only ever written by the client.</summary>
        public uint Unused74a9c { get; set; }
        /// <summary>Number of logins (+0xf4): /played "Character Logins".</summary>
        public uint LoginCount { get; set; }
        /// <summary>A server timestamp (+0x290), moved into client time on read; nothing in the 0.589 client reads it.</summary>
        public ulong UnusedTimestamp { get; set; }
        /// <summary>Voice set for PlayerPlaySpeech (+0x280).</summary>
        public uint SpeechSetId { get; set; }
        /// <summary>Stored at +0x284; nothing in the 0.589 client reads it.</summary>
        public bool Unused284 { get; set; }
        /// <summary>Class progression per class id (+0x7a9d0, ClassProgression objects keyed by class).</summary>
        public List<(uint, uint, uint)> ClassProgressions { get; set; } = new();
        /// <summary>SOEmote facial animation receiving (+0x285): /soemote receiver; the client sends ClientFacialAnimSettings.</summary>
        public bool FacialAnimReceiver { get; set; }
        /// <summary>While set, the "Combat" input context stays off even in combat (+0x286).</summary>
        public bool CombatInputDisabled { get; set; }
        /// <summary>Claim permission and camera checks pass outright (+0x287, e.g. FUN_142d15900, FUN_142d37330).</summary>
        public bool BypassClaimPermissions { get; set; }
        /// <summary>Content builder mode (+0x288), toggled by ContentManagement SetContentBuilder.</summary>
        public bool IsContentBuilder { get; set; }
        public List<Profile> Profiles { get; set; } = new();
        public uint CurrentProfile { get; set; }
        /// <summary>
        /// The last played profile per profile type (profile manager player +0x168, its HashListMap&lt;int,int&gt; at +0x1d0;
        /// FUN_142d4fa00 per entry, after FUN_142d2fc30 empties the map the profile records just filled) [P]: FUN_142fcaa60
        /// adds the first profile of each type ≥ 2, FUN_142fd33a0 sets it on a profile switch, and
        /// ClientRequirementCheckLastPlayedProfileOfTypeRank (FUN_14370f8c0) looks the type up to read that profile's
        /// experience record. Live sent it empty (the server sent the current profile until 2026-10-18).
        /// </summary>
        public List<(uint ProfileType, uint ProfileId)> LastPlayedProfileByType { get; set; } = new();
        public List<Collection> Collections { get; set; } = new();
        /// <summary>
        /// Ability cooldowns (+0x753f8 HashList&lt;Abilities::CooldownTracker,64&gt;, reader FUN_142c35860 / FUN_142c1e470) [P]:
        /// Key and CooldownGroup are the ability record's cooldown group (+0x7c; the key FUN_142fcd170 / FUN_142fcab90 /
        /// FUN_142fcb2f0 look up, and the record's +0 they compare); ElapsedMs is how long ago the cooldown started (the client
        /// stores timeGetTime - ElapsedMs, FUN_142b4f660). AbilityPacketClearAllCooldowns empties the list. Live: empty.
        /// </summary>
        public List<(uint Key, uint CooldownGroup, uint ElapsedMs)> AbilityCooldowns { get; set; } = new();
        /// <summary>
        /// +0x753f0 [P]: FUN_142e0f810 ("send cast to zone") mixes it into the value it builds for each cast,
        /// ((cast counter +0x753f4 &amp; 0xff) ^ CastKeySeed) * 0x502c3f11, kept with the request it hands to FUN_142e04a90;
        /// that the value guards the cast request is [I]. Live sent 0x5BB9EB0B (one capture); the server sends 0 and never
        /// checks the cast key (OPEN-LEADS: whether live varied it per login).
        /// </summary>
        public uint CastKeySeed { get; set; }
        public List<Buff> Buffs { get; set; } = new();
        public InventoryData Inventory { get; set; } = new();
        /// <summary>The player's palettes (FUN_142c3fc60 into player +0x753e8; <see cref="PlayerPalette"/>); sent empty.</summary>
        public List<PlayerPalette> Palettes { get; set; } = new();
        /// <summary>u32 → player +0xf0 (0.695 FUN_142c19bd0).</summary>
        public Gender GenderMaybe { get; set; } // Probably Gender
        /// <summary>The quest manager (player +0x268b0, <see cref="QuestData"/>); no quests, as live.</summary>
        public QuestData Quests { get; set; } = new();
        public List<Achievement> Achievements { get; set; } = new();
        /// <summary>Recent players (player +0x74aa0, <see cref="Acquaintance"/>); sent empty.</summary>
        public List<Acquaintance> Acquaintances { get; set; } = new();
        public RecipeData Recipes { get; set; } = new();
        /// <summary>Known props (ClientPlayer +0x7adf8; KnownProp manager at +0x7adf0): prop ids the player has learned (ClientRequirementPropIdKnown, KnownProp 0x115).</summary>
        public List<uint> KnownPropIds { get; set; } = new();
        /// <summary>Customisation knowledge (ClientPlayer +0x7ae30; manager at +0x7ae28): barbershop options unlocked beyond the defaults (CharacterCustomization 0x11C sub 2 adds to it).</summary>
        public List<uint> CustomizationKnowledge { get; set; } = new();
        /// <summary>ClientKnownCharCustomizationManager second set (+0x7ae48): AddKnowledgeResponse type 4 adds here (FUN_1435d7a50); type 3 → CustomizationKnowledge.</summary>
        public List<uint> CustomizationKnowledge4 { get; set; } = new();
        public List<Hotbar> Hotbars { get; set; } = new();
        public List<Mount> Mounts { get; set; } = new();
        /// <summary>ClientFirstTimeEventManager enabled (+0x75088): when false the client never sends FirstTimeEventTriggerRequest (Game.FirstTimeEvents).</summary>
        public bool SendFirstTimeEvents { get; set; }
        /// <summary>First-time events already seen (+0x75090 Set&lt;int&gt;, FirstTimeEvents.txt ids).</summary>
        public List<uint> FirstTimeEventsSeen { get; set; } = new();
        /// <summary>Read and dropped by 0.589 (each id goes to an empty function, FUN_142f31eb0).</summary>
        public List<uint> IgnoredIds47 { get; set; } = new();
        public List<(uint, StatData)> Stats { get; set; } = new();
        public TitleData Titles { get; set; } = new();
        /// <summary>ClientPcData +0x2b0 HashListMap&lt;int,int&gt; (<see cref="IntMap2b0Entry"/>); never read by the client, sent empty as live.</summary>
        public List<IntMap2b0Entry> IntMap2b0 { get; set; } = new();
        /// <summary>+0x75ac0 HashList&lt;StrongType&lt;int,SignaturePhaseId&gt;&gt;: signature phases.</summary>
        public List<uint> SignaturePhases { get; set; } = new();
        /// <summary>
        /// ClientPcData +0x114 [P]: PlayerUpdate sub 0x36 (cPlayerUpdatePacketIdSetCurrentAdventure; dispatcher FUN_142cdfa60
        /// case 0x35, packet reader FUN_142c2b4b0) writes it, and the three arrays below, for the local player; nothing else
        /// reads it (no player-global, +0xf18 or ClientPcData-method access in the 0.695 decompile). That it is the adventure
        /// id is [I] (from the packet's name).
        /// </summary>
        public uint CurrentAdventureId { get; set; }
        /// <summary>ClientPcData +0x118 Array&lt;int&gt; (FUN_142b9afe0).</summary>
        public List<uint> IntArray118 { get; set; } = new();
        /// <summary>ClientPcData +0x130 Array&lt;int&gt;.</summary>
        public List<uint> IntArray130 { get; set; } = new();
        /// <summary>ClientPcData +0x148 Array&lt;bool&gt; (FUN_142ba6480).</summary>
        public List<bool> BoolArray148 { get; set; } = new();
        /// <summary>AcquireTimers::ClientAcquireTimerManager (+0x75be8; lists at +0x75bf0, +0x75c98, byte +0x75d48; <see cref="AcquireTimerManager"/>).</summary>
        public AcquireTimerManager AcquireTimers { get; set; } = new();
        /// <summary>Currency discounts (+0x75d68 HashListMap&lt;int,float,8&gt;, <see cref="CurrencyDiscount"/>).</summary>
        public List<CurrencyDiscount> CurrencyDiscounts { get; set; } = new();
        /// <summary>The currency rate tier (+0x75dd0 = currency manager +0x68, <see cref="RateTier"/>); never read by the client.</summary>
        public RateTier CurrencyRateTier { get; set; } = new();
        /// <summary>DefinitionFilters::ClientDefinitionVariableManager (+0x75df0) first map (+0x75e00, <see cref="DefinitionVariable"/>).</summary>
        public List<DefinitionVariable> DefinitionVariables { get; set; } = new();
        /// <summary>DefinitionFilters::ClientDefinitionVariableManager second map (+0x75e30, <see cref="DefinitionInts"/>).</summary>
        public List<DefinitionInts> DefinitionIntSets { get; set; } = new();
        /// <summary>Equipment records (+0x75e70 HashListMap&lt;int,CharacterEquipmentRecord&gt;, <see cref="CharacterEquipmentRecord"/>).</summary>
        public List<CharacterEquipmentRecord> EquipmentRecords { get; set; } = new();
        /// <summary>Experience records (+0x75f18 HashListMap&lt;int,ExperienceRecord&gt;, <see cref="ExperienceRecord"/>).</summary>
        public List<ExperienceRecord> ExperienceRecords { get; set; } = new();
        /// <summary>The experience rate tier (+0x75fc0 = experience manager +0xa8, <see cref="RateTier"/>); never read by the client.</summary>
        public RateTier ExperienceRateTier { get; set; } = new();
        /// <summary>+0x76920: the grief state, 3 × i32 (FUN_142b98950; see <see cref="GriefData"/>).</summary>
        public GriefData Grief { get; set; } = new();
        /// <summary>The item timer manager (+0x76938, ItemsBase 0xE6; <see cref="ItemTimerManager"/>).</summary>
        public ItemTimerManager ItemTimers { get; set; } = new();
        public List<Loadout> Loadouts { get; set; } = new List<Loadout>
        {
            new Loadout()
        };
        /// <summary>The mission instances (player +0x76db8, <see cref="MissionInstanceManager"/>); all five maps empty, as live.</summary>
        public MissionInstanceManager Missions { get; set; } = new();
        /// <summary>Skills::ClientSkillInstanceManager (+0x77198): the skill point block at +0x771a0 (<see cref="SkillPointManager"/>).</summary>
        public SkillPointManager SkillPoints { get; set; } = new();
        /// <summary>
        /// 0.695 tail [P FUN_142c19bd0]: the skill instances (+0x771d0, FUN_142c3c190: u32 n × {u32, u32}), then the player's
        /// vehicles for ClientVehicleManager (player +0x24490; "ClientVehicleManager::Update" FUN_142f79dc0): u64 + u32 to
        /// FUN_142f75760 (the owned-vehicle slot), u64, u64, u32, u32 to FUN_142f756f0 (the occupied-vehicle slot at +0x178),
        /// then the admin flag. Live sent no vehicles (seat 0xFFFFFFFF); so does the server.
        /// </summary>
        public List<(uint, uint)> SkillInstances { get; set; } = new();
        /// <summary>
        /// Vehicle manager +0x00 [P]: the slot VehicleOwnerPacket (0xBF sub 1, FUN_142f783c0) fills with its vehicle guid;
        /// ClientVehicleManager::Update (FUN_142f79dc0) looks the entity up by it. FUN_142f75760 sets the slot's owner
        /// (+0x10) to the player's own guid (+0x30). Live 0.
        /// </summary>
        public ulong OwnedVehicleGuid { get; set; }
        /// <summary>
        /// Player +0x244a8 (vehicle manager +0x18; VehicleOwnerPacket's last u32 goes there too): stored, never read [P] (only
        /// the constructor FUN_142f71860, the reset FUN_142f73830 and the writers FUN_142f75760 / FUN_142f783c0 touch it). Live 0.
        /// </summary>
        public uint Unused244a8 { get; set; }
        /// <summary>
        /// Vehicle manager +0x178 [P]: the vehicle the player is in, the slot VehicleOccupyPacket (0xBF sub 2, FUN_142f77eb0)
        /// fills; FUN_142f74040 / FUN_143618940 hand back that vehicle's entity instead of the player's. Live 0.
        /// </summary>
        public ulong OccupiedVehicleGuid { get; set; }
        /// <summary>
        /// Player +0x24618 (vehicle manager +0x188, VehicleOccupyPacket's second guid): stored, never read [P] (writers
        /// FUN_142f756f0 / FUN_142f77eb0, reset FUN_142f73830 only). Live 0.
        /// </summary>
        public ulong Unused24618 { get; set; }
        /// <summary>
        /// Player +0x24620 (vehicle manager +0x190, VehicleOccupyPacket's first u32): stored, never read [P] (writers
        /// FUN_142f756f0 / FUN_142f77eb0 only). Live 0.
        /// </summary>
        public uint Unused24620 { get; set; }
        /// <summary>
        /// Player +0x24624 (vehicle manager +0x194) [P]: 0xFFFFFFFF (-1) means "in no vehicle": the constructor FUN_142f71860
        /// and the reset FUN_142f73830 set -1, and leaving a vehicle (FUN_142f73940) passes -1; FUN_142f77eb0 tears the old
        /// occupancy down when a VehicleOccupyPacket brings a different value. That the value is a seat index is [I] (the
        /// shared PlanetSide 2 vehicle code). Live 0xFFFFFFFF; the server sent 0 until 2026-10-18.
        /// </summary>
        public uint OccupiedVehicleSeat { get; set; } = uint.MaxValue;
        public bool IsAdminMaybe { get; set; }
        /// <summary>The inventory filters (+0x7ae60, <see cref="InventoryFilterManager"/>).</summary>
        public InventoryFilterManager InventoryFilters { get; set; } = new();

        /// <summary>The player's profile text (0.695 only, the last field; at most 512 characters).</summary>
        public string Bio { get; set; } = "";

        /// <summary>Everything from the guid to the skill instance manager: the same in the capture and in the 0.695 body.</summary>
        private void ReadBody(GamePacketReader reader)
        {
            Guid = reader.ReadULong();
            AccountName = reader.ReadString();
            CharacterId = reader.ReadULong();
            TransientId = reader.ReadUInt2BitLength();
            ServerTime = reader.ReadULong();
            ServerDateTime = DateTimeOffset.FromUnixTimeSeconds((long)ServerTime).DateTime;
            ModelId = reader.ReadUInt();

            Model.Read(reader);

            Position = new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            Rotation = new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

            Identity.Read(reader);

            Coin = reader.ReadUInt();

            var currenciesCount = reader.ReadUInt();
            for (int i = 0; i < currenciesCount; i++)
                Currencies.Add((reader.ReadUInt(), reader.ReadUInt()));

            CreationDate = reader.ReadULong();
            Unused74a78 = reader.ReadUInt();
            PlayedMinutes = reader.ReadUInt();
            IsUnderage = reader.ReadBool();
            UnusedFa = reader.ReadBool();
            IsDead = reader.ReadBool();
            MembershipStatus = reader.ReadUInt();
            IsPlayerStudioRegistered = reader.ReadBool();
            ShowMemberNagScreen = reader.ReadBool();
            CountryId = reader.ReadUInt();
            LanguageId = reader.ReadUInt();
            PreferredLanguages = reader.ReadUInt();
            Unused74a9c = reader.ReadUInt();
            LoginCount = reader.ReadUInt();
            UnusedTimestamp = reader.ReadULong();
            SpeechSetId = reader.ReadUInt();
            Unused284 = reader.ReadBool();

            var classProgressionsCount = reader.ReadUInt();
            for (int i = 0; i < classProgressionsCount; i++)
                ClassProgressions.Add((reader.ReadUInt(), reader.ReadUInt(), reader.ReadUInt()));

            FacialAnimReceiver = reader.ReadBool();
            CombatInputDisabled = reader.ReadBool();
            BypassClaimPermissions = reader.ReadBool();
            IsContentBuilder = reader.ReadBool();

            uint struct142C1AC40Count = reader.ReadUInt();
            for (int i = 0; i < struct142C1AC40Count; i++)
            {
                Profile obj = new();
                obj.Read(reader);
                Profiles.Add(obj);
            }

            CurrentProfile = reader.ReadUInt();

            uint lastPlayedProfileByTypeCount = reader.ReadUInt();
            for (int i = 0; i < lastPlayedProfileByTypeCount; i++)
                LastPlayedProfileByType.Add((reader.ReadUInt(), reader.ReadUInt()));

            uint collectionCount = reader.ReadUInt();
            for (int i = 0; i < collectionCount; i++)
            {
                Collection collection = new();
                collection.Read(reader);
                Collections.Add(collection);
            }

            var abilityCooldownsCount = reader.ReadUInt();
            for (int i = 0; i < abilityCooldownsCount; i++)
                AbilityCooldowns.Add((reader.ReadUInt(), reader.ReadUInt(), reader.ReadUInt()));

            CastKeySeed = reader.ReadUInt();

            uint buffCount = reader.ReadUInt();
            for (int i = 0; i < buffCount; i++)
            {
                Buff buff = new();
                buff.Read(reader);
                Buffs.Add(buff);
            }

            Inventory.Read(reader);

            Palettes = ReadList(reader, ReadModel<PlayerPalette>);

            GenderMaybe = (Gender)reader.ReadUInt();

            Quests.Read(reader);

            uint achievementsCount = reader.ReadUInt();
            for (int i = 0; i < achievementsCount; i++)
            {
                Achievement achievement = new();
                achievement.Read(reader);
                Achievements.Add(achievement);
            }

            Acquaintances = ReadList(reader, ReadModel<Acquaintance>);

            Recipes.Read(reader);

            uint knownPropIdsCount = reader.ReadUInt();
            for (int i = 0; i < knownPropIdsCount; i++)
                KnownPropIds.Add(reader.ReadUInt());

            uint customizationKnowledgeCount = reader.ReadUInt();
            for (int i = 0; i < customizationKnowledgeCount; i++)
                CustomizationKnowledge.Add(reader.ReadUInt());

            uint customizationKnowledge4Count = reader.ReadUInt();
            for (int i = 0; i < customizationKnowledge4Count; i++)
                CustomizationKnowledge4.Add(reader.ReadUInt());

            uint hotbarsCount = reader.ReadUInt();
            for (int i = 0; i < hotbarsCount; i++)
            {
                Hotbar hotbar = new();
                hotbar.Read(reader);
                Hotbars.Add(hotbar);
            }

            uint mountsCount = reader.ReadUInt();
            for (int i = 0; i < mountsCount; i++)
            {
                Mount mount= new();
                mount.Read(reader);
                Mounts.Add(mount);
            }

            SendFirstTimeEvents = reader.ReadBool();

            uint firstTimeEventsSeenCount = reader.ReadUInt();
            for (int i = 0; i < firstTimeEventsSeenCount; i++)
                FirstTimeEventsSeen.Add(reader.ReadUInt());

            uint ignoredIds47Count = reader.ReadUInt();
            for (int i = 0; i < ignoredIds47Count; i++)
                IgnoredIds47.Add(reader.ReadUInt());

            uint statsCount = reader.ReadUInt();
            for (int i = 0; i < statsCount; i++)
            {
                uint statId = reader.ReadUInt();
                StatData statData = new();
                statData.Read(reader);
                Stats.Add((statId, statData));
            }

            Titles.Read(reader);

            uint intMap2b0Count = reader.ReadUInt();
            for (int i = 0; i < intMap2b0Count; i++)
            {
                IntMap2b0Entry entry = new();
                entry.Read(reader);
                IntMap2b0.Add(entry);
            }

            uint signaturePhasesCount = reader.ReadUInt();
            for (int i = 0; i < signaturePhasesCount; i++)
                SignaturePhases.Add(reader.ReadUInt());

            CurrentAdventureId = reader.ReadUInt();

            uint intArray118Count = reader.ReadUInt();
            for (int i = 0; i < intArray118Count; i++)
                IntArray118.Add(reader.ReadUInt());

            uint intArray130Count = reader.ReadUInt();
            for (int i = 0; i < intArray130Count; i++)
                IntArray130.Add(reader.ReadUInt());

            uint boolArray148Count = reader.ReadUInt();
            for (int i = 0; i < boolArray148Count; i++)
                BoolArray148.Add(reader.ReadBool());

            AcquireTimers.Read(reader);

            uint currencyDiscountsCount = reader.ReadUInt();
            for (int i = 0; i < currencyDiscountsCount; i++)
            {
                CurrencyDiscount discount = new();
                discount.Read(reader);
                CurrencyDiscounts.Add(discount);
            }

            CurrencyRateTier.Read(reader);

            uint definitionVariablesCount = reader.ReadUInt();
            for (int i = 0; i < definitionVariablesCount; i++)
            {
                DefinitionVariable variable = new();
                variable.Read(reader);
                DefinitionVariables.Add(variable);
            }

            uint definitionIntSetsCount = reader.ReadUInt();
            for (int i = 0; i < definitionIntSetsCount; i++)
            {
                DefinitionInts ints = new();
                ints.Read(reader);
                DefinitionIntSets.Add(ints);
            }

            uint equipmentRecordsCount = reader.ReadUInt();
            for (int i = 0; i < equipmentRecordsCount; i++)
            {
                CharacterEquipmentRecord record = new();
                record.Read(reader);
                EquipmentRecords.Add(record);
            }

            uint experienceRecordsCount = reader.ReadUInt();
            for (int i = 0; i < experienceRecordsCount; i++)
            {
                ExperienceRecord record = new();
                record.Read(reader);
                ExperienceRecords.Add(record);
            }

            ExperienceRateTier.Read(reader);

            Grief.Read(reader);

            ItemTimers.Read(reader);

            // The property starts with one default loadout (for Write): replaced, not added to.
            Loadouts = new();
            uint loadoutsCount = reader.ReadUInt();
            for (int i = 0; i < loadoutsCount; i++)
            {
                Loadout loadout = new();
                loadout.Read(reader);
                Loadouts.Add(loadout);
            }

            Missions.Read(reader);

            SkillPoints.Read(reader);
        }

        /// <summary>
        /// Reads the 0.695 body exactly as <see cref="Write"/> writes it (0.695 reader FUN_142c19bd0; no size prefix, the
        /// framework adds that: prependSize).
        /// </summary>
        public void Read(GamePacketReader reader)
        {
            ReadBody(reader);

            SkillInstances = ReadList(reader, r => (r.ReadUInt(), r.ReadUInt()));
            OwnedVehicleGuid = reader.ReadULong();
            Unused244a8 = reader.ReadUInt();
            OccupiedVehicleGuid = reader.ReadULong();
            Unused24618 = reader.ReadULong();
            Unused24620 = reader.ReadUInt();
            OccupiedVehicleSeat = reader.ReadUInt();
            IsAdminMaybe = reader.ReadBool();
            InventoryFilters.Read(reader);
            Bio = reader.ReadString();
        }

        /// <summary>
        /// Reads a SendSelfToClient from a live capture (the capture tool, LandmarkEmulator.Parser CaptureReading): the size
        /// prefix, then the 0.695 body exactly as <see cref="Read"/>. The live capture (2017-02-20) is the 0.695 layout to the
        /// last byte: the "six bytes, a string and 34 unread bytes" tail this used to parse was the vehicle slots (seat
        /// 0xFFFFFFFF), the admin flag, the inventory filters (NextFilterId 1) and the empty bio, read out of step.
        /// </summary>
        public void ReadCapture(GamePacketReader reader)
        {
            SizePrefix = reader.ReadUInt();
            Read(reader);

            log.Info($"\n\n   {reader.TotalBytes - reader.BytesRemaining} / {reader.TotalBytes} ({((float)(reader.TotalBytes - reader.BytesRemaining) / (float)reader.TotalBytes) * 100f}%) Bytes Read\n");
        }

        public void Write(GamePacketWriter writer)
        {
            writer.Write(Guid);
            writer.Write(AccountName);
            writer.Write(CharacterId);
            writer.WriteUIntWith2BitLength((int)TransientId);
            writer.Write(ServerTime);

            writer.Write(ModelId);
            Model.Write(writer);

            writer.Write(Position);
            writer.Write(Rotation);

            Identity.Write(writer);

            writer.Write(Coin);

            writer.Write((uint)Currencies.Count);
            foreach (var item in Currencies)
            {
                writer.Write(item.Item1);
                writer.Write(item.Item2);
            }

            writer.Write(CreationDate);
            writer.Write(Unused74a78);
            writer.Write(PlayedMinutes);
            writer.Write(IsUnderage);
            writer.Write(UnusedFa);
            writer.Write(IsDead);
            writer.Write(MembershipStatus);
            writer.Write(IsPlayerStudioRegistered);
            writer.Write(ShowMemberNagScreen);
            writer.Write(CountryId);
            writer.Write(LanguageId);
            writer.Write(PreferredLanguages);
            writer.Write(Unused74a9c);
            writer.Write(LoginCount);
            writer.Write(UnusedTimestamp);
            writer.Write(SpeechSetId);
            writer.Write(Unused284);

            writer.Write((uint)ClassProgressions.Count);
            foreach (var item in ClassProgressions)
            {
                writer.Write(item.Item1);
                writer.Write(item.Item2);
                writer.Write(item.Item3);
            }

            writer.Write(FacialAnimReceiver);
            writer.Write(CombatInputDisabled);
            writer.Write(BypassClaimPermissions);
            writer.Write(IsContentBuilder);

            writer.Write((uint)Profiles.Count);
            foreach (Profile profile in Profiles)
                profile.Write(writer);
            writer.Write(CurrentProfile);

            writer.Write((uint)LastPlayedProfileByType.Count);
            foreach (var item in LastPlayedProfileByType)
            {
                writer.Write(item.Item1);
                writer.Write(item.Item2);
            }

            writer.Write((uint)Collections.Count);
            foreach (Collection collection in Collections)
                collection.Write(writer);

            writer.Write((uint)AbilityCooldowns.Count);
            foreach (var item in AbilityCooldowns)
            {
                writer.Write(item.Item1);
                writer.Write(item.Item2);
                writer.Write(item.Item3);
            }

            writer.Write(CastKeySeed);

            writer.Write((uint)Buffs.Count);
            foreach (Buff buff in Buffs)
                buff.Write(writer);

            Inventory.Write(writer);

            WriteList(writer, Palettes);

            writer.Write((uint)GenderMaybe);

            Quests.Write(writer);

            writer.Write((uint)Achievements.Count);
            foreach (Achievement achievement in Achievements)
                achievement.Write(writer);

            WriteList(writer, Acquaintances);

            Recipes.Write(writer);

            writer.Write((uint)KnownPropIds.Count);
            foreach (var item in KnownPropIds)
                writer.Write(item);

            writer.Write((uint)CustomizationKnowledge.Count);
            foreach (var item in CustomizationKnowledge)
                writer.Write(item);

            writer.Write((uint)CustomizationKnowledge4.Count);
            foreach (var item in CustomizationKnowledge4)
                writer.Write(item);

            writer.Write((uint)Hotbars.Count);
            foreach (Hotbar hotbar in Hotbars)
                hotbar.Write(writer);

            writer.Write((uint)Mounts.Count);
            foreach (Mount mount in Mounts)
                mount.Write(writer);

            writer.Write(SendFirstTimeEvents);

            writer.Write((uint)FirstTimeEventsSeen.Count);
            foreach (var item in FirstTimeEventsSeen)
                writer.Write(item);

            writer.Write((uint)IgnoredIds47.Count);
            foreach (var item in IgnoredIds47)
                writer.Write(item);

            writer.Write((uint)Stats.Count);
            foreach (var stat in Stats)
            {
                writer.Write(stat.Item1);
                stat.Item2.Write(writer);
            }

            Titles.Write(writer);

            writer.Write((uint)IntMap2b0.Count);
            foreach (var entry in IntMap2b0)
                entry.Write(writer);

            writer.Write((uint)SignaturePhases.Count);
            foreach (var item in SignaturePhases)
                writer.Write(item);

            writer.Write(CurrentAdventureId);

            writer.Write((uint)IntArray118.Count);
            foreach (var item in IntArray118)
                writer.Write(item);

            writer.Write((uint)IntArray130.Count);
            foreach (var item in IntArray130)
                writer.Write(item);

            writer.Write((uint)BoolArray148.Count);
            foreach (var item in BoolArray148)
                writer.Write(item);

            AcquireTimers.Write(writer);

            writer.Write((uint)CurrencyDiscounts.Count);
            foreach (var discount in CurrencyDiscounts)
                discount.Write(writer);

            CurrencyRateTier.Write(writer);

            writer.Write((uint)DefinitionVariables.Count);
            foreach (var variable in DefinitionVariables)
                variable.Write(writer);

            writer.Write((uint)DefinitionIntSets.Count);
            foreach (var ints in DefinitionIntSets)
                ints.Write(writer);

            writer.Write((uint)EquipmentRecords.Count);
            foreach (var record in EquipmentRecords)
                record.Write(writer);

            writer.Write((uint)ExperienceRecords.Count);
            foreach (var record in ExperienceRecords)
                record.Write(writer);

            // The 0.695 reader (FUN_142c19bd0) is the 0.589 one (SendSelfToClient_Read 142b80420) plus one final string,
            // the player's bio (FUN_142d4b5d0 into player +0x7aea8; at most 512 characters, "BioLengthMax"); traced
            // 2026-10-15, trace/0695/TASKS.md §0. (The capture tail once parsed as six bytes, a string and 34 unread bytes
            // was this same layout read out of step; ReadCapture now reads the capture with Read.)
            WriteTail(writer);
            writer.Write(Bio);
        }

        /// <summary>
        /// Everything after ExperienceRecords up to the bio: the layout the 0.589 client (Steam build 0.589.1.128895) reads, which
        /// the 0.695 client reads too before its bio. The client aborts if the packet isn't consumed exactly, so this must
        /// match its reader byte for byte. Fields with no 0.695 counterpart are written empty.
        /// </summary>
        private void WriteTail(GamePacketWriter writer)
        {
            // same layout as 0.695
            ExperienceRateTier.Write(writer);
            Grief.Write(writer);
            ItemTimers.Write(writer);

            writer.Write((uint)Loadouts.Count);
            foreach (Loadout loadout in Loadouts)
                loadout.Write(writer);

            // The mission instances: five maps (MissionInstanceManager; empty, as live).
            Missions.Write(writer);

            // same layout as 0.695
            SkillPoints.Write(writer);

            // The skill instances (u32 n × {u32, u32}), then the owned-vehicle slot (u64, u32) and the occupied-vehicle slot
            // (u64, u64, u32, u32): no vehicles, seat 0xFFFFFFFF, as live.
            writer.Write((uint)SkillInstances.Count);
            foreach ((uint, uint) item in SkillInstances)
            {
                writer.Write(item.Item1);
                writer.Write(item.Item2);
            }
            writer.Write(OwnedVehicleGuid);
            writer.Write(Unused244a8);
            writer.Write(OccupiedVehicleGuid);
            writer.Write(Unused24618);
            writer.Write(Unused24620);
            writer.Write(OccupiedVehicleSeat);

            // The admin-player flag (0.695 reader FUN_142c19bd0 → player +0x7b0d8; 0.589 +0x7aea8): unlocks the admin slash
            // commands (AdminCommands.cs). It was written as false here until 2026-10-17, so no admin command ever left
            // the client (found by reading the byte in the running client, re/test/flag_probe.js).
            writer.Write(IsAdminMaybe);

            // The inventory filters (FUN_142c3d470: u32 n × {u32, u32, string, string}, then u32); empty.
            InventoryFilters.Write(writer);
        }
    }
}
