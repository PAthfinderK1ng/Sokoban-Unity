using UnityEngine;

namespace Sokoban.Game
{
    public enum SfxId
    {
        Step,
        Push,
        BoxOnGoal,
        Blocked,
        Undo,
        Click,
        Win,
        Error
    }

    /// <summary>Synthesised placeholder sound effects (no audio files required).</summary>
    public class SfxPlayer
    {
        private const int Rate = 44100;
        private readonly AudioSource _source;
        private readonly AudioClip[] _clips;
        private float _lastStepTime;

        public float Volume = 0.8f;
        public bool Enabled = true;

        public SfxPlayer(GameObject host)
        {
            _source = host.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0;
            _clips = new AudioClip[8];
            _clips[(int)SfxId.Step] = Synth("step", 0.045f, (t, i) => Noise(i) * Env(t, 0.045f, 0.002f) * 0.25f + Sine(t, 180) * Env(t, 0.045f, 0.001f) * 0.2f);
            _clips[(int)SfxId.Push] = Synth("push", 0.14f, (t, i) => (Sine(t, 95 - t * 120) * 0.6f + Noise(i) * 0.25f) * Env(t, 0.14f, 0.004f));
            _clips[(int)SfxId.BoxOnGoal] = Synth("goal", 0.35f, (t, i) =>
                (Sine(t, 880) * Env(t, 0.2f, 0.002f) + Sine(t - 0.08f, 1320) * (t > 0.08f ? Env(t - 0.08f, 0.27f, 0.002f) : 0)) * 0.35f);
            _clips[(int)SfxId.Blocked] = Synth("blocked", 0.07f, (t, i) => Square(t, 70) * Env(t, 0.07f, 0.002f) * 0.18f);
            _clips[(int)SfxId.Undo] = Synth("undo", 0.08f, (t, i) => Sine(t, 600 - t * 3000) * Env(t, 0.08f, 0.002f) * 0.25f);
            _clips[(int)SfxId.Click] = Synth("click", 0.05f, (t, i) => Sine(t, 740) * Env(t, 0.05f, 0.001f) * 0.25f);
            _clips[(int)SfxId.Error] = Synth("error", 0.22f, (t, i) => Square(t, t < 0.1f ? 220 : 165) * Env(t, 0.22f, 0.003f) * 0.15f);
            float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f };
            _clips[(int)SfxId.Win] = Synth("win", 0.9f, (t, i) =>
            {
                float v = 0;
                for (int n = 0; n < notes.Length; n++)
                {
                    float st = n * 0.11f;
                    if (t < st) continue;
                    float len = n == notes.Length - 1 ? 0.55f : 0.25f;
                    v += (Sine(t - st, notes[n]) + 0.3f * Sine(t - st, notes[n] * 2)) * Env(t - st, len, 0.004f);
                }
                return v * 0.22f;
            });
        }

        public void Play(SfxId s, float volumeScale = 1f, float pitch = 1f)
        {
            if (!Enabled || Volume <= 0.001f) return;
            if (s == SfxId.Step)
            {
                // avoid machine-gun effect on fast auto-walk
                if (Time.unscaledTime - _lastStepTime < 0.05f) return;
                _lastStepTime = Time.unscaledTime;
            }
            _source.pitch = pitch;
            _source.PlayOneShot(_clips[(int)s], Volume * volumeScale);
        }

        // ------------------------------------------------------------------ synthesis helpers

        private delegate float Wave(float t, int i);

        private static AudioClip Synth(string name, float seconds, Wave fn)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(fn(i / (float)Rate, i), -1f, 1f);
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static float Sine(float t, float f) { return t < 0 ? 0 : Mathf.Sin(2 * Mathf.PI * f * t); }
        private static float Square(float t, float f) { return Mathf.Sign(Mathf.Sin(2 * Mathf.PI * f * t)); }

        private static float Noise(int i)
        {
            uint x = (uint)i * 747796405u + 2891336453u;
            x = ((x >> (int)((x >> 28) + 4u)) ^ x) * 277803737u;
            x = (x >> 22) ^ x;
            return (x / (float)uint.MaxValue) * 2f - 1f;
        }

        /// <summary>Attack/decay envelope.</summary>
        private static float Env(float t, float len, float attack)
        {
            if (t < 0 || t > len) return 0;
            if (t < attack) return t / attack;
            float k = 1f - (t - attack) / (len - attack);
            return k * k;
        }
    }
}
