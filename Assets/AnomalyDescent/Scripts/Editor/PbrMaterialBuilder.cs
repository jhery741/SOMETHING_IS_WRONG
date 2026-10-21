using System.IO;
using UnityEditor;
using UnityEngine;

namespace SomethingIsWrong.EditorTools
{
    /// <summary>
    /// Convierte un juego de texturas PBR (ambientCG, Poly Haven, Quixel, Substance...) en un material del
    /// shader Standard (Built-in):
    ///   Color/Albedo → _MainTex · NormalGL → _BumpMap (tipo Normal map) · AO → _OcclusionMap
    ///   Roughness (+Metalness) → se empaquetan en un PNG "_MS" (R = metal, A = 1 - rugosidad) → _MetallicGlossMap
    /// El shader Standard lee la suavidad del canal alfa del mapa metálico; por eso hace falta empaquetar.
    /// </summary>
    public class PbrMaterialBuilder : EditorWindow
    {
        Material target;
        DefaultAsset folder;
        Texture2D albedo, normal, roughness, metallic, ao, arm;
        Vector2 tiling = Vector2.one;
        float normalStrength = 1f, occlusionStrength = 1f, smoothnessScale = 1f;
        Vector2 scroll;

        [MenuItem("ANOMALY DESCENT/Arte/6. Constructor de materiales PBR", priority = 45)]
        static void Open() { GetWindow<PbrMaterialBuilder>("Material PBR"); }

        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.HelpBox(
                "1) Importa la carpeta de texturas (p. ej. ambientCG 'Wallpaper001_1K-JPG' o Poly Haven 'plaster_1k').\n" +
                "2) Elige el material de destino (Art/Materials/Surfaces/...). Todas las paredes/suelos que lo usan cambiarán a la vez.\n" +
                "3) 'Detectar en carpeta' rellena los campos por el nombre del archivo. Revisa y pulsa 'Construir'.\n" +
                "Usa el mapa normal en formato OpenGL (NormalGL / nor_gl). Resolución recomendada: 1K (2K solo para suelos).",
                MessageType.Info);

            target = (Material)EditorGUILayout.ObjectField("Material de destino", target, typeof(Material), false);
            folder = (DefaultAsset)EditorGUILayout.ObjectField("Carpeta de texturas", folder, typeof(DefaultAsset), false);
            using (new EditorGUI.DisabledScope(folder == null))
                if (GUILayout.Button("Detectar en carpeta")) Detect();

            EditorGUILayout.Space();
            albedo = Tex("Color / Albedo", albedo);
            normal = Tex("Normal (OpenGL)", normal);
            roughness = Tex("Roughness", roughness);
            metallic = Tex("Metalness (opcional)", metallic);
            ao = Tex("Ambient Occlusion (opcional)", ao);
            arm = Tex("ARM empaquetado (Poly Haven, opcional)", arm);

