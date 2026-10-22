using EntityStates;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
using UnityEngine.Networking;
using V1Mod.Modules;
using V1Mod.Survivors.V1.SkillStates;

namespace V1Mod.Survivors.V1.Weapons
{
    // ULTRAKILL arms (Punch.cs, FistControl.cs, HookArm.cs, WeaponCharges.punchStamina):
    // - F punches, G swaps the arm. Arm stamina 2 (regenerates at 1.25/s).
    // - Feedbacker (blue): 1 damage, costs 1 stamina, 0.25 s pause. Parries enemy projectiles back:
    //   the projectile becomes yours and flies where you look with double damage; a parry heals.
    // - Knuckleblaster (red): 2.5 damage, costs 1.5 stamina, 0.375 s pause; holding F releases after 0.42 s
    //   a blast wave (an explosion for 1 damage with radius 6).
    // - Whiplash (R, the "special" skill): the hook flies at 250 m/s up to 300 m. A light enemy is pulled to you (60 m/s),
    //   a heavy one pulls you to it. Release R and the hook returns. 0.5 s pause.
    public class V1Arms : MonoBehaviour
    {
        public const float ActiveStart = 0.0376f;
        public const float BlastCheckTime = 0.416f;
        public int arm = V1ViewModel.ArmFeedbacker;
        public float stamina = 2f;
        private float fistCooldown;
        private float punchTimer = -1f;
        private bool blastPending;
        private CharacterBody body;

        public static V1Arms Get(GameObject go)
        {
            V1Arms a = go.GetComponent<V1Arms>();
            return a ? a : go.AddComponent<V1Arms>();
        }

        private void Awake()
        {
            body = GetComponent<CharacterBody>();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (stamina < 2f)
            {
                stamina = Mathf.MoveTowards(stamina, 2f, dt * 1.25f);
            }
            if (fistCooldown > 0f)
            {
                fistCooldown = Mathf.MoveTowards(fistCooldown, 0f, dt * 2f);
            }
            if (!V1Input.KeysAllowed(body))
            {
                return;
            }
            // the Whiplash key from the config (besides the RoR2 special skill)
            if (V1Config.Down(V1Config.Whiplash.Value) && body.skillLocator && body.skillLocator.special)
            {
                body.skillLocator.special.ExecuteIfReady();
            }
            if (V1Config.Down(V1Config.SwapArm.Value))
            {
                arm = arm == V1ViewModel.ArmFeedbacker ? V1ViewModel.ArmKnuckleblaster : V1ViewModel.ArmFeedbacker;
                V1ViewModel.Get(gameObject)?.SetArm(arm);
                V1Sounds.Play("Footstep2", gameObject, 0.3f, 1.4f, true);
            }
            if (V1Config.Down(V1Config.Punch.Value) && fistCooldown <= 0f && stamina >= 1f)
            {
                PunchStart();
            }
            if (punchTimer >= 0f)
            {
                float before = punchTimer;
                punchTimer += dt;
                if (before < ActiveStart && punchTimer >= ActiveStart)
                {
                    Hit();
                }
                if (blastPending && punchTimer >= BlastCheckTime)
                {
                    blastPending = false;
                    if (V1Config.Held(V1Config.Punch.Value))
                    {
                        Blast();
                    }
                }
                if (punchTimer > 0.75f)
                {
                    punchTimer = -1f;
                }
            }
        }

        private void PunchStart()
        {
            bool heavy = arm == V1ViewModel.ArmKnuckleblaster;
            float cost = heavy ? 3f : 2f;
            fistCooldown = cost * 0.25f;
            stamina -= cost / 2f;
            punchTimer = 0f;
            blastPending = heavy;
            V1ViewModel vm = V1ViewModel.Get(gameObject);
            vm?.ArmTrigger("Punch");
            V1Sounds.Play(heavy ? "PunchHeavy" : "PunchSwoosh", gameObject, 0.6f, Random.Range(0.9f, 1.1f), true);
        }

