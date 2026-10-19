using UnityEngine;

namespace PolyCar
{
    /// <summary>Skid marks and tyre smoke from the rear tyres' sliding speed, flames while boosting.</summary>
    public class CarFx : MonoBehaviour
    {
        public PolyCarController car;
        public TrailRenderer[] skids = new TrailRenderer[0];
        public ParticleSystem[] smoke = new ParticleSystem[0];
        public ParticleSystem[] boostFlames = new ParticleSystem[0];
        public Renderer[] brakeLights = new Renderer[0];

        private PolyCarController.Wheel[] _rear;

        private void Start()
        {
            if (!car) car = GetComponent<PolyCarController>();
            var rear = new System.Collections.Generic.List<PolyCarController.Wheel>();
            foreach (var w in car.wheels) if (!w.front) rear.Add(w);
            _rear = rear.ToArray();
            foreach (var s in skids) if (s) { s.emitting = false; s.Clear(); }
            foreach (var p in smoke) if (p) p.Stop();
            foreach (var p in boostFlames) if (p) p.Stop();
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
        }
    }
}
