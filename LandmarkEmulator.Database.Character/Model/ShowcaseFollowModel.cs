using System;

namespace LandmarkEmulator.Database.Character.Model
{
    /// <summary>
    /// One thing a player follows in the Showcase (Game.ShowcaseFollow): a Player Studio / Gallery creator, or a group of
    /// tag words. Live: the Showcase's My Store tab let a player follow creators by name and tags, single or grouped
    /// (Update Notes 27 Aug 2014, design/forum/patches/2014-08-27-46891).
    /// <list type="bullet">
    /// <item>A creator: <see cref="CreatorGuid"/> = their character guid (the id the client's FollowCreator 0x35 carries,
    /// [I]: Commerce.Follow), <see cref="CreatorName"/> their name when followed, <see cref="Tags"/> "".</item>
    /// <item>A tag group: <see cref="CreatorGuid"/> 0, <see cref="Tags"/> the words, lower case, sorted, one space apart
    /// ("castle pvp" = only items tagged with both).</item>
    /// </list>
    /// </summary>
    public class ShowcaseFollowModel
    {
        public ulong Id { get; set; }
        /// <summary>The follower's character guid.</summary>
        public ulong FollowerGuid { get; set; }
        /// <summary>The followed creator's character guid; 0 for a tag group.</summary>
        public ulong CreatorGuid { get; set; }
        public string CreatorName { get; set; } = "";
        /// <summary>The followed tag group (normalised words); "" for a creator.</summary>
        public string Tags { get; set; } = "";
        public DateTime FollowedAt { get; set; }
    }
}
