using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using Raylib_cs;
using IslandWorldGenerator.Models;
using IslandWorldGenerator.Services;

namespace IslandWorldGenerator
{
    class Program
    {
        private const int ScreenWidth = 1280;
        private const int ScreenHeight = 720;

        // Paramètres de la carte (dynamiques)
        private static int _mapWidth = 128;
        private static int _mapLength = 128;
        
        private static int _seed = 42;
        private static float _scale = 0.02f;
        private static int _octaves = 4;
        private static float _persistence = 0.45f;      // Rugosité / Variations du relief
        private static float _lacunarity = 2.0f;
        private static float _maxHeight = 20f;
        private static float _waterLevel = 0.25f;
        private static int _humanCount = 12;
        private static int _biomeEdgePrecision = 2;
        private static BiomeBoundaryMode _biomeBoundaryMode = BiomeBoundaryMode.SharpTiles;
        
        // Contrôles de répartition des biomes et de surface de l'île
        private static float _moistureShift = 0.0f;      // Ajustement global de l'humidité
        private static int _temperatureCelsius = 15;     // Température en Degrés Celsius (-50°C à +70°C)
        private static float _islandSize = 1.0f;         // Taille/Surface de l'île ronde

        private static WorldBlock[,] _worldGrid = new WorldBlock[0, 0];
        private static bool _needsRegen = true;
        private static bool _showMenu = true;

        // Gestion des onglets
        private static int _activeTab = 0; // 0: Terrain & Hauteur, 1: Climat & Surface, 2: Configuration, 3: Aide
        private static string? _activeNumberInput = null;
        private static string _numberInputText = "";

        private static Model _terrainModel;
        private static bool _hasModel = false;
        private static readonly List<IslandHuman> _humans = new List<IslandHuman>();
        private static Random _humanRandom = new Random(_seed + 9001);

        private enum BiomeBoundaryMode
        {
            SharpTiles,
            MarchingSquares
        }

        private readonly struct TerrainTriangle
        {
            public TerrainTriangle(Vector3 a, Vector3 b, Vector3 c, Color color)
            {
                A = a;
                B = b;
                C = c;
                Color = color;
            }

            public readonly Vector3 A;
            public readonly Vector3 B;
            public readonly Vector3 C;
            public readonly Color Color;
        }

        private class IslandHuman
        {
            public string Name = "";
            public Vector2 Position;
            public Vector2 Direction;
            public float Speed;
            public float WanderTimer;
            public float JumpTimer;
            public float JumpDuration;
            public float NextJumpDelay;
            public Color BodyColor;
        }

        private static readonly string[] FrenchFirstNames =
        {
            "Jean", "Pierre", "Louis", "Henri", "Marcel", "Lucien", "Gaston", "Armand",
            "Jules", "Emile", "Paul", "Andre", "Bernard", "Claude", "Michel", "Alain",
            "Rene", "Robert", "Georges", "Jacques", "Maurice", "Roger", "Yves", "Etienne",
            "Baptiste", "Antoine", "Francois", "Nicolas", "Marin", "Basile", "Leon", "Gustave",
            "Marie", "Jeanne", "Louise", "Alice", "Madeleine", "Suzanne", "Simone", "Lucie",
            "Marguerite", "Juliette", "Camille", "Claire", "Sophie", "Helene", "Anne", "Celine",
            "Adele", "Manon", "Charlotte", "Pauline", "Gabrielle", "Colette", "Nadine", "Elise"
        };

        // Variables pour la caméra orbitale personnalisée
        private static float _cameraRotationAngle = 0.0f;     // Rotation horizontale (degrés)
        private static float _cameraElevationAngle = 35.0f;    // Élévation verticale (degrés)
        private static float _cameraDistance = 160.0f;         // Distance de la caméra
        private static Vector3 _cameraTarget = new Vector3(_mapWidth / 2.0f, 4.0f, _mapLength / 2.0f);

