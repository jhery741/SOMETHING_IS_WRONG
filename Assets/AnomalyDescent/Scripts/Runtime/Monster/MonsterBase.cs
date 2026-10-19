using System.Collections.Generic;
using UnityEngine;

namespace SomethingIsWrong
{
    /// <summary>
    /// Base común de los monstruos de ANOMALY: DESCENT. Solo contiene lo COMPARTIDO:
    /// · navegación por el grafo real de habitaciones y puertas (LevelRoot.FindPath) con CharacterController,
    /// · colisión de juego (cápsula) separada del modelo artístico,
    /// · instanciado del modelo (prefab de Meshy) y su Animator, configurables en el Inspector,
    /// · respeto del refugio.
    /// El COMPORTAMIENTO (percepción, estados, ataque) es propio de cada subclase:
    ///   Piso 1 → ElCiegoController · Piso 2 → ElTorcidoController · Piso 3 → LaPresenciaController.
    ///
    /// Movimiento: SIEMPRE lo pone el script (CharacterController). El Animator va sin root motion y no hay NavMeshAgent.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public abstract class MonsterBase : MonoBehaviour
    {
        [Header("Identidad")]
        public string monsterName = "Monstruo";
        /// <summary>Nombre canónico del monstruo de cada piso (El Ciego, El Torcido, La Presencia).</summary>
        protected abstract string CanonicalName { get; }

        protected virtual void Reset() { monsterName = CanonicalName; }
        protected virtual void OnValidate()
        {
            // Un monstruo nunca debe mostrarse como "Monstruo" genérico en el HUD.
            if (string.IsNullOrEmpty(monsterName) || monsterName == "Monstruo") monsterName = CanonicalName;
        }
        [Tooltip("Piso al que pertenece según el canon (1, 2 o 3).")]
        public int floor = 1;

        [Header("Visual (Meshy) — vacío = figura PROVISIONAL")]
        [Tooltip("Prefab del modelo (p. ej. Monster/Prefabs/ElCiego_Visual). Solo visual: sin colisionadores ni lógica.")]
        public GameObject visualPrefab;
        public RuntimeAnimatorController animatorController;
        [Tooltip("Altura final del modelo en metros (se escala por su caja).")]
        public float visualHeight = 2.15f;
        [Tooltip("Corrección de orientación del modelo (su frente debe mirar a +Z).")]
        public Vector3 visualRotation = Vector3.zero;

        [Header("Colisión de juego (independiente de la malla)")]
        [Tooltip("Debe ser menor que la altura libre de las puertas (2.2 m).")]
        public float capsuleHeight = 2.05f;
        public float capsuleRadius = 0.4f;

        [Header("Percepción común")]
        [Tooltip("Multiplicador de oído: sube con los errores del jugador (casa adaptativa).")]
        public float hearingMultiplier = 1f;
        [Tooltip("Distancia de captura (contacto físico).")]
        public float catchDistance = 1.2f;

        [Header("Depuración")]
        public bool drawDebug = true;

        public float CurrentSpeed { get; protected set; }
        public abstract bool IsActive { get; }
        /// <summary>Activo y buscando (no dormido, ni atacando, ni retirándose).</summary>
        public abstract bool IsHunting { get; }
        public abstract bool IsChasing { get; }
        /// <summary>Nombre del estado interno (depuración).</summary>
        public abstract string StateName { get; }
        /// <summary>Texto de amenaza para el HUD: Acecho / Búsqueda / Cacería...</summary>
        public abstract string ThreatLabel { get; }
        /// <summary>Mensaje al despertar: explica la regla principal para que el jugador pueda reaccionar.</summary>
        public abstract string AwakeningHint { get; }
        /// <summary>Consejo que se muestra la primera vez que persigue.</summary>
        public virtual string ChaseHint { get { return "Lanza una botella lejos con [G] o clic derecho para desviarlo."; } }

        protected PlayerController player;
        protected LevelRoot level;
        protected CharacterController cc;
        protected Animator anim;
        protected Transform visual;
        protected GameObject placeholder;
        protected bool pSpeed, pChasing, pSearching, pAttack, pAwaken;

        protected readonly List<Vector3> path = new List<Vector3>();
        protected int pathIndex;
        protected Vector3 goal;
        protected bool goalUnreachable;
        float vy, stuckTimer;

        // ------------------------------------------------------------ preparación
        public virtual void Setup(PlayerController p, LevelRoot l)
        {
            if (string.IsNullOrEmpty(monsterName) || monsterName == "Monstruo") monsterName = CanonicalName;
            player = p;
            level = l;
            cc = GetComponent<CharacterController>();
            cc.height = capsuleHeight;
            cc.radius = capsuleRadius;
            cc.center = new Vector3(0f, capsuleHeight * 0.5f, 0f);
            cc.stepOffset = 0.3f;
            BuildVisual();
            OnSetup();
        }

        protected virtual void OnSetup() { }

        public abstract void Awaken(Vector3 position);
        public abstract void Hide();
        public abstract void Retreat();
        /// <summary>Un error del jugador "delata" su zona aproximada (cada monstruo decide qué hacer).</summary>
        public virtual void Pulse(Vector3 approx) { }

        protected GameDirector Director { get { return GameDirector.Instance; } }
        protected bool RefugeProtects { get { var d = Director; return d != null && d.RefugeProtects; } }

        /// <summary>Jugador dentro del refugio mientras este protege: el monstruo no lo percibe ni lo atrapa.</summary>
        protected bool PlayerSheltered
        {
            get
            {
                if (!RefugeProtects || player == null || level == null) return false;
                var r = level.RoomAt(player.transform.position);
                return r != null && r.isSafe;
            }
        }

        protected static float Flat(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        protected static Vector3 RandomOffset(float r)
        {
            var c = Random.insideUnitCircle * r;
            return new Vector3(c.x, 0f, c.y);
        }

        /// <summary>¿Hay una línea sin obstáculos desde la cabeza del monstruo hasta la cámara del jugador?</summary>
        protected bool LineToPlayerClear(float eyeHeight = 1.4f)
        {
            Vector3 from = transform.position + Vector3.up * eyeHeight;
            Vector3 to = player.Cam.transform.position;
            Vector3 dir = to - from;
            float dist = dir.magnitude;
            if (dist < 0.05f) return true;
            RaycastHit hit;
            if (Physics.Raycast(from, dir / dist, out hit, dist, ~(1 << Layers.PhotoOnly), QueryTriggerInteraction.Ignore))
                return hit.collider.GetComponentInParent<PlayerController>() != null || hit.collider.GetComponentInParent<MonsterBase>() == this;
            return true;
        }

        /// <summary>Captura: contacto físico, sin paredes de por medio, nunca dentro del refugio.</summary>
        protected bool CanCatchNow()
        {
            if (player == null || player.IsDead || player.IsHidden || PlayerSheltered) return false;
            return Flat(transform.position, player.transform.position) <= catchDistance && LineToPlayerClear(1.2f);
        }

        // ------------------------------------------------------------ navegación
        protected void SetGoal(Vector3 g)
        {
            g.y = 0f;
            bool refuge = RefugeProtects;
            Room gr = level.RoomAt(g);
            if (refuge && gr != null && gr.isSafe) g = level.OutsideOfSafeRoom(gr);
            goal = g;
            path.Clear();
            goalUnreachable = !level.TryFindPath(transform.position, g, !refuge, path);
            pathIndex = 0;
            stuckTimer = 0f;
        }

        protected float GravityStep()
        {
            if (cc.isGrounded) vy = -1f;
            else vy -= 9.8f * Time.deltaTime;
            return vy * Time.deltaTime;
        }

        protected void IdleGravity()
        {
            if (cc != null && cc.enabled) cc.Move(new Vector3(0f, GravityStep(), 0f));
        }

        /// <summary>Sigue la ruta. Devuelve true al llegar (o si la ruta se agota por atasco).</summary>
        protected bool FollowPath(float speed, bool openDoors = true)
        {
            if (pathIndex >= path.Count) { IdleGravity(); return true; }
            Vector3 wp = path[pathIndex];
            Vector3 to = wp - transform.position;
            to.y = 0f;
            float dist = to.magnitude;
            float reach = pathIndex == path.Count - 1 ? 0.6f : 0.4f;
            if (dist < reach)
            {
                pathIndex++;
                return pathIndex >= path.Count;
            }
            Vector3 dir = to / dist;
            if (openDoors) OpenNearbyDoors();
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), 300f * Time.deltaTime);
            Vector3 move = dir * speed * Time.deltaTime;
            move.y = GravityStep();
            Vector3 before = transform.position;
            cc.Move(move);
            float moved = Flat(before, transform.position);
            CurrentSpeed = moved / Mathf.Max(Time.deltaTime, 1e-4f);
            if (moved < speed * Time.deltaTime * 0.2f)
            {
                stuckTimer += Time.deltaTime;
                if (stuckTimer > 1.2f)
                {
                    // Recuperación de atascos: salta al siguiente punto; si era el último, da la ruta por terminada.
                    stuckTimer = 0f;
                    pathIndex++;
                    if (pathIndex >= path.Count) return true;
                }
            }
            else stuckTimer = 0f;
            return false;
        }

