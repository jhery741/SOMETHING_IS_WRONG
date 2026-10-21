using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SomethingIsWrong
{
    /// <summary>
    /// Crea materiales compatibles con el render pipeline activo (Built-in "Standard" o URP "Lit").
    /// En el editor, "Persist" permite guardar texturas/materiales/mallas como assets al hornear el nivel.
    /// </summary>
    public static class MaterialLibrary
    {
        public static System.Action<Object, string> Persist;

        static Dictionary<string, Material> mats = new Dictionary<string, Material>();
        static Dictionary<string, Texture2D> texs = new Dictionary<string, Texture2D>();
        static Material baseLit, baseEmissive;
        static bool resolved;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { ClearCache(); }

        public static void ClearCache()
        {
            mats = new Dictionary<string, Material>();
            texs = new Dictionary<string, Texture2D>();
            baseLit = null;
            baseEmissive = null;
            resolved = false;
        }

        public static string DesiredShaderName()
        {
            var rp = GraphicsSettings.currentRenderPipeline;
            if (rp == null) return "Standard";
            string n = rp.GetType().Name;
            if (n.Contains("HDRender")) return "HDRP/Lit";
            return "Universal Render Pipeline/Lit";
        }

        static void Resolve()
        {
            if (resolved && baseLit != null) return;
            resolved = true;
            string want = DesiredShaderName();
            var b = Resources.Load<Material>("SIW_BaseLit");
            if (b != null && b.shader != null && b.shader.name == want) baseLit = b;
            var e = Resources.Load<Material>("SIW_BaseEmissive");
            if (e != null && e.shader != null && e.shader.name == want) baseEmissive = e;
            if (baseLit == null)
            {
                Shader s = Shader.Find(want);
                if (s == null) s = Shader.Find("Standard");
                if (s == null) s = Shader.Find("Universal Render Pipeline/Lit");
                if (s == null) s = Shader.Find("Sprites/Default");
                baseLit = new Material(s);
            }
            if (baseEmissive == null) baseEmissive = baseLit;
        }

        public static Texture2D Tex(string key, System.Func<Texture2D> make)
        {
            Texture2D t;
            if (texs.TryGetValue(key, out t) && t != null) return t;
            t = make();
            t.name = key;
            texs[key] = t;
            if (Persist != null) Persist(t, key);
            return t;
        }

        static void SetF(Material m, string prop, float v) { if (m.HasProperty(prop)) m.SetFloat(prop, v); }

        public static Material Get(string key, Color color, Texture2D tex = null, float smoothness = 0.15f, float metallic = 0f)
        {
            Material m;
            if (mats.TryGetValue(key, out m) && m != null) return m;
            Resolve();
            m = new Material(baseLit);
            m.name = key;
            m.color = color;
            m.mainTexture = tex;
            SetF(m, "_Glossiness", smoothness);
            SetF(m, "_Smoothness", smoothness);
            SetF(m, "_Metallic", metallic);
            m.DisableKeyword("_EMISSION");
            mats[key] = m;
            if (Persist != null) Persist(m, key);
            return m;
        }

        public static Material Emissive(string key, Color color, Color emission)
        {
            Material m;
            if (mats.TryGetValue(key, out m) && m != null) return m;
            Resolve();
            m = new Material(baseEmissive);
            m.name = key;
            m.color = color;
            m.mainTexture = null;
            SetF(m, "_Glossiness", 0.2f);
            SetF(m, "_Smoothness", 0.2f);
            m.EnableKeyword("_EMISSION");
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", emission);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            mats[key] = m;
            if (Persist != null) Persist(m, key);
            return m;
        }
    }
}
