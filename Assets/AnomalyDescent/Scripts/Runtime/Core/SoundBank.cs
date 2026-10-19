using System.Collections.Generic;
using UnityEngine;

namespace SomethingIsWrong
{
    /// <summary>
    /// Sonidos generados por código (sin archivos de audio) para que el prototipo funcione desde el primer minuto.
    /// Puedes reemplazar cualquiera más adelante: coloca un AudioClip en Assets/AnomalyDescent/Resources/Audio/
    /// con el mismo id (por ejemplo "step.wav") y se usará en lugar del generado.
    /// </summary>
    public static class SoundBank
    {
        const int Rate = 22050;
        static Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();
        static System.Random rng = new System.Random(7);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            cache = new Dictionary<string, AudioClip>();
            variants = new Dictionary<string, AudioClip[]>();
            lastVariant = new Dictionary<string, int>();
            rng = new System.Random(7);
        }

        // Clips reales (CC0) en Resources/Audio/<id>/ — si hay varios se alterna al azar sin repetir el último.
        // Si no hay clip real se usa el sonido sintetizado PROVISIONAL de Build(); algunos ids nuevos caen en otro similar.
        static Dictionary<string, AudioClip[]> variants = new Dictionary<string, AudioClip[]>();
        static Dictionary<string, int> lastVariant = new Dictionary<string, int>();
        static readonly Dictionary<string, string> Fallback = new Dictionary<string, string>
        {
            { "door_open", "creak" }, { "door_close", "click" }, { "gate", "creak" }, { "gate_locked", "click" }, { "step_stone", "step" }
        };

