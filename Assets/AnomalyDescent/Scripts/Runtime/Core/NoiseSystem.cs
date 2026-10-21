using UnityEngine;

namespace SomethingIsWrong
{
    public struct NoiseEvent
    {
        public Vector3 position;
        public float radius;      // distancia a la que se oye en condiciones ideales (misma habitación)
        public bool fromPlayer;
        public HideSpot hideSpot; // si el ruido viene de entrar/salir de un escondite
        public bool lure;         // señuelo (botella lanzada): atrae a El Ciego lejos del jugador
    }

    /// <summary>
    /// Todo lo que hace ruido pasa por aquí. El Ciego escucha estos eventos.
    /// Radios de referencia: agachado 1.5 m · caminar 5 m · correr 12 m · puerta 6 m · flash 7 m · radio 9 m · botella 11 m.
    /// </summary>
    public static class NoiseSystem
    {
        public static event System.Action<NoiseEvent> OnNoise;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { OnNoise = null; }

        public static void Emit(Vector3 position, float radius, bool fromPlayer, HideSpot hideSpot = null, bool lure = false)
        {
            if (radius <= 0f) return;
            var e = new NoiseEvent { position = position, radius = radius, fromPlayer = fromPlayer, hideSpot = hideSpot, lure = lure };
            if (OnNoise != null) OnNoise(e);
            HUD.NotifyNoise(radius, fromPlayer);
        }
    }
}
