using System;

namespace LandmarkEmulator.Database.Character.Model
{
    /// <summary>
    /// One run of the Showcase following digest (Game.ShowcaseFollow; live: once a day, a mail listing what the
    /// followed creators and tags had newly on the Marketplace or Gallery, Update Notes 27 Aug 2014). The next run
    /// covers what was listed after the newest
    /// <see cref="RanAt"/>. <see cref="Slot"/> is unique: "day:yyyy-MM-dd" for the scheduled run of that UTC day, so of
    /// several worlds sharing one character database only the first to insert the row sends that day's mail;
    /// "dev:&lt;ticks&gt;" for a dev command run.
    /// </summary>
    public class ShowcaseDigestRunModel
    {
        public ulong Id { get; set; }
        public string Slot { get; set; } = "";
        public DateTime RanAt { get; set; }
        /// <summary>
        /// Mails sent by the run (filled in when its last mail went out); uint.MaxValue while the run is unfinished
        /// (Game.ShowcaseFollow.RunPending, since 2026-10-18): the next run's window starts at the newest finished run.
        /// </summary>
        public uint Mails { get; set; }
    }
}
