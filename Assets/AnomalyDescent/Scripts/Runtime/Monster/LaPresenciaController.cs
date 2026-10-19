using UnityEngine;

namespace SomethingIsWrong
{
    public enum PresenciaState { Dormant, Manifesting, Drifting, Watched, Repelled, Lingering, Attack, Retreat }

    /// <summary>
    /// PISO 3 — LA PRESENCIA: entidad sobrenatural difícil de identificar. Probada en Play en el Piso 3.
    ///
    /// Reglas sobrenaturales, pero CONSISTENTES (sin teletransportes: siempre se desplaza por el grafo de puertas):
    /// 1. SOLO SE MUEVE CUANDO NADIE LA MIRA. Si está dentro del encuadre de la cámara del jugador y sin paredes
    ///    de por medio, se congela (estado Watched). Fuera de la vista avanza hacia el jugador a "driftSpeed".
    /// 2. ATRAVIESA PUERTAS CERRADAS (no las abre ni las golpea), pero NO paredes: sigue el mismo grafo que los demás.
    /// 3. LUZ: mantener la linterna sobre ella "repelTime" s la hace retroceder (Repelled) hasta otra habitación.
    ///    Esto gasta batería: es un recurso, no una solución permanente.
    /// 4. SEÑALES LEGIBLES: la luz de la habitación donde está parpadea sin parar, y a menos de "humRange" m se oye
    ///    un zumbido con subtítulo direccional. Nunca aparece sin avisar.
    /// 5. ESCONDITES: no los abre; espera fuera "lingerTime" s y se marcha.
    /// 6. REFUGIO: no entra. 7. CAPTURA: solo si llega a "catchDistance" sin estar siendo observada.
    /// </summary>
    public class LaPresenciaController : MonsterBase
    {
        public PresenciaState State { get; private set; }

        [Header("Movimiento")]
        public float driftSpeed = 1.6f;
        [Tooltip("Tiempo de aparición al despertar (se ve borrosa e inmóvil).")]
        public float manifestTime = 3f;

        [Header("Luz y señales")]
        public float repelTime = 2.5f;
        public float repelDuration = 6f;
        public float humRange = 8f;
        public float lingerTime = 6f;

        public override bool IsActive { get { return State != PresenciaState.Dormant; } }
        public override bool IsHunting { get { return State == PresenciaState.Drifting || State == PresenciaState.Watched || State == PresenciaState.Lingering; } }
        public override bool IsChasing { get { return State == PresenciaState.Drifting && player != null && Flat(transform.position, player.transform.position) < 6f; } }
        public override string StateName { get { return State.ToString(); } }
        public override string ThreatLabel
        {
            get
            {
                switch (State)
                {
                    case PresenciaState.Watched: return "Observada";
                    case PresenciaState.Repelled: return "Repelida";
                    case PresenciaState.Lingering: return "Espera";
                    case PresenciaState.Drifting: return IsChasing ? "Cerca" : "Deriva";
                }
                return "Presente";
            }
        }
        public override string AwakeningHint
        {
            get { return "Algo está en el piso. Solo se mueve cuando NO la miras. La luz de la linterna la aparta. Sigue el parpadeo de las luces."; }
        }
        public override string ChaseHint { get { return "No le des la espalda. Mantén la linterna sobre ella para hacerla retroceder."; } }

        float stateTimer, lightOnIt, repathTimer;
        bool attackReported;
        AudioSource hum;

        protected override string CanonicalName { get { return "La Presencia"; } }

        protected override void OnSetup()
        {
            hum = SoundBank.AddLoop(gameObject, "drone", 0.0f, humRange, true);
            // Atraviesa puertas: se ignoran los colisionadores de las hojas (no los de las paredes).
            foreach (var d in level.doors)
                if (d != null && d.leaf != null)
                    foreach (var c in d.leaf.GetComponentsInChildren<Collider>(true)) Physics.IgnoreCollision(cc, c, true);
            Hide();
        }

        public override void Hide()
        {
            State = PresenciaState.Dormant;
            SetVisible(false);
            if (hum != null) hum.Stop();
            if (cc != null) cc.enabled = false;
            path.Clear();
            CurrentSpeed = 0f;
        }

        public override void Awaken(Vector3 pos)
        {
            Teleport(pos);
            SetVisible(true);
            State = PresenciaState.Manifesting;
            stateTimer = manifestTime;
            attackReported = false;
            Trigger("Awaken", pAwaken);
            if (hum != null) { hum.volume = 0.5f; hum.Play(); }
            SoundBank.PlayAt("whisper", pos + Vector3.up * 1.6f, 0.9f, 30f, 0.8f, "[susurros]");
        }

