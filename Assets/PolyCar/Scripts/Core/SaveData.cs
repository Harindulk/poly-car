using System;
using System.Collections.Generic;
using UnityEngine;

namespace PolyCar
{
    [Serializable]
    public class TrackRecord
    {
        public string mapId;
        public int laps;
        public float bestTime;     // whole race, seconds
        public float bestLap;      // seconds (circuits only)
        public int medal;          // Medal enum value
        public float[] splits = Array.Empty<float>(); // checkpoint times of the best run
        public string carId;
    }

    /// <summary>Player progress, stored as JSON in PlayerPrefs. Ghosts live in files (see GhostStore).</summary>
    [Serializable]
    public class SaveData
    {
        private const string Key = "polycar.save.v1";

        public string selectedCar = "muscle";
        public string selectedMap = "green_forest";
        public List<TrackRecord> records = new List<TrackRecord>();
        public long totalDriftScore;
        public int racesFinished;
        public int racesWon;
        public float totalDistanceKm;
        public int cupsPlayed;
        public int cupsWon;

        private static SaveData _current;
        public static SaveData Current => _current ??= Load();

        public static SaveData Load()
        {
            try
            {
                string json = PlayerPrefs.GetString(Key, "");
                if (!string.IsNullOrEmpty(json))
                {
                    var data = JsonUtility.FromJson<SaveData>(json);
                    if (data != null)
                    {
                        data.records ??= new List<TrackRecord>();
                        return data;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("Poly Car: save data unreadable, starting fresh. " + e.Message);
            }
            return new SaveData();
        }

        public void Save()
        {
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(this));
            PlayerPrefs.Save();
        }

        public TrackRecord Get(string mapId, int laps)
        {
            foreach (var r in records) if (r.mapId == mapId && r.laps == laps) return r;
            return null;
        }

        /// <summary>Best medal earned on a map at any lap count.</summary>
        public Medal BestMedal(string mapId)
        {
            int best = 0;
            foreach (var r in records) if (r.mapId == mapId) best = Mathf.Max(best, r.medal);
            return (Medal)best;
        }

        /// <summary>Stars = sum of best medal values per map (bronze 1, silver 2, gold 3). Unlocks cars.</summary>
        public int Stars
        {
            get
            {
                var seen = new HashSet<string>();
                int stars = 0;
                foreach (var r in records)
                {
                    if (!seen.Add(r.mapId)) continue;
                    stars += (int)BestMedal(r.mapId);
                }
                return stars;
            }
        }

        public bool IsUnlocked(CarSpec car) => car != null && Stars >= car.unlockMedals;

        /// <summary>Stores a finished run; returns true if it beat the previous best time.</summary>
        public bool Submit(string mapId, int laps, float time, float bestLap, Medal medal, float[] splits, string carId)
        {
            var r = Get(mapId, laps);
            bool isNew = r == null || r.bestTime <= 0f || time < r.bestTime;
            if (r == null)
            {
                r = new TrackRecord { mapId = mapId, laps = laps };
                records.Add(r);
            }
            if (isNew)
            {
                r.bestTime = time;
                r.splits = splits ?? Array.Empty<float>();
                r.carId = carId;
            }
            if (bestLap > 0f && (r.bestLap <= 0f || bestLap < r.bestLap)) r.bestLap = bestLap;
            r.medal = Mathf.Max(r.medal, (int)medal);
            return isNew;
        }

        public static void ResetAll()
        {
            PlayerPrefs.DeleteKey(Key);
            _current = new SaveData();
            GhostStore.DeleteAll();
        }
    }
}
