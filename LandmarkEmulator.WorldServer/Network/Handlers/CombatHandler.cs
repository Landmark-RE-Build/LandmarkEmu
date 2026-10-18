using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using NLog;
using System;

namespace LandmarkEmulator.WorldServer.Network.Handlers
{
    /// <summary>
    /// CombatBase 0x24 from the client (u16 sub). Landmark's combat is ability-based (Game.Abilities / WeaponAttacks); these
    /// three are the engine's auto-attack packets, which the 0.695 client still sends (trace/0695/agent-l140.md §2, B2):
    /// <list type="bullet">
    /// <item>3 AutoAttackOff {} (FUN_1433ca460): sent unconditionally by FUN_142d090a0 when the player's current target dies
    /// (ProxiedCharacter vtable slot 143, FUN_142e9a8f0, from the death handling FUN_142eb6a10 / FUN_142e978c0) and on every
    /// interaction-list auto-select (FUN_1433d3f90, after CommandPacketInteractionSelect) — normal play [P]. The client
    /// clears its own auto-attack state (+0x32802 / +0x32808) before sending, so nothing is answered [P].</item>
    /// <item>2 AutoAttackTarget {u64 target} (FUN_1433ca3e0): typed /attack Name (CommandAttack FUN_1433a1a90) [P].</item>
    /// <item>4 SingleAttackTarget {u64 target} (FUN_1433cbad0): typed /hit Name (CommandHit FUN_1433a2010) [P].</item>
    /// </list>
    /// No Landmark source gives /attack or /hit a meaning [I], so all three are accepted without a reply (debug log only).
    /// </summary>
    public static class CombatHandler
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public const ushort AutoAttackTargetSub = 2, AutoAttackOffSub = 3, SingleAttackTargetSub = 4;

        [ZoneMessageHandler(ZoneMessageOpcode.CombatBase)]
        public static void HandleCombat(WorldSession session, CombatRequest request)
        {
            switch (request.SubOpcode)
            {
                case AutoAttackOffSub:
                    break;
                case AutoAttackTargetSub:
                case SingleAttackTargetSub:
                    ulong target = request.Body.Length >= 8 ? BitConverter.ToUInt64(request.Body, 0) : 0;
                    log.Debug($"{session.CharacterName}: combat 0x{request.SubOpcode:X} (/{(request.SubOpcode == AutoAttackTargetSub ? "attack" : "hit")}) on 0x{target:X}; no reply.");
                    break;
                default:
                    OpcodeStats.Unanswered("Combat 0x24", request.SubOpcode);
                    log.Debug($"Unhandled combat request 0x{request.SubOpcode:X2}: {BitConverter.ToString(request.Body)}");
                    break;
            }
        }
    }
}
