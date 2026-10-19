using System.IO;
using UnityEditor;
using UnityEngine;

namespace SomethingIsWrong.EditorTools
{
    /// <summary>
    /// El proyecto usa el Input Manager clásico (sin el paquete Input System, que no compila en 6000.5).
    /// Si "Active Input Handling" está en "Input System Package (New)", lo cambia a "Both" y ofrece reiniciar.
    /// </summary>
    [InitializeOnLoad]
    public static class SiwInputHandlerFix
    {
        static SiwInputHandlerFix() { EditorApplication.delayCall += Check; }

        static void Check()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (assets == null || assets.Length == 0 || assets[0] == null) return;
            var so = new SerializedObject(assets[0]);
            var p = so.FindProperty("activeInputHandler");
            if (p == null || p.intValue != 1) return;
            p.intValue = 2; // 0 = Old, 1 = New, 2 = Both
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log("[ANOMALY DESCENT] Active Input Handling cambiado a 'Both'. Hace falta reiniciar Unity.");
            if (EditorUtility.DisplayDialog("ANOMALY: DESCENT",
                    "Se ha activado el sistema de entrada clásico (Active Input Handling = Both).\nUnity necesita reiniciarse para aplicarlo.",
                    "Reiniciar ahora", "Más tarde"))
                EditorApplication.OpenProject(Directory.GetCurrentDirectory());
        }
    }
}
