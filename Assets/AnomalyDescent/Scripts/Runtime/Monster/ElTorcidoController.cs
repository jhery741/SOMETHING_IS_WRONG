using UnityEngine;

namespace SomethingIsWrong
{
    public enum TorcidoState { Dormant, Emerging, Prowl, Stalk, Windup, Lunge, Recover, Search, Distracted, Attack, Retreat }

    /// <summary>
    /// PISO 2 — EL TORCIDO: criatura deforme, rápida y agresiva. PREPARADO, SIN PROBAR: el Piso 2 aún no existe.
    ///
    /// Se diferencia de El Ciego en que CAZA CON LA VISTA y ataca a embestidas:
    /// 1. VISTA: cono de "viewAngle" grados y "viewRange" m con línea libre; la linterna encendida y apuntada
    ///    hacia él duplica el alcance. El ruido solo lo hace investigar (oído débil: ×0.6).
    /// 2. ACECHO: al verte se acerca a "stalkSpeed" hasta "lungeRange".
    /// 3. ANTICIPACIÓN (señal clara): se detiene "windupTime" s, chilla y fija la DIRECCIÓN (no te sigue al embestir).
    /// 4. EMBESTIDA: carrera recta a "lungeSpeed" durante como máximo "lungeDistance" m. Si te toca, te atrapa.
    ///    Puedes esquivarla apartándote de la línea o rompiendo la línea de visión durante la anticipación.
    /// 5. RECUPERACIÓN: tras cada embestida queda aturdido "recoverTime" s (oportunidad de escapar).
    ///    Máximo "maxLungesBeforeSearch" embestidas seguidas; después busca durante unos segundos.
    /// 6. DISTRACCIÓN: una botella lo atrae y se queda "distractTime" s destrozando el punto del ruido.
    /// 7. REFUGIO y escondites: no entra en el refugio ni revisa escondites (no sabe abrirlos: es su debilidad).
    /// </summary>
    public class ElTorcidoController : MonsterBase
    {
        public TorcidoState State { get; private set; }

        [Header("Velocidades (m/s)")]
        public float prowlSpeed = 1.8f;
        public float stalkSpeed = 2.6f;
        public float lungeSpeed = 7.5f;

        [Header("Vista")]
        public float viewAngle = 110f;
        public float viewRange = 11f;
        [Tooltip("Pierde el interés tras estos segundos sin verte.")]
        public float loseSightSeconds = 3.5f;

        [Header("Embestida")]
        public float lungeRange = 6f;
        public float windupTime = 0.9f;
        public float lungeDistance = 7f;
        public float recoverTime = 2.2f;
        public int maxLungesBeforeSearch = 2;
        public float distractTime = 4f;

        public override bool IsActive { get { return State != TorcidoState.Dormant; } }
        public override bool IsHunting { get { return IsActive && State != TorcidoState.Attack && State != TorcidoState.Retreat && State != TorcidoState.Emerging; } }
        public override bool IsChasing { get { return State == TorcidoState.Stalk || State == TorcidoState.Windup || State == TorcidoState.Lunge; } }
        public override string StateName { get { return State.ToString(); } }
        public override string ThreatLabel
        {
            get
            {
                switch (State)
                {
                    case TorcidoState.Stalk:
                    case TorcidoState.Windup:
                    case TorcidoState.Lunge: return "Cacería";
                    case TorcidoState.Recover: return "Aturdido";
                    case TorcidoState.Search:
                    case TorcidoState.Distracted: return "Búsqueda";
                }
                return "Acecho";
            }
        }
        public override string AwakeningHint
        {
            get { return monsterName + " te VE. Cuando chilla y se detiene, va a embestir en línea recta: apártate o rompe la línea de visión. Tras embestir queda aturdido."; }
        }
        public override string ChaseHint { get { return "No te oye bien, pero te ve: escóndete detrás de puertas y esquinas, o lanza una botella."; } }

        float stateTimer, lastSeenTime, lungeTravelled;
        int lungeCount;
        Vector3 lungeDir, lastSeenPos;
        bool attackReported;

        void OnEnable() { NoiseSystem.OnNoise += OnNoise; }
        void OnDisable() { NoiseSystem.OnNoise -= OnNoise; }

        protected override string CanonicalName { get { return "El Torcido"; } }

        protected override void OnSetup() { Hide(); }

        public override void Hide()
        {
            State = TorcidoState.Dormant;
            SetVisible(false);
            if (cc != null) cc.enabled = false;
            path.Clear();
            CurrentSpeed = 0f;
        }