            EditorGUILayout.Space();
            tiling = EditorGUILayout.Vector2Field("Repetición (tiling)", tiling);
            normalStrength = EditorGUILayout.Slider("Intensidad normal", normalStrength, 0f, 2f);
            occlusionStrength = EditorGUILayout.Slider("Intensidad AO", occlusionStrength, 0f, 1f);
            smoothnessScale = EditorGUILayout.Slider("Escala de suavidad", smoothnessScale, 0f, 1f);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(target == null || albedo == null))
                if (GUILayout.Button("Construir material", GUILayout.Height(30))) Build();
            EditorGUILayout.EndScrollView();
        }

        static Texture2D Tex(string label, Texture2D t)
        {
            return (Texture2D)EditorGUILayout.ObjectField(label, t, typeof(Texture2D), false);
        }

        void Detect()
        {
            string dir = AssetDatabase.GetAssetPath(folder);
            if (!AssetDatabase.IsValidFolder(dir)) return;
            albedo = normal = roughness = metallic = ao = arm = null;
            Texture2D normalDX = null;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { dir }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                string n = Path.GetFileNameWithoutExtension(p).ToLowerInvariant();
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                if (n.EndsWith("_ms")) continue;   // generado por esta herramienta
                if (Has(n, "normalgl", "nor_gl", "normal_gl")) normal = t;
                else if (Has(n, "normaldx", "nor_dx", "normal_dx")) normalDX = t;
                else if (Has(n, "_normal", "_nrm", "_nor")) { if (normal == null) normal = t; }
                else if (Has(n, "_arm")) arm = t;
                else if (Has(n, "roughness", "_rough", "_rgh")) roughness = t;
                else if (Has(n, "metalness", "metallic", "_metal")) metallic = t;
                else if (Has(n, "ambientocclusion", "_ao", "occlusion")) ao = t;
                else if (Has(n, "_color", "basecolor", "albedo", "_diff", "diffuse", "_col")) albedo = t;
            }
            if (normal == null && normalDX != null)
            {
                normal = normalDX;
                Debug.LogWarning("[PBR] Solo hay mapa normal DirectX (verde invertido). Se usará, pero conviene descargar la versión OpenGL.");
            }
        }

        static bool Has(string n, params string[] keys)
        {
            foreach (var k in keys) if (n.Contains(k)) return true;
            return false;
        }

        void Build()
        {
            BuildMaterial(target, albedo, normal, roughness, metallic, ao, arm, tiling, normalStrength, occlusionStrength, smoothnessScale);
        }

        /// <summary>Versión reutilizable (la usa también el importador de assets CC0).</summary>
        public static void BuildMaterial(Material target, Texture2D albedo, Texture2D normal, Texture2D roughness, Texture2D metallic,
            Texture2D ao, Texture2D arm, Vector2 tiling, float normalStrength = 1f, float occlusionStrength = 1f, float smoothnessScale = 1f)
        {
            Undo.RecordObject(target, "Construir material PBR");
            SetImport(albedo, true, false);
            target.SetTexture("_MainTex", albedo);
            target.color = Color.white;
            target.mainTextureScale = tiling;

            if (normal != null)
            {
                SetImport(normal, false, true);
                target.SetTexture("_BumpMap", normal);
                if (target.HasProperty("_BumpScale")) target.SetFloat("_BumpScale", normalStrength);
                target.EnableKeyword("_NORMALMAP");
            }

            Texture2D aoTex = ao;
            Texture2D ms = null;
            if (arm != null)
            {
                // ARM de Poly Haven: R = oclusión, G = rugosidad, B = metal.
                ms = PackFromArm(arm);
                aoTex = PackAoFromArm(arm);   // el Standard lee la oclusión del canal G: se copia R a RGB
            }
            else if (roughness != null)
                ms = Pack(roughness, metallic);

            if (ms != null)
            {
                target.SetTexture("_MetallicGlossMap", ms);
                target.EnableKeyword("_METALLICGLOSSMAP");
                if (target.HasProperty("_GlossMapScale")) target.SetFloat("_GlossMapScale", smoothnessScale);
                if (target.HasProperty("_SmoothnessTextureChannel")) target.SetFloat("_SmoothnessTextureChannel", 0f);
                target.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            }
            if (aoTex != null)
            {
                SetImport(aoTex, false, false);
                target.SetTexture("_OcclusionMap", aoTex);
                if (target.HasProperty("_OcclusionStrength")) target.SetFloat("_OcclusionStrength", occlusionStrength);
            }
            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssets();
            Debug.Log("[PBR] Material '" + target.name + "' construido.");
        }

        static void SetImport(Texture2D t, bool srgb, bool isNormal)
        {
            if (t == null) return;
            var imp = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t)) as TextureImporter;
            if (imp == null) return;
            bool dirty = false;
            if (isNormal && imp.textureType != TextureImporterType.NormalMap) { imp.textureType = TextureImporterType.NormalMap; dirty = true; }
            if (!isNormal && imp.sRGBTexture != srgb) { imp.sRGBTexture = srgb; dirty = true; }
            if (imp.wrapMode != TextureWrapMode.Repeat) { imp.wrapMode = TextureWrapMode.Repeat; dirty = true; }
            if (imp.anisoLevel < 4) { imp.anisoLevel = 4; dirty = true; }
            if (dirty) imp.SaveAndReimport();
        }

        // --------------------------------------------------------------- empaquetado

        static Color[] ReadPixels(Texture2D t, out int w, out int h)
        {
            string path = AssetDatabase.GetAssetPath(t);
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            bool wasReadable = imp != null && imp.isReadable;
            var oldCompression = imp != null ? imp.textureCompression : TextureImporterCompression.Uncompressed;
            bool tweak = imp != null && (!wasReadable || oldCompression != TextureImporterCompression.Uncompressed);
            if (tweak) { imp.isReadable = true; imp.textureCompression = TextureImporterCompression.Uncompressed; imp.SaveAndReimport(); }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            w = tex.width; h = tex.height;
            var px = tex.GetPixels();
            if (tweak) { imp.isReadable = wasReadable; imp.textureCompression = oldCompression; imp.SaveAndReimport(); }
            return px;
        }

        static Color Sample(Color[] px, int w, int h, float u, float v)
        {
            int x = Mathf.Clamp(Mathf.RoundToInt(u * (w - 1)), 0, w - 1);
            int y = Mathf.Clamp(Mathf.RoundToInt(v * (h - 1)), 0, h - 1);
            return px[y * w + x];
        }

        static Texture2D Pack(Texture2D rough, Texture2D metal)
        {
            int w, h, mw = 0, mh = 0;
            SetImport(rough, false, false);
            var r = ReadPixels(rough, out w, out h);
            Color[] m = null;
            if (metal != null) { SetImport(metal, false, false); m = ReadPixels(metal, out mw, out mh); }
            var outPx = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float rv = r[y * w + x].r;
                    float mv = m != null ? Sample(m, mw, mh, x / (float)(w - 1), y / (float)(h - 1)).r : 0f;
                    outPx[y * w + x] = new Color(mv, mv, mv, 1f - rv);
                }
            return WritePacked(rough, outPx, w, h, "_MS");
        }

        static Texture2D PackFromArm(Texture2D armTex)
        {
            int w, h;
            SetImport(armTex, false, false);
            var a = ReadPixels(armTex, out w, out h);
            var outPx = new Color[w * h];
            for (int i = 0; i < a.Length; i++) outPx[i] = new Color(a[i].b, a[i].b, a[i].b, 1f - a[i].g);
            return WritePacked(armTex, outPx, w, h, "_MS");
        }

        static Texture2D PackAoFromArm(Texture2D armTex)
        {
            int w, h;
            var a = ReadPixels(armTex, out w, out h);
            var outPx = new Color[w * h];
            for (int i = 0; i < a.Length; i++) outPx[i] = new Color(a[i].r, a[i].r, a[i].r, 1f);
            return WritePacked(armTex, outPx, w, h, "_AO");
        }

        /// <summary>Si no es nulo, las texturas empaquetadas se escriben aquí (rutas cortas: Windows limita a 260 caracteres).</summary>
        public static string PackedOutputDir;

        static Texture2D WritePacked(Texture2D source, Color[] px, int w, int h, string suffix)
        {
            string src = AssetDatabase.GetAssetPath(source);
            string dir = string.IsNullOrEmpty(PackedOutputDir) ? Path.GetDirectoryName(src).Replace('\\', '/') : PackedOutputDir;
            string path = dir + "/" + Path.GetFileNameWithoutExtension(src) + suffix + ".png";
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            t.SetPixels(px);
            t.Apply(false, false);
            File.WriteAllBytes(path, t.EncodeToPNG());
            DestroyImmediate(t);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp != null)
            {
                imp.sRGBTexture = false;
                imp.alphaSource = TextureImporterAlphaSource.FromInput;
                imp.wrapMode = TextureWrapMode.Repeat;
                imp.anisoLevel = 4;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
