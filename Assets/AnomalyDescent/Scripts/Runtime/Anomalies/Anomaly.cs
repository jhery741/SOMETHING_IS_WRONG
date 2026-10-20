using System.Collections.Generic;
using UnityEngine;

namespace SomethingIsWrong
{
    /// <summary>Categorías de reporte (documento de concepto, sección 4).</summary>
    public enum AnomalyCategory { Ambiental = 0, Visual = 1, Sonora = 2, Espacial = 3, Criatura = 4 }

    public enum AnomalyKind
    {
        Move,           // mueble desplazado
        Rotate,         // objeto girado
        Hide,           // objeto ausente (el colisionador sigue para poder reportar el hueco)
        Show,           // aparece algo: duplicado, sombra, huellas, presencia en foto...
        Toggle,         // un objeto se sustituye por otro (cortina cerrada, lámpara encendida)
        SwapTexture,    // un cuadro / espejo cambia
        Sound,          // sonido imposible
        ClockReverse,   // el reloj corre hacia atrás
        DoorVanish,     // una puerta desaparece (cambia las rutas)
        CeilingLower,   // la habitación cambia de proporción
        DoorLock        // una puerta que siempre está abierta aparece cerrada con llave (cambia las rutas)
    }

    public static class AnomalyText
    {
        public static string Name(AnomalyCategory c)
        {
            switch (c)
            {
                case AnomalyCategory.Ambiental: return "Ambiental";
                case AnomalyCategory.Visual: return "Visual";
                case AnomalyCategory.Sonora: return "Sonora";
                case AnomalyCategory.Espacial: return "Espacial";
                case AnomalyCategory.Criatura: return "Criatura";
            }
            return c.ToString();
        }

        public static string Hint(AnomalyCategory c)
        {
            switch (c)
            {
                case AnomalyCategory.Ambiental: return "objeto movido, ausente, duplicado, luz";
                case AnomalyCategory.Visual: return "retratos, sombras, reflejos, giros";
                case AnomalyCategory.Sonora: return "voces, golpes, agua, música";
                case AnomalyCategory.Espacial: return "puertas, proporciones, rutas";
                case AnomalyCategory.Criatura: return "marcas, huellas, presencias";
            }
            return "";
        }
    }

    public class Anomaly : MonoBehaviour
    {
        [Header("Identidad")]
        public string anomalyName = "Anomalía";
        public Room room;
        public AnomalyKind kind;
        public AnomalyCategory primary;
        public bool hasSecondary;
        public AnomalyCategory secondary;
        [Tooltip("Anomalía de tutorial: siempre se elige y es la primera en aparecer.")]
        public bool tutorial;
        [Tooltip("Crítica: se anuncia con claridad y si se ignora cuenta doble.")]
        public bool critical;

        [Header("Objetivo")]
        [Tooltip("Vacío en Move/Rotate/Hide = el propio objeto que lleva este componente.")]
        public Transform target;
        public Vector3 moveOffset;
        public Vector3 rotateEuler;
        public GameObject showObject;
        public Renderer swapRenderer;
        public Texture2D normalTexture;
        public Texture2D altTexture;
        public string soundId;
        public string caption;
        public float soundVolume = 0.8f;
        public float soundRange = 10f;
        public Clock clock;
        public DoorLink door;
        public float ceilingDrop = 0.5f;

        [Header("Reporte")]
        [Tooltip("Objeto al que el jugador debe apuntar con [R]. Vacío = se busca solo (objetivo, objeto que aparece, parche de puerta o techo).")]
        public Reportable reportTarget;

        [System.NonSerialized] public bool active;
        [System.NonSerialized] public bool reported;
        [System.NonSerialized] public float unreportedTime;
        [System.NonSerialized] public bool ignoredPenalized;
        /// <summary>Vuelta en la que se usó (0 = libre). Una anomalía no se repite mientras queden otras sin usar.</summary>
        [System.NonSerialized] public int usedInLap;

        Vector3 basePos;
        Quaternion baseLocalRot;
        bool wasOpen;
        AudioSource src;
        readonly List<Renderer> hiddenRenderers = new List<Renderer>();
        readonly List<Light> hiddenLights = new List<Light>();

        public bool Accepts(AnomalyCategory c) { return c == primary || (hasSecondary && c == secondary); }

