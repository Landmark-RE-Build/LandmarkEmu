using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System;
using System.Collections.Generic;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// A player shown as another model: the admin /setmodel command (AdminCommandPacketSetModel 0x1E / 0x4F0) and its
    /// undo. Built 2026-10-26 (trace/0695/phaseE2-packets.md, REACHABLE-GAPS-695 "PlayerUpdatePacketUpdateTemporaryAppearance").
    /// <para>Client side (0.695) [P]:
    /// <list type="bullet">
    /// <item>S→C PlayerUpdate UpdateTemporaryAppearance 0x26 / 9 (dispatcher FUN_142cdfa60 case 8, reader FUN_142c4e180 →
    /// FUN_142c2ce10): u16 base, u32 sub, <b>u32 model</b> (+0x20), <b>u64 character</b> (+0x18), consumed exactly. The
    /// character is found by guid (FUN_142f85390) and FUN_142eca100 ("ProxiedCharacter::SwitchToTemporaryAppearance")
    /// ends any temporary look first (FUN_142eca480), builds an actor from the model's file (FUN_143b88ba0 over the
    /// client's model table, Models.txt; an unknown id falls back to the client's default actor) and sets entity bit
    /// +0x1456 &amp; 0x10. While the actor is not loaded yet the id waits in +0x24a0 and FUN_142eb8600 applies it later.</item>
    /// <item>S→C RemoveTemporaryAppearance 0x26 / 0xA {u64 character} (case 9, FUN_142eca480) restores the body.</item>
    /// <item>C→S RemoveTemporaryAppearance 0x26 / 0xA {u64 own guid}: sent by the client's /profile handling
    /// (FUN_1433b0810) while bit 0x10 is set, i.e. the player drops the look (Network/Handlers/PlayerHandler.cs).</item>
    /// <item>LightweightToFullPc carries the id for clients that meet the player later (blob u32 before the title →
    /// +0x24a0).</item>
    /// </list>
    /// Nothing else in the client starts a temporary appearance: FUN_142eca100's only callers are the case above and
    /// FUN_142eb8600 (its own re-apply), and no item, ability or data table names one. So on live it was the GM tool
    /// /setmodel (the client's command FUN_1433b4110, "Processing set model model_id=[%d]"), which is what this answers.</para>
    /// </summary>
    public static class TemporaryAppearance
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>UpdateTemporaryAppearance body after the 6 opcode bytes: u32 model, u64 character (reader FUN_142c2ce10).</summary>
        public static byte[] UpdateBody(ulong character, uint model)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write(model);
            w.Write(character);
            return output.ToArray();
        }

        /// <summary>RemoveTemporaryAppearance body after the opcode bytes: u64 character.</summary>
        public static byte[] RemoveBody(ulong character) => BitConverter.GetBytes(character);

        /// <summary>
        /// Shows <paramref name="target"/> as model <paramref name="model"/> (0 = their own body again) to everyone on the
        /// landscape, themselves included, and keeps it for players who arrive later.
        /// </summary>
        public static void Set(WorldSession target, uint model)
        {
            if (target == null || target.Disconnected)
                return;
            target.TemporaryAppearanceModel = model;
            foreach (WorldSession p in PlayerManager.InZone(target.ZoneId))
                Send(p, target.PlayerGuid, model);
            log.Info($"{target.CharacterName} is shown as {(model == 0 ? "their own body" : $"model {model}")}.");
        }

        /// <summary>The player dropped the look themselves (C→S RemoveTemporaryAppearance): forget it and tell everyone.</summary>
        public static void Clear(WorldSession player)
        {
            if (player == null)
                return;
            player.TemporaryAppearanceModel = 0;
            foreach (WorldSession p in PlayerManager.InZone(player.ZoneId))
                Send(p, player.PlayerGuid, 0);
        }

        /// <summary>
        /// After a re-zone the client builds the player's own character anew from SendSelf, which has no such field: the
        /// look is sent again (other players get it from LightweightToFullPc).
        /// </summary>
        public static void SendOwn(WorldSession session)
        {
            if (session?.TemporaryAppearanceModel is uint model && model != 0)
                Send(session, session.PlayerGuid, model);
        }

        private static void Send(WorldSession to, ulong character, uint model)
        {
            if (model == 0)
                to.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateRemoveTemporaryAppearanceWire, RemoveBody(character));
            else
                to.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateUpdateTemporaryAppearance, UpdateBody(character, model));
        }
    }
}
