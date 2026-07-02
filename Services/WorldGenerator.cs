using System;
using System.Numerics;
using Raylib_cs;
using IslandWorldGenerator.Models;

namespace IslandWorldGenerator.Services
{
    public class WorldGenerator
    {
        private readonly int _width;
        private readonly int _length;
        private readonly int _seed;
        private readonly float _scale;
        private readonly int _octaves;
        private readonly float _persistence;
        private readonly float _lacunarity;
        private readonly float _maxHeight;
        private readonly float _waterLevel;
        private readonly float _moistureShift;
        private readonly float _temperatureShift;
        private readonly float _temperatureCelsius;
        private readonly float _islandSize;

        private readonly NoiseGenerator _elevationNoise;
        private readonly NoiseGenerator _moistureNoise;
        private readonly Random _random;

        /*
         * Constructeur de WorldGenerator. Configure les parametres de bruit
         * et d'echelle pour la generation procedurale de l'ile.
         */
        public WorldGenerator(int width, int length, int seed, float scale = 0.02f, int octaves = 4, 
                              float persistence = 0.45f, float lacunarity = 2.0f, float maxHeight = 20f, float waterLevel = 0.25f,
                              float moistureShift = 0.0f, float temperatureShift = 0.0f, float islandSize = 1.0f)
        {
            _width = width;
            _length = length;
            _seed = seed;
            _scale = scale;
            _octaves = octaves;
            _persistence = persistence;
            _lacunarity = lacunarity;
            _maxHeight = maxHeight;
            _waterLevel = waterLevel;
            _moistureShift = moistureShift;
            _temperatureShift = temperatureShift;
            _temperatureCelsius = 15.0f + temperatureShift / 0.008f;
            _islandSize = islandSize;

            _elevationNoise = new NoiseGenerator(_seed);
            _moistureNoise = new NoiseGenerator(_seed + 12345);
            _random = new Random(_seed);
        }

        /*
         * Genere la grille bidimensionnelle de blocs constituant l'ile
         * en calculant le relief, l'humidite et les biomes pour chaque position.
         */
        public WorldBlock[,] GenerateWorld()
        {
            WorldBlock[,] grid = new WorldBlock[_width, _length];

            // 1. Première passe : calculer le relief brut et l'humidité brute.
            // L'humidité est normalisée par carte pour éviter qu'une seed supprime entièrement
            // les biomes secs ou froids.
            float[,] rawElevation = new float[_width, _length];
            float[,] rawMoisture = new float[_width, _length];
            float maxElevation = 0f;
            float minMoisture = float.MaxValue;
            float maxMoisture = float.MinValue;

            for (int x = 0; x < _width; x++)
            {
                for (int z = 0; z < _length; z++)
                {
                    double nx = x * _scale;
                    double nz = z * _scale;

                    // Calcul du masque de distance circulaire pour sculpter une île ronde
                    float dx = (2.0f * x / _width) - 1.0f;
                    float dz = (2.0f * z / _length) - 1.0f;
                    float dist = MathF.Sqrt(dx * dx + dz * dz);
                    
                    float falloff = Math.Clamp(1.0f - dist * dist * (1.35f / _islandSize), 0.0f, 1.0f);

                    float elevation = (float)_elevationNoise.Fbm(nx, nz, _octaves, _persistence, _lacunarity);
                    rawElevation[x, z] = elevation * falloff;

                    if (rawElevation[x, z] > maxElevation)
                    {
                        maxElevation = rawElevation[x, z];
                    }

                    float moisture = (float)_moistureNoise.Fbm(nx, nz, _octaves, _persistence, _lacunarity);
                    rawMoisture[x, z] = moisture;
                    minMoisture = Math.Min(minMoisture, moisture);
                    maxMoisture = Math.Max(maxMoisture, moisture);
                }
            }

            // 2. Deuxième passe : normalisation et calcul final des hauteurs/biomes/couleurs
            for (int x = 0; x < _width; x++)
            {
                for (int z = 0; z < _length; z++)
                {
                    float elevation = rawElevation[x, z];
                    
                    // Normalisation pour garantir la présence de tous les biomes d'altitude (neige/montagne)
                    if (maxElevation > 0f)
                    {
                        elevation = elevation / maxElevation;
                    }

                    double nx = x * _scale;
                    double nz = z * _scale;

                    float moistureRange = maxMoisture - minMoisture;
                    float moisture = moistureRange > 0f
                        ? (rawMoisture[x, z] - minMoisture) / moistureRange
                        : rawMoisture[x, z];
                    moisture = Math.Clamp(moisture + _moistureShift, 0.0f, 1.0f);

                    // Courbe de redistribution pour creuser l'océan et dresser les montagnes
                    float adjustedElevation = elevation;
                    if (elevation > _waterLevel)
                    {
                        float normalizedLand = (elevation - _waterLevel) / (1.0f - _waterLevel);
                        float steepLand = MathF.Pow(normalizedLand, 1.5f);
                        adjustedElevation = _waterLevel + steepLand * (1.0f - _waterLevel);
                    }
                    else
                    {
                        float normalizedWater = elevation / _waterLevel;
                        adjustedElevation = MathF.Pow(normalizedWater, 1.2f) * _waterLevel;
                    }

                    BiomeType biome = GetBiome(adjustedElevation, moisture);
                    Color color = GetBiomeColor(biome);

                    // Calcul de la hauteur physique en jeu (relief continu sans cratère)
                    float blockHeight = adjustedElevation * _maxHeight;

                    Vector3 position = new Vector3(x, blockHeight, z);

                    bool hasTree = false;
                    float treeHeight = 0;
                    Color foliageColor = Color.Green;

                    if (adjustedElevation > _waterLevel)
                    {
                        ConfigureVegetation(biome, moisture, adjustedElevation, out hasTree, out treeHeight, out foliageColor);
                    }

                    grid[x, z] = new WorldBlock
                    {
                        Position = position,
                        Height = blockHeight,
                        Biome = biome,
                        Color = color,
                        HasTree = hasTree,
                        TreeHeight = treeHeight,
                        FoliageColor = foliageColor
                    };
                }
            }

            return grid;
        }