        static void Main(string[] args)
        {
            // Permettre le redimensionnement natif de la fenêtre par l'utilisateur
            Raylib.SetConfigFlags(ConfigFlags.ResizableWindow | ConfigFlags.Msaa4xHint);
            
            // Initialisation de la fenêtre Raylib
            Raylib.InitWindow(ScreenWidth, ScreenHeight, "Générateur d'Île 3D Procédural");
            Raylib.SetTargetFPS(60);

            // Charger la police moderne avec le jeu Latin-1 (pour afficher correctement les accents français)
            int[] codepoints = new int[224];
            for (int i = 0; i < 224; i++)
            {
                codepoints[i] = 32 + i;
            }
            string fontPath = Path.Combine(AppContext.BaseDirectory, "assets", "fonts", "LiberationSans-Regular.ttf");
            Font font = Raylib.LoadFontEx(fontPath, 32, codepoints, 224);
            Raylib.SetTextureFilter(font.Texture, TextureFilter.Bilinear);

            // Rendre le curseur visible
            Raylib.EnableCursor();

            // Initialisation de la caméra 3D
            Camera3D camera = new Camera3D();
            camera.Up = new Vector3(0.0f, 1.0f, 0.0f);
            camera.FovY = 50.0f;
            camera.Projection = CameraProjection.Perspective;

            // Aligner la caméra initialement sur la taille de départ (128)
            ResetCamera();

            // Générateur pour nouvelles graines
            Random random = new Random();

            // Répartition des biomes
            int[] biomeCounts = new int[Enum.GetValues(typeof(BiomeType)).Length];

            while (!Raylib.WindowShouldClose())
            {
                // ---- LOGIQUE DE MISE A JOUR ----

                // Régénération de l'île si nécessaire
                if (_needsRegen)
                {
                    if (_hasModel)
                    {
                        Raylib.UnloadModel(_terrainModel);
                        _hasModel = false;
                    }

                    // Calcul de la déviation de température à partir des Degrés Celsius
                    float tempShift = (_temperatureCelsius - 15.0f) * 0.008f;

                    WorldGenerator generator = new WorldGenerator(
                        _mapWidth, _mapLength, _seed, _scale, _octaves, 
                        _persistence, _lacunarity, _maxHeight, _waterLevel,
                        _moistureShift, tempShift, _islandSize
                    );
                    _worldGrid = generator.GenerateWorld();
                    
                    _terrainModel = GenerateSmoothTerrainMesh(_worldGrid, _mapWidth, _mapLength);
                    _hasModel = true;
                    _needsRegen = false;

                    // Calculer les statistiques des biomes
                    Array.Clear(biomeCounts, 0, biomeCounts.Length);
                    for (int x = 0; x < _mapWidth; x++)
                    {
                        for (int z = 0; z < _mapLength; z++)
                        {
                            biomeCounts[(int)_worldGrid[x, z].Biome]++;
                        }
                    }

                    RegenerateHumans();
                }

                // Touche d'action rapide pour une nouvelle graine
                if (Raylib.IsKeyPressed(KeyboardKey.Space))
                {
                    _seed = random.Next(1, 100000);
                    _needsRegen = true;
                }

                // Réinitialiser la caméra au clavier
                if (Raylib.IsKeyPressed(KeyboardKey.R))
                {
                    ResetCamera();
                }

                // Touche pour masquer/afficher le menu
                if (Raylib.IsKeyPressed(KeyboardKey.H) || Raylib.IsKeyPressed(KeyboardKey.Tab))
                {
                    _showMenu = !_showMenu;
                }

                // Mise à jour de la caméra orbitale
                UpdateOrbitCamera(ref camera);
                UpdateHumans(Raylib.GetFrameTime());

                // ---- LOGIQUE DE RENDU ----

                Raylib.BeginDrawing();
                
                // Fond sombre
                Raylib.ClearBackground(new Color((byte)18, (byte)20, (byte)28, (byte)255));

                // Rendu de la scène 3D
                Raylib.BeginMode3D(camera);

                // 1. Dessiner le maillage de terrain de l'île
                if (_hasModel)
                {
                    Raylib.DrawModel(_terrainModel, Vector3.Zero, 1.0f, Color.White);
                }

                // 2. Dessiner la mer (eau translucide)
                float waterHeight = _waterLevel * _maxHeight;
                Vector3 waterPos = new Vector3(_mapWidth / 2.0f - 0.5f, waterHeight / 2.0f, _mapLength / 2.0f - 0.5f);
                Raylib.DrawCube(
                    waterPos, 
                    _mapWidth, 
                    waterHeight, 
                    _mapLength, 
                    new Color((byte)30, (byte)100, (byte)190, (byte)140)
                );

                // 3. Dessiner la végétation
                for (int x = 0; x < _mapWidth; x++)
                {
                    for (int z = 0; z < _mapLength; z++)
                    {
                        var block = _worldGrid[x, z];

                        if (block.HasTree && block.Height > waterHeight)
                        {
                            float trunkH = block.TreeHeight;

                            if (block.Biome == BiomeType.Desert)
                            {
                                // Cactus
                                Vector3 trunkPos = new Vector3(x, block.Height + trunkH / 2.0f, z);
                                Raylib.DrawCube(trunkPos, 0.2f, trunkH, 0.2f, block.FoliageColor);
                                
                                Vector3 branch1Pos = new Vector3(x - 0.3f, block.Height + trunkH * 0.7f, z);
                                Raylib.DrawCube(branch1Pos, 0.4f, 0.15f, 0.15f, block.FoliageColor);
                                Raylib.DrawCube(new Vector3(x - 0.5f, block.Height + trunkH * 0.8f, z), 0.15f, 0.3f, 0.15f, block.FoliageColor);

                                Vector3 branch2Pos = new Vector3(x + 0.3f, block.Height + trunkH * 0.5f, z);
                                Raylib.DrawCube(branch2Pos, 0.4f, 0.15f, 0.15f, block.FoliageColor);
                                Raylib.DrawCube(new Vector3(x + 0.5f, block.Height + trunkH * 0.65f, z), 0.15f, 0.3f, 0.15f, block.FoliageColor);
                            }
                            else if (block.Biome == BiomeType.Taiga)
                            {
                                // Sapin conique
                                Vector3 trunkPos = new Vector3(x, block.Height + trunkH / 3.0f, z);
                                Raylib.DrawCube(trunkPos, 0.15f, trunkH * 0.6f, 0.15f, new Color((byte)100, (byte)65, (byte)35, (byte)255));

                                float leavesStartY = block.Height + trunkH * 0.3f;
                                Vector3 base1 = new Vector3(x, leavesStartY, z);
                                Vector3 top1 = new Vector3(x, leavesStartY + trunkH * 0.5f, z);
                                Raylib.DrawCylinderEx(base1, top1, 0.75f, 0.0f, 8, block.FoliageColor);

                                Vector3 base2 = new Vector3(x, leavesStartY + trunkH * 0.3f, z);
                                Vector3 top2 = new Vector3(x, leavesStartY + trunkH * 0.8f, z);
                                Raylib.DrawCylinderEx(base2, top2, 0.5f, 0.0f, 8, block.FoliageColor);
                            }
                            else if (block.Biome != BiomeType.Beach && block.Biome != BiomeType.Mountain && block.Biome != BiomeType.Snow)
                            {
                                // Arbre rond standard
                                Vector3 trunkPos = new Vector3(x, block.Height + trunkH / 2.0f, z);
                                Raylib.DrawCube(trunkPos, 0.2f, trunkH, 0.2f, new Color((byte)101, (byte)67, (byte)33, (byte)255));

                                Vector3 foliagePos = new Vector3(x, block.Height + trunkH, z);
                                Raylib.DrawSphere(foliagePos, 0.75f, block.FoliageColor);
                                Raylib.DrawSphere(foliagePos + new Vector3(0.2f, 0.2f, 0.1f), 0.5f, block.FoliageColor);
                                Raylib.DrawSphere(foliagePos + new Vector3(-0.15f, 0.1f, -0.2f), 0.45f, block.FoliageColor);
                            }
                        }
                    }
                }

                // 4. Dessiner les habitants
                DrawHumans();

                // 5. Dessiner la bordure bleue de la carte à la surface de l'eau
                float borderY = waterHeight + 0.05f;
                Color borderColor = new Color((byte)0, (byte)150, (byte)255, (byte)100);
                Raylib.DrawLine3D(new Vector3(0, borderY, 0), new Vector3(_mapWidth, borderY, 0), borderColor);
                Raylib.DrawLine3D(new Vector3(_mapWidth, borderY, 0), new Vector3(_mapWidth, borderY, _mapLength), borderColor);
                Raylib.DrawLine3D(new Vector3(_mapWidth, borderY, _mapLength), new Vector3(0, borderY, _mapLength), borderColor);
                Raylib.DrawLine3D(new Vector3(0, borderY, _mapLength), new Vector3(0, borderY, 0), borderColor);

                Raylib.EndMode3D();

                DrawHumanNames(camera, font);

                // Rendu de l'interface graphique (HUD / Menu)
                DrawInterface(biomeCounts, font, random);

                Raylib.EndDrawing();
            }

            // Nettoyage de la mémoire avant fermeture
            Raylib.UnloadFont(font);
            if (_hasModel)
            {
                Raylib.UnloadModel(_terrainModel);
            }
            Raylib.CloseWindow();
        }

        private static void RegenerateHumans()
        {
            _humans.Clear();
            _humanRandom = new Random(_seed + 9001);

            for (int i = 0; i < _humanCount; i++)
            {
                if (TryFindHumanSpawn(out Vector2 spawn))
                {
                    _humans.Add(CreateHuman(spawn));
                }
            }
        }

        private static void SyncHumanCount()
        {
            while (_humans.Count > _humanCount)
            {
                _humans.RemoveAt(_humans.Count - 1);
            }

            while (_humans.Count < _humanCount)
            {
                if (!TryFindHumanSpawn(out Vector2 spawn))
                {
                    break;
                }

                _humans.Add(CreateHuman(spawn));
            }
        }

