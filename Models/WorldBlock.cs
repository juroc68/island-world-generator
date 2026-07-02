using System.Numerics;
using Raylib_cs;

namespace IslandWorldGenerator.Models
{
    public struct WorldBlock
    {
        public Vector3 Position { get; set; }
        public float Height { get; set; }
        public BiomeType Biome { get; set; }
        public Color Color { get; set; }
        public bool HasTree { get; set; }
        public float TreeHeight { get; set; }
        public Color FoliageColor { get; set; }
    }
}
