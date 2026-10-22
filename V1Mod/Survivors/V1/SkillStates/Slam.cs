using EntityStates;
using RoR2;
using UnityEngine;

namespace V1Mod.Survivors.V1.SkillStates
{
    // Ground slam from ULTRAKILL (NewMovement.TryStartSlam / GroundCheck):
    // - fall straight down at 100 m/s
    // - slamForce grows by 5 per second while falling
    // - after landing, 0.1 s for a super jump: jump power x (3 + slamForce - 1), at most x12.5;
    //   then another 0.306 s with the same multiplier
    // - a slide right after the slam goes faster (preSlideSpeed = slamForce)
    public class Slam : BaseSkillState
    {
        public const float SlamSpeed = 100f;

        private V1MovementData data;

        public override void OnEnter()
        {
            base.OnEnter();
            data = V1MovementData.Get(gameObject);
            data.heavyFall = true;
            data.stillHolding = true;
            data.slamForce = 1f;
            data.dashing = false;
            if (isAuthority && characterMotor)
            {
                characterMotor.velocity = new Vector3(0f, -SlamSpeed * data.scale, 0f);
            }
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!isAuthority || !characterMotor)
            {
                return;
            }
            V1MovementData.PerformSkillInputs(skillLocator, inputBank);
            if (data.launched)
            {
                // an explosion launch cancels the ground slam
                data.launched = false;
                outer.SetNextStateToMain();
                return;
            }

            float dt = Time.fixedDeltaTime;
            data.Tick(dt, characterMotor.isGrounded);
            data.slamForce += dt * 5f;
            data.TrackPreSlide(characterMotor.velocity, true, dt);
            if (!inputBank.rawSprintInput.down)
            {
                data.stillHolding = false;
            }

            if (characterMotor.isGrounded)
            {
                Land();
                return;
            }

            characterMotor.velocity = new Vector3(0f, -SlamSpeed * data.scale, 0f);
        }

        private void Land()
        {
            data.heavyFall = false;
            data.superJumpChance = 0.1f;
            if (data.stillHolding)
            {
                KnockUp(characterBody, characterBody.footPosition, 10f + data.slamForce, data.slamForce);
                V1SlamShockwave.Spawn(characterBody.footPosition);
            }
            // in ULTRAKILL the ground slam deals almost no damage; for RoR2 there is an explosion on landing
            // (as in the other V1 mod): damage 200% + 25% per slamForce unit, radius 10 + slamForce, stun
            new BlastAttack
            {
                attacker = gameObject,
                inflictor = gameObject,
                teamIndex = teamComponent ? teamComponent.teamIndex : TeamIndex.Player,
                baseDamage = (2f + 0.25f * data.slamForce) * damageStat,
                baseForce = 2500f,
                position = characterBody.footPosition,
                radius = 10f + data.slamForce,
                falloffModel = BlastAttack.FalloffModel.SweetSpot,
                crit = RollCrit(),
                procCoefficient = 0.8f,
                damageType = Weapons.V1Damage.Combo(DamageType.Stun1s, DamageSource.Utility),
                attackerFiltering = AttackerFiltering.NeverHitSelf,
            }.Fire();
            // heavy landing dust is V1MovementFx.HeavyLanding (as in ULTRAKILL); the RoR2 effect was removed
            outer.SetNextStateToMain();
        }

