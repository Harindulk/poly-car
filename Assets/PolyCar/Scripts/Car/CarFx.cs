using UnityEngine;

namespace PolyCar
{
    /// <summary>
    /// Skid marks and tyre smoke from the rear tyres' sliding speed, flames while boosting, and art of rally's
    /// dust: on loose surfaces each rear tyre throws a cloud that grows with speed and slip, coloured by the surface.
    /// </summary>
    public class CarFx : MonoBehaviour
    {
        public PolyCarController car;
        public TrailRenderer[] skids = new TrailRenderer[0];
        public ParticleSystem[] smoke = new ParticleSystem[0];
        public ParticleSystem[] boostFlames = new ParticleSystem[0];
        public Renderer[] brakeLights = new Renderer[0];

        private PolyCarController.Wheel[] _rear;
        private ParticleSystem[] _dust = new ParticleSystem[0];

        private void Start()
        {
            if (!car) car = GetComponent<PolyCarController>();
            var rear = new System.Collections.Generic.List<PolyCarController.Wheel>();
            foreach (var w in car.wheels) if (!w.front) rear.Add(w);
            _rear = rear.ToArray();
            foreach (var s in skids) if (s) { s.emitting = false; s.Clear(); }
            foreach (var p in smoke) if (p) p.Stop();
            foreach (var p in boostFlames) if (p) p.Stop();
            var mat = smoke.Length > 0 && smoke[0] ? smoke[0].GetComponent<ParticleSystemRenderer>().sharedMaterial : null;
            if (mat)
            {
                _dust = new ParticleSystem[_rear.Length];
                for (int i = 0; i < _dust.Length; i++) _dust[i] = MakeDust(mat);
            }
        }

        private ParticleSystem MakeDust(Material mat)
        {
            var go = new GameObject("Dust");
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = false;
            main.maxParticles = 260;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.1f, 2.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.6f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = -0.012f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.35f;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.55f, 1f, 2.4f));
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.45f, 0.5f), new GradientAlphaKey(0f, 1f) });
            fade.color = g;
            var em = ps.emission;
            em.rateOverTime = 0f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        /// <summary>Dust per m/s of speed, and its colour, for each surface (tarmac throws none).</summary>
        private static float DustRate(SurfaceKind k, out Color c)
        {
            switch (k)
            {
                case SurfaceKind.Gravel: c = new Color(0.76f, 0.66f, 0.52f, 0.42f); return 1.5f;
                case SurfaceKind.Verge: c = new Color(0.74f, 0.65f, 0.52f, 0.38f); return 1.1f;
                case SurfaceKind.Sand: c = new Color(0.9f, 0.72f, 0.52f, 0.5f); return 2f;
                case SurfaceKind.Snow: c = new Color(0.96f, 0.97f, 1f, 0.55f); return 1.6f;
                case SurfaceKind.DeepSnow: c = new Color(0.97f, 0.98f, 1f, 0.65f); return 2.2f;
                case SurfaceKind.Grass: c = new Color(0.52f, 0.47f, 0.35f, 0.3f); return 0.35f;
                default: c = default; return 0f;
            }
        }

        private void LateUpdate()
        {
            if (car == null) return;
            for (int i = 0; i < skids.Length && i < _rear.Length; i++)
            {
                var w = _rear[i];
                bool mark = w.grounded && w.slipSpeed > 5f;
                var s = skids[i];
                if (!s) continue;
                if (w.grounded) s.transform.position = w.hit.point + w.hit.normal * 0.03f;
                s.emitting = mark;
            }
            for (int i = 0; i < smoke.Length; i++)
            {
                var p = smoke[i];
                if (!p) continue;
                float slip = i < _rear.Length && _rear[i].grounded ? _rear[i].slipSpeed : 0f;
                bool on = slip > 8.5f;
                var em = p.emission;
                em.rateOverTimeMultiplier = on ? Mathf.Lerp(14f, 60f, Mathf.Clamp01((slip - 8.5f) / 10f)) : 0f;
                if (on && !p.isPlaying) p.Play();
            }
            for (int i = 0; i < _dust.Length; i++)
            {
                var w = _rear[i];
                var p = _dust[i];
                if (!p) continue;
                float rate = 0f;
                if (w.grounded)
                {
                    float perMs = DustRate(w.surface, out Color col);
                    rate = perMs * (Mathf.Max(0f, car.Body.linearVelocity.magnitude - 2f) + w.slipSpeed * 2f);
                    if (rate > 0f) { var main = p.main; main.startColor = col; p.transform.position = w.hit.point + Vector3.up * 0.25f; }
                }
                var em = p.emission;
                em.rateOverTimeMultiplier = Mathf.Min(rate, 120f);
            }
            foreach (var p in boostFlames)
            {
                if (!p) continue;
                if (car.Boosting && !p.isPlaying) p.Play();
                else if (!car.Boosting && p.isPlaying) p.Stop();
            }
        }

        /// <summary>Clears skid trails on teleport so they don't streak across the map.</summary>
        public void ResetTrails()
        {
            foreach (var s in skids) if (s) { s.emitting = false; s.Clear(); }
            foreach (var d in _dust) if (d) d.Clear();
        }
    }
}
