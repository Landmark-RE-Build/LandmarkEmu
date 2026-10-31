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
    /// {u64 cell, u64 cell, u32 4, u32 length, packed (changed mask + format-2 block)}, cells counted from the lowest one
    /// (<see cref="Templates.VoxelsOf"/> reads it back). Props go back to the owner as materials (a design here carries
    /// voxels only; Props.ReturnInBox / Props.StowInBox), and the design gets the site's screenshots.
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
            // The owner's voxels whose centre lies inside the site's box (global decimetres).
            var owned = EnvironmentServer.Instance.OwnedBy(zone, claim.OwnerGuid,
                new VoxelBox(claim.MinX, claim.MinY, claim.MinZ, claim.MaxX, claim.MaxY, claim.MaxZ));
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
                List<TerrainEdits.VoxelValue> values = EnvironmentServer.Instance.Read(cell, mask, zone);
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
            // Cells counted from the design's lowest cell, as designs are (Templates.Rebased).
            byte[] stored = Templates.Rebased(blocks.ToArray());
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
                Blocks = stored,
                CreatedAt = DateTime.UtcNow
            };
            using (var context = DatabaseManager.Instance.CharacterDatabase.NewContext())
            {
                context.Template.Add(design);
                context.SaveChanges();
            }
            // designs made automatically when upkeep runs out keep their screenshots now (hotfix
            // 1 Apr 2016, P4365; TASKS 6.39): the site's Site Management shots become the design's, before the client
            // hears of the design. Deleted sites' designs get them too (same Save).
            int shots = Templates.CopySiteImages(claim.Id, design.Id);
            if (PlayerManager.FindByGuid(claim.OwnerGuid) is WorldSession owner)
                Templates.SendAdded(owner, design);
            log.Info($"Site {claim.Id}: saved design {design.Id} \"{name}\" ({cells} cell(s), {voxels} voxel(s), {shots} screenshot(s)) for 0x{claim.OwnerGuid:X}.");
            return (design, materials);
        }

        /// <summary>
        /// The materials of the owner's voxels as items, at the rate building charged them (BuildMaterials.VoxelsPerItem
        /// voxels per item, rounded down). Free-to-use materials (no item) give nothing, and nothing at all under
        /// FreeBuild, where building cost nothing (<see cref="Building.FreeMaterialsNow"/>): otherwise one could build for
        /// free, delete the site and collect the items (review 2026-10-31).
        /// </summary>
        public static List<(uint Item, uint Count)> RefundItems(Dictionary<ushort, uint> materials)
        {
            if (Building.FreeMaterialsNow)
                return new List<(uint Item, uint Count)>();
            var voxelsByItem = new Dictionary<uint, long>();
            foreach ((ushort material, uint voxels) in materials)
                if (BuildMaterials.ItemFor(material) is uint item and not 0)
                    voxelsByItem[item] = voxelsByItem.GetValueOrDefault(item) + voxels;
            var items = new List<(uint Item, uint Count)>();
            foreach ((uint item, long voxels) in voxelsByItem)
            {
                long count = voxels / BuildMaterials.VoxelsPerItem;
                if (count > 0)
                    items.Add((item, (uint)Math.Min(count, uint.MaxValue)));
            }
            return items;
        }

        /// <summary>
        /// The dialog's "All resources … will be returned to your inventory": <see cref="RefundItems"/> given to the owner
        /// (online: the site was deleted). Returns how many items.
        /// </summary>
        public static int Refund(WorldSession owner, Dictionary<ushort, uint> materials)
        {
            int given = 0;
            foreach ((uint item, uint count) in RefundItems(materials))
            {
                owner.GiveItem(item, count);
                given += (int)count;
            }
            return given;
        }

        private static string Trim(string name) => name.Length > 64 ? name[..64] : name;

        private static string Owner(ulong character)
            => PlayerManager.FindByGuid(character)?.CharacterName
               ?? DatabaseManager.Instance.CharacterDatabase.GetCharacterNames(new[] { character }).GetValueOrDefault(character, "");
    }
}