        private static IslandHuman CreateHuman(Vector2 position)
        {
            return new IslandHuman
            {
                Name = GenerateHumanName(),
                Position = position,
                Direction = RandomDirection(),
                Speed = 0.8f + (float)_humanRandom.NextDouble() * 0.8f,
                WanderTimer = 0.4f + (float)_humanRandom.NextDouble() * 2.5f,
                JumpTimer = 0.0f,
                JumpDuration = 0.35f,
                NextJumpDelay = 0.8f + (float)_humanRandom.NextDouble() * 2.8f,
                BodyColor = new Color((byte)225, (byte)175, (byte)130, (byte)255)
            };
        }

        private static bool TryFindHumanSpawn(out Vector2 spawn)
        {
            for (int attempt = 0; attempt < 1200; attempt++)
            {
                int x = _humanRandom.Next(1, Math.Max(2, _mapWidth - 1));
                int z = _humanRandom.Next(1, Math.Max(2, _mapLength - 1));

                if (IsHumanWalkable(x, z))
                {
                    spawn = new Vector2(x + 0.5f, z + 0.5f);
                    return true;
                }
            }

            spawn = Vector2.Zero;
            return false;
        }

        private static void UpdateHumans(float deltaTime)
        {
            if (_worldGrid.Length == 0 || _humans.Count == 0)
            {
                return;
            }

            foreach (var human in _humans)
            {
                human.WanderTimer -= deltaTime;
                human.NextJumpDelay -= deltaTime;

                if (human.WanderTimer <= 0.0f)
                {
                    human.Direction = RandomDirection();
                    human.WanderTimer = 0.6f + (float)_humanRandom.NextDouble() * 2.4f;
                }

                if (human.JumpTimer <= 0.0f && human.NextJumpDelay <= 0.0f)
                {
                    human.JumpTimer = human.JumpDuration;
                    human.NextJumpDelay = 0.8f + (float)_humanRandom.NextDouble() * 3.2f;
                }

                if (human.JumpTimer > 0.0f)
                {
                    human.JumpTimer = Math.Max(0.0f, human.JumpTimer - deltaTime);
                }

                Vector2 nextPosition = human.Position + human.Direction * human.Speed * deltaTime;
                int nextX = (int)MathF.Round(nextPosition.X);
                int nextZ = (int)MathF.Round(nextPosition.Y);

                if (IsHumanWalkable(nextX, nextZ))
                {
                    human.Position = nextPosition;
                }
                else
                {
                    human.Direction = RandomDirection();
                    human.WanderTimer = 0.4f + (float)_humanRandom.NextDouble() * 1.2f;
                }
            }
        }

        private static void DrawHumans()
        {
            if (_worldGrid.Length == 0)
            {
                return;
            }

            foreach (var human in _humans)
            {
                float terrainHeight = GetTerrainHeightAt(human.Position);
                float jumpProgress = human.JumpTimer > 0.0f ? 1.0f - human.JumpTimer / human.JumpDuration : 0.0f;
                float jumpOffset = human.JumpTimer > 0.0f ? MathF.Sin(jumpProgress * MathF.PI) * 0.45f : 0.0f;
                float baseY = terrainHeight + jumpOffset;

                float bodyRadius = 0.23f;
                Vector3 bodyBase = new Vector3(human.Position.X, baseY + 0.32f, human.Position.Y);
                Vector3 bodyTop = new Vector3(human.Position.X, baseY + 0.88f, human.Position.Y);
                Vector3 head = new Vector3(human.Position.X, baseY + 1.18f, human.Position.Y);
                Vector3 spearCenter = new Vector3(human.Position.X + 0.34f, baseY + 0.88f, human.Position.Y);

                Color skin = human.BodyColor;
                Color wood = new Color((byte)120, (byte)75, (byte)35, (byte)255);

                Raylib.DrawCylinderEx(bodyBase, bodyTop, bodyRadius, bodyRadius, 12, human.BodyColor);
                Raylib.DrawSphere(bodyBase, bodyRadius, human.BodyColor);
                Raylib.DrawSphere(bodyTop, bodyRadius, human.BodyColor);
                Raylib.DrawSphere(head, 0.22f, skin);
                Raylib.DrawCube(spearCenter, 0.08f, 1.45f, 0.08f, wood);
            }
        }

        private static void DrawHumanNames(Camera3D camera, Font font)
        {
            if (_worldGrid.Length == 0 || _humans.Count == 0)
            {
                return;
            }

            int screenWidth = Raylib.GetScreenWidth();
            int screenHeight = Raylib.GetScreenHeight();

            foreach (var human in _humans)
            {
                float terrainHeight = GetTerrainHeightAt(human.Position);
                float jumpProgress = human.JumpTimer > 0.0f ? 1.0f - human.JumpTimer / human.JumpDuration : 0.0f;
                float jumpOffset = human.JumpTimer > 0.0f ? MathF.Sin(jumpProgress * MathF.PI) * 0.45f : 0.0f;
                Vector3 labelWorldPosition = new Vector3(human.Position.X, terrainHeight + jumpOffset + 1.55f, human.Position.Y);
                Vector2 labelScreenPosition = Raylib.GetWorldToScreen(labelWorldPosition, camera);

                if (labelScreenPosition.X < -80 || labelScreenPosition.X > screenWidth + 80 ||
                    labelScreenPosition.Y < -30 || labelScreenPosition.Y > screenHeight + 30)
                {
                    continue;
                }

                float fontSize = 11f;
                Vector2 textSize = Raylib.MeasureTextEx(font, human.Name, fontSize, 1.0f);
                Vector2 textPosition = new Vector2(
                    labelScreenPosition.X - textSize.X / 2.0f,
                    labelScreenPosition.Y - textSize.Y / 2.0f
                );

                Raylib.DrawTextEx(font, human.Name, textPosition, fontSize, 1.0f, Color.White);
            }
        }

        private static bool IsHumanWalkable(int x, int z)
        {
            if (x < 0 || z < 0 || x >= _mapWidth || z >= _mapLength || _worldGrid.Length == 0)
            {
                return false;
            }

            var block = _worldGrid[x, z];
            return block.Height > _waterLevel * _maxHeight + 0.05f
                && block.Biome != BiomeType.DeepOcean
                && block.Biome != BiomeType.ShallowWater;
        }

        private static float GetTerrainHeightAt(Vector2 position)
        {
            int x = Math.Clamp((int)MathF.Round(position.X), 0, _mapWidth - 1);
            int z = Math.Clamp((int)MathF.Round(position.Y), 0, _mapLength - 1);
            return _worldGrid[x, z].Height;
        }

        private static Vector2 RandomDirection()
        {
            float angle = (float)_humanRandom.NextDouble() * MathF.PI * 2.0f;
            return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        }

        private static string GenerateHumanName()
        {
            return FrenchFirstNames[_humanRandom.Next(FrenchFirstNames.Length)];
        }

        private static void ResetCamera()
        {
            _cameraRotationAngle = 0.0f;
            _cameraElevationAngle = 35.0f;
            _cameraDistance = _mapWidth * 1.25f;
            _cameraTarget = new Vector3(_mapWidth / 2.0f, 4.0f, _mapLength / 2.0f);
        }

