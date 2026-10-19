using System;
using UnityEngine;

namespace PolyCar
{
    /// <summary>
    /// A rival who never drives on the road. Like art of rally's AI drivers, a rival's stage time is a baseline
    /// (a near-perfect run, just under the gold time) x (1 + a deviation drawn from their skill band), plus the
    /// odd mistake. You race them against the clock, with live position and gaps on the HUD.
    /// </summary>
    [Serializable]
    public class VirtualRival
    {
        public string name;
        public string carId;
        [Tooltip("0 = slowest of the difficulty band, 1 = fastest.")]
        public float talent;
        public float time;
        public string note = "";
    }

    public static class RivalTimes
    {
        public static readonly string[] Names = { "Kenji", "Maya", "Rico", "Lena", "Viktor", "Aiko", "Sol", "Nova" };
        private static readonly string[] Mistakes = { "SPIN", "OFF ROAD", "STALLED", "OVERSHOT A HAIRPIN", "PUNCTURE" };

        public static float LapScale(MapSpec map, int laps) => map != null && map.IsCircuit && map.laps > 0 ? Mathf.Max(1, laps) / (float)map.laps : 1f;

        /// <summary>Time of a near-perfect run for this map and lap count.</summary>
        public static float Baseline(MapSpec map, int laps) => map == null ? 120f : map.goldTime * LapScale(map, laps) * 0.94f;

        /// <summary>Deviation over the baseline for a difficulty; less talent widens and slows the band.</summary>
        public static void Band(Difficulty diff, float talent, out float min, out float max)
        {
            float slow = 1f - Mathf.Clamp01(talent);
            switch (diff)
            {
                case Difficulty.Easy: min = 0.10f * (1f + slow); max = min + 0.30f * (1f + slow); break;
                case Difficulty.Hard: min = 0.02f * slow; max = min + 0.10f * (1f + slow); break;
                default: min = 0.04f * (1f + slow); max = min + 0.16f * (1f + slow); break;
            }
        }

        public static void SetStageTime(VirtualRival r, float baseline, Difficulty diff, System.Random rng)
        {
            Band(diff, r.talent, out float min, out float max);
            float dev = Mathf.Lerp(min, max, (float)rng.NextDouble());
            r.note = "";
            float mistakeChance = diff == Difficulty.Easy ? 0.12f : diff == Difficulty.Hard ? 0.05f : 0.08f;
            if (rng.NextDouble() < mistakeChance)
            {
                dev += Mathf.Lerp(0.03f, 0.12f, (float)rng.NextDouble());
                r.note = Mistakes[rng.Next(Mistakes.Length)];
            }
            r.time = baseline * (1f + dev);
        }

        /// <summary>Names, cars (any but the player's) and talents for a field of rivals.</summary>
        public static VirtualRival[] Field(int count, string playerCar, System.Random rng)
        {
            var cars = CarCatalog.Instance ? CarCatalog.Instance.cars : new CarSpec[0];
            var pool = new System.Collections.Generic.List<string>();
            foreach (var c in cars) if (c.id != playerCar) pool.Add(c.id);
            if (pool.Count == 0) pool.Add(playerCar);
            var names = (string[])Names.Clone();
            for (int i = names.Length - 1; i > 0; i--) { int j = rng.Next(i + 1); (names[i], names[j]) = (names[j], names[i]); }
            for (int i = pool.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (pool[i], pool[j]) = (pool[j], pool[i]); }
            count = Mathf.Clamp(count, 0, names.Length);
            var field = new VirtualRival[count];
            for (int i = 0; i < count; i++)
                field[i] = new VirtualRival { name = names[i], carId = pool[i % pool.Count], talent = (float)rng.NextDouble() };
            return field;
        }
    }
}
