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
        float swing = 1f;          // sentido de apertura: se elige para que la hoja se aleje de quien abre
        Quaternion closedRot;
        Vector3 leafLocalCenter;   // centro de la hoja en el espacio local de la bisagra
        bool hasCenter;
        bool init;

        void Awake() { Init(); }

        void Init()
        {
            if (init) return;
            init = true;
            closedRot = transform.localRotation;
            var rends = GetComponentsInChildren<Renderer>(true);
            if (rends.Length > 0)
            {
                var b = rends[0].bounds; for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                leafLocalCenter = transform.InverseTransformPoint(b.center);
                hasCenter = true;
            }
            angle = isOpen ? openAngle : 0f;
            transform.localRotation = closedRot * Quaternion.Euler(0f, angle, 0f);
        }

        public string GetPrompt(PlayerController p)
        {
            var exit = GetComponent<ExitDoor>();
            if (exit != null) return exit.GetPrompt();
            if (locked) return "Cerrada con llave";
            return isOpen ? "[E] Cerrar puerta" : "[E] Abrir puerta";
        }

        public void Interact(PlayerController p)
        {
            var exit = GetComponent<ExitDoor>();
            if (exit != null) { exit.Interact(); return; }
            if (locked) { SoundBank.PlayAt("click", transform.position, 0.6f, 8f); return; }
            if (!isOpen && p != null) ChooseSwing(p.transform.position);
            SetOpen(!isOpen, true);
        }

        /// <summary>
        /// Abre la hoja hacia el lado contrario de "actor" (jugador o monstruo). Antes siempre giraba hacia el mismo lado
        /// y, si el monstruo llegaba por ese lado, la hoja le cerraba el paso y quedaba atascado detrás de ella.
        /// </summary>
        public void ChooseSwing(Vector3 actor)
        {
            Init();
            if (!hasCenter || isOpen) return;
            Vector3 plus = LeafCenterAt(openAngle), minus = LeafCenterAt(-openAngle);
            plus.y = minus.y = actor.y = 0f;
            swing = (plus - actor).sqrMagnitude >= (minus - actor).sqrMagnitude ? 1f : -1f;
        }

        Vector3 LeafCenterAt(float deg)
        {
            Vector3 local = transform.localPosition + (closedRot * Quaternion.Euler(0f, deg, 0f)) * Vector3.Scale(transform.localScale, leafLocalCenter);
            return transform.parent != null ? transform.parent.TransformPoint(local) : local;
        }

        public void SetOpen(bool open, bool byPlayer)
        {
            Init();
            if (isOpen == open) return;
            isOpen = open;
            SoundBank.PlayAt(open ? "door_open" : "door_close", transform.position + Vector3.up, 0.7f, 14f, Random.Range(0.94f, 1.06f));
            if (byPlayer)
            {
                NoiseSystem.Emit(transform.position, 6f, true);
                var d = GameDirector.Instance;
                if (d != null && d.Presence != null) d.Presence.OnPlayerDoor(this, open);
            }
        }

        /// <summary>Puerta ya abierta con la hoja del lado del monstruo: la empuja hacia el otro lado (batiente).</summary>
        public void MonsterPushFrom(Vector3 from)
        {
            Init();
            if (!isOpen || !hasCenter || locked) return;
            Vector3 now = LeafCenterAt(openAngle * swing), other = LeafCenterAt(-openAngle * swing);
            now.y = other.y = from.y = 0f;
            if ((now - from).sqrMagnitude >= (other - from).sqrMagnitude) return;
            swing = -swing;
            speed = 420f;
            SnapMonsterOpening();
            SoundBank.PlayAt("bang", transform.position + Vector3.up, 0.8f, 22f);
        }

        public void MonsterOpen(Vector3 from)
        {
            ChooseSwing(from);
            MonsterOpen();
        }

        public void MonsterOpen()
        {
            Init();
            if (isOpen) return;
            isOpen = true;
            SnapMonsterOpening();
            SoundBank.PlayAt("bang", transform.position + Vector3.up, 0.9f, 25f);
        }

        // Monster interactions are forceful: don't sweep a collidable leaf through
        // the approaching capsule while moving it to the opposite side of the door.
        void SnapMonsterOpening()
        {
            angle = openAngle * swing;
            transform.localRotation = closedRot * Quaternion.Euler(0f, angle, 0f);
            Physics.SyncTransforms();
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
            float target = isOpen ? openAngle * swing : 0f;
            if (Mathf.Approximately(angle, target)) { speed = 200f; return; }
            angle = Mathf.MoveTowards(angle, target, speed * Time.deltaTime);
            transform.localRotation = closedRot * Quaternion.Euler(0f, angle, 0f);
        }
    }
}