        private static void UpdateOrbitCamera(ref Camera3D camera)
        {
            int currentScreenWidth = Raylib.GetScreenWidth();
            int currentScreenHeight = Raylib.GetScreenHeight();

            // Zoom avec la molette de la souris
            float wheelMove = Raylib.GetMouseWheelMove();
            _cameraDistance -= wheelMove * (_mapWidth * 0.05f);
            _cameraDistance = Math.Clamp(_cameraDistance, 15.0f, _mapWidth * 2.5f);

            // Déplacement orbital
            if (Raylib.IsMouseButtonDown(MouseButton.Right) || Raylib.IsMouseButtonDown(MouseButton.Left))
            {
                Vector2 mousePos = Raylib.GetMousePosition();
                
                // Ignorer les clics dans les panneaux d'interface.
                bool insideLeftMenu = _showMenu && (mousePos.X < 380);
                bool insideRightMenu = _showMenu && (mousePos.X > currentScreenWidth - 240 && mousePos.Y < 230);

                if (!insideLeftMenu && !insideRightMenu)
                {
                    Vector2 mouseDelta = Raylib.GetMouseDelta();
                    _cameraRotationAngle -= mouseDelta.X * 0.35f;
                    _cameraElevationAngle += mouseDelta.Y * 0.35f;
                    _cameraElevationAngle = Math.Clamp(_cameraElevationAngle, 5.0f, 85.0f);
                }
            }

            // Déplacement panoramique (Pan) relatif à la caméra.
            // Z/W avance dans la direction regardée, Q/A se déplace vers la gauche de l'écran.
            float speed = _mapWidth * 0.006f;
            float radRotation = _cameraRotationAngle * (MathF.PI / 180.0f);
            Vector3 forward = new Vector3(-MathF.Sin(radRotation), 0.0f, -MathF.Cos(radRotation));
            Vector3 right = new Vector3(MathF.Cos(radRotation), 0.0f, -MathF.Sin(radRotation));

            if (_activeNumberInput == null)
            {
                if (Raylib.IsKeyDown(KeyboardKey.W) || Raylib.IsKeyDown(KeyboardKey.Z) || Raylib.IsKeyDown(KeyboardKey.Up))
                    _cameraTarget += forward * speed;
                if (Raylib.IsKeyDown(KeyboardKey.S) || Raylib.IsKeyDown(KeyboardKey.Down))
                    _cameraTarget -= forward * speed;
                if (Raylib.IsKeyDown(KeyboardKey.A) || Raylib.IsKeyDown(KeyboardKey.Q) || Raylib.IsKeyDown(KeyboardKey.Left))
                    _cameraTarget -= right * speed;
                if (Raylib.IsKeyDown(KeyboardKey.D) || Raylib.IsKeyDown(KeyboardKey.Right))
                    _cameraTarget += right * speed;
            }

            // Clamper la cible
            _cameraTarget.X = Math.Clamp(_cameraTarget.X, 0f, _mapWidth);
            _cameraTarget.Z = Math.Clamp(_cameraTarget.Z, 0f, _mapLength);
            _cameraTarget.Y = Math.Clamp(_cameraTarget.Y, 0f, _maxHeight);

            // Position 3D de la caméra
            float radElevation = _cameraElevationAngle * (MathF.PI / 180.0f);
            camera.Position = new Vector3(
                _cameraTarget.X + _cameraDistance * MathF.Cos(radElevation) * MathF.Sin(radRotation),
                _cameraTarget.Y + _cameraDistance * MathF.Sin(radElevation),
                _cameraTarget.Z + _cameraDistance * MathF.Cos(radElevation) * MathF.Cos(radRotation)
            );
            camera.Target = _cameraTarget;
        }

        // Dessin d'un bouton moderne
        private static void DrawButton(string text, Rectangle rect, Color baseCol, Color hoverCol, Font font, out bool clicked)
        {
            Vector2 mousePos = Raylib.GetMousePosition();
            bool isHovered = Raylib.CheckCollisionPointRec(mousePos, rect);
            clicked = isHovered && Raylib.IsMouseButtonPressed(MouseButton.Left);

            Color currentCol = isHovered ? hoverCol : baseCol;

            Raylib.DrawRectangleRec(rect, currentCol);
            Raylib.DrawRectangleLinesEx(rect, 1.0f, new Color((byte)255, (byte)255, (byte)255, (byte)45));

            float fontSize = 14f;
            Vector2 textSize = Raylib.MeasureTextEx(font, text, fontSize, 1.0f);
            Vector2 textPos = new Vector2(
                rect.X + (rect.Width - textSize.X) / 2.0f,
                rect.Y + (rect.Height - textSize.Y) / 2.0f
            );

            Raylib.DrawTextEx(font, text, textPos, fontSize, 1.0f, Color.White);
        }

        private static bool DrawFloatInput(string id, Rectangle rect, Font font, ref float value, float min, float max, int decimals)
        {
            Vector2 mousePos = Raylib.GetMousePosition();
            bool isHovered = Raylib.CheckCollisionPointRec(mousePos, rect);
            bool isActive = _activeNumberInput == id;

            if (Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                if (isHovered)
                {
                    _activeNumberInput = id;
                    _numberInputText = value.ToString($"F{decimals}", CultureInfo.InvariantCulture);
                    isActive = true;
                }
                else if (isActive)
                {
                    bool committed = CommitFloatInput(ref value, min, max);
                    _activeNumberInput = null;
                    return committed;
                }
            }

            if (isActive)
            {
                ReadNumberInputText();

                if (Raylib.IsKeyPressed(KeyboardKey.Enter))
                {
                    bool committed = CommitFloatInput(ref value, min, max);
                    _activeNumberInput = null;
                    return committed;
                }

                if (Raylib.IsKeyPressed(KeyboardKey.Escape))
                {
                    _activeNumberInput = null;
                }
            }

            Color fill = isActive
                ? new Color((byte)20, (byte)55, (byte)80, (byte)255)
                : isHovered
                    ? new Color((byte)45, (byte)52, (byte)70, (byte)255)
                    : new Color((byte)22, (byte)27, (byte)39, (byte)255);
            Color border = isActive
                ? new Color((byte)0, (byte)180, (byte)255, (byte)255)
                : new Color((byte)255, (byte)255, (byte)255, (byte)55);

            Raylib.DrawRectangleRec(rect, fill);
            Raylib.DrawRectangleLinesEx(rect, 1.0f, border);

            string displayText = isActive ? _numberInputText : value.ToString($"F{decimals}", CultureInfo.InvariantCulture);
            Raylib.DrawTextEx(font, displayText, new Vector2(rect.X + 7, rect.Y + 7), 14f, 1.0f, Color.White);

            return false;
        }

        private static bool DrawIntInput(string id, Rectangle rect, Font font, ref int value, int min, int max)
        {
            float floatValue = value;
            bool changed = DrawFloatInput(id, rect, font, ref floatValue, min, max, 0);
            if (!changed)
            {
                return false;
            }

            value = (int)Math.Clamp(MathF.Round(floatValue), min, max);
            return true;
        }

