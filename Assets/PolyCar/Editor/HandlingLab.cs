using System;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PolyCar.Editor
{
    /// <summary>
    /// Scripted handling tests on a flat test pad, stepped by hand (Physics.Simulate), so the numbers don't depend
    /// on frame rate or Editor focus: launch, top speed, braking, steady cornering, a handbrake drift held with
    /// counter-steer, a keyboard lane change at speed, and a power-on slide.
    /// Poly Car > Debug > Handling Lab writes the report to the console.
    /// </summary>
    public static class HandlingLab
    {
        private const float Dt = 0.02f;
        // Far below any open scene, so nothing there gets in the way.
        private static readonly Vector3 Origin = new Vector3(0f, -3000f, 0f);
        private static readonly MethodInfo Fixed = typeof(PolyCarController).GetMethod("FixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo Wake = typeof(PolyCarController).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);

        [MenuItem("Poly Car/Debug/Handling Lab")]
        public static void RunMenu() => Debug.Log(RunAll());

        public static string RunAll(string only = null)
        {
            var cars = Resources.Load<CarCatalog>("CarCatalog");
            var sb = new StringBuilder();
            var mode = Physics.simulationMode;
            float fixedDt = Time.fixedDeltaTime;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var prevActive = EditorSceneManager.GetActiveScene();
            EditorSceneManager.SetActiveScene(scene);
            try
            {
                Physics.simulationMode = SimulationMode.Script;
                Time.fixedDeltaTime = Dt;
                var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ground.name = "Pad";
                ground.transform.localScale = new Vector3(6000f, 1f, 6000f);
                ground.transform.position = Origin + new Vector3(0f, -0.5f, 0f);
                foreach (var spec in cars.cars)
                {
                    if (only != null && spec.id != only) continue;
                    sb.AppendLine($"== {spec.displayName} ({spec.id}) mass {spec.mass} top {spec.topSpeedKmh} accel {spec.acceleration} grip {spec.grip} drift {spec.driftGrip}");
                    Launch(spec, sb);
                    Braking(spec, sb);
                    Corner(spec, sb, 60f, 0.6f);
                    Corner(spec, sb, 100f, 0.35f);
                    HandbrakeDrift(spec, sb);
                    LaneChange(spec, sb);
                    PowerSlide(spec, sb);
                }
            }
            catch (Exception e) { sb.AppendLine("EXCEPTION " + e); }
            finally
            {
                Physics.simulationMode = mode;
                Time.fixedDeltaTime = fixedDt;
                if (prevActive.IsValid()) EditorSceneManager.SetActiveScene(prevActive);
                EditorSceneManager.CloseScene(scene, true);
            }
            return sb.ToString();
        }

        private static PolyCarController Spawn(CarSpec spec, float kmh)
        {
            var go = (GameObject)Object.Instantiate(spec.prefab, Origin, Quaternion.identity);
            var car = go.GetComponent<PolyCarController>();
            Wake.Invoke(car, null);
            car.ApplySpec(spec);
            car.Teleport(Origin + new Vector3(0f, 0.05f, 0f), Quaternion.identity);
            for (int i = 0; i < 50; i++) Step(car, default);   // settle on the springs
            if (kmh > 0f)
            {
                float v = kmh / 3.6f;
                car.Body.linearVelocity = Vector3.forward * v;
                foreach (var w in car.wheels) w.omega = v / car.wheelRadius;
                for (int i = 0; i < 400 && car.SpeedKmh < kmh - 1f; i++) Step(car, new DriveInput { Throttle = 1f });
            }
            return car;
        }

        private static void Step(PolyCarController car, DriveInput input)
        {
            car.Input = input;
            Fixed.Invoke(car, null);
            Physics.Simulate(Dt);
        }

        private static float Yaw(PolyCarController c) => c.transform.eulerAngles.y;

        private static void Launch(CarSpec spec, StringBuilder sb)
        {
            var car = Spawn(spec, 0f);
            float t100 = -1f, v10 = 0f, v20 = 0f, maxSlip = 0f;
            string gears = "";
            int lastGear = car.Gear;
            for (int i = 1; i <= 2500; i++)
            {
                Step(car, new DriveInput { Throttle = 1f });
                float t = i * Dt;
                if (t100 < 0f && car.SpeedKmh >= 100f) t100 = t;
                if (i == 500) v10 = car.SpeedKmh;
                if (i == 1000) v20 = car.SpeedKmh;
                if (t < 3f) maxSlip = Mathf.Max(maxSlip, car.SlipSpeed);
                if (car.Gear != lastGear) { gears += $"{car.Gear}@{car.SpeedKmh:0} "; lastGear = car.Gear; }
            }
            sb.AppendLine($"  launch: 0-100 {t100:0.0}s, 10s {v10:0}, 20s {v20:0}, 50s {car.SpeedKmh:0} km/h (spec {spec.topSpeedKmh}), launch slip {maxSlip:0.0} m/s, shifts {gears}");
            Object.DestroyImmediate(car.gameObject);
        }

        private static void Braking(CarSpec spec, StringBuilder sb)
        {
            var car = Spawn(spec, 100f);
            Vector3 start = car.transform.position;
            float t = 0f, maxDrift = 0f;
            while (car.SpeedKmh > 1f && t < 15f)
            {
                Step(car, new DriveInput { Brake = 1f });
                t += Dt;
                maxDrift = Mathf.Max(maxDrift, Mathf.Abs(car.DriftAngle));
            }
            sb.AppendLine($"  brake 100-0: {Vector3.Distance(start, car.transform.position):0.0} m in {t:0.00}s, heading drift {maxDrift:0}°, gear {car.Gear}");
            Object.DestroyImmediate(car.gameObject);
        }

        private static void Corner(CarSpec spec, StringBuilder sb, float kmh, float steer)
        {
            var car = Spawn(spec, kmh);
            float latG = 0f, yaw = 0f, angle = 0f, minV = 999f;
            for (int i = 0; i < 300; i++)
            {
                float throttle = car.SpeedKmh < kmh ? 0.6f : 0.2f;
                Step(car, new DriveInput { Throttle = throttle, Steer = steer });
                if (i > 200)
                {
                    float v = car.Body.linearVelocity.magnitude;
                    float w = car.Body.angularVelocity.y;
                    latG = Mathf.Max(latG, Mathf.Abs(v * w) / 9.81f);
                    yaw = Mathf.Max(yaw, Mathf.Abs(w) * Mathf.Rad2Deg);
                    angle = Mathf.Max(angle, Mathf.Abs(car.DriftAngle));
                    minV = Mathf.Min(minV, car.SpeedKmh);
                }
            }
            sb.AppendLine($"  corner {kmh:0} km/h steer {steer}: {latG:0.00} g, yaw {yaw:0}°/s, body slip {angle:0}°, speed held {minV:0}, upright {!car.IsUpsideDown}");
            Object.DestroyImmediate(car.gameObject);
        }

        private static void HandbrakeDrift(CarSpec spec, StringBuilder sb)
        {
            var car = Spawn(spec, 80f);
            var trace = new StringBuilder();
            float maxAngle = 0f;
            bool spun = false;
            for (int i = 0; i < 350; i++)
            {
                float t = i * Dt;
                var input = new DriveInput { Throttle = 1f, Steer = -1f, Handbrake = t < 0.5f };
                if (t >= 0.8f && t < 5f)
                {
                    // Hold the slide: counter-steer in proportion to the slip angle, like a player would.
                    float a = car.DriftAngle;
                    input.Steer = Mathf.Clamp((a - 25f) / 25f, -1f, 1f);
                    input.Throttle = Mathf.Abs(a) > 40f ? 0.5f : 1f;
                }
                if (t >= 5f) input = new DriveInput { Throttle = 0.6f, Steer = Mathf.Clamp(car.DriftAngle / 20f, -1f, 1f) };
                Step(car, input);
                maxAngle = Mathf.Max(maxAngle, Mathf.Abs(car.DriftAngle));
                if (Mathf.Abs(car.DriftAngle) > 100f) spun = true;
                if (i % 25 == 0) trace.Append($"{t:0.0}:{car.DriftAngle:0}/{car.SpeedKmh:0} ");
            }
            sb.AppendLine($"  handbrake drift @80: max {maxAngle:0}°, spun {spun}, end angle {car.DriftAngle:0}° speed {car.SpeedKmh:0}");
            sb.AppendLine($"    angle/kmh: {trace}");
            Object.DestroyImmediate(car.gameObject);
        }

        private static void LaneChange(CarSpec spec, StringBuilder sb)
        {
            var car = Spawn(spec, 120f);
            float y0 = Yaw(car), maxYaw = 0f, maxAngle = 0f;
            for (int i = 0; i < 250; i++)
            {
                float t = i * Dt;
                float steer = t < 0.4f ? 1f : t < 0.8f ? -1f : 0f;
                Step(car, new DriveInput { Throttle = 1f, Steer = steer });
                maxYaw = Mathf.Max(maxYaw, Mathf.Abs(Mathf.DeltaAngle(y0, Yaw(car))));
                maxAngle = Mathf.Max(maxAngle, Mathf.Abs(car.DriftAngle));
            }
            float lateral = car.transform.position.x - Origin.x;
            sb.AppendLine($"  keyboard lane change @120: heading swing {maxYaw:0}°, max slip {maxAngle:0}°, end heading {Mathf.DeltaAngle(y0, Yaw(car)):0}°, sideways {lateral:0.0} m, end slip {car.DriftAngle:0}°");
            Object.DestroyImmediate(car.gameObject);
        }

        private static void PowerSlide(CarSpec spec, StringBuilder sb)
        {
            var car = Spawn(spec, 45f);
            float maxAngle = 0f;
            for (int i = 0; i < 150; i++)
            {
                Step(car, new DriveInput { Throttle = 1f, Steer = 1f });
                maxAngle = Mathf.Max(maxAngle, Mathf.Abs(car.DriftAngle));
            }
            sb.AppendLine($"  power-on full lock @45: max slip {maxAngle:0}°, speed {car.SpeedKmh:0}, drifting {car.IsDrifting}");
            Object.DestroyImmediate(car.gameObject);
        }
    }
}
