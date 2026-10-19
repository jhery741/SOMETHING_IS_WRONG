using UnityEngine;
#if SIW_POSTFX
using UnityEngine.Rendering.PostProcessing;
#endif

namespace SomethingIsWrong
{
    /// <summary>
    /// Postprocesado (Post Processing v2, Built-in). En la escena: un objeto "PostFX" con un PostProcessVolume global
    /// (perfil en Art/PostFX) y este componente. La cámara del jugador se crea en tiempo de ejecución, así que
    /// GameBootstrap llama a Attach(cam) para añadirle el PostProcessLayer con los recursos del paquete.
    /// Sin el paquete instalado (o sin este objeto) el juego funciona igual, sin postprocesado.
    /// </summary>
    public class PostFxRig : MonoBehaviour
    {
#if SIW_POSTFX
        [Tooltip("PostProcessResources del paquete (lo rellena 'Escena > 4. Postprocesado').")]
        public PostProcessResources resources;
#endif
        [Tooltip("Desactivar para comparar con/sin postprocesado.")]
        public bool enabledInGame = true;

        /// <summary>Cierto si la cámara del jugador tiene postprocesado (el HUD omite entonces su viñeta propia).</summary>
        public static bool Active { get; private set; }
        void OnDestroy() { Active = false; }

        public void Attach(Camera cam)
        {
            if (cam == null || !enabledInGame) return;
            cam.allowHDR = true;
#if SIW_POSTFX
            if (resources == null) { Debug.LogWarning("[ANOMALY DESCENT] PostFX sin 'resources': se omite el postprocesado."); return; }
            var go = cam.gameObject;
            bool was = go.activeSelf;
            go.SetActive(false);                       // Init antes de OnEnable
            var layer = go.GetComponent<PostProcessLayer>();
            if (layer == null) layer = go.AddComponent<PostProcessLayer>();
            layer.Init(resources);
            layer.volumeTrigger = cam.transform;
            layer.volumeLayer = 1 << gameObject.layer;
            layer.antialiasingMode = PostProcessLayer.Antialiasing.None;  // MSAA de la calidad
            layer.stopNaNPropagation = true;
            go.SetActive(was);
            Active = true;
#endif
        }

        public static void AttachTo(Camera cam)
        {
            var rig = FindAnyObjectByType<PostFxRig>();
            if (rig != null) rig.Attach(cam);
        }
    }
}