        private void Hit()
        {
            InputBankTest input = body.inputBank;
            Ray aim = new Ray(input.aimOrigin, input.aimDirection);
            bool heavy = arm == V1ViewModel.ArmKnuckleblaster;
            if (!heavy && TryParry(aim))
            {
                return;
            }
            // your own saw or S.R.S. cannonball in front of the fist (Punch: Chainsaw, Cannonball)
            // a Feedbacker punch right after a shotgun shot parries a pellet (shotgun parry)
            if (!heavy && ShotgunParry.TryBoost(body, aim))
            {
                return;
            }
            if (TryPunchOwnProjectile(aim, heavy))
            {
                return;
            }
            // Feedbacker on a coin: the coin flies at an enemy and a new one appears in its place (Coin.Punchflection)
            if (!heavy && V1Coin.TryPunch(body, aim))
            {
                V1Sounds.Play("PunchHit", gameObject, 0.6f, 1.3f, true);
                return;
            }
            if (Physics.SphereCast(aim, 1f, out RaycastHit h, 4f, LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Collide))
            {
                HurtBox box = h.collider.GetComponent<HurtBox>();
                if (box && V1Damage.IsEnemy(body, box.healthComponent))
                {
                    V1Damage.Deal(body, box, heavy ? 2.5f : 1f, h.point == Vector3.zero ? box.transform.position : h.point, aim.direction * (heavy ? 3000f : 1500f), source: DamageSource.Special);
                    V1Sounds.Play(heavy ? "PunchHitHeavy" : "PunchHit", gameObject, 0.6f, Random.Range(0.9f, 1.1f), true);
                    V1HitStop.HitStop(0.1f);
                }
            }
        }

        // the point projectiles are parried toward (Punch.GetParryLookTarget): where the player looks
        private static Vector3 ParryTarget(Ray aim)
        {
            if (Physics.Raycast(aim, out RaycastHit h, 1000f, LayerIndex.world.mask | LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Ignore))
            {
                return h.point;
            }
            return aim.origin + aim.direction * 1000f;
        }

        // Punching your own saw: it breaks off the chain and flies as a disc at 105 m/s where you look.
        // A Feedbacker punch on an S.R.S. cannonball: the ball flies straight at 150 m/s (Cannonball.Launch).
        private bool TryPunchOwnProjectile(Ray aim, bool heavy)
        {
            Vector3 center = aim.origin + aim.direction * 2f;
            Vector3 target = ParryTarget(aim);
            foreach (V1Chainsaw saw in V1Chainsaw.active.ToArray())
            {
                if (saw && saw.body == body && Vector3.Distance(saw.transform.position, center) < 2.5f)
                {
                    saw.transform.position = aim.origin + aim.direction;
                    saw.TurnIntoSawblade(target - saw.transform.position);
                    // Punch: hitting the saw restores arm stamina
                    stamina = 2f;
                    V1Sounds.Play("Parry", gameObject, 0.6f, 1.2f, true);
                    V1Style.Add(body, 75, "SERVE");
                    V1HitStop.HitStop(0.1f);
                    return true;
                }
            }
            if (!heavy)
            {
                foreach (IV1Explosive e in V1Explosives.all.ToArray())
                {
                    if (e is V1Cannonball ball && ball && ball.body == body && Vector3.Distance(ball.transform.position, center) < 2.5f)
                    {
                        ball.Launch(target - ball.transform.position);
                        V1Sounds.Play("Parry", gameObject, 0.6f, 1f, true);
                        V1Style.Add(body, 50, "CANNONBOOST");
                        V1HitStop.HitStop(0.1f);
                        return true;
                    }
                }
            }
            return false;
        }

        // Parry: an enemy projectile in front of your face becomes yours and flies where you look with double damage
        private bool TryParry(Ray aim)
        {
            TeamIndex team = body.teamComponent ? body.teamComponent.teamIndex : TeamIndex.Player;
            Vector3 center = aim.origin + aim.direction * 2f;
            foreach (Collider c in Physics.OverlapSphere(center, 2.5f, LayerIndex.projectile.mask, QueryTriggerInteraction.Collide))
            {
                ProjectileController pc = c.GetComponentInParent<ProjectileController>();
                if (!pc || (pc.teamFilter && pc.teamFilter.teamIndex == team))
                {
                    continue;
                }
                if (NetworkServer.active)
                {
                    Vector3 target = aim.origin + aim.direction * 1000f;
                    if (Physics.Raycast(aim, out RaycastHit wall, 1000f, LayerIndex.world.mask | LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Ignore))
                    {
                        target = wall.point;
                    }
                    pc.owner = body.gameObject;
                    if (pc.teamFilter)
                    {
                        pc.teamFilter.teamIndex = team;
                    }
                    ProjectileDamage pd = pc.GetComponent<ProjectileDamage>();
                    if (pd)
                    {
                        pd.damage *= 2f;
                    }
                    ProjectileTargetComponent ptc = pc.GetComponent<ProjectileTargetComponent>();
                    if (ptc)
                    {
                        ptc.target = null;
                    }
                    Vector3 dir = (target - pc.transform.position).normalized;
                    pc.transform.rotation = Quaternion.LookRotation(dir);
                    Rigidbody rb = pc.GetComponent<Rigidbody>();
                    ProjectileSimple ps = pc.GetComponent<ProjectileSimple>();
                    float speed = ps ? ps.desiredForwardSpeed : (rb ? rb.velocity.magnitude : 50f);
                    if (rb)
                    {
                        rb.velocity = dir * Mathf.Max(speed, 50f);
                    }
                    // NewMovement.Parry: health to full
                    body.healthComponent.HealFraction(1f, default);
                }
                V1Sounds.Play("Parry", gameObject, 0.8f, 1f, true);
                V1Style.Add(body, 100, "PARRY");
                V1HitStop.HitStop(0.25f);
                return true;
            }
            return false;
        }

