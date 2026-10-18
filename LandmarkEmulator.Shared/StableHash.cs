namespace LandmarkEmulator.Shared
{
    /// <summary>
    /// A hash of a few ints that is the same in every run. <see cref="System.HashCode.Combine{T1, T2}"/> is seeded at random
    /// for each process (by design in .NET), so seeds built from it changed on every server start: trees and plants per
    /// region (and with them the saved felled / gathered nodes), creature camps, loot chest sites and players' daily goals
    /// were re-rolled by each restart. Use this wherever the result must survive a restart.
    /// </summary>
    public static class StableHash
    {
        /// <summary>FNV-1a over the values' bytes, then a final avalanche (Murmur3 fmix32).</summary>
        public static int Combine(params int[] values)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (int v in values)
                    for (int shift = 0; shift < 32; shift += 8)
                        h = (h ^ (byte)(v >> shift)) * 16777619;
                h ^= h >> 16;
                h *= 0x85EBCA6B;
                h ^= h >> 13;
                h *= 0xC2B2AE35;
                h ^= h >> 16;
                return (int)h;
            }
        }
    }
}
