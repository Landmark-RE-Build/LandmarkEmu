using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using NLog;
using System;
using System.Collections.Generic;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Overflow items expire (trace/0695/audit-wiki-gaps.md #8): OverflowExpiryHours (default 24, wiki Inventory:
    /// overflow items are lost after 24 hours) after they arrive they are deleted. Offline
    /// players' items go at their next login (Inventory's constructor); online players are checked once a minute here, and
    /// the client is told with ItemDelete. The client counts the time down itself from the record's timed-delete seconds
    /// (Inventory.ToPacket; "Overflow: Items expire in H:MM:SS"). A chest item that still held items loses them too, as
    /// everything else in overflow ("unrecoverable").
    /// </summary>
    public static class OverflowExpiry
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private static DateTime next;

        /// <summary>World tick: once a minute, every online player's overflow.</summary>
        public static void Update()
        {
            if (DateTime.UtcNow < next)
                return;
            next = DateTime.UtcNow.AddMinutes(1);
            foreach (WorldSession session in PlayerManager.Snapshot())
            {
                if (session.Inventory == null || session.Disconnected)
                    continue;
                List<CharacterItemModel> gone = session.Inventory.ExpireOverflow();
                foreach (CharacterItemModel item in gone)
                {
                    session.EnqueueMessage(new ClientUpdateItemDelete { Guid = item.Id });
                    if (Chests.HasItems(item.Id))
                        Chests.Empty(item.Id, null);
                }
                if (gone.Count > 0)
                    log.Info($"{session.CharacterName}: {gone.Count} overflow stack(s) expired.");
            }
        }
    }
}
