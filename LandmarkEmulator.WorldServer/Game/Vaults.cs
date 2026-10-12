using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System.Collections.Generic;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The Universal Vault Network (wiki "Vault"): vault props placed on build sites ("Can be placed in build sites to gain
    /// access to the universal vault network", text 560111) all open the same private store — the user's own vault
    /// (inventory container 1, size <see cref="ItemUses.VaultSizeOf"/>, 60 to 120 with Vault Pockets). Only the owner can
    /// take from it, whatever the site; the Void Vault (founder vault) works only on the player's own sites.
    /// <para>
    /// Using a vault prop sends Command 0x1E sub 0x50 InteractionVault {u16 sub, u64 vault entity} (FUN_1433120f0 →
    /// FUN_142e84350: remembers the entity, closes the window when the player walks away (Vault close distance 10 m),
    /// fires GameEvents:OnOpenVaultWindow). Items move bag ↔ vault with TransferItem (container 1) while the window is
    /// open within reach of that vault.
    /// </para>
    /// </summary>
    public static class Vaults
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>Vault props: 407 Void Vault, 752 Rustic Stone Vault, 780 the stone safe (Theme_Human_Vaults_Safes_SafeStone01).</summary>
        private static readonly HashSet<uint> vaultProps = new() { 407, 752, 780 };
        private const uint VoidVault = 407;
        private const ushort InteractionVaultSub = 0x50;
        /// <summary>The client closes the window beyond 10 m; a little slack for movement updates.</summary>
        private const float Reach = 13f;

        private static readonly Dictionary<WorldSession, ulong> open = new();
        private static readonly object sync = new();

        public static bool IsVaultProp(uint propId) => vaultProps.Contains(propId);

        public static bool IsVault(ulong guid) => Props.Find(guid) is { } p && vaultProps.Contains(p.PropId);

        /// <summary>A player used a vault prop: their own vault opens.</summary>
        public static void Open(WorldSession session, ulong guid)
        {
            if (Props.Find(guid) is not { } prop)
                return;
            if (prop.PropId == VoidVault && prop.OwnerGuid != session.PlayerGuid)
            {
                Chat.System(session, "A Void Vault can only be used on your own build site.");
                return;
            }
            lock (sync)
                open[session] = guid;
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write(InteractionVaultSub);
            w.Write(guid);
            session.EnqueueMessage(ZoneMessageOpcode.CommandBase, output.ToArray());
            log.Info($"{session.CharacterName} opened their vault at 0x{guid:X}.");
        }

        /// <summary>May the player move items into or out of their vault now (a vault opened and within reach)?</summary>
        public static bool CanAccess(WorldSession session)
        {
            ulong guid;
            lock (sync)
                if (!open.TryGetValue(session, out guid))
                    return false;
            if (Props.Find(guid) is not { } prop)
                return false;
            var at = new Vector3(prop.Spawn.Position.X, prop.Spawn.Position.Y, prop.Spawn.Position.Z);
            return Vector3.Distance(at, session.Position) <= Reach;
        }

        public static void Forget(WorldSession session)
        {
            lock (sync)
                open.Remove(session);
        }
    }
}
