using UnityEngine;

namespace SomethingIsWrong
{
    public enum CiegoState { Dormant, Emerging, Patrol, Investigate, Chase, Search, CheckHide, Attack, Retreat, Watch }

    /// <summary>
    /// PISO 1 — EL CIEGO: humanoide alto, pálido y delgado. Localiza al jugador POR EL SONIDO.
    ///
    /// Reglas (documentadas y comprobables en el Inspector):
    /// 1. OÍDO (regla principal). Escucha los NoiseEvent: pasos (agachado 1.5 m · caminar 5 m · correr 12 m), puertas,
    ///    radio, flash, botellas. El radio efectivo se atenúa por paredes y puertas (LevelRoot.NoiseAttenuation:
    ///    puerta abierta ×0.75, cerrada ×0.45, sin conexión ×0.25) y se multiplica por "hearingMultiplier".
    /// 2. PROXIMIDAD (regla separada, NO es vista): percibe respiración y vibraciones a muy corta distancia y solo
    ///    sin pared de por medio. Radios por defecto: quieto y agachado 0 m (indetectable), quieto de pie 0.9 m,
    ///    caminando agachado 1.3 m, caminando 2.0 m. Un jugador silencioso NUNCA es detectado por tenerlo delante.
    /// 3. AVISO PREVIO: antes de lanzarse se detiene 0.5–0.7 s y gira la cabeza hacia el sonido.
    /// 4. PIERDE EL RASTRO tras "loseTrackSeconds" sin oír ni percibir al jugador.
    /// 5. ESCONDITES: solo los revisa si oyó entrar/salir a alguien cerca o si se usan mucho (≥2 veces).
    /// 6. REFUGIO: no entra, no oye ni atrapa a quien está dentro mientras el refugio proteja.
    /// 7. CAPTURA: contacto físico (≤ catchDistance, sin pared de por medio) mientras te PERSIGUE y pasada su pausa de aviso.
    ///    Si choca contigo mientras busca o patrulla, eso te delata: se para 0,5 s y empieza a perseguirte (puedes huir).
    /// </summary>
    public class ElCiegoController : MonsterBase
    {
        public CiegoState State { get; private set; }

        [Header("Velocidades (m/s)")]
        public float patrolSpeed = 1.4f;
        public float investigateSpeed = 2.5f;
        public float chaseSpeed = 3.6f;
        public float retreatSpeed = 2.2f;

        [Header("Aparición")]
        [Tooltip("Durante una aparición, distancia a la que nota al jugador (sin pared de por medio) y se lanza.")]
        public float watchTriggerDistance = 3f;

        [Header("Oído y rastro")]
        [Tooltip("Segundos sin detectar al jugador para perder su rastro durante una persecución.")]
        public float loseTrackSeconds = 7f;
        [Tooltip("Pausa de aviso antes de lanzarse a perseguir.")]
        public float chaseTell = 0.5f;

        [Header("Proximidad (regla separada del oído)")]
        public float proximityStillCrouched = 0f;
        public float proximityStill = 0.9f;
        public float proximityCrouchWalk = 1.3f;
        public float proximityWalk = 2.0f;

        public override bool IsActive { get { return State != CiegoState.Dormant; } }
        public override bool IsHunting
        {
            get
            {
                return State == CiegoState.Patrol || State == CiegoState.Investigate || State == CiegoState.Chase ||
                       State == CiegoState.Search || State == CiegoState.CheckHide || State == CiegoState.Watch;
            }
        }
        public override bool IsChasing { get { return State == CiegoState.Chase; } }
        public override string StateName { get { return State.ToString(); } }
        public override string ThreatLabel
        {
            get
            {
                switch (State)
                {
                    case CiegoState.Chase: return "Cacería";
                    case CiegoState.Investigate:
                    case CiegoState.Search:
                    case CiegoState.CheckHide: return "Búsqueda";
                    case CiegoState.Patrol: return "Acecho";
                    case CiegoState.Watch: return "Acercamiento";
                    case CiegoState.Emerging: return "Despertar";
                }
                return "Activo";
            }
        }
        public override string AwakeningHint
        {
            get { return monsterName + " está despierto y no se irá. No ve: te OYE. Agáchate, quédate quieto, rompe el contacto o ve al REFUGIO."; }
        }

        float stateTimer, stepAcc, searchSpin, heardHideTime;
        bool decided, attackReported;
        bool hasRetreatAnimation;
        float lastDetectTime, pauseUntil;
        HideSpot checkSpot, heardHide;
        AudioSource breath;


        protected override string CanonicalName { get { return "El Ciego"; } }

