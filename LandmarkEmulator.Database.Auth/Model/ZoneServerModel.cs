using System;

namespace LandmarkEmulator.Database.Auth.Model
{
    public class ZoneServerModel
    {
        public ulong Id { get; set; }
        public uint NameId { get; set; }
        public string Host { get; set; }
        public ushort Port { get; set; }
        public uint Flags { get; set; }
        /// <summary>When the world server last said it is running (it writes this every few seconds; null = stopped).</summary>
        public DateTime? Heartbeat { get; set; }
        /// <summary>Players in the world at the last heartbeat.</summary>
        public uint PlayersOnline { get; set; }
    }
}
