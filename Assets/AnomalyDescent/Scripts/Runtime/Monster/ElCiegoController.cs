using UnityEngine;

namespace SomethingIsWrong
{
    public enum CiegoState { Dormant, Emerging, Patrol, Investigate, Chase, Search, CheckHide, Attack, Retreat }

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
    /// 7. CAPTURA: contacto físico (≤ catchDistance, sin pared de por medio).
    /// </summary>
    public class ElCiegoController : MonsterBase
    {
        public CiegoState State { get; private set; }

        [Header("Velocidades (m/s)")]
        public float patrolSpeed = 1.4f;
        public float investigateSpeed = 2.5f;
        public float chaseSpeed = 3.6f;
        public float retreatSpeed = 2.2f;

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
                       State == CiegoState.Search || State == CiegoState.CheckHide;
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
                    case CiegoState.Emerging: return "Despertar";
                }
                return "Activo";
            }
        }
        public override string AwakeningHint
        {
            get { return monsterName + " está despierto y no se irá. No ve: te OYE. Agáchate, quédate quieto, rompe el contacto o ve al REFUGIO."; }
        }

        float stateTimer, stepAcc, searchSpin, animTime, heardHideTime;
        bool decided, attackReported;
        float lastDetectTime, pauseUntil;
        HideSpot checkSpot, heardHide;
        AudioSource breath;
        Transform phArmL, phArmR, phHead;

        protected override string CanonicalName { get { return "El Ciego"; } }

        protected override void OnSetup()
        {
            breath = SoundBank.AddLoop(gameObject, "breath", 0.9f, 14f, true);
            Hide();
        }

        void OnEnable() { NoiseSystem.OnNoise += OnNoise; }
        void OnDisable() { NoiseSystem.OnNoise -= OnNoise; }

        // ------------------------------------------------------------ ciclo de vida
        public override void Hide()
        {
            State = CiegoState.Dormant;
            SetVisible(false);
            if (breath != null) breath.Stop();
            if (cc != null) cc.enabled = false;
            path.Clear();
            CurrentSpeed = 0f;
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
                    if (stateTimer <= 0f) StartInvestigate(player.transform.position + RandomOffset(2.5f));
                    break;
                case CiegoState.Patrol:
                    if (FollowPath(patrolSpeed)) PickPatrol();
                    break;
                case CiegoState.Investigate:
                    if (FollowPath(investigateSpeed)) ArriveInvestigate();
                    break;
                case CiegoState.Chase:
                    if (Time.time - lastDetectTime > loseTrackSeconds || PlayerSheltered)
                    {
                        BeginSearch(4f);
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
                    if (FollowPath(retreatSpeed) || stateTimer <= 0f) { Hide(); return; }
                    break;
            }

            if (IsHunting) Sense();
            Footsteps(dt);
            Animate(dt);
            if (State != CiegoState.Dormant) HUD.Caption("[respiración]", transform.position + Vector3.up * 1.8f, 7f);
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
            if (CanCatchNow()) { Catch(); return; }
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
            if (HasModel) { SetAnimator(CurrentSpeed, State == CiegoState.Chase, searching, dt); return; }
            if (placeholder == null || phArmL == null) return;
            animTime += dt * (1f + CurrentSpeed * 1.5f);
            float sw = Mathf.Sin(animTime * 3f) * (CurrentSpeed > 0.1f ? 25f : 6f);
            if (State == CiegoState.Attack) sw = -70f;
            phArmL.localRotation = Quaternion.Euler(sw - 20f, 0f, 0f);
            phArmR.localRotation = Quaternion.Euler(-sw - 20f, 0f, 0f);
            bool look = searching || State == CiegoState.Emerging || Time.time < pauseUntil;
            phHead.localRotation = Quaternion.Euler(Mathf.Sin(animTime * 0.7f) * 8f + (look ? 15f : 0f),
                                                   Mathf.Sin(animTime * 0.5f) * (look ? 40f : 10f), 0f);
        }

        /// <summary>Figura PROVISIONAL (no es arte final): alta y delgada, con venda sobre los ojos.</summary>
        protected override GameObject BuildPlaceholder(Transform parent)
        {
            var root = new GameObject("Placeholder");
            root.transform.SetParent(parent, false);
            var t = root.transform;
            var skin = MaterialLibrary.Get("m_ciego_skin", new Color(0.62f, 0.6f, 0.55f), null, 0.3f);
            var dark = MaterialLibrary.Get("m_ciego_dark", new Color(0.12f, 0.12f, 0.11f), null, 0.2f);
            var band = MaterialLibrary.Get("m_ciego_band", new Color(0.3f, 0.04f, 0.03f), null, 0.1f);
            Part(PrimitiveType.Capsule, "Torso", t, new Vector3(0f, 1.35f, 0f), new Vector3(0.42f, 0.55f, 0.3f), skin, new Vector3(8f, 0f, 0f));
            Part(PrimitiveType.Capsule, "PiernaI", t, new Vector3(-0.11f, 0.45f, 0f), new Vector3(0.13f, 0.45f, 0.13f), dark, Vector3.zero);
            Part(PrimitiveType.Capsule, "PiernaD", t, new Vector3(0.11f, 0.45f, 0f), new Vector3(0.13f, 0.45f, 0.13f), dark, Vector3.zero);
            phHead = new GameObject("Cabeza").transform;
            phHead.SetParent(t, false);
            phHead.localPosition = new Vector3(0f, 1.9f, 0.06f);
            Part(PrimitiveType.Sphere, "Craneo", phHead, new Vector3(0f, 0.16f, 0f), new Vector3(0.3f, 0.38f, 0.32f), skin, Vector3.zero);
            Part(PrimitiveType.Cube, "Venda", phHead, new Vector3(0f, 0.2f, 0.04f), new Vector3(0.33f, 0.08f, 0.28f), band, Vector3.zero);
            phArmL = new GameObject("BrazoI").transform;
            phArmL.SetParent(t, false);
            phArmL.localPosition = new Vector3(-0.3f, 1.72f, 0f);
            Part(PrimitiveType.Capsule, "Brazo", phArmL, new Vector3(0f, -0.6f, 0.04f), new Vector3(0.09f, 0.6f, 0.09f), skin, Vector3.zero);
            phArmR = new GameObject("BrazoD").transform;
            phArmR.SetParent(t, false);
            phArmR.localPosition = new Vector3(0.3f, 1.72f, 0f);
            Part(PrimitiveType.Capsule, "Brazo", phArmR, new Vector3(0f, -0.6f, 0.04f), new Vector3(0.09f, 0.6f, 0.09f), skin, Vector3.zero);
            return root;
        }
    }
}