        protected override void OnSetup()
        {
            if (anim != null)
                foreach (var p in anim.parameters)
                    if (p.name == "Retreating" && p.type == AnimatorControllerParameterType.Bool) hasRetreatAnimation = true;
            breath = SoundBank.AddLoop(gameObject, "breath", 0.9f, 14f, true);
            Hide();
        }

        void OnEnable() { NoiseSystem.OnNoise += OnNoise; }
        void OnDisable() { NoiseSystem.OnNoise -= OnNoise; }

        // ------------------------------------------------------------ ciclo de vida
        public override bool IsAppearing { get { return State == CiegoState.Watch; } }

        /// <summary>
        /// Aparición: quieto, de cara al jugador, sin perseguir. Si el jugador se acerca a menos de "watchTriggerDistance"
        /// sin pared de por medio, o hace ruido que le llega, pasa a buscar/perseguir con las reglas normales.
        /// Al acabar el tiempo se retira caminando y desaparece.
        /// </summary>
        public override void Appear(Vector3 pos, Vector3 lookAt, float seconds)
        {
            Teleport(pos);
            SetVisible(true);
            Face(lookAt, true);
            State = CiegoState.Watch;
            stateTimer = seconds;
            attackReported = false;
            path.Clear();
            if (breath != null) { breath.pitch = 0.9f; breath.Play(); }
        }

        public override void Hunt(Vector3 spawn, Vector3 target)
        {
            if (State == CiegoState.Dormant) { Awaken(spawn); return; }
            if (State == CiegoState.Watch || State == CiegoState.Retreat) StartInvestigate(DoorwayTowardPlayer());
        }

        public override void Hide()
        {
            bool was = State != CiegoState.Dormant;
            State = CiegoState.Dormant;
            SetVisible(false);
            if (breath != null) breath.Stop();
            if (cc != null) cc.enabled = false;
            path.Clear();
            CurrentSpeed = 0f;
            if (was) RaiseEvent("oculto");
        }

        public override void Awaken(Vector3 pos)
        {
            Teleport(pos);   // aparición inicial al despertar (fuera de la vista del jugador: GameDirector elige la habitación más lejana)
            SetVisible(true);
            Face(player.transform.position, true);
            State = CiegoState.Emerging;
            stateTimer = 3.2f;
            attackReported = false;
            Trigger("Awaken", pAwaken);
            SoundBank.PlayAt("scream", pos + Vector3.up * 2f, 1f, 40f, 1f, "[alarido]");
            if (breath != null) { breath.pitch = 1f; breath.Play(); }
        }

        public override void Retreat()
        {
            if (!IsActive || State == CiegoState.Attack) return;
            State = CiegoState.Retreat;
            stateTimer = 10f;
            RaiseEvent("retirada");
            SetGoal(FarthestRoomFromPlayer().Center);
            if (breath != null) breath.pitch = 0.9f;
        }

        public override void Pulse(Vector3 approx)
        {
            if (!IsHunting || State == CiegoState.Chase) return;
            StartInvestigate(approx);
        }

