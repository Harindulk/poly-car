using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PolyCar.Tests
{
    public class CoreLogicTests
    {
        private static MapSpec Circuit() => new MapSpec { id = "t", laps = 3, goldTime = 90f, silverTime = 100f, bronzeTime = 120f };

        [Test]
        public void MedalsFollowParTimes()
        {
            var m = Circuit();
            Assert.AreEqual(Medal.Gold, Medals.For(m, 89f, 3));
            Assert.AreEqual(Medal.Silver, Medals.For(m, 95f, 3));
            Assert.AreEqual(Medal.Bronze, Medals.For(m, 119f, 3));
            Assert.AreEqual(Medal.None, Medals.For(m, 130f, 3));
            Assert.AreEqual(Medal.None, Medals.For(m, 0f, 3));
        }

        [Test]
        public void MedalTimesScaleWithLapCount()
        {
            var m = Circuit();
            Assert.AreEqual(Medal.Gold, Medals.For(m, 59f, 2));   // 2/3 of 90 = 60
            Assert.AreEqual(Medal.Silver, Medals.For(m, 61f, 2));
            Assert.AreEqual(Medal.Gold, Medals.For(m, 175f, 6));  // double
        }

        [Test]
        public void SubmitKeepsBestTimeAndBestMedal()
        {
            var save = new SaveData();
            Assert.IsTrue(save.Submit("a", 3, 100f, 30f, Medal.Silver, new[] { 10f, 20f }, "muscle"));
            Assert.IsFalse(save.Submit("a", 3, 110f, 28f, Medal.Bronze, new[] { 11f }, "apex"));
            var r = save.Get("a", 3);
            Assert.AreEqual(100f, r.bestTime);
            Assert.AreEqual(28f, r.bestLap, "best lap improves even when the race time doesn't");
            Assert.AreEqual((int)Medal.Silver, r.medal);
            Assert.AreEqual("muscle", r.carId);
            Assert.AreEqual(2, r.splits.Length);
            Assert.IsTrue(save.Submit("a", 3, 90f, 0f, Medal.Gold, null, "zenith"));
            Assert.AreEqual(3, save.Stars);
        }

        [Test]
        public void StarsCountBestMedalPerMapOnce()
        {
            var save = new SaveData();
            save.Submit("a", 3, 100f, 0f, Medal.Bronze, null, "x");
            save.Submit("a", 5, 160f, 0f, Medal.Gold, null, "x");
            save.Submit("b", 0, 80f, 0f, Medal.Silver, null, "x");
            Assert.AreEqual(5, save.Stars); // map a gold (3) + map b silver (2)
            Assert.IsTrue(save.IsUnlocked(new CarSpec { unlockMedals = 5 }));
            Assert.IsFalse(save.IsUnlocked(new CarSpec { unlockMedals = 6 }));
        }

        [Test]
        public void SaveDataRoundTripsThroughJson()
        {
            var save = new SaveData { selectedCar = "apex", totalDriftScore = 1234 };
            save.Submit("a", 3, 100f, 30f, Medal.Gold, new[] { 1f }, "apex");
            var back = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(save));
            Assert.AreEqual("apex", back.selectedCar);
            Assert.AreEqual(1234, back.totalDriftScore);
            Assert.AreEqual(100f, back.Get("a", 3).bestTime);
        }

        private static TrackPath Square(bool loop)
        {
            var go = new GameObject("path");
            var p = go.AddComponent<TrackPath>();
            p.points = new List<Vector3> { new Vector3(0, 0, 0), new Vector3(100, 0, 0), new Vector3(100, 0, 100), new Vector3(0, 0, 100) };
            p.loop = loop;
            p.halfWidth = 5f;
            p.Build();
            return p;
        }

        [Test]
        public void TrackPathLengthAndPoints()
        {
            var p = Square(true);
            Assert.AreEqual(400f, p.Length, 0.01f);
            Assert.That(Vector3.Distance(p.PointAt(150f), new Vector3(100, 0, 50)), Is.LessThan(0.01f));
            Assert.That(Vector3.Distance(p.PointAt(450f), new Vector3(50, 0, 0)), Is.LessThan(0.01f), "wraps on loops");
            var open = Square(false);
            Assert.AreEqual(300f, open.Length, 0.01f);
            Assert.That(Vector3.Distance(open.PointAt(999f), new Vector3(0, 0, 100)), Is.LessThan(0.01f), "clamps on sprints");
            Object.DestroyImmediate(p.gameObject); Object.DestroyImmediate(open.gameObject);
        }

        [Test]
        public void ProjectGivesDistanceAndSignedLateral()
        {
            var p = Square(true);
            float d = p.Project(new Vector3(40, 0, -3), -1f, 0f, out float lat);
            Assert.AreEqual(40f, d, 0.01f);
            Assert.AreEqual(3f, lat, 0.01f, "right of travel (+x heading, -z side) is positive");
            d = p.Project(new Vector3(40, 0, 3), 30f, 60f, out lat);
            Assert.AreEqual(40f, d, 0.01f);
            Assert.AreEqual(-3f, lat, 0.01f);
            Object.DestroyImmediate(p.gameObject);
        }

        [Test]
        public void SpeedProfileBrakesBeforeCorners()
        {
            var p = Square(true);
            float corner = p.SpeedLimitAt(100f, 14f, 12f);
            float before = p.SpeedLimitAt(60f, 14f, 12f);
            float mid = p.SpeedLimitAt(50f, 14f, 12f);
            Assert.Less(corner, before);
            Assert.LessOrEqual(before, mid + 0.01f);
            Assert.LessOrEqual(before, Mathf.Sqrt(corner * corner + 2f * 12f * 44f) + 0.5f, "reachable by braking");
            Object.DestroyImmediate(p.gameObject);
        }

        [Test]
        public void GhostSerializesAndInterpolates()
        {
            var g = new GhostData { carId = "muscle" };
            g.positions.Add(Vector3.zero); g.rotations.Add(Quaternion.identity);
            g.positions.Add(new Vector3(10, 0, 0)); g.rotations.Add(Quaternion.Euler(0, 90, 0));
            var back = GhostData.FromBytes(g.ToBytes());
            Assert.AreEqual("muscle", back.carId);
            Assert.AreEqual(2, back.positions.Count);
            back.Sample(GhostData.Interval * 0.5f, out var pos, out var rot);
            Assert.AreEqual(5f, pos.x, 0.01f);
            Assert.AreEqual(45f, rot.eulerAngles.y, 0.5f);
            Assert.IsNull(GhostData.FromBytes(new byte[] { 9, 0, 0, 0 }), "unknown versions are rejected");
        }

        [Test]
        public void TimeFormatting()
        {
            Assert.AreEqual("1:05.25", UiKit.FormatTime(65.25f));
            Assert.AreEqual("--:--.--", UiKit.FormatTime(0f));
            Assert.AreEqual("+1.50", UiKit.FormatTime(1.5f, true));
            Assert.AreEqual("-0.25", UiKit.FormatTime(-0.25f, true));
        }

        [Test]
        public void CatalogsAreConsistent()
        {
            var cars = Resources.Load<CarCatalog>("CarCatalog");
            var maps = Resources.Load<MapCatalog>("MapCatalog");
            Assert.IsNotNull(cars); Assert.IsNotNull(maps);
            Assert.GreaterOrEqual(cars.cars.Length, 7);
            Assert.GreaterOrEqual(maps.maps.Length, 3);
            var ids = new HashSet<string>();
            foreach (var c in cars.cars)
            {
                Assert.IsTrue(ids.Add(c.id), "duplicate car id " + c.id);
                Assert.IsNotNull(c.prefab, c.id + " has no prefab");
                var ctl = c.prefab.GetComponent<PolyCarController>();
                Assert.IsNotNull(ctl, c.id);
                Assert.AreEqual(4, ctl.wheels.Length);
                foreach (var w in ctl.wheels) Assert.IsNotNull(w.pivot, c.id + " wheel");
            }
            Assert.IsTrue(System.Array.Exists(cars.cars, c => c.unlockMedals == 0), "something is unlocked from the start");
            foreach (var m in maps.maps)
            {
                Assert.Less(m.goldTime, m.silverTime, m.id);
                Assert.Less(m.silverTime, m.bronzeTime, m.id);
            }
        }

        [Test]
        public void CupStandingsSortByTotalTime()
        {
            var cup = new CupState { maps = new[] { "a", "b" }, index = 0 };
            cup.totals["You"] = 250f; cup.totals["Maya"] = 245.5f; cup.totals["Rico"] = 260f;
            var st = cup.Standings();
            Assert.AreEqual("Maya", st[0].Key);
            Assert.AreEqual("You", st[1].Key);
            Assert.AreEqual("Rico", st[2].Key);
            Assert.IsFalse(cup.IsLastRace);
            cup.index = 1;
            Assert.IsTrue(cup.IsLastRace);
            Assert.AreEqual("b", cup.CurrentMap);
        }

        [Test]
        public void RivalTimesFollowTheDifficultyBands()
        {
            var map = new MapSpec { id = "m", laps = 2, goldTime = 100f, silverTime = 112f, bronzeTime = 128f };
            float baseline = RivalTimes.Baseline(map, 4);
            Assert.AreEqual(188f, baseline, 0.01f, "baseline scales with laps (2x gold x 0.94)");
            var rng = new System.Random(7);
            float easy = 0f, normal = 0f, hard = 0f;
            for (int i = 0; i < 400; i++)
            {
                var r = new VirtualRival { talent = (float)rng.NextDouble() };
                RivalTimes.SetStageTime(r, baseline, Difficulty.Easy, rng); easy += r.time;
                Assert.GreaterOrEqual(r.time, baseline * 1.1f - 0.01f);
                RivalTimes.SetStageTime(r, baseline, Difficulty.Normal, rng); normal += r.time;
                RivalTimes.SetStageTime(r, baseline, Difficulty.Hard, rng); hard += r.time;
                Assert.GreaterOrEqual(r.time, baseline - 0.01f, "nobody beats a perfect run");
            }
            Assert.Less(hard, normal);
            Assert.Less(normal, easy);
            // Same seed, same stage times (restarting a championship stage doesn't re-roll the rivals).
            var a = new VirtualRival { talent = 0.5f }; var b = new VirtualRival { talent = 0.5f };
            RivalTimes.SetStageTime(a, baseline, Difficulty.Normal, new System.Random(3));
            RivalTimes.SetStageTime(b, baseline, Difficulty.Normal, new System.Random(3));
            Assert.AreEqual(a.time, b.time);
        }
    }
}