        public override void Awaken(Vector3 pos)
        {
            Teleport(pos);
            SetVisible(true);
            State = TorcidoState.Emerging;
            stateTimer = 2.5f;
            attackReported = false;
            lungeCount = 0;
            Trigger("Awaken", pAwaken);
            SoundBank.PlayAt("scream", pos + Vector3.up * 1.5f, 1f, 40f, 1.35f, "[chillido]");
        }

        public override void Retreat()
        {
            if (!IsActive || State == TorcidoState.Attack) return;
            State = TorcidoState.Retreat;
            stateTimer = 10f;
            SetGoal(FarthestRoomFromPlayer().Center);
        }

        public override void Pulse(Vector3 approx)
        {
            if (State == TorcidoState.Prowl || State == TorcidoState.Search) GoInvestigate(approx);
        }

        bool CanSeePlayer()
        {
            if (player == null || player.IsDead || player.IsHidden || PlayerSheltered) return false;
            Vector3 to = player.transform.position - transform.position;
            to.y = 0f;
            float dist = to.magnitude;
            float range = viewRange;
            var fl = player.Flashlight;
            if (fl != null && fl.On && Vector3.Dot(player.Cam.transform.forward, -to.normalized) > 0.8f) range *= 2f;
            if (dist > range) return false;
            if (dist > 1.5f && Vector3.Angle(transform.forward, to) > viewAngle * 0.5f) return false;
            return LineToPlayerClear(1.2f);
        }

