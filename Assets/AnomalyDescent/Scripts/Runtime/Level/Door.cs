using UnityEngine;

namespace SomethingIsWrong
{
    /// <summary>Hoja de puerta con bisagra. El jugador la abre con [E]; el monstruo la abre de golpe.</summary>
    public class Door : MonoBehaviour, IInteractable
    {
        public float openAngle = 95f;
        public bool isOpen;
        public bool locked;
        public float speed = 200f;
        float angle;
        Quaternion closedRot;
        bool init;

        void Awake() { Init(); }

        void Init()
        {
            if (init) return;
            init = true;
            closedRot = transform.localRotation;
            angle = isOpen ? openAngle : 0f;
            transform.localRotation = closedRot * Quaternion.Euler(0f, angle, 0f);
        }

        public string GetPrompt(PlayerController p)
        {
            if (locked) return "Cerrada con llave";
            return isOpen ? "[E] Cerrar puerta" : "[E] Abrir puerta";
        }

        public void Interact(PlayerController p)
        {
            if (locked) { SoundBank.PlayAt("click", transform.position, 0.6f, 8f); return; }
            SetOpen(!isOpen, true);
        }

        public void SetOpen(bool open, bool byPlayer)
        {
            Init();
            if (isOpen == open) return;
            isOpen = open;
            SoundBank.PlayAt(open ? "door_open" : "door_close", transform.position + Vector3.up, 0.7f, 14f, Random.Range(0.94f, 1.06f));
            if (byPlayer) NoiseSystem.Emit(transform.position, 6f, true);
        }

        public void MonsterOpen()
        {
            Init();
            if (isOpen) return;
            isOpen = true;
            SoundBank.PlayAt("bang", transform.position + Vector3.up, 0.9f, 25f);
        }

        public void Slam()
        {
            Init();
            if (!isOpen) return;
            isOpen = false;
            speed = 500f;
            SoundBank.PlayAt("bang", transform.position + Vector3.up, 1f, 30f);
        }

        void Update()
        {
            float target = isOpen ? openAngle : 0f;
            if (Mathf.Approximately(angle, target)) { speed = 200f; return; }
            angle = Mathf.MoveTowards(angle, target, speed * Time.deltaTime);
            transform.localRotation = closedRot * Quaternion.Euler(0f, angle, 0f);
        }
    }
}