        private static bool CommitFloatInput(ref float value, float min, float max)
        {
            string normalizedText = _numberInputText.Replace(',', '.');
            if (!float.TryParse(normalizedText, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
            {
                return false;
            }

            float clamped = Math.Clamp(parsed, min, max);
            bool changed = Math.Abs(value - clamped) > 0.0001f;
            value = clamped;
            return changed;
        }

        private static void ReadNumberInputText()
        {
            int key = Raylib.GetCharPressed();
            while (key > 0)
            {
                char character = (char)key;
                if (char.IsDigit(character) || character == '-' || character == '+' || character == '.' || character == ',')
                {
                    _numberInputText += character == ',' ? '.' : character;
                }

                key = Raylib.GetCharPressed();
            }

            if (Raylib.IsKeyPressed(KeyboardKey.Backspace) && _numberInputText.Length > 0)
            {
                _numberInputText = _numberInputText[..^1];
            }
        }

        // Dessin de l'interface graphique adaptative avec onglets
        private static void DrawInterface(int[] biomeCounts, Font font, Random random)
        {
            int currentScreenWidth = Raylib.GetScreenWidth();
            int currentScreenHeight = Raylib.GetScreenHeight();

            Color btnNormal = new Color((byte)35, (byte)42, (byte)60, (byte)255);
            Color btnActive = new Color((byte)0, (byte)150, (byte)255, (byte)255);
            Color btnHover = new Color((byte)0, (byte)130, (byte)240, (byte)255);

            // Cas où le menu est masqué
            if (!_showMenu)
            {
                bool clickShow;
                Rectangle showBtn = new Rectangle(15, 15, 185, 35);
                DrawButton("AFFICHER LE MENU [H]", showBtn, new Color((byte)15, (byte)18, (byte)26, (byte)235), btnHover, font, out clickShow);
                if (clickShow)
                {
                    _showMenu = true;
                }

                // FPS discret en haut à droite (adaptatif)
                Raylib.DrawRectangle(currentScreenWidth - 95, 15, 80, 30, new Color((byte)15, (byte)18, (byte)26, (byte)220));
                Raylib.DrawRectangleLines(currentScreenWidth - 95, 15, 80, 30, new Color((byte)0, (byte)150, (byte)255, (byte)100));
                Raylib.DrawTextEx(font, $"FPS: {Raylib.GetFPS()}", new Vector2(currentScreenWidth - 85, 22), 14f, 1.0f, Color.Lime);
                return;
            }

            // ==========================================
            // PANNEAU DE CONTROLE GAUCHE (Formulaire vertical)
            // ==========================================
            Rectangle leftPanel = new Rectangle(15, 15, 350, currentScreenHeight - 30);
            Raylib.DrawRectangleRec(leftPanel, new Color((byte)15, (byte)18, (byte)26, (byte)235));
            Raylib.DrawRectangleLinesEx(leftPanel, 1.0f, new Color((byte)0, (byte)150, (byte)255, (byte)180));

            Raylib.DrawTextEx(font, "PARAMETRES DU MONDE", new Vector2(33, 30), 15f, 1.0f, new Color((byte)0, (byte)180, (byte)255, (byte)255));
            Raylib.DrawLine(33, 51, 347, 51, new Color((byte)255, (byte)255, (byte)255, (byte)35));

            string[] tabs = { "Terrain & relief", "Climat & surface", "Monde", "Aide" };
            float tabY = 64f;
            for (int i = 0; i < tabs.Length; i++)
            {
                bool tabClicked;
                Rectangle tabRect = new Rectangle(33, tabY, 314, 28);
                Color tabCol = (_activeTab == i) ? btnActive : btnNormal;
                DrawButton(tabs[i], tabRect, tabCol, btnHover, font, out tabClicked);
                if (tabClicked)
                {
                    _activeTab = i;
                }
                tabY += 34f;
            }

            Raylib.DrawLine(33, 205, 347, 205, new Color((byte)255, (byte)255, (byte)255, (byte)35));

            float rowX = 33f;
            float rowY = 221f;
            float rowStep = 66f;
            float inputX = 33f;
            float inputWidth = 82f;
            float minusX = 124f;
            float plusX = 166f;
            // ==========================================
            // RENDU DU CONTENU SELON L'ONGLET ACTIF
            // ==========================================
            if (_activeTab == 0)
            {
                // Onglet 0: Terrain & Relief
                // Graine
                Raylib.DrawTextEx(font, "Graine", new Vector2(rowX, rowY), 13f, 1.0f, Color.LightGray);
                Raylib.DrawTextEx(font, $"{_seed}", new Vector2(rowX, rowY + 16), 11f, 1.0f, new Color((byte)155, (byte)165, (byte)185, (byte)255));
                if (DrawIntInput("seed", new Rectangle(inputX, rowY + 31, inputWidth, 30), font, ref _seed, 1, 999999))
                {
                    _needsRegen = true;
                }
                bool clickRegen;
                DrawButton("Nouvelle", new Rectangle(minusX, rowY + 31, 112, 30), btnNormal, btnHover, font, out clickRegen);
                if (clickRegen)
                {
                    _seed = random.Next(1, 100000);
                    _needsRegen = true;
                }
                rowY += rowStep;

                // Échelle du relief : affichée en tuiles, stockée en fréquence de bruit.
                float reliefScaleTiles = 1.0f / _scale;
                Raylib.DrawTextEx(font, "Taille des reliefs", new Vector2(rowX, rowY), 13f, 1.0f, Color.LightGray);
                Raylib.DrawTextEx(font, $"{reliefScaleTiles:F0} tuiles entre grands reliefs (bruit {_scale:F4})", new Vector2(rowX, rowY + 16), 11f, 1.0f, new Color((byte)155, (byte)165, (byte)185, (byte)255));
                if (DrawFloatInput("relief-scale", new Rectangle(inputX, rowY + 31, inputWidth, 30), font, ref reliefScaleTiles, 8f, 200f, 0))
                {
                    _scale = Math.Clamp(1.0f / reliefScaleTiles, 0.005f, 0.12f);
                    _needsRegen = true;
                }
                bool scaleMinus, scalePlus;
                DrawButton("-", new Rectangle(minusX, rowY + 31, 36, 30), btnNormal, btnHover, font, out scaleMinus);
                DrawButton("+", new Rectangle(plusX, rowY + 31, 36, 30), btnNormal, btnHover, font, out scalePlus);
                if (scaleMinus)
                {
                    reliefScaleTiles = Math.Max(8f, reliefScaleTiles - 5f);
                    _scale = Math.Clamp(1.0f / reliefScaleTiles, 0.005f, 0.12f);
                    _needsRegen = true;
                }
                if (scalePlus)
                {
                    reliefScaleTiles = Math.Min(200f, reliefScaleTiles + 5f);
                    _scale = Math.Clamp(1.0f / reliefScaleTiles, 0.005f, 0.12f);
                    _needsRegen = true;
                }
                rowY += rowStep;

                // Hauteur Max
                Raylib.DrawTextEx(font, "Hauteur maximale", new Vector2(rowX, rowY), 13f, 1.0f, Color.LightGray);
                Raylib.DrawTextEx(font, $"{_maxHeight:F0} blocs de hauteur", new Vector2(rowX, rowY + 16), 11f, 1.0f, new Color((byte)155, (byte)165, (byte)185, (byte)255));
                if (DrawFloatInput("max-height", new Rectangle(inputX, rowY + 31, inputWidth, 30), font, ref _maxHeight, 5f, 40f, 0))
                {
                    _needsRegen = true;
                }
                bool heightMinus, heightPlus;
                DrawButton("-", new Rectangle(minusX, rowY + 31, 36, 30), btnNormal, btnHover, font, out heightMinus);
                DrawButton("+", new Rectangle(plusX, rowY + 31, 36, 30), btnNormal, btnHover, font, out heightPlus);
                if (heightMinus)
                {
                    _maxHeight = Math.Max(5f, _maxHeight - 1f);
                    _needsRegen = true;
                }
                if (heightPlus)
                {
                    _maxHeight = Math.Min(40f, _maxHeight + 1f);
                    _needsRegen = true;
                }
                rowY += rowStep;

                // Variations Relief (Persistance)
                float roughnessPercent = _persistence * 100.0f;
                Raylib.DrawTextEx(font, "Rugosité", new Vector2(rowX, rowY), 13f, 1.0f, Color.LightGray);
                Raylib.DrawTextEx(font, $"{roughnessPercent:F0}% de détails secondaires", new Vector2(rowX, rowY + 16), 11f, 1.0f, new Color((byte)155, (byte)165, (byte)185, (byte)255));
                if (DrawFloatInput("roughness", new Rectangle(inputX, rowY + 31, inputWidth, 30), font, ref roughnessPercent, 10f, 80f, 0))
                {
                    _persistence = Math.Clamp(roughnessPercent / 100.0f, 0.10f, 0.80f);
                    _needsRegen = true;
                }
                bool persistenceMinus, persistencePlus;
                DrawButton("-", new Rectangle(minusX, rowY + 31, 36, 30), btnNormal, btnHover, font, out persistenceMinus);
                DrawButton("+", new Rectangle(plusX, rowY + 31, 36, 30), btnNormal, btnHover, font, out persistencePlus);
                if (persistenceMinus)
                {
                    _persistence = Math.Max(0.10f, _persistence - 0.05f);
                    _needsRegen = true;
                }
                if (persistencePlus)
                {
                    _persistence = Math.Min(0.80f, _persistence + 0.05f);
                    _needsRegen = true;
                }
            }
            else if (_activeTab == 1)
            {
                // Onglet 1: Climat & Surface
                // Niveau d'eau
                float waterPercent = _waterLevel * 100.0f;
                Raylib.DrawTextEx(font, "Niveau d'eau", new Vector2(rowX, rowY), 13f, 1.0f, Color.LightGray);
                Raylib.DrawTextEx(font, $"{waterPercent:F0}% de la hauteur de terrain", new Vector2(rowX, rowY + 16), 11f, 1.0f, new Color((byte)155, (byte)165, (byte)185, (byte)255));
                if (DrawFloatInput("water-level", new Rectangle(inputX, rowY + 31, inputWidth, 30), font, ref waterPercent, 5f, 50f, 0))
                {
                    _waterLevel = Math.Clamp(waterPercent / 100.0f, 0.05f, 0.50f);
                    _needsRegen = true;
                }
                bool waterMinus, waterPlus;
                DrawButton("-", new Rectangle(minusX, rowY + 31, 36, 30), btnNormal, btnHover, font, out waterMinus);
                DrawButton("+", new Rectangle(plusX, rowY + 31, 36, 30), btnNormal, btnHover, font, out waterPlus);
                if (waterMinus)
                {
                    _waterLevel = Math.Max(0.05f, _waterLevel - 0.02f);
                    _needsRegen = true;
                }
                if (waterPlus)
                {
                    _waterLevel = Math.Min(0.5f, _waterLevel + 0.02f);
                    _needsRegen = true;
                }
                rowY += rowStep;

                // Humidité
                float moistureValue = _moistureShift * 100.0f;
                float humidityEquivalent = Math.Clamp(50.0f + moistureValue, 0.0f, 100.0f);
                Raylib.DrawTextEx(font, "Humidité", new Vector2(rowX, rowY), 13f, 1.0f, Color.LightGray);
                Raylib.DrawTextEx(font, $"{moistureValue:+0;-0;0} ({humidityEquivalent:F0}% d'humidité)", new Vector2(rowX, rowY + 16), 11f, 1.0f, new Color((byte)155, (byte)165, (byte)185, (byte)255));
                if (DrawFloatInput("moisture", new Rectangle(inputX, rowY + 31, inputWidth, 30), font, ref moistureValue, -50f, 50f, 0))
                {
                    _moistureShift = Math.Clamp(moistureValue / 100.0f, -0.50f, 0.50f);
                    _needsRegen = true;
                }
                bool moistMinus, moistPlus;
                DrawButton("-", new Rectangle(minusX, rowY + 31, 36, 30), btnNormal, btnHover, font, out moistMinus);
                DrawButton("+", new Rectangle(plusX, rowY + 31, 36, 30), btnNormal, btnHover, font, out moistPlus);
                if (moistMinus)
                {
                    _moistureShift = Math.Max(-0.5f, _moistureShift - 0.05f);
                    _needsRegen = true;
                }
                if (moistPlus)
                {
                    _moistureShift = Math.Min(0.5f, _moistureShift + 0.05f);
                    _needsRegen = true;
                }
                rowY += rowStep;

                // Température Celsius
                string tempText = _temperatureCelsius > 0 ? $"+{_temperatureCelsius}°C" : $"{_temperatureCelsius}°C";
                Raylib.DrawTextEx(font, "Température", new Vector2(rowX, rowY), 13f, 1.0f, Color.LightGray);
                Raylib.DrawTextEx(font, $"{tempText} (70°C = végétation impossible)", new Vector2(rowX, rowY + 16), 11f, 1.0f, new Color((byte)155, (byte)165, (byte)185, (byte)255));
                if (DrawIntInput("temperature", new Rectangle(inputX, rowY + 31, inputWidth, 30), font, ref _temperatureCelsius, -50, 70))
                {
                    _needsRegen = true;
                }
                bool tempMinus, tempPlus;
                DrawButton("-", new Rectangle(minusX, rowY + 31, 36, 30), btnNormal, btnHover, font, out tempMinus);
                DrawButton("+", new Rectangle(plusX, rowY + 31, 36, 30), btnNormal, btnHover, font, out tempPlus);
                if (tempMinus)
                {
                    _temperatureCelsius = Math.Max(-50, _temperatureCelsius - 10);
                    _needsRegen = true;
                }
                if (tempPlus)
                {
                    _temperatureCelsius = Math.Min(70, _temperatureCelsius + 10);
                    _needsRegen = true;
                }
                rowY += rowStep;

                // Surface de l'île
                float islandPercent = _islandSize * 100.0f;
                Raylib.DrawTextEx(font, "Surface de l'île", new Vector2(rowX, rowY), 13f, 1.0f, Color.LightGray);
                Raylib.DrawTextEx(font, $"{islandPercent:F0}% (facteur interne {_islandSize:F2})", new Vector2(rowX, rowY + 16), 11f, 1.0f, new Color((byte)155, (byte)165, (byte)185, (byte)255));
                if (DrawFloatInput("island-size", new Rectangle(inputX, rowY + 31, inputWidth, 30), font, ref islandPercent, 20f, 400f, 0))
                {
                    _islandSize = Math.Clamp(islandPercent / 100.0f, 0.20f, 4.00f);
                    _needsRegen = true;
                }
                bool sizeMinus, sizePlus;
                DrawButton("-", new Rectangle(minusX, rowY + 31, 36, 30), btnNormal, btnHover, font, out sizeMinus);
                DrawButton("+", new Rectangle(plusX, rowY + 31, 36, 30), btnNormal, btnHover, font, out sizePlus);
                if (sizeMinus)
                {
                    _islandSize = Math.Max(0.2f, _islandSize - 0.05f);
                    _needsRegen = true;
                }
                if (sizePlus)
                {
                    _islandSize = Math.Min(4.0f, _islandSize + 0.05f);
                    _needsRegen = true;
                }
            }
            else if (_activeTab == 2)
            {
                // Onglet 2: Paramètres Monde & Système
                // Taille du monde
                Raylib.DrawTextEx(font, "Taille du monde", new Vector2(rowX, rowY), 13f, 1.0f, Color.LightGray);
                Raylib.DrawTextEx(font, $"{_mapWidth} x {_mapLength} tuiles", new Vector2(rowX, rowY + 16), 11f, 1.0f, new Color((byte)155, (byte)165, (byte)185, (byte)255));
                bool click64, click128, click256;
                DrawButton("64", new Rectangle(inputX, rowY + 31, 58, 30), _mapWidth == 64 ? btnActive : btnNormal, btnHover, font, out click64);
                DrawButton("128", new Rectangle(inputX + 66, rowY + 31, 58, 30), _mapWidth == 128 ? btnActive : btnNormal, btnHover, font, out click128);
                DrawButton("256", new Rectangle(inputX + 132, rowY + 31, 58, 30), _mapWidth == 256 ? btnActive : btnNormal, btnHover, font, out click256);
                
                if (click64 && _mapWidth != 64)
                {
                    _mapWidth = 64; _mapLength = 64;
                    ResetCamera();
                    _needsRegen = true;
                }
                if (click128 && _mapWidth != 128)
                {
                    _mapWidth = 128; _mapLength = 128;
                    ResetCamera();
                    _needsRegen = true;
                }
                if (click256 && _mapWidth != 256)
                {
                    _mapWidth = 256; _mapLength = 256;
                    ResetCamera();
                    _needsRegen = true;
                }
                rowY += rowStep;

                // Habitants
                Raylib.DrawTextEx(font, "Habitants", new Vector2(rowX, rowY), 13f, 1.0f, Color.LightGray);
                Raylib.DrawTextEx(font, $"{_humanCount} personnages (humanCount {_humanCount})", new Vector2(rowX, rowY + 16), 11f, 1.0f, new Color((byte)155, (byte)165, (byte)185, (byte)255));
                if (DrawIntInput("humans", new Rectangle(inputX, rowY + 31, inputWidth, 30), font, ref _humanCount, 0, 200))
                {
                    SyncHumanCount();
                }
                bool humansMinus, humansPlus;
                DrawButton("-", new Rectangle(minusX, rowY + 31, 36, 30), btnNormal, btnHover, font, out humansMinus);
                DrawButton("+", new Rectangle(plusX, rowY + 31, 36, 30), btnNormal, btnHover, font, out humansPlus);
                if (humansMinus)
                {
                    _humanCount = Math.Max(0, _humanCount - 1);
                    SyncHumanCount();
                }
                if (humansPlus)
                {
                    _humanCount = Math.Min(200, _humanCount + 1);
                    SyncHumanCount();
                }
                rowY += rowStep;

                // Caméra
                Raylib.DrawTextEx(font, "Caméra", new Vector2(rowX, rowY), 13f, 1.0f, Color.LightGray);
                Raylib.DrawTextEx(font, "Recentrer la vue orbitale", new Vector2(rowX, rowY + 16), 11f, 1.0f, new Color((byte)155, (byte)165, (byte)185, (byte)255));
                bool clickResetCam;
                DrawButton("Réinitialiser [R]", new Rectangle(inputX, rowY + 31, 190, 30), new Color((byte)45, (byte)55, (byte)75, (byte)255), new Color((byte)80, (byte)95, (byte)125, (byte)255), font, out clickResetCam);
                if (clickResetCam)
                {
                    ResetCamera();
                }
                rowY += rowStep;

                // Masquage interface
                Raylib.DrawTextEx(font, "Affichage", new Vector2(rowX, rowY), 13f, 1.0f, Color.LightGray);
                Raylib.DrawTextEx(font, "Masquer le panneau de contrôle", new Vector2(rowX, rowY + 16), 11f, 1.0f, new Color((byte)155, (byte)165, (byte)185, (byte)255));
                bool clickHide2;
                DrawButton("Masquer [H]", new Rectangle(inputX, rowY + 31, 190, 30), btnNormal, new Color((byte)180, (byte)50, (byte)50, (byte)255), font, out clickHide2);
                if (clickHide2)
                {
                    _showMenu = false;
                }
                rowY += rowStep;

                // Affichage FPS (adapté au bord droit)
                Raylib.DrawTextEx(font, "Performances", new Vector2(rowX, rowY), 13f, 1.0f, Color.LightGray);
                Raylib.DrawTextEx(font, $"FPS: {Raylib.GetFPS()}", new Vector2(rowX, rowY + 22), 14f, 1.0f, Color.Lime);
            }
            else if (_activeTab == 3)
            {
                // Onglet 3: Raccourcis & Aide (Texte d'aide très grand et lisible)
                Raylib.DrawTextEx(font, "Souris", new Vector2(rowX, rowY), 13f, 1.0f, Color.LightGray);
                Raylib.DrawTextEx(font, "Clic gauche/droit glissé : rotation", new Vector2(rowX, rowY + 20), 12f, 1.0f, Color.LightGray);
                Raylib.DrawTextEx(font, "Molette : zoom", new Vector2(rowX, rowY + 39), 12f, 1.0f, Color.LightGray);
                rowY += 78f;

                Raylib.DrawTextEx(font, "Clavier", new Vector2(rowX, rowY), 13f, 1.0f, Color.LightGray);
                Raylib.DrawTextEx(font, "ZQSD / WASD / flèches : déplacer", new Vector2(rowX, rowY + 20), 12f, 1.0f, Color.LightGray);
                Raylib.DrawTextEx(font, "R : réinitialiser la caméra", new Vector2(rowX, rowY + 39), 12f, 1.0f, Color.LightGray);
                Raylib.DrawTextEx(font, "H ou Tab : masquer le menu", new Vector2(rowX, rowY + 58), 12f, 1.0f, Color.LightGray);
                Raylib.DrawTextEx(font, "Espace : nouvelle graine", new Vector2(rowX, rowY + 77), 12f, 1.0f, Color.LightGray);
            }

            // ==========================================
            // PANNEAU LATÉRAL DROIT (Biomes - 9 Biomes) (Adaptatif)
            // ==========================================
            Rectangle rightPanel = new Rectangle(currentScreenWidth - 240, 15, 225, 230);
            Raylib.DrawRectangleRec(rightPanel, new Color((byte)15, (byte)18, (byte)26, (byte)235));
            Raylib.DrawRectangleLinesEx(rightPanel, 1.0f, new Color((byte)0, (byte)150, (byte)255, (byte)180));

            Raylib.DrawTextEx(font, "RÉPARTITION BIOMES", new Vector2(currentScreenWidth - 220, 25), 14f, 1.0f, new Color((byte)0, (byte)180, (byte)255, (byte)255));
            Raylib.DrawLine(currentScreenWidth - 220, 43, currentScreenWidth - 30, 43, new Color((byte)255, (byte)255, (byte)255, (byte)30));

            int yOffset = 53;
            int totalBlocks = _mapWidth * _mapLength;

            for (int i = 0; i < biomeCounts.Length; i++)
            {
                BiomeType biome = (BiomeType)i;
                int count = biomeCounts[i];
                float percent = (float)count / totalBlocks * 100.0f;

                Color barColor = biome switch
                {
                    BiomeType.DeepOcean => new Color((byte)20, (byte)40, (byte)110, (byte)255),
                    BiomeType.ShallowWater => new Color((byte)35, (byte)95, (byte)185, (byte)255),
                    BiomeType.Beach => new Color((byte)225, (byte)200, (byte)130, (byte)255),
                    BiomeType.Desert => new Color((byte)210, (byte)175, (byte)90, (byte)255),
                    BiomeType.Plains => new Color((byte)85, (byte)170, (byte)70, (byte)255),
                    BiomeType.Forest => new Color((byte)34, (byte)110, (byte)42, (byte)255),
                    BiomeType.Taiga => new Color((byte)28, (byte)80, (byte)52, (byte)255),
                    BiomeType.Mountain => new Color((byte)110, (byte)115, (byte)120, (byte)255),
                    BiomeType.Snow => new Color((byte)245, (byte)248, (byte)250, (byte)255),
                    _ => Color.White
                };

                // Label biome
                string biomeName = biome.ToString();
                Raylib.DrawTextEx(font, $"{biomeName.PadRight(12)}: {percent:F1}%", new Vector2(currentScreenWidth - 220, yOffset), 11f, 1.0f, Color.LightGray);

                // Barre de distribution
                Raylib.DrawRectangle(currentScreenWidth - 95, yOffset + 2, 75, 8, new Color((byte)35, (byte)40, (byte)50, (byte)255));
                Raylib.DrawRectangle(currentScreenWidth - 95, yOffset + 2, (int)(0.75f * percent), 8, barColor);

                yOffset += 18;
            }
        }

        // ---- METHODES DE GENERATION DU MAILLAGE LISSE 3D ----

        private static unsafe Model GenerateSmoothTerrainMesh(WorldBlock[,] grid, int width, int length)
        {
            Mesh mesh = new Mesh();
            mesh.VertexCount = width * length;
            mesh.TriangleCount = (width - 1) * (length - 1) * 2;

            // Allocation mémoire native
            int verticesSize = mesh.VertexCount * 3 * sizeof(float);
            mesh.Vertices = (float*)Marshal.AllocHGlobal(verticesSize);

            int colorsSize = mesh.VertexCount * 4 * sizeof(byte);
            mesh.Colors = (byte*)Marshal.AllocHGlobal(colorsSize);

            int normalsSize = mesh.VertexCount * 3 * sizeof(float);
            mesh.Normals = (float*)Marshal.AllocHGlobal(normalsSize);

            int indicesSize = mesh.TriangleCount * 3 * sizeof(ushort);
            mesh.Indices = (ushort*)Marshal.AllocHGlobal(indicesSize);

            // Remplissage des sommets (vertices) et des couleurs
            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < length; z++)
                {
                    int index = x + z * width;
                    var block = grid[x, z];

                    // Position (X, Y=hauteur lissée, Z)
                    mesh.Vertices[index * 3 + 0] = x;
                    mesh.Vertices[index * 3 + 1] = block.Height;
                    mesh.Vertices[index * 3 + 2] = z;

                    // Couleur du sommet (la couleur de son biome)
                    mesh.Colors[index * 4 + 0] = block.Color.R;
                    mesh.Colors[index * 4 + 1] = block.Color.G;
                    mesh.Colors[index * 4 + 2] = block.Color.B;
                    mesh.Colors[index * 4 + 3] = block.Color.A;

                    // Normales (par défaut, recalculées ensuite)
                    mesh.Normals[index * 3 + 0] = 0f;
                    mesh.Normals[index * 3 + 1] = 1f;
                    mesh.Normals[index * 3 + 2] = 0f;
                }
            }

            // Remplissage des indices pour former les triangles
            int triIndex = 0;
            for (int x = 0; x < width - 1; x++)
            {
                for (int z = 0; z < length - 1; z++)
                {
                    ushort topLeft = (ushort)(x + z * width);
                    ushort topRight = (ushort)((x + 1) + z * width);
                    ushort bottomLeft = (ushort)(x + (z + 1) * width);
                    ushort bottomRight = (ushort)((x + 1) + (z + 1) * width);

                    // Triangle 1
                    mesh.Indices[triIndex++] = topLeft;
                    mesh.Indices[triIndex++] = bottomLeft;
                    mesh.Indices[triIndex++] = topRight;

                    // Triangle 2
                    mesh.Indices[triIndex++] = topRight;
                    mesh.Indices[triIndex++] = bottomLeft;
                    mesh.Indices[triIndex++] = bottomRight;
                }
            }

            // Calcul des normales lissées pour l'éclairage de Raylib
            ComputeMeshNormals(mesh, width, length);

            // Charger le maillage en mémoire GPU
            Raylib.UploadMesh(ref mesh, false);

            // Convertir le maillage en Modèle 3D utilisable par Raylib
            return Raylib.LoadModelFromMesh(mesh);
        }

        private static unsafe void ComputeMeshNormals(Mesh mesh, int width, int length)
        {
            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < length; z++)
                {
                    int index = x + z * width;

                    // Calcul de la normale en fonction des voisins directs (hauteur)
                    float hL = GetHeightAt(mesh, x - 1, z, width, length);
                    float hR = GetHeightAt(mesh, x + 1, z, width, length);
                    float hD = GetHeightAt(mesh, x, z - 1, width, length);
                    float hU = GetHeightAt(mesh, x, z + 1, width, length);

                    // Formule de normale lissée
                    Vector3 normal = Vector3.Normalize(new Vector3(hL - hR, 2.0f, hD - hU));

                    mesh.Normals[index * 3 + 0] = normal.X;
                    mesh.Normals[index * 3 + 1] = normal.Y;
                    mesh.Normals[index * 3 + 2] = normal.Z;
                }
            }
        }

        private static unsafe float GetHeightAt(Mesh mesh, int x, int z, int width, int length)
        {
            if (x < 0) x = 0;
            if (x >= width) x = width - 1;
            if (z < 0) z = 0;
            if (z >= length) z = length - 1;
            return mesh.Vertices[(x + z * width) * 3 + 1];
        }
    }
}
