using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LandmarkEmulator.Internal.Social
{
    /// <summary>
    /// CommandPacketRemoveFriendRequest (Command 0x1E / 0x12), read the same way by both places that carry it out — the
    /// world's own friends code (Game/Friends.cs) and the social service (SocialServer/FriendService.cs).
    /// <para>[P] 0.695: the packet (ctor FUN_1435d5a50) is {identity +0x20, u64 guid +0xD0}; its sender FUN_1435d4aa0
    /// writes the header, the identity (FUN_142ddc260: u32, u32, u32, string first, string second) and then the u64
    /// (FUN_142c02460). The two Lua natives fill it differently:</para>
    /// <list type="bullet">
    /// <item>Ui.RemoveFriend (FUN_1435c7bd0; the friends window, "/friend remove Name"): the typed name in the identity's
    /// first string (normalised by FUN_143b87120); the guid field is never set by that native, so it is not to be
    /// trusted.</item>
    /// <item>Ui.RemoveFriendByGuid (FUN_1435c8110; the player right-click menu, RadialMenuButtons row 16): an empty
    /// identity and the guid (Lua argument 1, FUN_143c9f460) in the u64.</item>
    /// </list>
    /// So the name decides when there is one, the guid when the name is empty.
    /// </summary>
    public sealed record FriendRemoveRequest(string Name, ulong Guid)
    {
        /// <summary>Reads the body after the u16 sub; false when the identity runs short. A missing guid reads as 0.</summary>
        public static bool TryRead(byte[] body, out FriendRemoveRequest request)
        {
            request = null;
            if (body == null)
                return false;
            try
            {
                var r = new InternalReader(body);
                r.U32(); r.U32(); r.U32();
                string first = (r.Str() ?? "").Trim();
                string second = (r.Str() ?? "").Trim();
                ulong guid = r.Remaining >= 8 ? r.U64() : 0;
                request = new FriendRemoveRequest(second.Length > 0 ? (first + " " + second).Trim() : first, guid);
                return true;
            }
            catch (InvalidDataException)
            {
                return false;
            }
        }

        /// <summary>The friend this request means on a list: by name (ignoring case) when one was typed, else by guid.</summary>
        public T Find<T>(IEnumerable<T> friends, Func<T, ulong> guidOf, Func<T, string> nameOf) where T : class
        {
            if (Name.Length > 0)
                return friends.FirstOrDefault(f => string.Equals(nameOf(f), Name, StringComparison.OrdinalIgnoreCase));
            return Guid != 0 ? friends.FirstOrDefault(f => guidOf(f) == Guid) : null;
        }

        /// <summary>Writes a body the way the client does (tests, tools): identity with the name, then the guid.</summary>
        public static byte[] Write(string name, ulong guid)
        {
            var w = new InternalWriter();
            w.U32(0).U32(0).U32(0).Str(name ?? "").Str("").U64(guid);
            return w.ToArray();
        }
    }
}