        void Update()
        {
            if (State == TorcidoState.Dormant || player == null || level == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            CurrentSpeed = 0f;
            bool sees = State != TorcidoState.Recover && State != TorcidoState.Lunge && State != TorcidoState.Windup && CanSeePlayer();
            if (sees) { lastSeenTime = Time.time; lastSeenPos = player.transform.position; }

            switch (State)
            {
                case TorcidoState.Emerging:
                    stateTimer -= dt;
                    IdleGravity();
                    if (stateTimer <= 0f) StartProwl();
                    break;
                case TorcidoState.Prowl:
                    if (sees) { State = TorcidoState.Stalk; SoundBank.PlayAt("scream", transform.position + Vector3.up * 1.5f, 0.6f, 25f, 1.6f, "[gruñido]"); break; }
                    if (FollowPath(prowlSpeed)) StartProwl();
                    break;
                case TorcidoState.Stalk:
                    if (Time.time - lastSeenTime > loseSightSeconds) { BeginSearch(3f); break; }
                    float d = Flat(transform.position, player.transform.position);
                    if (sees && d <= lungeRange) { BeginWindup(); break; }
                    if (sees || pathIndex >= path.Count) SetGoal(lastSeenPos);
                    if (goalUnreachable) { BeginSearch(2f); break; }
                    FollowPath(stalkSpeed);
                    break;
                case TorcidoState.Windup:
                    stateTimer -= dt;
                    IdleGravity();
                    if (stateTimer <= 0f) BeginLunge();
                    break;
                case TorcidoState.Lunge:
                    UpdateLunge(dt);
                    break;
                case TorcidoState.Recover:
                    stateTimer -= dt;
                    IdleGravity();
                    if (stateTimer <= 0f)
                    {
                        if (lungeCount >= maxLungesBeforeSearch) { lungeCount = 0; BeginSearch(4f); }
                        else if (CanSeePlayer()) State = TorcidoState.Stalk;
                        else BeginSearch(3f);
                    }
                    break;
                case TorcidoState.Search:
                    stateTimer -= dt;
                    transform.Rotate(0f, 90f * dt, 0f);
                    IdleGravity();
                    if (sees) State = TorcidoState.Stalk;
                    else if (stateTimer <= 0f) StartProwl();
                    break;
                case TorcidoState.Distracted:
                    if (pathIndex < path.Count) FollowPath(stalkSpeed);
                    else { IdleGravity(); stateTimer -= dt; if (stateTimer <= 0f) StartProwl(); }
                    if (sees && Flat(transform.position, player.transform.position) < 4f) State = TorcidoState.Stalk;
                    break;
                case TorcidoState.Attack:
                    Face(player.transform.position, false);
                    IdleGravity();
                    break;
                case TorcidoState.Retreat:
                    stateTimer -= dt;
                    if (FollowPath(prowlSpeed) || stateTimer <= 0f) { Hide(); return; }
                    break;
            }
            if (State != TorcidoState.Attack && State != TorcidoState.Recover && CanCatchNow()) Catch();
            SetAnimator(CurrentSpeed, IsChasing, State == TorcidoState.Search, dt);
        }

        void StartProwl()
        {
            State = TorcidoState.Prowl;
            var candidates = new System.Collections.Generic.List<Room>();
            Room cur = level.RoomAt(transform.position);
            foreach (var r in level.rooms) if (r != cur && !(r.isSafe && RefugeProtects)) candidates.Add(r);
            if (candidates.Count == 0) { BeginSearch(3f); return; }
            SetGoal(candidates[Random.Range(0, candidates.Count)].Center + RandomOffset(1f));
            if (goalUnreachable) BeginSearch(2f);
        }

        void GoInvestigate(Vector3 p)
        {
            State = TorcidoState.Search;
            SetGoal(p);
            if (!goalUnreachable) { State = TorcidoState.Distracted; stateTimer = 1.5f; }
        }

        void BeginSearch(float s) { State = TorcidoState.Search; stateTimer = s; path.Clear(); }

        void BeginWindup()
        {
            State = TorcidoState.Windup;
            stateTimer = windupTime;
            Face(player.transform.position, true);
            lungeDir = transform.forward;
            Trigger("Awaken", pAwaken);
            SoundBank.PlayAt("scream", transform.position + Vector3.up * 1.5f, 0.9f, 30f, 1.5f, "[chillido: va a embestir]");
        }

        void BeginLunge()
        {
            // La dirección se fija al final de la anticipación: si ya te apartaste o rompiste la línea, falla.
            State = TorcidoState.Lunge;
            lungeTravelled = 0f;
            lungeCount++;
        }

        void UpdateLunge(float dt)
        {
            Vector3 before = transform.position;
            Vector3 move = lungeDir * lungeSpeed * dt;
            move.y = GravityStep();
            cc.Move(move);
            float moved = Flat(before, transform.position);
            CurrentSpeed = moved / dt;
            lungeTravelled += moved;
            if (CanCatchNow()) { Catch(); return; }
            bool blocked = moved < lungeSpeed * dt * 0.3f;
            if (lungeTravelled >= lungeDistance || blocked)
            {
                State = TorcidoState.Recover;
                stateTimer = recoverTime + (blocked ? 0.8f : 0f);   // si choca contra una pared queda más aturdido
                SoundBank.PlayAt("bang", transform.position + Vector3.up, blocked ? 0.9f : 0.4f, 20f, 0.8f, blocked ? "[golpe contra la pared]" : null);
            }
        }

        void OnNoise(NoiseEvent e)
        {
            if (!IsHunting || level == null || IsChasing) return;
            if (e.fromPlayer && PlayerSheltered) return;
            float eff = e.radius * 0.6f * level.NoiseAttenuation(e.position, transform.position) * hearingMultiplier;
            if (Flat(transform.position, e.position) > eff && !e.lure) return;
            if (e.lure && Flat(transform.position, e.position) > e.radius * 1.2f) return;
            GoInvestigate(e.position);
            if (e.lure) stateTimer = distractTime;
        }

        void Catch()
        {
            if (State == TorcidoState.Attack) return;
            State = TorcidoState.Attack;
            path.Clear();
            Trigger("Attack", pAttack);
            if (!attackReported) { attackReported = true; if (Director != null) Director.OnPlayerCaught(this); }
        }

        protected override GameObject BuildPlaceholder(Transform parent)
        {
            var root = new GameObject("Placeholder");
            root.transform.SetParent(parent, false);
            var t = root.transform;
            var flesh = MaterialLibrary.Get("m_torcido_flesh", new Color(0.35f, 0.22f, 0.2f), null, 0.4f);
            Part(PrimitiveType.Capsule, "Cuerpo", t, new Vector3(0f, 0.8f, 0.1f), new Vector3(0.6f, 0.55f, 0.5f), flesh, new Vector3(55f, 0f, 12f));
            Part(PrimitiveType.Sphere, "Cabeza", t, new Vector3(0.12f, 1.05f, 0.55f), new Vector3(0.3f, 0.26f, 0.34f), flesh, Vector3.zero);
            Part(PrimitiveType.Capsule, "BrazoLargo", t, new Vector3(-0.35f, 0.55f, 0.4f), new Vector3(0.12f, 0.6f, 0.12f), flesh, new Vector3(30f, 0f, -20f));
            Part(PrimitiveType.Capsule, "BrazoCorto", t, new Vector3(0.35f, 0.65f, 0.3f), new Vector3(0.1f, 0.35f, 0.1f), flesh, new Vector3(60f, 0f, 25f));
            return root;
        }
    }
}
