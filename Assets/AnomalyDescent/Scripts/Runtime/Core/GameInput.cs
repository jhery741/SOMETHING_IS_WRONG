using UnityEngine;

namespace SomethingIsWrong
{
    /// <summary>Teclas que usa el juego. Funciona con el Input System nuevo o con el Input Manager clásico.</summary>
    public enum GKey
    {
        W, A, S, D, Up, Down, Left, Right,
        Shift, Ctrl, C, E, F, Q, R, Tab, Escape, Enter, Space,
        Alpha1, Alpha2, Alpha3, Alpha4, Alpha5, F1, G
    }

    /// <summary>
    /// Capa de entrada única sobre el Input Manager clásico (UnityEngine.Input).
    /// El proyecto NO usa el paquete Input System: "Active Input Handling" debe ser
    /// "Input Manager (Old)" o "Both" (SiwInputHandlerFix lo corrige si está en "New").
    /// </summary>
    public static class GameInput
    {
        static KeyCode Map(GKey k)
        {
            switch (k)
            {
                case GKey.W: return KeyCode.W;
                case GKey.A: return KeyCode.A;
                case GKey.S: return KeyCode.S;
                case GKey.D: return KeyCode.D;
                case GKey.Up: return KeyCode.UpArrow;
                case GKey.Down: return KeyCode.DownArrow;
                case GKey.Left: return KeyCode.LeftArrow;
                case GKey.Right: return KeyCode.RightArrow;
                case GKey.Shift: return KeyCode.LeftShift;
                case GKey.Ctrl: return KeyCode.LeftControl;
                case GKey.C: return KeyCode.C;
                case GKey.E: return KeyCode.E;
                case GKey.F: return KeyCode.F;
                case GKey.Q: return KeyCode.Q;
                case GKey.R: return KeyCode.R;
                case GKey.Tab: return KeyCode.Tab;
                case GKey.Escape: return KeyCode.Escape;
                case GKey.Enter: return KeyCode.Return;
                case GKey.Space: return KeyCode.Space;
                case GKey.Alpha1: return KeyCode.Alpha1;
                case GKey.Alpha2: return KeyCode.Alpha2;
                case GKey.Alpha3: return KeyCode.Alpha3;
                case GKey.Alpha4: return KeyCode.Alpha4;
                case GKey.Alpha5: return KeyCode.Alpha5;
                case GKey.F1: return KeyCode.F1;
                case GKey.G: return KeyCode.G;
            }
            return KeyCode.None;
        }

        public static bool Held(GKey k)
        {
            if (k == GKey.Shift) return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (k == GKey.Ctrl) return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            return Input.GetKey(Map(k));
        }

        public static bool Down(GKey k)
        {
            if (k == GKey.Shift) return Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift);
            if (k == GKey.Ctrl) return Input.GetKeyDown(KeyCode.LeftControl) || Input.GetKeyDown(KeyCode.RightControl);
            if (k == GKey.Enter) return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
            return Input.GetKeyDown(Map(k));
        }

        /// <summary>Movimiento del ratón aproximado a píxeles por frame.</summary>
        public static Vector2 MouseDelta
        {
            get { return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 10f; }
        }

        public static bool MouseDown(int button)
        {
            return Input.GetMouseButtonDown(button);
        }

        public static Vector2 Move
        {
            get
            {
                float x = 0f, y = 0f;
                if (Held(GKey.D) || Held(GKey.Right)) x += 1f;
                if (Held(GKey.A) || Held(GKey.Left)) x -= 1f;
                if (Held(GKey.W) || Held(GKey.Up)) y += 1f;
                if (Held(GKey.S) || Held(GKey.Down)) y -= 1f;
                return Vector2.ClampMagnitude(new Vector2(x, y), 1f);
            }
        }
    }
}