        /// <summary>Abre (de golpe) las puertas cerradas que tiene delante. Respeta cerraduras y el refugio.</summary>
        protected void OpenNearbyDoors()
        {
            foreach (var link in level.doors)
            {
                if (link == null || link.leaf == null || link.vanished || link.leaf.isOpen || link.leaf.locked) continue;
                if (Flat(transform.position, link.transform.position) > 1.8f) continue;
                if (link.TouchesSafeRoom && RefugeProtects) continue;
                link.leaf.MonsterOpen();
            }
        }

        protected void Face(Vector3 target, bool instant, float degPerSec = 220f)
        {
            Vector3 dir = target - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f) return;
            var rot = Quaternion.LookRotation(dir.normalized);
            transform.rotation = instant ? rot : Quaternion.RotateTowards(transform.rotation, rot, degPerSec * Time.deltaTime);
        }

        protected Room FarthestRoomFromPlayer()
        {
            Room best = null;
            float bd = -1f;
            foreach (var r in level.rooms)
            {
                if (r.isSafe) continue;
                float d = Flat(r.Center, player.transform.position);
                if (d > bd) { bd = d; best = r; }
            }
            return best != null ? best : level.rooms[0];
        }

        protected void Teleport(Vector3 pos)
        {
            cc.enabled = false;
            transform.position = pos;
            cc.enabled = true;
            vy = 0f;
        }

        // ------------------------------------------------------------ visual / animación
        protected void Trigger(string name, bool has)
        {
            if (has && anim != null && anim.runtimeAnimatorController != null) anim.SetTrigger(name);
        }

        protected void SetAnimator(float speed, bool chasing, bool searching, float dt)
        {
            if (anim == null || anim.runtimeAnimatorController == null) return;
            if (pSpeed) anim.SetFloat("Speed", speed, 0.15f, dt);
            if (pChasing) anim.SetBool("Chasing", chasing);
            if (pSearching) anim.SetBool("Searching", searching);
        }

        protected bool HasModel { get { return anim != null; } }

        void CacheParams()
        {
            pSpeed = pChasing = pSearching = pAttack = pAwaken = false;
            if (anim == null || anim.runtimeAnimatorController == null) return;
            foreach (var p in anim.parameters)
            {
                if (p.name == "Speed" && p.type == AnimatorControllerParameterType.Float) pSpeed = true;
                if (p.name == "Chasing" && p.type == AnimatorControllerParameterType.Bool) pChasing = true;
                if (p.name == "Searching" && p.type == AnimatorControllerParameterType.Bool) pSearching = true;
                if (p.name == "Attack" && p.type == AnimatorControllerParameterType.Trigger) pAttack = true;
                if (p.name == "Awaken" && p.type == AnimatorControllerParameterType.Trigger) pAwaken = true;
            }
        }

        protected void SetVisible(bool v) { if (visual != null) visual.gameObject.SetActive(v); }

        void BuildVisual()
        {
            // Si el modelo ya está colocado a mano como hijo "Visual" en la escena, se respeta.
            visual = transform.Find("Visual");
            if (visual == null)
            {
                visual = new GameObject("Visual").transform;
                visual.SetParent(transform, false);
            }
            if (visual.childCount == 0)
            {
                if (visualPrefab == null)
                {
                    placeholder = BuildPlaceholder(visual);
                    if (placeholder != null) placeholder.name = "PROVISIONAL_" + monsterName;
                    if (Application.isEditor) Debug.Log("[" + monsterName + "] (desarrollo) sin modelo asignado: figura provisional. Asigna 'Visual Prefab' en el Inspector.");
                    return;
                }
                var m = Instantiate(visualPrefab, visual);
                m.name = "Modelo_" + visualPrefab.name;
                m.transform.localPosition = Vector3.zero;
                m.transform.localRotation = Quaternion.Euler(visualRotation);
                FitHeight(m);
            }
            foreach (var c in visual.GetComponentsInChildren<Collider>(true)) Destroy(c);
            foreach (var rb in visual.GetComponentsInChildren<Rigidbody>(true)) Destroy(rb);

            anim = visual.GetComponentInChildren<Animator>();
            if (anim == null && visual.childCount > 0) anim = visual.GetChild(0).gameObject.AddComponent<Animator>();
            if (anim != null)
            {
                if (animatorController != null) anim.runtimeAnimatorController = animatorController;
                anim.applyRootMotion = false;   // el movimiento lo pone el script
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                CacheParams();
                if (anim.runtimeAnimatorController == null)
                    Debug.LogWarning("[" + monsterName + "] El modelo no tiene Animator Controller asignado.");
                if (Application.isEditor || Debug.isDebugBuild)
                    gameObject.AddComponent<MonsterAnimProbe>().Init(this, anim);   // solo desarrollo: diagnóstico de animación
            }
        }

        void FitHeight(GameObject m)
        {
            var rends = m.GetComponentsInChildren<Renderer>(true);
            if (rends.Length == 0 || visualHeight <= 0.1f) return;
            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            if (b.size.y < 0.01f) return;
            m.transform.localScale = m.transform.localScale * (visualHeight / b.size.y);
            b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            m.transform.position += Vector3.up * (transform.position.y - b.min.y);
        }

        /// <summary>Figura TEMPORAL mientras no exista el modelo real. Cada monstruo define la suya.</summary>
        protected abstract GameObject BuildPlaceholder(Transform parent);

        protected static GameObject Part(PrimitiveType t, string name, Transform parent, Vector3 lp, Vector3 scale, Material mat, Vector3 euler)
        {
            var go = GameObject.CreatePrimitive(t);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = lp;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            Destroy(go.GetComponent<Collider>());
            return go;
        }

        protected virtual void OnDrawGizmosSelected()
        {
            if (!drawDebug) return;
            Gizmos.color = Color.red;
            for (int i = pathIndex; i < path.Count; i++)
            {
                Vector3 a = i == pathIndex ? transform.position : path[i - 1];
                Gizmos.DrawLine(a + Vector3.up * 0.1f, path[i] + Vector3.up * 0.1f);
                Gizmos.DrawWireSphere(path[i] + Vector3.up * 0.1f, 0.15f);
            }
            Gizmos.color = new Color(1f, 0.5f, 0f);
            Gizmos.DrawWireSphere(transform.position, catchDistance);
        }
    }
}
