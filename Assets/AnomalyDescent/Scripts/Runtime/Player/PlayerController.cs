using UnityEngine;

namespace SomethingIsWrong
{
    /// <summary>
    /// Jugador en primera persona: caminar, correr (aliento), agacharse, pasos que hacen ruido,
    /// esconderse y muerte. Las herramientas (linterna, cámara, radio) están en componentes aparte.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        public Camera Cam { get; private set; }
        public Flashlight Flashlight { get; private set; }
        public EvidenceCamera Evidence { get; private set; }
        public PlayerInteraction Interaction { get; private set; }

        [Header("Movimiento")]
        public float walkSpeed = 2.2f;
        public float runSpeed = 4.4f;
        public float crouchSpeed = 1.15f;
        public float sensitivity = 0.1f;
        /// <summary>Multiplicadores que pone la cámara de evidencia (con zoom se gira más despacio y se camina más lento).</summary>
        [System.NonSerialized] public float lookScale = 1f;
        [System.NonSerialized] public float moveScale = 1f;

        [Header("Aliento")]
        public float stamina = 100f;
        public float maxStamina = 100f;

        [System.NonSerialized] public bool inputLocked;
        public const int MaxThrowables = 3;
        [System.NonSerialized] public int throwables = 1;

        public bool IsCrouching { get; private set; }
        public bool IsRunning { get; private set; }
        public bool IsExhausted { get; private set; }
        public bool IsDead { get; private set; }
        public HideSpot CurrentHide { get; private set; }
        public bool IsHidden { get { return CurrentHide != null; } }
        public float CurrentSpeed { get; private set; }

        const float StandH = 1.75f, CrouchH = 1.15f, CamStand = 1.62f, CamCrouch = 1.0f;

        CharacterController cc;
        float yaw, pitch, vy, stepAcc, bobT, staminaDelay, camY, hideYaw, nextExhaustNoise;
        bool crouchToggle;
        Vector3 deathLook;

        public void Setup(Camera cam, float sens)
        {
            Cam = cam;
            sensitivity = sens;
            cc = GetComponent<CharacterController>();
            yaw = transform.eulerAngles.y;
            camY = CamStand;
            Flashlight = gameObject.AddComponent<Flashlight>();
            Flashlight.Setup(cam);
            Evidence = gameObject.AddComponent<EvidenceCamera>();
            Evidence.Setup(this);
            Interaction = gameObject.AddComponent<PlayerInteraction>();
            Interaction.Setup(this);
        }

        void Update()
        {
            if (cc == null || Cam == null) return;
            if (IsDead) { DeathCam(); return; }
            var d = GameDirector.Instance;
            bool playing = d != null && d.IsPlaying;
            if (IsHidden)
            {
                CurrentSpeed = 0f;
                if (playing) HiddenLook();
                return;
            }
            bool allow = playing && !inputLocked;
            if (allow) Look();
            Move(allow);
            if (allow && GameInput.Down(GKey.G)) Throw();   // el clic derecho es la cámara
        }

        // ------------------------------------------------------------ botellas (distracción)
        void Throw()
        {
            if (throwables <= 0) { HUD.Show("No tienes nada que lanzar. Busca botellas.", 2f); return; }
            throwables--;
            var go = new GameObject("BotellaLanzada");
            go.transform.position = Cam.transform.position + Cam.transform.forward * 0.45f;
            go.transform.rotation = Random.rotation;
            var col = go.AddComponent<CapsuleCollider>();
            col.radius = 0.04f;
            col.height = 0.26f;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.4f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            Physics.IgnoreCollision(col, cc);
            var mat = MaterialLibrary.Get("m_bottle", new Color(0.15f, 0.32f, 0.18f), null, 0.9f);
            BottlePart(go.transform, new Vector3(0f, -0.03f, 0f), new Vector3(0.08f, 0.09f, 0.08f), mat);
            BottlePart(go.transform, new Vector3(0f, 0.1f, 0f), new Vector3(0.03f, 0.05f, 0.03f), mat);
            go.AddComponent<Throwable>();
            rb.AddForce(Cam.transform.forward * 10f + Vector3.up * 1.5f, ForceMode.VelocityChange);
            rb.AddTorque(Random.insideUnitSphere * 10f, ForceMode.VelocityChange);
            SoundBank.Play2D("click", 0.3f, 0.6f);
            NoiseSystem.Emit(transform.position, 1.5f, true);
        }

