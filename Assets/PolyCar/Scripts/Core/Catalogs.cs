using System;
using UnityEngine;

namespace PolyCar
{
    [Serializable]
    public class CarSpec
    {
        public string id = "car";
        public string displayName = "Car";
        [TextArea] public string blurb = "";
        public GameObject prefab;
        [Tooltip("Total medals needed to unlock (0 = from the start).")]
        public int unlockMedals;

        [Header("Handling")]
        public float topSpeedKmh = 175f;
        public float acceleration = 10f;   // m/s² at low speed
        public float grip = 7f;            // lateral slip decay per second
        public float driftGrip = 1.6f;
        public float turnRate = 95f;       // deg/s at full lock
        public float mass = 1200f;

        // 0..1 ratings for the garage bars, relative to the fastest/quickest car in the game.
        public float SpeedRating => Mathf.InverseLerp(130f, 215f, topSpeedKmh);
        public float AccelRating => Mathf.InverseLerp(6f, 14f, acceleration);
        public float HandlingRating => Mathf.InverseLerp(4f, 10f, grip) * 0.6f + Mathf.InverseLerp(70f, 120f, turnRate) * 0.4f;
        public float DriftRating => Mathf.InverseLerp(2.6f, 1.0f, driftGrip);
    }

    [Serializable]
    public class MapSpec
    {
        public string id = "map";
        public string displayName = "Map";
        public string sceneName = "";
        [TextArea] public string blurb = "";
        public Texture2D thumbnail;
        [Tooltip("0 = point-to-point sprint; otherwise default lap count.")]
        public int laps;
        [Tooltip("Gold / silver / bronze target times in seconds for the default lap count.")]
        public float goldTime = 60f, silverTime = 70f, bronzeTime = 85f;
        public string lengthLabel = "";
        public Color accent = new Color(1f, 0.55f, 0.1f);

        public bool IsCircuit => laps > 0;
    }

    public enum Medal { None = 0, Bronze = 1, Silver = 2, Gold = 3 }

    public static class Medals
    {
        /// <summary>Medal for a finishing time; par times scale with the lap count chosen.</summary>
        public static Medal For(MapSpec map, float seconds, int laps)
        {
            if (map == null || seconds <= 0f) return Medal.None;
            float scale = map.IsCircuit && map.laps > 0 ? Mathf.Max(1, laps) / (float)map.laps : 1f;
            if (seconds <= map.goldTime * scale) return Medal.Gold;
            if (seconds <= map.silverTime * scale) return Medal.Silver;
            if (seconds <= map.bronzeTime * scale) return Medal.Bronze;
            return Medal.None;
        }
    }
}