        // GroundCheck: if the slam button is held on landing, a shockwave (PhysicalShockwavePlayer, force 5000 x slamForce x 2.25,
        // x2 upwards on enemies, Enemy.KnockBack: /100 and another /10 = speed 22.5 x slamForce with ULTRAKILL gravity 40).
        // RoR2 gravity is 30: convert the speed to the same height, regardless of weight. Flyers and bosses don't move,
        // launched enemies take no fall damage.
        public static void KnockUp(CharacterBody attacker, Vector3 pos, float radius, float slamForce)
        {
            if (!UnityEngine.Networking.NetworkServer.active)
            {
                return;
            }
            // with the formula above enemies flew tens of meters (in ULTRAKILL Rigidbody and brakes slow them down);
            // tuned by eye: 4 m for a normal slam, up to 9 m after a long fall
            float height = Mathf.Clamp(2.5f + 1.5f * slamForce, 3f, 9f);
            float g = Mathf.Abs(Physics.gravity.y);
            float speed = Mathf.Sqrt(2f * g * height);
            TeamIndex team = attacker.teamComponent ? attacker.teamComponent.teamIndex : TeamIndex.Player;
            var done = new System.Collections.Generic.HashSet<CharacterBody>();
            foreach (Collider c in Physics.OverlapSphere(pos, radius, LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Collide))
            {
                HurtBox box = c.GetComponent<HurtBox>();
                CharacterBody b = box && box.healthComponent ? box.healthComponent.body : null;
                if (!b || !done.Add(b) || b == attacker || (b.teamComponent && b.teamComponent.teamIndex == team))
                {
                    continue;
                }
                CharacterMotor m = b.characterMotor;
                if (!m || m.isFlying || b.isBoss || b.isChampion)
                {
                    continue;
                }
                m.Motor.ForceUnground();
                m.velocity = new Vector3(m.velocity.x, speed, m.velocity.z);
                if ((b.bodyFlags & CharacterBody.BodyFlags.IgnoreFallDamage) == 0)
                {
                    b.bodyFlags |= CharacterBody.BodyFlags.IgnoreFallDamage;
                    b.gameObject.AddComponent<V1NoFallDamage>();
                }
            }
        }

        public override void OnExit()
        {
            data.heavyFall = false;
            base.OnExit();
        }

        public override InterruptPriority GetMinimumInterruptPriority()
        {
            return InterruptPriority.Skill;
        }
    }
}

namespace V1Mod.Survivors.V1.SkillStates
{
    // an enemy launched by the slam takes no fall damage: the flag is removed after landing
    public class V1NoFallDamage : MonoBehaviour
    {
        private float age;

        private void FixedUpdate()
        {
            age += Time.fixedDeltaTime;
            CharacterBody b = GetComponent<CharacterBody>();
            if (!b || !b.characterMotor)
            {
                Destroy(this);
                return;
            }
            if ((age > 0.5f && b.characterMotor.isGrounded) || age > 10f)
            {
                b.bodyFlags &= ~CharacterBody.BodyFlags.IgnoreFallDamage;
                Destroy(this);
            }
        }
    }
}

namespace V1Mod.Survivors.V1.SkillStates
{
    // Ground slam shockwave (PhysicalShockwave): the ring grows along the ground at 75 per second
    // up to size 12, then fades quickly (ScaleNFade, fadeSpeed = speed / 10).
    public class V1SlamShockwave : MonoBehaviour
    {
        public const float Speed = 75f;
        public const float MaxSize = 12f;
        private bool fading;
        private float alpha = 1f;
        private Renderer[] renderers;

        public static void Spawn(Vector3 pos)
        {
            if (!V1Mod.Modules.V1Assets.slamShockwave)
            {
                return;
            }
            GameObject go = Object.Instantiate(V1Mod.Modules.V1Assets.slamShockwave, pos, Quaternion.identity);
            go.AddComponent<V1SlamShockwave>();
            Object.Destroy(go, 3f);
        }

        private void Start()
        {
            renderers = GetComponentsInChildren<Renderer>();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            Vector3 s = transform.localScale;
            transform.localScale = new Vector3(s.x + dt * Speed, s.y, s.z + dt * Speed);
            if (!fading && (transform.localScale.x > MaxSize || transform.localScale.z > MaxSize))
            {
                fading = true;
            }
            if (fading)
            {
                alpha -= dt * Speed / 10f;
                foreach (Renderer r in renderers)
                {
                    if (r && r.material.HasProperty("_Color"))
                    {
                        Color c = r.material.color;
                        c.a = Mathf.Max(0f, alpha);
                        r.material.color = c;
                    }
                }
                if (alpha <= 0f)
                {
                    Destroy(gameObject);
                }
            }
        }
    }
}