        private void Blast()
        {
            InputBankTest input = body.inputBank;
            Ray aim = new Ray(input.aimOrigin, input.aimDirection);
            Vector3 pos = aim.origin + aim.direction * 2f;
            if (Physics.Raycast(aim, out RaycastHit h, 2f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
            {
                pos = h.point - aim.direction * 0.1f;
            }
            V1ViewModel.Get(gameObject)?.ArmTrigger("PunchBlast");
            // the Knuckleblaster wave hits only enemies (canHit: EnemiesOnly)
            RocketLauncher.Explode(body, pos, 1f, 12f, V1Assets.blastWave, 1f, 0f, push: 1.5f, source: DamageSource.Special);
            V1Sounds.Play("BlastWave", gameObject, 0.8f, 1f, true);
        }
    }

    // R: Whiplash
    public class WhiplashThrow : BaseSkillState
    {
        private enum HookState { Throwing, Pulling, Returning }

        private HookState state;
        private Vector3 hookPoint;
        private Vector3 direction;
        private HurtBox caught;
        private bool lightTarget;
        private LineRenderer line;
        private GameObject lineObject;
        private SoundHandle pullLoop;
        private V1MovementData move;

        // the hook comes out of the left hand: for the local player from the hand of the on-screen Whiplash arm, for others slightly below and left of the eyes
        private Vector3 Hand
        {
            get
            {
                Vector3? vm = isAuthority ? V1ViewModel.Get(gameObject)?.WhipHand : null;
                if (vm.HasValue)
                {
                    return vm.Value;
                }
                Vector3 aim = inputBank.aimDirection;
                return characterBody.aimOrigin + aim * 0.5f + Vector3.down * 0.25f + Vector3.Cross(Vector3.up, aim).normalized * -0.3f;
            }
        }

        // the hook holds while the RoR2 special skill key or the Whiplash key from the config is held
        private bool HookHeld => (inputBank && inputBank.skill4.down) || V1Config.Held(V1Config.Whiplash.Value);

        public override void OnEnter()
        {
            base.OnEnter();
            move = V1MovementData.Get(gameObject);
            Ray aim = GetAimRay();
            direction = aim.direction;
            hookPoint = Hand;
            state = HookState.Throwing;
            lineObject = new GameObject("V1Whiplash");
            line = lineObject.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.widthMultiplier = 0.06f;
            V1ViewModel vmWhip = V1ViewModel.Get(gameObject);
            if (vmWhip && vmWhip.WhipWireMaterial)
            {
                line.sharedMaterial = vmWhip.WhipWireMaterial;
                line.textureMode = LineTextureMode.Tile;
            }
            else
            {
                line.material = new Material(Shader.Find("Sprites/Default"));
            }
            vmWhip?.SetWhip(true, "Throw");
            line.startColor = new Color(0.35f, 0.35f, 0.38f);
            line.endColor = new Color(0.55f, 0.55f, 0.6f);
            V1Sounds.Play("HookArmThrow", gameObject, 0.6f, Random.Range(0.9f, 1.1f), isAuthority);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            float dt = Time.fixedDeltaTime;
            Vector3 hand = Hand;
            switch (state)
            {
                case HookState.Throwing:
                {
                    float step = 250f * dt;
                    int mask = LayerIndex.world.mask | LayerIndex.entityPrecise.mask;
                    if (V1Physics.Cast(characterBody, hookPoint, 0.25f, direction, step, out RaycastHit h, out _))
                    {
                        HurtBox box = h.collider.GetComponent<HurtBox>();
                        if (box && V1Damage.IsEnemy(characterBody, box.healthComponent))
                        {
                            caught = box;
                            hookPoint = h.point;
                            CharacterBody target = box.healthComponent.body;
                            // a light enemy is small and not a boss
                            lightTarget = target && !target.isChampion && !target.isBoss && (target.characterMotor ? target.characterMotor.mass < 250f : target.rigidbody && target.rigidbody.mass < 250f);
                            state = HookState.Pulling;
                            V1ViewModel.Get(gameObject)?.SetWhip(true, "Pull");
                            V1Sounds.Play("HookArmHit", gameObject, 0.5f, 1f, isAuthority);
                            V1Sounds.Play("HookArmPull", gameObject, 0.6f, 1f, isAuthority);
                            break;
                        }
                        if (!box && !h.collider.isTrigger)
                        {
                            hookPoint = h.point;
                            V1Sounds.Play("HookArmClink", gameObject, 0.5f, Random.Range(0.9f, 1.1f), isAuthority);
                            state = HookState.Returning;
            V1ViewModel.Get(gameObject)?.SetWhip(true, "Catch");
                            break;
                        }
                    }
                    hookPoint += direction * step;
                    if (Vector3.Distance(hand, hookPoint) > 300f || (isAuthority && !HookHeld && fixedAge > 0.1f))
                    {
                        state = HookState.Returning;
            V1ViewModel.Get(gameObject)?.SetWhip(true, "Catch");
                    }
                    break;
                }
                case HookState.Pulling:
                {
                    if (!caught || !caught.healthComponent || !caught.healthComponent.alive || (isAuthority && !HookHeld))
                    {
                        StopPull();
                        break;
                    }
                    hookPoint = caught.transform.position;
                    Vector3 toPlayer = transform.position - hookPoint;
                    if (lightTarget)
                    {
                        // the enemy is pulled to the player and stops right in front of them (doesn't fly past)
                        CharacterBody target = caught.healthComponent.body;
                        float stopDistance = characterBody.radius + (target ? target.radius : 1f) + 1f;
                        float left = toPlayer.magnitude - stopDistance;
                        if (left <= 0.05f)
                        {
                            if (NetworkServer.active && target)
                            {
                                if (target.characterMotor) target.characterMotor.velocity = Vector3.zero;
                                else if (target.rigidbody) target.rigidbody.velocity = Vector3.zero;
                            }
                            StopPull();
                            break;
                        }
                        if (NetworkServer.active && target)
                        {
                            Vector3 v = toPlayer.normalized * Mathf.Min(60f, left / dt);
                            if (target.characterMotor)
                            {
                                target.characterMotor.Motor.ForceUnground();
                                target.characterMotor.velocity = v;
                            }
                            else if (target.rigidbody)
                            {
                                target.rigidbody.velocity = v;
                            }
                        }
                    }
                    else if (isAuthority)
                    {
                        // the player is pulled to the enemy
                        if (toPlayer.magnitude < 3f)
                        {
                            StopPull();
                            break;
                        }
                        move.hookPull = -toPlayer.normalized * 60f;
                    }
                    break;
                }
                case HookState.Returning:
                {
                    float distance = Vector3.Distance(hand, hookPoint);
                    float step = dt * (100f + distance / 2f);
                    hookPoint = Vector3.MoveTowards(hookPoint, hand, step);
                    if (Vector3.Distance(hand, hookPoint) < 0.5f && isAuthority)
                    {
                        outer.SetNextStateToMain();
                    }
                    break;
                }
            }
            if (line)
            {
                line.SetPosition(0, hand);
                line.SetPosition(1, hookPoint);
            }
        }

        private void StopPull()
        {
            state = HookState.Returning;
            V1ViewModel.Get(gameObject)?.SetWhip(true, "Catch");
            if (move && move.hookPull.HasValue)
            {
                // after the pull the speed is capped at 30 (HookArm.StopThrow)
                characterMotor.velocity = Vector3.ClampMagnitude(move.hookPull.Value, 30f);
                if (characterMotor.velocity.y < 1f)
                {
                    characterMotor.velocity = new Vector3(characterMotor.velocity.x, 1f, characterMotor.velocity.z);
                }
            }
            if (move)
            {
                move.hookPull = null;
            }
            caught = null;
        }

        public override void OnExit()
        {
            if (move)
            {
                move.hookPull = null;
            }
            pullLoop?.Stop();
            V1ViewModel.Get(gameObject)?.SetWhip(false);
            if (lineObject)
            {
                Object.Destroy(lineObject);
            }
            // the 0.5 s pause between throws is set by the skill cooldown
            base.OnExit();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Pain;
    }
}