        public override void Retreat()
        {
            if (!IsActive || State == PresenciaState.Attack) return;
            State = PresenciaState.Retreat;
            stateTimer = 10f;
            SetGoal(FarthestRoomFromPlayer().Center);
        }

        bool IsObserved()
        {
            if (player == null || player.IsDead || player.IsHidden) return false;
            return Visibility.IsVisible(player.Cam, transform.position + Vector3.up * 1.2f, 0.6f, transform);
        }

        bool FlashlightOnIt()
        {
            var fl = player.Flashlight;
            if (fl == null || !fl.On || fl.Spot == null) return false;
            Vector3 to = (transform.position + Vector3.up * 1.2f) - fl.Spot.transform.position;
            if (to.magnitude > fl.Spot.range) return false;
            return Vector3.Angle(fl.Spot.transform.forward, to) < fl.Spot.spotAngle * 0.35f && IsObserved();
        }

        void Update()
        {
            if (State == PresenciaState.Dormant || player == null || level == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            CurrentSpeed = 0f;

            // Señal constante: la luz de su habitación parpadea y el zumbido se oye cerca.
            var room = level.RoomAt(transform.position);
            if (room != null && room.roomLight != null) room.roomLight.Flicker(0.25f);
            float dist = Flat(transform.position, player.transform.position);
            if (dist < humRange) HUD.Caption("[zumbido]", transform.position + Vector3.up * 1.5f, humRange);

            bool watched = IsObserved();
            switch (State)
            {
                case PresenciaState.Manifesting:
                    stateTimer -= dt;
                    IdleGravity();
                    if (stateTimer <= 0f) State = PresenciaState.Drifting;
                    break;
                case PresenciaState.Drifting:
                case PresenciaState.Watched:
                    State = watched ? PresenciaState.Watched : PresenciaState.Drifting;
                    lightOnIt = FlashlightOnIt() ? lightOnIt + dt : Mathf.Max(0f, lightOnIt - dt);
                    if (lightOnIt >= repelTime) { BeginRepel(); break; }
                    if (watched) { IdleGravity(); break; }
                    if (player.IsHidden) { State = PresenciaState.Lingering; stateTimer = lingerTime; break; }
                    if (PlayerSheltered) { SetGoal(player.transform.position); FollowPath(driftSpeed, false); break; }
                    repathTimer -= dt;
                    if (repathTimer <= 0f || pathIndex >= path.Count) { repathTimer = 0.5f; SetGoal(player.transform.position); }
                    FollowPath(driftSpeed, false);
                    if (CanCatchNow()) Catch();
                    break;
                case PresenciaState.Repelled:
                    stateTimer -= dt;
                    if (!watched) FollowPath(driftSpeed * 1.2f, false); else IdleGravity();
                    if (stateTimer <= 0f) { lightOnIt = 0f; State = PresenciaState.Drifting; }
                    break;
                case PresenciaState.Lingering:
                    stateTimer -= dt;
                    IdleGravity();
                    if (!player.IsHidden) State = PresenciaState.Drifting;
                    else if (stateTimer <= 0f) BeginRepel();
                    break;
                case PresenciaState.Attack:
                    IdleGravity();
                    break;
                case PresenciaState.Retreat:
                    stateTimer -= dt;
                    if (FollowPath(driftSpeed, false) || stateTimer <= 0f) { Hide(); return; }
                    break;
            }
            if (hum != null) hum.volume = Mathf.Lerp(0.6f, 0.05f, dist / humRange);
            SetAnimator(CurrentSpeed, IsChasing, State == PresenciaState.Watched, dt);
        }

        void BeginRepel()
        {
            State = PresenciaState.Repelled;
            stateTimer = repelDuration;
            lightOnIt = 0f;
            SetGoal(FarthestRoomFromPlayer().Center);
            SoundBank.PlayAt("whisper", transform.position + Vector3.up * 1.6f, 0.7f, 20f, 0.6f, "[lamento que se aleja]");
        }

        void Catch()
        {
            if (State == PresenciaState.Attack) return;
            State = PresenciaState.Attack;
            path.Clear();
            Trigger("Attack", pAttack);
            if (!attackReported) { attackReported = true; if (Director != null) Director.OnPlayerCaught(this); }
        }

        protected override GameObject BuildPlaceholder(Transform parent)
        {
            var root = new GameObject("Placeholder");
            root.transform.SetParent(parent, false);
            var mat = MaterialLibrary.Get("m_presencia", new Color(0.05f, 0.05f, 0.06f), null, 0f);
            Part(PrimitiveType.Capsule, "Silueta", root.transform, new Vector3(0f, 1.05f, 0f), new Vector3(0.5f, 1.05f, 0.3f), mat, Vector3.zero);
            return root;
        }
    }
}