        static void BottlePart(Transform parent, Vector3 lp, Vector3 scale, Material mat)
        {
            var p = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            p.transform.SetParent(parent, false);
            p.transform.localPosition = lp;
            p.transform.localScale = scale;
            p.GetComponent<Renderer>().sharedMaterial = mat;
            Destroy(p.GetComponent<Collider>());
        }

        /// <summary>Accesibilidad: girar con teclado (J/L horizontal, I/K vertical), 90°/s y 60°/s, también con zoom.</summary>
        Vector2 KeyLook()
        {
            float dt = Time.deltaTime;
            float x = (Input.GetKey(KeyCode.L) ? 1f : 0f) - (Input.GetKey(KeyCode.J) ? 1f : 0f);
            float y = (Input.GetKey(KeyCode.I) ? 1f : 0f) - (Input.GetKey(KeyCode.K) ? 1f : 0f);
            return new Vector2(x * 90f * dt, y * 60f * dt) * lookScale;
        }

        public float Yaw { get { return yaw; } }
        public float Pitch { get { return pitch; } }

        void Look()
        {
            Vector2 m = GameInput.MouseDelta * sensitivity * lookScale;
            if (GameSettings.InvertY) m.y = -m.y;
            m += KeyLook();
            yaw += m.x;
            pitch = Mathf.Clamp(pitch - m.y, -85f, 85f);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            Cam.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        void HiddenLook()
        {
            Vector2 m = GameInput.MouseDelta * sensitivity * lookScale;
            if (GameSettings.InvertY) m.y = -m.y;
            m += KeyLook();
            yaw = Mathf.Clamp(yaw + m.x, hideYaw - 70f, hideYaw + 70f);
            pitch = Mathf.Clamp(pitch - m.y, -35f, 35f);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            Cam.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        void Move(bool allow)
        {
            float dt = Time.deltaTime;
            Vector2 input = allow ? GameInput.Move : Vector2.zero;

            if (allow && GameInput.Down(GKey.C)) crouchToggle = !crouchToggle;
            IsCrouching = allow ? (crouchToggle || GameInput.Held(GKey.Ctrl)) : IsCrouching;

            bool wantRun = allow && GameInput.Held(GKey.Shift) && input.y > 0.1f && !IsCrouching && !IsExhausted;
            if (wantRun && stamina > 0f)
            {
                IsRunning = true;
                if (crouchToggle) crouchToggle = false;
                stamina = Mathf.Max(0f, stamina - 22f * dt);
                staminaDelay = 1.2f;
                if (stamina <= 0f)
                {
                    IsExhausted = true;
                    HUD.Show("Sin aliento. Respiras con fuerza: te pueden oír.", 3f);
                }
            }
            else
            {
                IsRunning = false;
                if (staminaDelay > 0f) staminaDelay -= dt;
                else stamina = Mathf.Min(maxStamina, stamina + 14f * dt);
                if (IsExhausted && stamina >= 30f) IsExhausted = false;
            }

            if (IsExhausted && Time.time >= nextExhaustNoise)
            {
                nextExhaustNoise = Time.time + 1.6f;
                SoundBank.Play2D("breath_player", 0.35f, 1.3f); // jadeo propio; "breath" es la grabación de El Ciego
                NoiseSystem.Emit(transform.position, 3f, true);
            }

            float speed = (IsCrouching ? crouchSpeed : (IsRunning ? runSpeed : walkSpeed)) * moveScale;
            Vector3 move = (transform.right * input.x + transform.forward * input.y) * speed;
            if (cc.isGrounded && vy < 0f) vy = -2f;
            vy -= 19.6f * dt;
            move.y = vy;

            float targetH = IsCrouching ? CrouchH : StandH;
            cc.height = Mathf.MoveTowards(cc.height, targetH, dt * 4f);
            cc.center = new Vector3(0f, cc.height * 0.5f, 0f);
            cc.Move(move * dt);

            Vector3 hv = cc.velocity;
            hv.y = 0f;
            CurrentSpeed = hv.magnitude;

            float targetCam = IsCrouching ? CamCrouch : CamStand;
            camY = Mathf.MoveTowards(camY, targetCam, dt * 3f);
            float bob = 0f;
            if (GameSettings.CameraBob && CurrentSpeed > 0.2f)
            {
                bobT += dt * CurrentSpeed * 3.2f;
                bob = Mathf.Sin(bobT) * 0.035f * (IsRunning ? 1.6f : 1f);
            }
            Cam.transform.localPosition = new Vector3(0f, camY + bob, 0f);

            if (CurrentSpeed > 0.3f && cc.isGrounded)
            {
                stepAcc += CurrentSpeed * dt;
                float stride = IsRunning ? 0.8f : (IsCrouching ? 0.55f : 0.62f);
                if (stepAcc >= stride) { stepAcc = 0f; Footstep(); }
            }
        }

        void Footstep()
        {
            float vol = IsRunning ? 0.85f : (IsCrouching ? 0.12f : 0.38f);
            float radius = IsRunning ? 12f : (IsCrouching ? 1.5f : 5f);
            SoundBank.PlayAt("step", transform.position, vol, 16f, Random.Range(0.85f, 1.15f));
            NoiseSystem.Emit(transform.position, radius, true);
        }

        // ------------------------------------------------------------ escondites
        public void EnterHide(HideSpot h)
        {
            if (IsHidden || h == null || IsDead) return;
            CurrentHide = h;
            h.occupied = true;
            h.timesUsed++;
            cc.enabled = false;
            IsCrouching = false;
            crouchToggle = false;
            camY = CamStand;
            transform.position = h.viewPoint.position - Vector3.up * CamStand;
            Cam.transform.localPosition = new Vector3(0f, CamStand, 0f);
            hideYaw = h.viewPoint.eulerAngles.y;
            yaw = hideYaw;
            pitch = 0f;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            Cam.transform.localRotation = Quaternion.identity;
            SoundBank.PlayAt("creak", h.transform.position + Vector3.up, 0.5f, 10f, 1.25f);
            NoiseSystem.Emit(h.transform.position, 4f, true, h);
            if (h.timesUsed >= 3) HUD.Show("Ya te has escondido aquí varias veces. La entidad aprende tus hábitos.", 4f);
        }

        public void ExitHide()
        {
            var h = CurrentHide;
            if (h == null) return;
            h.occupied = false;
            CurrentHide = null;
            transform.position = h.exitPoint.position;
            yaw = h.exitPoint.eulerAngles.y;
            pitch = 0f;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            Cam.transform.localRotation = Quaternion.identity;
            cc.enabled = true;
            SoundBank.PlayAt("creak", h.transform.position + Vector3.up, 0.5f, 10f, 1.15f);
            NoiseSystem.Emit(h.transform.position, 4f, true, h);
        }

        // ------------------------------------------------------------ muerte
        public void Kill(Vector3 killerPosition)
        {
            if (IsDead) return;
            IsDead = true;
            inputLocked = true;
            if (CurrentHide != null) CurrentHide.occupied = false;
            deathLook = killerPosition + Vector3.up * 2f;
        }

        void DeathCam()
        {
            Vector3 dir = deathLook - Cam.transform.position;
            if (dir.sqrMagnitude > 0.01f)
                Cam.transform.rotation = Quaternion.Slerp(Cam.transform.rotation, Quaternion.LookRotation(dir), Time.unscaledDeltaTime * 6f);
        }
    }
}

