using System;

namespace LandmarkEmulator.Database.Character.Model
{
    /// <summary>
    /// A comment left on a build site's gallery entry (Showcase gallery, Game.Gallery; launch notes P4139 / P4686,
    /// 21 Mar / 22 Apr 2016: gallery entries got a comments area). The author's name is kept with the comment so it
    /// survives the character's deletion.
    /// </summary>
    public class GalleryCommentModel
    {
        public ulong Id { get; set; }
        /// <summary>The build site (ClaimModel.Id) the comment is on.</summary>
        public ulong ClaimId { get; set; }
        public ulong AuthorGuid { get; set; }
        public string AuthorName { get; set; } = "";
        public string Text { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }
}