        /// <summary>Comprueba que la anomalía tiene todo lo necesario para aplicarse Y revertirse.</summary>
        public bool IsValid(out string error)
        {
            error = null;
            // Atajo de escena: si el componente está puesto en el propio objeto, ese objeto es el objetivo.
            if (target == null && (kind == AnomalyKind.Move || kind == AnomalyKind.Rotate || kind == AnomalyKind.Hide)) target = transform;
            if (room == null) error = "sin habitación (Room)";
            else switch (kind)
            {
                case AnomalyKind.Move: if (target == null || moveOffset.sqrMagnitude < 1e-4f) error = "Move necesita 'target' y un 'moveOffset' distinto de cero"; break;
                case AnomalyKind.Rotate: if (target == null || rotateEuler.sqrMagnitude < 1e-4f) error = "Rotate necesita 'target' y 'rotateEuler'"; break;
                case AnomalyKind.Hide: if (target == null) error = "Hide necesita 'target'"; break;
                case AnomalyKind.Show: if (showObject == null) error = "Show necesita 'showObject' (inactivo al empezar)"; else if (showObject.activeSelf) error = "'showObject' debe empezar DESACTIVADO"; break;
                case AnomalyKind.Toggle: if (target == null || showObject == null) error = "Toggle necesita 'target' y 'showObject'"; break;
                case AnomalyKind.SwapTexture: if (swapRenderer == null || normalTexture == null || altTexture == null) error = "SwapTexture necesita 'swapRenderer', 'normalTexture' y 'altTexture'"; break;
                case AnomalyKind.Sound: if (string.IsNullOrEmpty(soundId)) error = "Sound necesita 'soundId'"; break;
                case AnomalyKind.ClockReverse: if (clock == null) error = "ClockReverse necesita 'clock'"; break;
                case AnomalyKind.DoorVanish: if (door == null || door.vanishPatch == null) error = "DoorVanish necesita 'door' con 'vanishPatch'"; break;
                case AnomalyKind.CeilingLower: if (room.ceilingRoot == null) error = "CeilingLower necesita 'Ceiling Root' en la habitación"; break;
                case AnomalyKind.DoorLock: if (door == null || door.leaf == null) error = "DoorLock necesita 'door' con hoja (Door)"; break;
            }
            return error == null;
        }

        /// <summary>Reportable al que hay que apuntar. Lo enlaza si hacía falta. Null = no se podría reportar nunca.</summary>
        public Reportable EnsureLinked()
        {
            Reportable r = reportTarget;
            if (r == null && door != null && door.vanishPatch != null) r = door.vanishPatch.GetComponentInChildren<Reportable>(true);
            if (r == null && kind == AnomalyKind.DoorLock && door != null) r = door.GetComponentInChildren<Reportable>(true);
            if (r == null && kind == AnomalyKind.CeilingLower && room != null && room.ceilingRoot != null) r = room.ceilingRoot.GetComponentInChildren<Reportable>(true);
            if (r == null && target != null) r = target.GetComponentInParent<Reportable>(true);
            if (r == null && showObject != null) r = showObject.GetComponentInParent<Reportable>(true);
            if (r == null && swapRenderer != null) r = swapRenderer.GetComponentInParent<Reportable>(true);
            if (r != null && !r.linked.Contains(this)) r.linked.Add(this);
            // Las anomalías "Show" se reportan apuntando al objeto que aparece: se enlaza también ese.
            if (showObject != null)
            {
                var r2 = showObject.GetComponentInParent<Reportable>(true);
                if (r2 != null && !r2.linked.Contains(this)) r2.linked.Add(this);
            }
            return r;
        }

        /// <summary>Objetos que esta anomalía modifica (dos anomalías de la misma partida no deben compartirlos).</summary>
        public IEnumerable<Object> TouchedObjects()
        {
            if (target != null) yield return target;
            if (showObject != null) yield return showObject;
            if (swapRenderer != null) yield return swapRenderer;
            if (clock != null) yield return clock;
            if (door != null) yield return door;
            if (kind == AnomalyKind.CeilingLower && room != null && room.ceilingRoot != null) yield return room.ceilingRoot;
        }

        public Vector3 AnchorPosition
        {
            get
            {
                if (door != null) return door.transform.position + Vector3.up * 1.2f;
                if (kind == AnomalyKind.CeilingLower && room != null) return room.Center + Vector3.up * 2.4f;
                if (target != null) return target.position + Vector3.up * 0.3f;
                if (showObject != null) return showObject.transform.position + Vector3.up * 0.5f;
                return transform.position;
            }
        }

        void Update()
        {
            if (active && kind == AnomalyKind.Sound && !string.IsNullOrEmpty(caption))
                HUD.Caption(caption, AnchorPosition, soundRange);
        }

