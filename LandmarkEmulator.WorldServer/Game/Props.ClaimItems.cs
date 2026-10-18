using LandmarkEmulator.WorldServer.Network;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The placed props a build site's inventory is made of (<see cref="ClaimInventory"/>, OPEN-LEADS L146): props placed
    /// from a bag item are that item on the site.
    /// </summary>
    public static partial class Props
    {
        /// <summary>Every placed prop (anyone's) standing inside a box (local metres) on a landscape.</summary>
        public static List<PlacedProp> PlacedInBox(uint zone, Vector3 min, Vector3 max)
        {
            lock (sync)
                return props.Where(p => p.Placed && p.Zone == zone
                        && p.Spawn.Position.X >= min.X && p.Spawn.Position.X <= max.X
                        && p.Spawn.Position.Y >= min.Y && p.Spawn.Position.Y <= max.Y
                        && p.Spawn.Position.Z >= min.Z && p.Spawn.Position.Z <= max.Z)
                    .ToList();
        }

        /// <summary>
        /// A site's placed item goes to a player (ClaimRequestMoveItemToPlayer 0xF9/0x23): the prop is picked up exactly
        /// as its owner's pick-up would (<see cref="Refund.AsPlaced"/>: the bag item it was placed from), a mover's whole
        /// pack with it (<see cref="RemoveOtherParts"/>). Returns what the player got.
        /// </summary>
        public static List<(uint Item, uint Count)> TakeAsClaimItem(PlacedProp prop, WorldSession to)
        {
            RemoveOtherParts(prop);
            return Remove(prop, to, Refund.AsPlaced);
        }
    }
}
