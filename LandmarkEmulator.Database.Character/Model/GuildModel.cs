using System;

namespace LandmarkEmulator.Database.Character.Model
{
    public class GuildModel
    {
        public ulong Id { get; set; }
        public string Name { get; set; }
        public string Motd { get; set; } = "";
        public ulong LeaderId { get; set; }
        public DateTime CreateTime { get; set; }
        /// <summary>The guild window's description.</summary>
        public string Description { get; set; } = "";
        /// <summary>GuildGameData XML (emblem, recruiting text, rating, open to applications, keywords / tag).</summary>
        public string AppData { get; set; } = "";
        /// <summary>Custom ranks as JSON ({Id, Level, Name, Permissions}); null / empty = the default three.</summary>
        public string Ranks { get; set; }
    }
}