        public void Apply()
        {
            if (active) return;
            active = true;
            unreportedTime = 0f;
            switch (kind)
            {
                case AnomalyKind.Move:
                    basePos = target.position;
                    target.position = basePos + moveOffset;
                    break;
                case AnomalyKind.Rotate:
                    baseLocalRot = target.localRotation;
                    target.localRotation = baseLocalRot * Quaternion.Euler(rotateEuler);
                    break;
                case AnomalyKind.Hide:
                    hiddenRenderers.Clear();
                    hiddenLights.Clear();
                    foreach (var r in target.GetComponentsInChildren<Renderer>())
                        if (r.enabled) { r.enabled = false; hiddenRenderers.Add(r); }
                    foreach (var l in target.GetComponentsInChildren<Light>())
                        if (l.enabled) { l.enabled = false; hiddenLights.Add(l); }
                    break;
                case AnomalyKind.Show:
                    if (showObject != null) showObject.SetActive(true);
                    break;
                case AnomalyKind.Toggle:
                    if (target != null) target.gameObject.SetActive(false);
                    if (showObject != null) showObject.SetActive(true);
                    break;
                case AnomalyKind.SwapTexture:
                    if (swapRenderer != null) swapRenderer.material.mainTexture = altTexture;
                    break;
                case AnomalyKind.Sound:
                    if (src == null)
                    {
                        var host = target != null ? target.gameObject : gameObject;
                        src = SoundBank.AddLoop(host, soundId, soundVolume, soundRange, true);
                    }
                    if (src != null && src.clip != null) src.Play();
                    break;
                case AnomalyKind.ClockReverse:
                    if (clock != null) clock.reversed = true;
                    break;
                case AnomalyKind.DoorVanish:
                    if (door != null)
                    {
                        if (door.leaf != null) door.leaf.isOpen = false;
                        door.SetVanished(true);
                    }
                    break;
                case AnomalyKind.DoorLock:
                    if (door != null && door.leaf != null)
                    {
                        wasOpen = door.leaf.isOpen;
                        door.leaf.SetOpen(false, false);
                        door.leaf.locked = true;
                    }
                    break;
                case AnomalyKind.CeilingLower:
                    if (room != null && room.ceilingRoot != null)
                        room.ceilingRoot.position -= Vector3.up * ceilingDrop;
                    break;
            }
        }

        public void Revert()
        {
            if (!active) return;
            active = false;
            switch (kind)
            {
                case AnomalyKind.Move: target.position = basePos; break;
                case AnomalyKind.Rotate: target.localRotation = baseLocalRot; break;
                case AnomalyKind.Hide:
                    foreach (var r in hiddenRenderers) if (r != null) r.enabled = true;
                    foreach (var l in hiddenLights) if (l != null) l.enabled = true;
                    break;
                case AnomalyKind.Show: if (showObject != null) showObject.SetActive(false); break;
                case AnomalyKind.Toggle:
                    if (showObject != null) showObject.SetActive(false);
                    if (target != null) target.gameObject.SetActive(true);
                    break;
                case AnomalyKind.SwapTexture: if (swapRenderer != null) swapRenderer.material.mainTexture = normalTexture; break;
                case AnomalyKind.Sound: if (src != null) src.Stop(); break;
                case AnomalyKind.ClockReverse: if (clock != null) clock.reversed = false; break;
                case AnomalyKind.DoorVanish: if (door != null) door.SetVanished(false); break;
                case AnomalyKind.DoorLock:
                    if (door != null && door.leaf != null) { door.leaf.locked = false; if (wasOpen) door.leaf.SetOpen(true, false); }
                    break;
                case AnomalyKind.CeilingLower:
                    if (room != null && room.ceilingRoot != null)
                        room.ceilingRoot.position += Vector3.up * ceilingDrop;
                    break;
            }
        }
    }

    public static class Visibility
    {
        /// <summary>¿Puede la cámara ver este punto (dentro del encuadre y sin paredes delante)?</summary>
        public static bool IsVisible(Camera cam, Vector3 point, float radius) { return IsVisible(cam, point, radius, null); }

        /// <summary>
        /// Igual, pero si se indica <paramref name="self"/>, solo cuenta como visible cuando el rayo llega al propio
        /// objeto o no choca con nada antes: una puerta cerrada justo delante ya no "deja ver" lo que hay detrás.
        /// </summary>
        public static bool IsVisible(Camera cam, Vector3 point, float radius, Transform self)
        {
            if (cam == null) return false;
            Vector3 vp = cam.WorldToViewportPoint(point);
            if (vp.z < 0f) return false;
            const float m = 0.2f;
            if (vp.x < -m || vp.x > 1f + m || vp.y < -m || vp.y > 1f + m) return false;
            Vector3 from = cam.transform.position;
            Vector3 dir = point - from;
            float dist = dir.magnitude;
            if (dist < 0.01f) return true;
            RaycastHit hit;
            if (Physics.Raycast(from, dir / dist, out hit, dist, ~0, QueryTriggerInteraction.Ignore))
            {
                if (self != null) return hit.transform == self || hit.transform.IsChildOf(self) || hit.distance >= dist - 0.05f;
                return hit.distance >= dist - radius;
            }
            return true;
        }
    }
}
