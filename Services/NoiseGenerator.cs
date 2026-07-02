using System;

namespace IslandWorldGenerator.Services
{
    public class NoiseGenerator
    {
        private readonly int[] _p = new int[512];
        private readonly Random _random;

        /*
         * Constructeur de NoiseGenerator. Initialise la table de permutation
         * avec une graine pseudo-aleatoire donnee.
         */
        public NoiseGenerator(int seed)
        {
            _random = new Random(seed);
            InitializePermutationTable();
        }

        /*
         * Genere et melange la table de permutation en utilisant le melange
         * de Fisher-Yates, et la duplique pour eviter les debordements d'index.
         */
        private void InitializePermutationTable()
        {
            int[] permutation = new int[256];
            for (int i = 0; i < 256; i++)
            {
                permutation[i] = i;
            }

            // Mélange de Fisher-Yates pour la graine aléatoire
            for (int i = 255; i > 0; i--)
            {
                int j = _random.Next(i + 1);
                int temp = permutation[i];
                permutation[i] = permutation[j];
                permutation[j] = temp;
            }

            // Duplication de la table pour éviter les débordements d'index
            for (int i = 0; i < 256; i++)
            {
                _p[i] = permutation[i];
                _p[256 + i] = permutation[i];
            }
        }

        /*
         * Calcule le bruit de Perlin 2D classique pour des coordonnees donnees.
         */
        public double Noise(double x, double y)
        {
            int X = (int)Math.Floor(x) & 255;
            int Y = (int)Math.Floor(y) & 255;

            x -= Math.Floor(x);
            y -= Math.Floor(y);

            double u = Fade(x);
            double v = Fade(y);

            int a = _p[X] + Y;
            int b = _p[X + 1] + Y;

            return Lerp(v, Lerp(u, Grad(_p[a], x, y),
                                   Grad(_p[b], x - 1, y)),
                           Lerp(u, Grad(_p[a + 1], x, y - 1),
                                   Grad(_p[b + 1], x - 1, y - 1)));
        }

        /*
         * Calcule le mouvement brownien fractionnaire (FBM) en cumulant plusieurs
         * octaves de bruit de Perlin et renvoie une valeur normalisee entre 0 et 1.
         */
        public double Fbm(double x, double y, int octaves, double persistence, double lacunarity)
        {
            double total = 0;
            double frequency = 1;
            double amplitude = 1;
            double maxValue = 0;

            for (int i = 0; i < octaves; i++)
            {
                total += Noise(x * frequency, y * frequency) * amplitude;
                maxValue += amplitude;
                amplitude *= persistence;
                frequency *= lacunarity;
            }

            // Normalisation de [-1, 1] vers [0, 1]
            return (total / maxValue + 1.0) / 2.0;
        }

        /*
         * Calcule la courbe d'interpolation douce de Ken Perlin (fade function).
         */
        private static double Fade(double t)
        {
            return t * t * t * (t * (t * 6 - 15) + 10);
        }

        /*
         * Effectue une interpolation lineaire simple entre deux valeurs.
         */
        private static double Lerp(double t, double a, double b)
        {
            return a + t * (b - a);
        }

        /*
         * Calcule le produit scalaire entre un vecteur de gradient pseudo-aleatoire
         * et le vecteur de distance.
         */
        private static double Grad(int hash, double x, double y)
        {
            switch (hash & 7)
            {
                case 0: return x + y;
                case 1: return -x + y;
                case 2: return x - y;
                case 3: return -x - y;
                case 4: return x;
                case 5: return -x;
                case 6: return y;
                case 7: return -y;
                default: return 0;
            }
        }
    }
}
