using System;

namespace LandmarkEmulator.Database.Character.Model
{
    /// <summary>
    /// One peer-review vote on a Player Studio product (Game.Commerce; docs/commerce.md). One per reviewer and product.
    /// Verdict is the client's SubmitProductReview code: 1 approved, 0 rejected without a reason, 2-7 rejected with a
    /// reason (rejection type 1-3 x subtype 0/1).
    /// </summary>
    public class PlayerStudioReviewModel
    {
        public ulong Id { get; set; }
        public ulong ProductId { get; set; }
        public ulong ReviewerGuid { get; set; }
        public uint Verdict { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