        /*
         * Determine le biome d'une case de terrain en fonction de son altitude
         * et de son humidite locale relative.
         */
        private BiomeType GetBiome(float elevation, float moisture)
        {
            if (elevation < _waterLevel)
            {
                if (elevation < _waterLevel * 0.6f)
                    return BiomeType.DeepOcean;
                return BiomeType.ShallowWater;
            }

            // Plages : anneau sableux continu élargi
            if (elevation < _waterLevel + 0.028f)
            {
                return BiomeType.Beach;
            }

            float temperature = GetLocalTemperature(elevation);

            // Ajustement des lignes de montagne/neige selon la température de l'île.
            // Plus il fait chaud, plus la neige et la montagne "minérale" remontent.
            float snowThreshold = Math.Clamp(0.78f + _temperatureShift, 0.4f, 1.0f);
            float mountainThreshold = Math.Clamp(0.64f + _temperatureShift, 0.3f, 1.0f);

            if (elevation > snowThreshold)
            {
                return BiomeType.Snow;
            }
            if (elevation > mountainThreshold)
            {
                return BiomeType.Mountain;
            }

            // Textures climatiques des zones basses.
            // Même relief de plaine, mais rendu différent selon le climat :
            // chaud + sec => désert, froid => taïga, tempéré + humide => forêt.
            if (temperature > 0.68f && moisture < 0.48f)
            {
                return BiomeType.Desert;
            }

            if (temperature < 0.34f)
            {
                return BiomeType.Taiga;
            }

            if (moisture > 0.60f)
            {
                return BiomeType.Forest;
            }
            
            return BiomeType.Plains;
        }

        /*
         * Calcule la temperature locale corrigee par l'altitude du terrain.
         */
        private float GetLocalTemperature(float elevation)
        {
            float globalTemperature = Math.Clamp(0.5f + _temperatureShift, 0.0f, 1.0f);
            float landElevation = Math.Clamp((elevation - _waterLevel) / (1.0f - _waterLevel), 0.0f, 1.0f);

            // L'altitude refroidit localement le climat : à température globale identique,
            // un plateau élevé bascule plus facilement vers taïga/neige qu'une côte.
            return Math.Clamp(globalTemperature - landElevation * 0.28f, 0.0f, 1.0f);
        }

        /*
         * Definit la presence, la taille et la couleur de la vegetation (arbres)
         * pour un bloc en fonction de son biome, humidite et altitude.
         */
        private void ConfigureVegetation(BiomeType biome, float moisture, float elevation, out bool hasTree, out float treeHeight, out Color foliageColor)
        {
            hasTree = false;
            treeHeight = 0f;
            foliageColor = Color.Green;

            float temperature = GetLocalTemperature(elevation);
            if (_temperatureCelsius >= 70.0f)
            {
                return;
            }

            switch (biome)
            {
                case BiomeType.Forest:
                    if (_random.NextDouble() < Math.Clamp(0.06f + moisture * 0.08f, 0.06f, 0.14f))
                    {
                        hasTree = true;
                        treeHeight = _random.Next(3, 6);
                        foliageColor = temperature < 0.45f
                            ? new Color(35, 95, 55, 255)
                            : new Color(34, 139, 34, 255);
                    }
                    break;

                case BiomeType.Taiga:
                    if (_random.NextDouble() < Math.Clamp(0.05f + moisture * 0.05f, 0.05f, 0.10f))
                    {
                        hasTree = true;
                        treeHeight = _random.Next(4, 7);
                        foliageColor = new Color(20, 60, 45, 255);
                    }
                    break;

                case BiomeType.Plains:
                    if (_random.NextDouble() < Math.Clamp(0.005f + moisture * 0.025f, 0.005f, 0.03f))
                    {
                        hasTree = true;
                        treeHeight = _random.Next(2, 4);
                        foliageColor = new Color(70, 170, 65, 255);
                    }
                    break;

                case BiomeType.Desert:
                    if (_random.NextDouble() < 0.01)
                    {
                        hasTree = true;
                        treeHeight = _random.Next(2, 4);
                        foliageColor = new Color(70, 135, 55, 255);
                    }
                    break;
            }
        }

        /*
         * Associe une couleur de base a chaque type de biome pour le rendu visuel.
         */
        private Color GetBiomeColor(BiomeType biome)
        {
            return biome switch
            {
                BiomeType.DeepOcean => new Color(20, 40, 110, 255),
                BiomeType.ShallowWater => new Color(35, 95, 185, 255),
                BiomeType.Beach => new Color(225, 200, 130, 255),
                BiomeType.Desert => new Color(210, 175, 90, 255),
                BiomeType.Plains => new Color(85, 170, 70, 255),
                BiomeType.Forest => new Color(34, 110, 42, 255),
                BiomeType.Taiga => new Color(28, 80, 52, 255),
                BiomeType.Mountain => new Color(110, 115, 120, 255),
                BiomeType.Snow => new Color(245, 248, 250, 255),
                _ => Color.White
            };
        }
    }
}
