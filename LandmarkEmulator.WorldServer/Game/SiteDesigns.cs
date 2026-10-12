using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.WorldServer.Game.Terrain;
using LandmarkEmulator.WorldServer.Network;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// A design of a build site, saved when the site goes. The client's own delete dialog promises it ("A copy of your
    /// current Build Site will be created and stored in your Designs list", ClaimManagementWindow.lua), and so did the
    /// lapse mail ("A Design of each expired Build Site has been saved").
    /// <para>
    /// The design holds what the owner built on the site — the voxels Voxel Vision says are theirs
    /// (<see cref="VoxelOwnership"/>) inside the site's box, with their current material and shape — not the natural
    /// ground, so placing it puts the build down rather than a chunk of hillside. Stored like a multi-cell prefab: u32 n ×
    /// {u64 cell, u64 cell, u32 4, u32 length, packed (changed mask + format-2 block)} (<see cref="Templates.VoxelsOf"/>
    /// reads it back). Props go back to the owner's bag as before (a design here carries voxels only).
    /// </para>
    /// </summary>
    public static class SiteDesigns
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Saves the owner's build on a site as a design (before the site's terrain is reverted). Returns the design (null
        /// when the owner built nothing there) and the owner's voxels per material.
        /// </summary>
        public static (TemplateModel Design, Dictionary<ushort, uint> Materials) Save(ClaimModel claim)
        {
            uint zone = claim.ZoneId;
            double voxel = TerrainEdits.VoxelSize;
            // Voxel centre inside the site's box (global decimetres)?
            bool Inside(ulong cell, int index)
            {
                (_, uint cx, uint cy, uint cz) = TerrainStore.Decode(cell);
                int y = index % BlockData.Size, x = index / BlockData.Size % BlockData.Size, z = index / (BlockData.Size * BlockData.Size);
                double gx = ((double)cx * BlockData.Size + x + 0.5) * voxel, gy = ((double)cy * BlockData.Size + y + 0.5) * voxel,
                    gz = ((double)cz * BlockData.Size + z + 0.5) * voxel;
                return gx >= claim.MinX && gx <= claim.MaxX && gy >= claim.MinY && gy <= claim.MaxY && gz >= claim.MinZ && gz <= claim.MaxZ;
            }
            var owned = VoxelOwnership.OwnedBy(zone, claim.OwnerGuid, Inside);
            var blocks = new MemoryStream();
            var materials = new Dictionary<ushort, uint>();
            int cells = 0, voxels = 0;
            uint minX = uint.MaxValue, minY = uint.MaxValue, minZ = uint.MaxValue, maxX = 0, maxY = 0, maxZ = 0;
            var body = new MemoryStream();
            foreach ((ulong cell, List<int> indices) in owned)
            {
                var mask = new byte[BlockData.Count / 8];
                foreach (int i in indices)
                    mask[i >> 3] |= (byte)(1 << (7 - (i & 7)));
                List<TerrainEdits.VoxelValue> values = TerrainEdits.Read(cell, mask, zone);
                var block = new BlockData();
                int solid = 0;
                foreach (TerrainEdits.VoxelValue v in values)
                {
                    if ((v.Flags & 1) == 0 || v.Material == 0)
                        continue;
                    block.Flags[v.Index] = v.Flags;
                    block.Materials[v.Index] = v.Material;
                    block.Offsets[v.Index * 3] = v.O0;
                    block.Offsets[v.Index * 3 + 1] = v.O1;
                    block.Offsets[v.Index * 3 + 2] = v.O2;
                    materials[v.Material] = materials.GetValueOrDefault(v.Material) + 1;
                    solid++;
                }
                if (solid == 0)
                    continue;
                byte[] serialized = block.Serialize();
                byte[] packed = TerrainStore.Pack(mask.Concat(serialized).ToArray());
                body.Write(BitConverter.GetBytes(cell));
                body.Write(BitConverter.GetBytes(cell));
                body.Write(BitConverter.GetBytes(4u));
                body.Write(BitConverter.GetBytes((uint)packed.Length));
                body.Write(packed);
                (_, uint x, uint y, uint z) = TerrainStore.Decode(cell);
                minX = Math.Min(minX, x); minY = Math.Min(minY, y); minZ = Math.Min(minZ, z);
                maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); maxZ = Math.Max(maxZ, z);
                cells++;
                voxels += solid;
            }
            if (cells == 0)
            {
                log.Info($"Site {claim.Id}: nothing built by its owner, no design saved.");
                return (null, materials);
            }
            blocks.Write(BitConverter.GetBytes((uint)cells));
            body.Position = 0;
            body.CopyTo(blocks);
            string name = Trim($"{(string.IsNullOrWhiteSpace(claim.Name) ? "Build Site" : claim.Name)} ({DateTime.UtcNow:yyyy-MM-dd})");
            var design = new TemplateModel
            {
                OwnerGuid = claim.OwnerGuid,
                AuthorName = Owner(claim.OwnerGuid),
                Name = name,
                Tags = claim.Tags ?? "",
                SizeX = (maxX - minX + 1) * BlockData.Size - 1,
                SizeY = (maxY - minY + 1) * BlockData.Size - 1,
                SizeZ = (maxZ - minZ + 1) * BlockData.Size - 1,
                Materials = string.Join(";", materials.Select(m => $"{m.Key}:{m.Value}")),
                Blocks = blocks.ToArray(),
                CreatedAt = DateTime.UtcNow
            };
            using (var context = DatabaseManager.Instance.CharacterDatabase.NewContext())
            {
                context.Template.Add(design);
                context.SaveChanges();
            }
            if (PlayerManager.FindByGuid(claim.OwnerGuid) is WorldSession owner)
                Templates.SendAdded(owner, design);
            log.Info($"Site {claim.Id}: saved design {design.Id} \"{name}\" ({cells} cell(s), {voxels} voxel(s)) for 0x{claim.OwnerGuid:X}.");
            return (design, materials);
        }

        /// <summary>
        /// The dialog's "All resources … will be returned to your inventory": the materials of the owner's voxels, at the
        /// rate building charged them (BuildMaterials.VoxelsPerItem voxels per item, rounded down).
        /// </summary>
        public static int Refund(WorldSession owner, Dictionary<ushort, uint> materials)
        {
            var voxelsByItem = new Dictionary<uint, long>();
            foreach ((ushort material, uint voxels) in materials)
                if (BuildMaterials.ItemFor(material) is uint item and not 0)
                    voxelsByItem[item] = voxelsByItem.GetValueOrDefault(item) + voxels;
            int given = 0;
            foreach ((uint item, long voxels) in voxelsByItem)
            {
                uint items = (uint)(voxels / BuildMaterials.VoxelsPerItem);
                if (items == 0)
                    continue;
                owner.GiveItem(item, items);
                given += (int)items;
            }
            return given;
        }

        private static string Trim(string name) => name.Length > 64 ? name[..64] : name;

        private static string Owner(ulong character)
            => PlayerManager.FindByGuid(character)?.CharacterName
               ?? DatabaseManager.Instance.CharacterDatabase.GetCharacterNames(new[] { character }).GetValueOrDefault(character, "");
    }
}
