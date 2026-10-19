using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace SomethingIsWrong.EditorTools
{
    /// <summary>
    /// Descarga e importa los assets CC0 de Poly Haven elegidos para el Piso 1 (lista fija, revisada a mano).
    /// NO crea geometría ni coloca nada en la escena: solo baja archivos a Assets/AnomalyDescent/Art/,
    /// configura su importación (mapas normales, sRGB, tiling) y construye materiales Standard (Built-in).
    /// La colocación en la escena se hace a mano en el Editor.
    /// Licencia de todo lo que baja: CC0 1.0 (https://polyhaven.com/license). Se registra en Art/LICENCIAS.md.
    /// </summary>
    public static class PolyHavenImporter
    {
        const string Art = "Assets/AnomalyDescent/Art";
        const string Api = "https://api.polyhaven.com";

        // id de Poly Haven → uso en el juego (solo documentación)
        static readonly (string id, string use)[] Models =
        {
            ("GothicBed_01", "Cama del dormitorio"),
            ("painted_wooden_chair_01", "Silla del dormitorio (anomalía tutorial)"),
            ("painted_wooden_nightstand", "Mesilla"),
            ("vintage_oil_lamp", "Lámpara de la mesilla (anomalía Hide)"),
            ("GothicCabinet_01", "Armario / escondite"),
            ("GothicCommode_01", "Cómoda del dormitorio"),
            ("hanging_picture_frame_02", "Cuadro del dormitorio (anomalía Rotate)"),
            ("fancy_picture_frame_01", "Cuadro del pasillo (anomalía Rotate)"),
            ("ClassicConsole_01", "Consola del pasillo"),
            ("antique_ceramic_vase_01", "Jarrón (anomalía Hide)"),
            ("pull_chain_light_socket", "Portalámparas de techo del pasillo"),
            ("lightbulb_01", "Bombilla del portalámparas"),
            ("modern_ceiling_lamp_01", "Lámpara de techo del dormitorio"),
            ("industrial_wall_lamp", "Aplique del refugio / escalera"),
            ("ornate_mirror_01", "Espejo del pasillo"),
            ("large_iron_gate", "Reja de la escalera"),
            ("vintage_suitcase", "Maletas del refugio"),
            ("cardboard_box_01", "Caja del refugio"),
            ("office_notepads", "Papeles sobre la consola"),
            ("wine_bottles_01", "Botellas (refugio)"),
            ("vintage_grandfather_clock_01", "Reloj de pie del dormitorio"),
            ("mantel_clock_01", "Reloj de sobremesa (cómoda)"),
            ("wooden_candlestick", "Candelero (cómoda)"),
            ("standing_picture_frame_01", "Retrato de sobremesa (cómoda)"),
            ("hanging_picture_frame_01", "Cuadro (pared oeste del dormitorio)"),
            ("hanging_picture_frame_03", "Cuadro (pared sur del pasillo)"),
            ("steel_frame_shelves_02", "Estantería metálica del refugio"),
            ("wooden_crate_01", "Caja de madera del refugio"),
            ("wooden_bucket_01", "Cubo del refugio"),
            ("can_rusted", "Latas oxidadas del refugio"),
            ("Lantern_01", "Farol apagado del refugio"),
        };

        // id → (material, metros que cubre una repetición [0 = 1 repetición por cara], tinte de paleta, intensidad del mapa normal)
        // Paleta Piso 1 (2.ª pasada): menos marrón/amarillo; paredes crema-gris y oliva apagado, madera oscura, techo gris cálido.
        // Repeticiones más grandes (menos patrón visible) y normales suaves (el relieve completo parecía rugoso a luz rasante).
        static readonly (string id, string mat, float metersPerTile, Color tint, float normal)[] Textures =
        {
            ("decrepit_wallpaper", "M_Pared_Dormitorio", 2.0f, new Color(0.88f, 0.86f, 0.80f), 0.45f),
            // yellow_plaster tenía pinceladas horizontales que a luz rasante parecían borrones: el pasillo usa damaged_plaster
            ("damaged_plaster", "M_Pared_Pasillo", 2.4f, new Color(0.80f, 0.77f, 0.68f), 0.40f),
            ("worn_plaster_wall", "M_Pared_Refugio", 2.5f, new Color(0.80f, 0.82f, 0.80f), 0.50f),
            ("damaged_plaster", "M_Pared_Escalera", 2.0f, new Color(0.80f, 0.78f, 0.74f), 0.50f),
            ("ceiling_interior", "M_Techo", 1.6f, new Color(0.74f, 0.72f, 0.68f), 0.20f),
            ("old_wooden_floor_02", "M_Suelo_Dormitorio", 2.2f, new Color(0.82f, 0.78f, 0.74f), 0.70f),
            ("wood_floor_worn", "M_Suelo_Pasillo", 2.2f, new Color(0.78f, 0.73f, 0.68f), 0.70f),
            ("worn_tile_floor", "M_Suelo_Refugio", 1.5f, new Color(0.85f, 0.85f, 0.82f), 0.70f),
            ("concrete_floor_worn_001", "M_Suelo_Escalera", 2.0f, new Color(0.75f, 0.75f, 0.75f), 0.70f),
            ("stair_planks", "M_Escalones", 1.0f, new Color(0.70f, 0.62f, 0.55f), 0.80f),
            ("rough_pine_door", "M_Puerta", 0f, new Color(0.85f, 0.78f, 0.70f), 0.80f),
            ("dark_wood", "M_Madera_Oscura", 1.0f, Color.white, 0.80f),
            ("dirty_carpet", "M_Alfombra", 1.5f, new Color(0.75f, 0.55f, 0.45f), 0.80f),
            ("rusty_painted_metal", "M_Metal_Oxidado", 1.0f, Color.white, 0.80f),
        };

        // Tinte de algunos modelos para que no destaquen como nuevos/limpios en la casa (silla pintada, estantería de madera clara).
        static readonly System.Collections.Generic.Dictionary<string, Color> ModelTints = new System.Collections.Generic.Dictionary<string, Color>
        {
            { "painted_wooden_chair_01", new Color(0.55f, 0.52f, 0.47f) },
            { "steel_frame_shelves_02", new Color(0.62f, 0.57f, 0.52f) },
        };

        static readonly Regex AuthorsRx = new Regex("\"authors\"\\s*:\\s*\\{([^}]*)\\}");
        static readonly Regex KeyRx = new Regex("\"([^\"]+)\"\\s*:");

        [MenuItem("ANOMALY DESCENT/Arte/1. Descargar e importar assets CC0 (Poly Haven)", priority = 40)]
        public static void DownloadAll()
        {
            if (!EditorUtility.DisplayDialog("Assets CC0",
                "Se descargarán " + Models.Length + " modelos y " + Textures.Length + " texturas de Poly Haven (CC0, ~40 MB) en " + Art + ".\n" +
                "No se modifica la escena. ¿Continuar?", "Descargar", "Cancelar")) return;

            var licences = new List<string>();
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            int total = Models.Length + Textures.Length, n = 0, fails = 0;
            try
            {
                foreach (var m in Models)
                {
                    EditorUtility.DisplayProgressBar("Poly Haven", "Modelo " + m.id, n++ / (float)total);
                    string dir = Art + "/Models/" + m.id;
                    if (!DownloadAsset(m.id, "Models", dir, true)) { fails++; continue; }
                    licences.Add(LicenceRow(m.id, "Modelo", m.use, today));
                }
                foreach (var t in Textures)
                {
                    EditorUtility.DisplayProgressBar("Poly Haven", "Textura " + t.id, n++ / (float)total);
                    string dir = Art + "/Textures/" + t.id;
                    if (!DownloadAsset(t.id, "Textures", dir, false)) { fails++; continue; }
                    licences.Add(LicenceRow(t.id, "Textura", t.mat, today));
                }
            }
            finally { EditorUtility.ClearProgressBar(); }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            WriteLicences(licences);
            BuildAllMaterials();
            Debug.Log("[Poly Haven] Descarga terminada. Fallos: " + fails + ". Revisa " + Art + "/LICENCIAS.md");
        }

        [MenuItem("ANOMALY DESCENT/Arte/2. Reconstruir materiales de los assets CC0", priority = 41)]
        public static void BuildAllMaterials()
        {
            SiwProjectSetup.EnsureFolder(Art + "/Materials");
            // Superficies
            foreach (var t in Textures)
            {
                string dir = Art + "/Textures/" + t.id;
                if (!AssetDatabase.IsValidFolder(dir)) continue;
                var mat = GetOrCreateMat(Art + "/Materials/" + t.mat + ".mat");
                var set = FindSet(dir, null);
                float tile = t.metersPerTile <= 0f ? 1f : 1f / t.metersPerTile;
                PbrMaterialBuilder.BuildMaterial(mat, set.albedo, set.normal, set.rough, set.metal, set.ao, set.arm, new Vector2(tile, tile), t.normal);
                mat.color = t.tint;
                EditorUtility.SetDirty(mat);
            }
            // Modelos: un material por material del FBX, enlazado con remap (el FBX no se modifica)
            foreach (var m in Models)
            {
                string dir = Art + "/Models/" + m.id;
                string fbx = dir + "/" + m.id + "_1k.fbx";
                var imp = AssetImporter.GetAtPath(fbx) as ModelImporter;
                if (imp == null) continue;
                SiwProjectSetup.EnsureFolder(dir + "/Materials");
                var names = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Material>().Select(x => x.name).ToList();
                foreach (var kv in imp.GetExternalObjectMap())
                    if (kv.Key.type == typeof(Material) && !names.Contains(kv.Key.name)) names.Add(kv.Key.name);
                foreach (var name in names)
                {
                    var set = FindSet(dir + "/textures", name);
                    if (set.albedo == null) set = FindSet(dir + "/textures", null);
                    var mat = GetOrCreateMat(dir + "/Materials/" + Sanitize(name) + ".mat");
                    PbrMaterialBuilder.BuildMaterial(mat, set.albedo, set.normal, set.rough, set.metal, set.ao, set.arm, Vector2.one);
                    Color tint;
                    if (ModelTints.TryGetValue(m.id, out tint)) mat.color = tint;
                    string ln = name.ToLowerInvariant();
                    if (ln.Contains("glass")) MakeGlass(mat);
                    if (ln.Contains("flame")) { mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", new Color(1f, 0.55f, 0.2f) * 2f); mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive; }
                    imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name), mat);
                }
                imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                imp.importCameras = false;
                imp.importLights = false;
                imp.importAnimation = false;
                imp.SaveAndReimport();
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Poly Haven] Materiales construidos en " + Art + "/Materials y Models/*/Materials.");
        }

        // ------------------------------------------------------------------ audio (Kenney, CC0)

        const string KenneyRpg = "https://kenney.nl/media/pages/assets/rpg-audio/8e99002d76-1677590336/kenney_rpg-audio.zip";
        const string KenneyImpact = "https://kenney.nl/media/pages/assets/impact-sounds/87b4ddecda-1677589768/kenney_impact-sounds.zip";

        // (zip, prefijo de archivo dentro del zip, id de SoundBank)
        static readonly (string zip, string entry, string id)[] AudioMap =
        {
            (KenneyImpact, "Audio/footstep_wood_", "step"),
            (KenneyImpact, "Audio/footstep_concrete_", "step_stone"),
            (KenneyRpg, "Audio/doorOpen_", "door_open"),
            (KenneyRpg, "Audio/doorClose_", "door_close"),
            (KenneyRpg, "Audio/creak", "creak"),
            (KenneyRpg, "Audio/metalLatch", "gate"),
            (KenneyRpg, "Audio/metalClick", "gate_locked"),
            (KenneyRpg, "Audio/metalClick", "click"),
            (KenneyImpact, "Audio/impactWood_heavy_", "bang"),
            (KenneyImpact, "Audio/impactGlass_medium_", "glass"),
        };

        [MenuItem("ANOMALY DESCENT/Arte/3. Descargar e importar audio CC0 (Kenney)", priority = 42)]
        public static void DownloadAudio()
        {
            string root = Art + "/Audio/Resources/Audio";
            var zips = new Dictionary<string, byte[]>();
            int count = 0;
            try
            {
                foreach (var a in AudioMap)
                {
                    EditorUtility.DisplayProgressBar("Kenney", a.id, 0.5f);
                    byte[] data;
                    if (!zips.TryGetValue(a.zip, out data))
                    {
                        data = GetBytes(a.zip);
                        if (data == null) continue;
                        zips[a.zip] = data;
                    }
                    string dir = root + "/" + a.id;
                    Directory.CreateDirectory(dir);
                    using (var arch = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read))
                        foreach (var e in arch.Entries)
                        {
                            if (!e.FullName.StartsWith(a.entry) || !e.FullName.EndsWith(".ogg")) continue;
                            string dst = dir + "/kenney_" + Path.GetFileName(e.FullName);
                            using (var src = e.Open())
                            using (var fs = File.Create(dst)) src.CopyTo(fs);
                            count++;
                        }
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            WriteLicences(new List<string>
            {
                "| RPG Audio (doorOpen, doorClose, creak, metalLatch, metalClick) | Audio | Puertas, crujidos, reja, clics | Kenney | https://kenney.nl/assets/rpg-audio | CC0 1.0 | " + today + " |",
                "| Impact Sounds (footstep_wood, footstep_concrete, impactWood_heavy, impactGlass_medium) | Audio | Pasos, golpes, botella | Kenney | https://kenney.nl/assets/impact-sounds | CC0 1.0 | " + today + " |",
            });
            Debug.Log("[Kenney] " + count + " clips importados en " + root);
        }

        static byte[] GetBytes(string url)
        {
            using (var req = UnityWebRequest.Get(url))
            {
                req.SetRequestHeader("User-Agent", "AnomalyDescent-UnityEditor");
                var op = req.SendWebRequest();
                while (!op.isDone) System.Threading.Thread.Sleep(20);
                if (req.result != UnityWebRequest.Result.Success) { Debug.LogError("[Descarga] " + url + ": " + req.error); return null; }
                return req.downloadHandler.data;
            }
        }

        // ------------------------------------------------------------------ descarga

        static bool DownloadAsset(string id, string kind, string dir, bool isModel)
        {
            string filesJson = Get(Api + "/files/" + id);
            if (filesJson == null) return false;
            var urls = new List<string>();
            string prefix = "https://dl.polyhaven.org/file/ph-assets/" + kind + "/jpg/1k/" + id + "/";
            foreach (Match mm in Regex.Matches(filesJson, Regex.Escape(prefix) + "[^\"]+"))
            {
                string u = mm.Value, f = Path.GetFileName(u).ToLowerInvariant();
                if (f.Contains("nor_dx") || f.Contains("_disp") || f.Contains("_bump") || f.Contains("_spec") || f.Contains("_mask")) continue;
                if (!urls.Contains(u)) urls.Add(u);
            }
            // algunos modelos traen texturas con transparencia en png
            string pngPrefix = "https://dl.polyhaven.org/file/ph-assets/" + kind + "/png/1k/" + id + "/";
            foreach (Match mm in Regex.Matches(filesJson, Regex.Escape(pngPrefix) + "[^\"]+"))
            {
                string f = Path.GetFileName(mm.Value).ToLowerInvariant();
                if (f.Contains("_diff") && !urls.Any(x => Path.GetFileNameWithoutExtension(x) == Path.GetFileNameWithoutExtension(mm.Value)))
                    urls.Add(mm.Value);
            }
            string texDir = isModel ? dir + "/textures" : dir;
            Directory.CreateDirectory(texDir);
            bool ok = true;
            if (isModel)
            {
                string fbxUrl = "https://dl.polyhaven.org/file/ph-assets/Models/fbx/1k/" + id + "/" + id + "_1k.fbx";
                ok &= Save(fbxUrl, dir + "/" + id + "_1k.fbx");
            }
            foreach (var u in urls)
            {
                string name = Path.GetFileName(u);
                // si hay jpg y png del mismo mapa, basta uno
                if (name.EndsWith(".png") && File.Exists(texDir + "/" + Path.ChangeExtension(name, ".jpg"))) continue;
                ok &= Save(u, texDir + "/" + name);
            }
            return ok;
        }

        static string Get(string url)
        {
            using (var req = UnityWebRequest.Get(url))
            {
                req.SetRequestHeader("User-Agent", "AnomalyDescent-UnityEditor");
                var op = req.SendWebRequest();
                while (!op.isDone) System.Threading.Thread.Sleep(20);
                if (req.result != UnityWebRequest.Result.Success) { Debug.LogError("[Poly Haven] " + url + ": " + req.error); return null; }
                return req.downloadHandler.text;
            }
        }

        static bool Save(string url, string path)
        {
            if (File.Exists(path) && new FileInfo(path).Length > 0) return true;
            using (var req = UnityWebRequest.Get(url))
            {
                req.SetRequestHeader("User-Agent", "AnomalyDescent-UnityEditor");
                var op = req.SendWebRequest();
                while (!op.isDone) System.Threading.Thread.Sleep(20);
                if (req.result != UnityWebRequest.Result.Success) { Debug.LogError("[Poly Haven] " + url + ": " + req.error); return false; }
                File.WriteAllBytes(path, req.downloadHandler.data);
                return true;
            }
        }

        static string LicenceRow(string id, string type, string use, string date)
        {
            string info = Get(Api + "/info/" + id) ?? "";
            string name = id;
            var nm = Regex.Match(info, "\"name\"\\s*:\\s*\"([^\"]+)\"");
            if (nm.Success) name = nm.Groups[1].Value;
            var authors = new List<string>();
            var am = AuthorsRx.Match(info);
            if (am.Success) foreach (Match k in KeyRx.Matches(am.Groups[1].Value)) authors.Add(k.Groups[1].Value);
            return "| " + name + " | " + type + " | " + use + " | " + (authors.Count > 0 ? string.Join(", ", authors) : "(no indicado)") +
                   " | https://polyhaven.com/a/" + id + " | CC0 1.0 | " + date + " |";
        }

        static void WriteLicences(List<string> rows)
        {
            string path = Art + "/LICENCIAS.md";
            var sb = new StringBuilder();
            if (File.Exists(path)) sb.Append(File.ReadAllText(path));
            else
            {
                sb.AppendLine("# Licencias de recursos externos — ANOMALY: DESCENT");
                sb.AppendLine();
                sb.AppendLine("Todo lo listado aquí se descargó desde la fuente indicada. Licencia general de Poly Haven: https://polyhaven.com/license (CC0 1.0, dominio público; no exige atribución, se acredita igualmente).");
                sb.AppendLine();
                sb.AppendLine("| Recurso | Tipo | Uso en el juego | Autor(es) | Enlace | Licencia | Fecha de descarga |");
                sb.AppendLine("|---|---|---|---|---|---|---|");
            }
            string existing = sb.ToString();
            foreach (var r in rows)
            {
                string link = r.Split('|')[5].Trim();
                if (!existing.Contains(link)) sb.AppendLine(r);
            }
            File.WriteAllText(path, sb.ToString());
            AssetDatabase.ImportAsset(path);
        }

        // ------------------------------------------------------------------ materiales

        struct TexSet { public Texture2D albedo, normal, rough, metal, ao, arm; }

        static TexSet FindSet(string dir, string materialName)
        {
            var s = new TexSet();
            if (!AssetDatabase.IsValidFolder(dir)) return s;
            string key = materialName != null ? materialName.ToLowerInvariant() : null;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { dir }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                string n = Path.GetFileNameWithoutExtension(p).ToLowerInvariant();
                if (n.EndsWith("_ms") || n.EndsWith("_ao") && n.Contains("_arm")) continue;   // generados
                if (key != null && !n.StartsWith(key + "_")) continue;
                // con material concreto, evita coger texturas de otra pieza cuyo nombre empiece igual
                if (key != null)
                {
                    string rest = n.Substring(key.Length + 1);
                    if (!(rest.StartsWith("diff") || rest.StartsWith("nor_gl") || rest.StartsWith("rough") || rest.StartsWith("arm") ||
                          rest.StartsWith("metal") || rest.StartsWith("ao"))) continue;
                }
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                if (n.Contains("nor_gl")) s.normal = t;
                else if (n.Contains("_arm_")) s.arm = t;
                else if (n.Contains("_rough")) s.rough = t;
                else if (n.Contains("_metal")) s.metal = t;
                else if (n.Contains("_ao_")) s.ao = t;
                else if (n.Contains("_diff") || n.Contains("_col")) s.albedo = t;
            }
            return s;
        }

        /// <summary>Vidrio con el shader Standard en modo Transparent (lámparas, cristal de cuadros, botellas).</summary>
        static void MakeGlass(Material m)
        {
            m.SetFloat("_Mode", 3f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_ALPHABLEND_ON");
            m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            var c = m.color; c.a = 0.22f; m.color = c;
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.9f);
            if (m.HasProperty("_GlossMapScale")) m.SetFloat("_GlossMapScale", 0.9f);
            EditorUtility.SetDirty(m);
        }

        static Material GetOrCreateMat(string path)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            m = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static string Sanitize(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Replace(':', '_');
        }
    }
}