        // ------------------------------------------------------------ update
        void Update()
        {
            if (State == CiegoState.Dormant || player == null || level == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            CurrentSpeed = 0f;
            if (breath != null) breath.volume = 0.9f * GameSettings.SfxVolume; // respeta la opción de efectos

            if (Time.time < pauseUntil && State != CiegoState.Attack && State != CiegoState.Emerging)
            {
                Face(goal, false);
                IdleGravity();
                if (IsHunting) Sense();
                Animate(dt);
                return;
            }

            switch (State)
            {
                case CiegoState.Emerging:
                    stateTimer -= dt;
                    Face(player.transform.position, false);
                    IdleGravity();
                    if (stateTimer <= 0f) StartInvestigate(DoorwayTowardPlayer());
                    break;
                case CiegoState.Patrol:
                    if (FollowPath(patrolSpeed)) PickPatrol();
                    break;
                case CiegoState.Watch:
                    stateTimer -= dt;
                    Face(player.transform.position, false, 60f);
                    IdleGravity();
                    if (!player.IsHidden && !PlayerSheltered && Flat(transform.position, player.transform.position) < watchTriggerDistance && LineToPlayerClear())
                    {
                        lastDetectTime = Time.time;
                        StartChase(player.transform.position);
                        break;
                    }
                    if (stateTimer <= 0f) { Retreat(); break; }
                    break;
                case CiegoState.Investigate:
                    if (FollowPath(investigateSpeed)) ArriveInvestigate();
                    break;
                case CiegoState.Chase:
                    if (Time.time - lastDetectTime > loseTrackSeconds || PlayerSheltered)
                    {
                        BeginSearch(4f);
                        RaiseEvent("perdida");
                        HUD.Show("Ha perdido tu rastro. Muévete despacio.", 3.5f, new Color(0.75f, 0.85f, 1f));
                        break;
                    }
                    if (FollowPath(chaseSpeed)) BeginSearch(3.5f);
                    break;
                case CiegoState.Search:
                    stateTimer -= dt;
                    transform.Rotate(0f, searchSpin * dt, 0f);
                    IdleGravity();
                    if (stateTimer <= 0f) PickPatrol();
                    break;
                case CiegoState.CheckHide:
                    UpdateCheckHide(dt);
                    break;
                case CiegoState.Attack:
                    Face(player.transform.position, false);
                    IdleGravity();
                    break;
                case CiegoState.Retreat:
                    stateTimer -= dt;
                    if (FollowPath(retreatSpeed, true, HasRetreatAnimation) || stateTimer <= 0f) { Hide(); return; }
                    break;
            }

            if (IsHunting) Sense();
            Footsteps(dt);
            Animate(dt);
            if (State != CiegoState.Dormant) HUD.Caption("[respiración]", transform.position + Vector3.up * 1.8f, 7f);
        }

        /// <summary>
        /// Al despertar no va directo a tu posición: va a escuchar a la PUERTA de la habitación donde estás (por fuera).
        /// A partir de ahí solo te encuentra por ruido o proximidad. Si ya está en tu habitación, a su centro.
        /// </summary>
        Vector3 DoorwayTowardPlayer()
        {
            var tmp = new System.Collections.Generic.List<Vector3>();
            if (level.TryFindPath(transform.position, player.transform.position, !RefugeProtects, tmp) && tmp.Count >= 3)
                return tmp[tmp.Count - 3];   // punto de aproximación al otro lado de la última puerta
            Room r = level.RoomAt(player.transform.position);
            return r != null ? r.Center : player.transform.position + RandomOffset(3f);
        }

        // ------------------------------------------------------------ sentidos
        /// <summary>Radio de la regla de PROXIMIDAD según lo que hace el jugador (no es visión).</summary>
        public float ProximityRadius
        {
            get
            {
                if (player == null) return 0f;
                bool moving = player.CurrentSpeed > 0.3f;
                float r = player.IsCrouching ? (moving ? proximityCrouchWalk : proximityStillCrouched)
                                             : (moving ? proximityWalk : proximityStill);
                return r * Mathf.Lerp(1f, 1.3f, Mathf.Clamp01(hearingMultiplier - 1f));
            }
        }

        void Sense()
        {
            if (player.IsDead || player.IsHidden || PlayerSheltered) return;
            if (CanCatchNow())
            {
                // Regla de contacto: solo atrapa si ya te estaba persiguiendo (y tras su pausa de aviso).
                // Si tropieza contigo mientras busca, te delata: se detiene un instante y empieza a perseguirte.
                if (State == CiegoState.Chase && Time.time >= pauseUntil) { Catch(); return; }
                if (State != CiegoState.Chase) { lastDetectTime = Time.time; StartChase(player.transform.position); return; }
            }
            float hd = Flat(transform.position, player.transform.position);
            float r = ProximityRadius;
            if (r > 0f && hd <= r && LineToPlayerClear())
            {
                lastDetectTime = Time.time;
                if (State != CiegoState.Chase || Flat(goal, player.transform.position) > 1f)
                    StartChase(player.transform.position);
            }
        }

        void OnNoise(NoiseEvent e)
        {
            if (!IsHunting || level == null) return;
            if (!e.fromPlayer && !e.lure) return;
            if (e.fromPlayer && PlayerSheltered) return;    // el refugio amortigua todo
            float dist = Flat(transform.position, e.position);
            float eff = e.radius * level.NoiseAttenuation(e.position, transform.position) * hearingMultiplier;
            if (dist > eff) return;
            if (e.lure) { StartInvestigate(e.position); return; }   // las botellas lo desvían incluso persiguiendo
            if (e.hideSpot != null) { heardHide = e.hideSpot; heardHideTime = Time.time; }
            if (State == CiegoState.Chase || e.radius >= 9f || dist < eff * 0.5f)
            {
                lastDetectTime = Time.time;
                StartChase(e.position);
            }
            else StartInvestigate(e.position);
        }

        void Catch()
        {
            if (State == CiegoState.Attack) return;
            State = CiegoState.Attack;
            path.Clear();
            RaiseEvent("ataque");
            Trigger("Attack", pAttack);
            if (!attackReported)
            {
                attackReported = true;
                if (Director != null) Director.OnPlayerCaught(this);
            }
        }

        // ------------------------------------------------------------ estados
        void StartChase(Vector3 p)
        {
            if (State != CiegoState.Chase)
            {
                RaiseEvent("persecucion");
                pauseUntil = Time.time + chaseTell;   // aviso justo: se detiene y gira la cabeza
                lastDetectTime = Time.time;
                HUD.Caption("[respiración agitada]", transform.position, 14f);
            }
            State = CiegoState.Chase;
            SetGoal(p);
            if (goalUnreachable) { BeginSearch(3f); return; }
            if (breath != null) breath.pitch = 1.15f;
        }

        void StartInvestigate(Vector3 p)
        {
            if (State == CiegoState.Patrol || State == CiegoState.Search) pauseUntil = Time.time + 0.7f;
            State = CiegoState.Investigate;
            SetGoal(p);
            if (goalUnreachable) { BeginSearch(3f); return; }
            if (breath != null) breath.pitch = 1f;
        }

        void BeginSearch(float seconds)
        {
            State = CiegoState.Search;
            stateTimer = seconds;
            searchSpin = Random.value < 0.5f ? -70f : 70f;
            path.Clear();
        }

        void PickPatrol()
        {
            foreach (var h in level.hideSpots)
            {
                if (h != null && h.timesUsed >= 2 && Random.value < 0.35f && !(h.room != null && h.room.isSafe))
                {
                    BeginCheckHide(h);
                    return;
                }
            }
            Room current = level.RoomAt(transform.position);
            var candidates = new System.Collections.Generic.List<Room>();
            foreach (var r in level.rooms)
            {
                if (r == current) continue;
                if (r.isSafe && RefugeProtects) continue;
                candidates.Add(r);
            }
            if (candidates.Count == 0) { BeginSearch(3f); return; }
            for (int tries = 0; tries < 4; tries++)
            {
                var pick = candidates[Random.Range(0, candidates.Count)];
                State = CiegoState.Patrol;
                SetGoal(pick.Center + RandomOffset(1f));
                if (!goalUnreachable) break;
            }
            if (goalUnreachable) { BeginSearch(3f); return; }
            if (breath != null) breath.pitch = 0.95f;
        }

        void ArriveInvestigate()
        {
            HideSpot hs = null;
            float best = 2.2f;
            foreach (var h in level.hideSpots)
            {
                if (h == null || h.exitPoint == null) continue;
                // Llegar cerca de un armario no revela por sí solo a un jugador silencioso.
                bool heardRecently = heardHide == h && Time.time - heardHideTime < 8f;
                if (!heardRecently && h.timesUsed < 2) continue;
                float m = Mathf.Min(Flat(h.exitPoint.position, goal), Flat(h.transform.position, goal));
                if (m < best) { best = m; hs = h; }
            }
            if (hs != null) BeginCheckHide(hs);
            else BeginSearch(4f);
        }

        void BeginCheckHide(HideSpot hs)
        {
            State = CiegoState.CheckHide;
            checkSpot = hs;
            decided = false;
            stateTimer = 1.8f;
            SetGoal(hs.exitPoint.position);
            if (goalUnreachable) BeginSearch(2f);
        }

        void UpdateCheckHide(float dt)
        {
            if (checkSpot == null) { BeginSearch(2f); return; }
            if (pathIndex < path.Count) { FollowPath(investigateSpeed); return; }
            Face(checkSpot.transform.position, false);
            IdleGravity();
            stateTimer -= dt;
            if (!decided && stateTimer <= 0.9f)
            {
                decided = true;
                SoundBank.PlayAt("creak", checkSpot.transform.position + Vector3.up, 0.8f, 14f, 0.7f);
                if (checkSpot.occupied)
                {
                    // Regla: casi seguro si te OYÓ entrar hace poco; si no, depende de cuánto repites el escondite.
                    bool heard = heardHide == checkSpot && Time.time - heardHideTime < 8f;
                    float chance = heard ? 0.9f : Mathf.Clamp01(0.15f + 0.3f * (checkSpot.timesUsed - 1));
                    if (Random.value < chance) { Catch(); return; }
                }
            }
            if (stateTimer <= 0f) BeginSearch(2.5f);
        }

        void Footsteps(float dt)
        {
            if (CurrentSpeed < 0.2f) return;
            stepAcc += CurrentSpeed * dt;
            if (stepAcc >= 1.1f)
            {
                stepAcc = 0f;
                SoundBank.PlayAt("step_monster", transform.position, 0.9f, 22f, Random.Range(0.85f, 1f), "[pasos pesados]");
            }
        }

        // ------------------------------------------------------------ animación
        void Animate(float dt)
        {
            bool searching = State == CiegoState.Search || (State == CiegoState.CheckHide && pathIndex >= path.Count);
            if (HasModel)
            {
                if (HasRetreatAnimation) anim.SetBool("Retreating", State == CiegoState.Retreat);
                SetAnimator(CurrentSpeed, State == CiegoState.Chase, searching, dt);
                return;
            }
        }

        bool HasRetreatAnimation { get { return hasRetreatAnimation; } }

    }
}