        public static AudioClip Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            AudioClip[] v;
            if (!variants.TryGetValue(id, out v))
            {
                v = Resources.LoadAll<AudioClip>("Audio/" + id);
                variants[id] = v;
            }
            if (v != null && v.Length > 0)
            {
                int last;
                if (!lastVariant.TryGetValue(id, out last)) last = -1;
                int i = rng.Next(v.Length);
                if (v.Length > 1 && i == last) i = (i + 1) % v.Length;
                lastVariant[id] = i;
                return v[i];
            }
            AudioClip c;
            if (cache.TryGetValue(id, out c) && c != null) return c;
            c = Resources.Load<AudioClip>("Audio/" + id);
            string alt;
            if (c == null && Fallback.TryGetValue(id, out alt)) return Get(alt);
            if (c == null) c = Build(id);
            if (c != null) cache[id] = c;
            return c;
        }

        // ---------------------------------------------------------------- reproducción

        public static AudioSource PlayAt(string id, Vector3 pos, float volume = 1f, float maxDistance = 20f, float pitch = 1f, string caption = null)
        {
            if (caption != null) HUD.Caption(caption, pos, maxDistance);
            var clip = Get(id);
            if (clip == null) return null;
            var go = new GameObject("SFX_" + id);
            go.transform.position = pos;
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.volume = volume * GameSettings.SfxVolume;
            src.pitch = pitch;
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 1f;
            src.maxDistance = maxDistance;
            src.dopplerLevel = 0f;
            src.Play();
            Object.Destroy(go, clip.length / Mathf.Max(0.1f, pitch) + 0.2f);
            return src;
        }

        public static AudioSource Play2D(string id, float volume = 1f, float pitch = 1f)
        {
            var clip = Get(id);
            if (clip == null) return null;
            var go = new GameObject("SFX2D_" + id);
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.volume = volume * GameSettings.SfxVolume;
            src.pitch = pitch;
            src.spatialBlend = 0f;
            src.ignoreListenerPause = false;
            src.Play();
            Object.Destroy(go, clip.length / Mathf.Max(0.1f, pitch) + 0.2f);
            return src;
        }

        public static AudioSource AddLoop(GameObject host, string id, float volume, float maxDistance, bool spatial = true)
        {
            var src = host.AddComponent<AudioSource>();
            src.clip = Get(id);
            src.loop = true;
            src.volume = volume;
            src.spatialBlend = spatial ? 1f : 0f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 1f;
            src.maxDistance = maxDistance;
            src.dopplerLevel = 0f;
            src.playOnAwake = false;
            return src;
        }

        // ---------------------------------------------------------------- utilidades

        static float N() { return (float)(rng.NextDouble() * 2.0 - 1.0); }
        static float LpA(float cutoff) { return 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / Rate); }
        static float[] Buf(float seconds) { return new float[Mathf.Max(1, Mathf.CeilToInt(seconds * Rate))]; }
        static float Frac(float x) { return x - Mathf.Floor(x); }
        const float TwoPi = Mathf.PI * 2f;

        static void Normalize(float[] d, float peak)
        {
            float m = 0f;
            for (int i = 0; i < d.Length; i++) m = Mathf.Max(m, Mathf.Abs(d[i]));
            if (m < 1e-6f) return;
            float k = peak / m;
            for (int i = 0; i < d.Length; i++) d[i] *= k;
        }

        static void FadeEdges(float[] d, float seconds)
        {
            int n = Mathf.Min(d.Length / 2, Mathf.CeilToInt(seconds * Rate));
            for (int i = 0; i < n; i++)
            {
                float k = (float)i / n;
                d[i] *= k;
                d[d.Length - 1 - i] *= k;
            }
        }

        static AudioClip Clip(string name, float[] d, float peak)
        {
            Normalize(d, peak);
            var c = AudioClip.Create(name, d.Length, 1, Rate, false);
            c.SetData(d, 0);
            return c;
        }

        static AudioClip Build(string id)
        {
            switch (id)
            {
                case "step": return Step(id, 0.13f, 900f, 32f, 0.45f, 0f);
                case "step_monster": return Step(id, 0.4f, 260f, 10f, 0.95f, 52f);
                case "breath": return Breath(id);
                case "heartbeat": return Heartbeat(id);
                case "bang": return Bang(id);
                case "static": return Static(id);
                case "shutter": return Shutter(id);
                case "whisper": return Whisper(id);
                case "drone": return Drone(id);
                case "tick": return Tick(id);
                case "musicbox": return MusicBox(id);
                case "knock": return Knock(id);
                case "scream": return Scream(id);
                case "creak": return Creak(id);
                case "click": return Click(id);
                case "beep_ok": return Beep(id, new[] { 880f, 1320f }, 0.11f, false);
                case "beep_bad": return Beep(id, new[] { 220f, 170f }, 0.22f, true);
                case "pickup": return Beep(id, new[] { 660f, 990f }, 0.07f, false);
                case "glass": return Glass(id);
                case "water": return Water(id);
                case "drip_loop": return DripLoop(id);
            }
            Debug.LogWarning("[SoundBank] Sonido desconocido: " + id);
            return null;
        }

        // ---------------------------------------------------------------- generadores

        static AudioClip Step(string id, float len, float cutoff, float decay, float peak, float thumpHz)
        {
            var d = Buf(len);
            float a = LpA(cutoff), y = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / Rate;
                float env = Mathf.Exp(-t * decay);
                y += a * (N() * env - y);
                float s = y * 3f;
                if (thumpHz > 0f) s += Mathf.Sin(TwoPi * thumpHz * t) * Mathf.Exp(-t * 14f) * 0.8f;
                d[i] = s;
            }
            FadeEdges(d, 0.003f);
            return Clip(id, d, peak);
        }

        static AudioClip Breath(string id)
        {
            var d = Buf(2.8f);
            float a1 = LpA(900f), a2 = LpA(180f), y1 = 0f, y2 = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / Rate;
                float env = 0f;
                if (t < 1.1f) env = Mathf.Sin(Mathf.PI * t / 1.1f);
                else if (t > 1.3f && t < 2.7f) env = Mathf.Sin(Mathf.PI * (t - 1.3f) / 1.4f) * 1.2f;
                float n = N();
                y1 += a1 * (n - y1);
                y2 += a2 * (n - y2);
                d[i] = (y1 - y2) * env * env;
            }
            return Clip(id, d, 0.55f);
        }

        static float Thump(float t, float t0)
        {
            if (t < t0) return 0f;
            float u = t - t0;
            return Mathf.Sin(TwoPi * 48f * u) * Mathf.Exp(-u * 22f);
        }

        static AudioClip Heartbeat(string id)
        {
            var d = Buf(0.9f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / Rate;
                d[i] = Thump(t, 0f) + 0.7f * Thump(t, 0.24f);
            }
            FadeEdges(d, 0.003f);
            return Clip(id, d, 0.9f);
        }

        static AudioClip Bang(string id)
        {
            var d = Buf(0.9f);
            float a = LpA(1600f), y = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / Rate;
                y += a * (N() * Mathf.Exp(-t * 10f) - y);
                d[i] = y * 2.5f + Mathf.Sin(TwoPi * 62f * t) * Mathf.Exp(-t * 7f) * 0.9f;
            }
            FadeEdges(d, 0.003f);
            return Clip(id, d, 1f);
        }

        static AudioClip Static(string id)
        {
            var d = Buf(1.2f);
            float a = LpA(2500f), y = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / Rate;
                float n = N();
                y += a * (n - y);
                float am = 0.55f + 0.45f * Mathf.Sin(TwoPi * 7.3f * t) * Mathf.Sin(TwoPi * 1.1f * t);
                d[i] = (n - y) * am;
            }
            FadeEdges(d, 0.02f);
            return Clip(id, d, 0.4f);
        }

        static AudioClip Shutter(string id)
        {
            var d = Buf(0.1f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / Rate;
                float s = N() * Mathf.Exp(-t * 120f);
                if (t > 0.05f) s += N() * Mathf.Exp(-(t - 0.05f) * 140f) * 0.8f;
                d[i] = s;
            }
            return Clip(id, d, 0.7f);
        }

        static AudioClip Whisper(string id)
        {
            float len = 4f;
            var d = Buf(len);
            float a1 = LpA(3200f), a2 = LpA(700f), y1 = 0f, y2 = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / Rate;
                float n = N();
                y1 += a1 * (n - y1);
                y2 += a2 * (n - y2);
                float band = y1 - y2;
                float ph = t * 4.5f + 0.6f * Mathf.Sin(t * 1.7f);
                float syl = Mathf.Pow(Mathf.Abs(Mathf.Sin(Mathf.PI * ph)), 3f);
                float phrase = Mathf.Clamp01(Mathf.Sin(TwoPi * t / len * 2f) * 1.5f + 0.3f);
                float formant = 1f + 0.3f * Mathf.Sin(TwoPi * (220f + 40f * Mathf.Sin(t * 3f)) * t);
                d[i] = band * syl * phrase * formant;
            }
            FadeEdges(d, 0.05f);
            return Clip(id, d, 0.5f);
        }

        static AudioClip Drone(string id)
        {
            float len = 8f;
            var d = Buf(len);
            float a = LpA(160f), y = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / Rate;
                float s = 0.6f * Mathf.Sin(TwoPi * 40f * t) + 0.4f * Mathf.Sin(TwoPi * 60.25f * t) + 0.2f * Mathf.Sin(TwoPi * 80.5f * t);
                y += a * (N() - y);
                s += y * 3f;
                s *= 0.7f + 0.3f * Mathf.Sin(TwoPi * t / len);
                d[i] = s;
            }
            return Clip(id, d, 0.35f);
        }

        static AudioClip Tick(string id)
        {
            var d = Buf(0.04f);
            float a = LpA(1500f), y = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / Rate;
                float n = N() * Mathf.Exp(-t * 400f);
                y += a * (n - y);
                d[i] = n - y;
            }
            return Clip(id, d, 0.5f);
        }

        static AudioClip MusicBox(string id)
        {
            float[] notes = { 659.25f, 587.33f, 523.25f, 493.88f, 440f, 523.25f, 493.88f, 329.63f };
            float noteLen = 0.55f;
            float len = notes.Length * noteLen + 1.2f;
            var d = Buf(len);
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / Rate;
                float s = 0f;
                for (int k = 0; k < notes.Length; k++)
                {
                    float t0 = k * noteLen;
                    if (t < t0) break;
                    float u = t - t0;
                    if (u > 2.5f) continue;
                    float f = notes[k] * (1f + 0.012f * Mathf.Sin(TwoPi * 0.7f * t));
                    s += (Mathf.Sin(TwoPi * f * u) + 0.35f * Mathf.Sin(TwoPi * 2f * f * u) + 0.1f * Mathf.Sin(TwoPi * 3.01f * f * u)) * Mathf.Exp(-u * 3.2f);
                }
                d[i] = s;
            }
            FadeEdges(d, 0.02f);
            return Clip(id, d, 0.5f);
        }

        static AudioClip Knock(string id)
        {
            var d = Buf(3f);
            float[] hits = { 0f, 0.32f, 0.64f };
            float a = LpA(400f), y = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / Rate;
                float s = 0f, e = 0f;
                for (int h = 0; h < hits.Length; h++)
                {
                    if (t < hits[h]) continue;
                    float u = t - hits[h];
                    s += Mathf.Sin(TwoPi * 95f * u) * Mathf.Exp(-u * 35f);
                    e += Mathf.Exp(-u * 60f);
                }
                y += a * (N() * e - y);
                d[i] = s + y * 3f;
            }
            FadeEdges(d, 0.003f);
            return Clip(id, d, 0.8f);
        }

        static AudioClip Scream(string id)
        {
            float len = 2.4f;
            var d = Buf(len);
            float ph1 = 0f, ph2 = 0f, ph3 = 0f, a = LpA(2600f), y = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / Rate;
                float f = 170f + 160f * Mathf.SmoothStep(0f, 1f, t / 1.2f) - 60f * Mathf.Max(0f, t - 1.4f);
                f *= 1f + 0.03f * N();
                ph1 += f / Rate;
                ph2 += f * 1.013f / Rate;
                ph3 += f * 0.5f * 0.997f / Rate;
                float saw = (Frac(ph1) * 2f - 1f) + (Frac(ph2) * 2f - 1f) + 0.6f * (Frac(ph3) * 2f - 1f);
                float env = Mathf.Clamp01(t / 0.08f) * Mathf.Clamp01((len - t) / 0.6f);
                y += a * ((saw * 0.6f + N() * 0.5f) * env - y);
                d[i] = y;
            }
            return Clip(id, d, 0.9f);
        }

        static AudioClip Creak(string id)
        {
            float len = 0.9f;
            var d = Buf(len);
            float ph = 0f, a = LpA(1400f), y = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / Rate;
                float f = 120f + 50f * Mathf.Sin(TwoPi * 2.3f * t) + 15f * N();
                ph += f / Rate;
                float saw = Frac(ph) * 2f - 1f;
                float stick = Frac(t * 28f) < 0.5f ? 1f : 0.4f;
                float env = Mathf.Sin(Mathf.PI * t / len);
                y += a * (saw * env * stick - y);
                d[i] = y;
            }
            return Clip(id, d, 0.4f);
        }

        static AudioClip Click(string id)
        {
            var d = Buf(0.04f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / Rate;
                d[i] = (N() * 0.5f + Mathf.Sin(TwoPi * 2000f * t)) * Mathf.Exp(-t * 300f);
            }
            return Clip(id, d, 0.5f);
        }

        static AudioClip Glass(string id)
        {
            var d = Buf(0.8f);
            float a = LpA(3000f), y = 0f;
            float[] f = { 2100f, 3170f, 4430f, 5210f };
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / Rate;
                float n = N();
                y += a * (n - y);
                float s = (n - y) * Mathf.Exp(-t * 18f) * 1.5f;
                for (int k = 0; k < f.Length; k++) s += Mathf.Sin(TwoPi * f[k] * t + k) * Mathf.Exp(-t * (5f + k * 2f)) * 0.25f;
                d[i] = s;
            }
            FadeEdges(d, 0.002f);
            return Clip(id, d, 0.9f);
        }

        static AudioClip Water(string id)
        {
            var d = Buf(3f);
            float a1 = LpA(4000f), a2 = LpA(500f), y1 = 0f, y2 = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / Rate;
                float n = N();
                y1 += a1 * (n - y1);
                y2 += a2 * (n - y2);
                d[i] = (y1 - y2) * (0.8f + 0.2f * Mathf.Sin(TwoPi * 0.6666667f * t));
            }
            return Clip(id, d, 0.35f);
        }

        static AudioClip DripLoop(string id)
        {
            var d = Buf(2.4f);
            float[] at = { 0.3f, 1.55f };
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / Rate;
                float s = 0f;
                foreach (float t0 in at)
                {
                    if (t < t0 || t > t0 + 0.15f) continue;
                    float u = t - t0;
                    float f = Mathf.Lerp(1400f, 700f, u / 0.15f);
                    s += Mathf.Sin(TwoPi * f * u) * Mathf.Exp(-u * 30f);
                }
                d[i] = s;
            }
            return Clip(id, d, 0.4f);
        }

        static AudioClip Beep(string id, float[] freqs, float each, bool harsh)
        {
            var d = Buf(each * freqs.Length + 0.02f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / Rate;
                int k = Mathf.Min(freqs.Length - 1, Mathf.FloorToInt(t / each));
                float u = t - k * each;
                float s = Mathf.Sin(TwoPi * freqs[k] * u);
                if (harsh) s = Mathf.Sign(s) * 0.7f + s * 0.3f;
                float env = Mathf.Clamp01(u / 0.005f) * Mathf.Clamp01((each - u) / 0.02f);
                d[i] = s * env;
            }
            return Clip(id, d, 0.45f);
        }
    }
}
