using RoR2;
using UnityEngine;

namespace V1Mod.Survivors.V1.SkillStates
{
    // V1 movement state that lives longer than a single EntityState
    // (dash, slide, ground slam and the normal state replace each other).
    public class V1MovementData : MonoBehaviour
    {
        // movement scale: 1 = as in ULTRAKILL, 0.52 = "RoR2 balance" (alternate Shift).
        // All movement math is done in ULTRAKILL units, and speeds and gravity are multiplied by scale:
        // all distances (jump height, dash length) shrink by scale, while timings stay the same.
        public float scale = 1f;
        public const float RoR2Scale = 0.52f;

        public int currentWallJumps;
        // rocket jumps since last touching the ground and the harmless-wave pause (NewMovement.rocketJumps, safeExplosionLaunchCooldown)
        public int rocketJumps;
        public float safeLaunchCooldown;
        // an explosion launch interrupts the dash and the ground slam (NewMovement.Launch: boost = false, heavyFall = false)
        public bool launched;
        public float clingFade;
        public float jumping;          // > 0 for the first 0.25 s after a jump (NotJumping)
        public float jumpCooldown;     // JumpReady
        public float fallTime;         // grows by 5 per second in the air, as in ULTRAKILL
        public float slamCooldown;

        // slide
        public bool sliding;
        // Whiplash pulls V1 toward a heavy enemy: while set, movement is only this velocity
        public Vector3? hookPull;
        public int dodgeDirectionType; // 0 forward, 1 back, 2 sideways (like cc.dodgeDirection)
        public bool dashing;           // boost && boostLeft > 0
        public float preSlideSpeed;
        public float preSlideDelay;

        // ground slam
        public bool heavyFall;
        public bool stillHolding;
        public float slamForce;
        public float superJumpChance;
        public float extraJumpChance;

        public bool Jumping => jumping > 0f;

        public void Tick(float dt, bool grounded)
        {
            jumping = Mathf.Max(0f, jumping - dt);
            jumpCooldown = Mathf.Max(0f, jumpCooldown - dt);
            slamCooldown = Mathf.Max(0f, slamCooldown - dt);
            safeLaunchCooldown = Mathf.Max(0f, safeLaunchCooldown - dt);

            if (grounded)
            {
                fallTime = 0f;
                clingFade = 0f;
            }
            else if (fallTime < 1f)
            {
                fallTime += dt * 5f;
            }

            // jump windows after a ground slam (GroundCheck.Update)
            if (superJumpChance > 0f)
            {
                superJumpChance = Mathf.MoveTowards(superJumpChance, 0f, dt);
                if (superJumpChance == 0f)
                {
                    extraJumpChance = 0.306f;
                    stillHolding = false;
                }
            }
            if (extraJumpChance > 0f)
            {
                extraJumpChance = Mathf.MoveTowards(extraJumpChance, 0f, dt);
                if (extraJumpChance <= 0f && superJumpChance <= 0f)
                {
                    slamForce = 0f;
                }
            }
        }

        // NewMovement.FixedUpdate: remember the speed before the slide (in units of 24 m/s)
        public void TrackPreSlide(Vector3 velocity, bool falling, float dt)
        {
            if (sliding)
            {
                return;
            }
            if (heavyFall)
            {
                preSlideDelay = 0.2f;
                preSlideSpeed = slamForce;
            }
            else if (!dashing && falling && velocity.magnitude / scale / 24f > preSlideSpeed)
            {
                preSlideSpeed = velocity.magnitude / scale / 24f;
                preSlideDelay = 0.2f;
            }
            else
            {
                preSlideDelay = Mathf.MoveTowards(preSlideDelay, 0f, dt);
                if (preSlideDelay <= 0f)
                {
                    preSlideDelay = 0.2f;
                    preSlideSpeed = velocity.magnitude / scale / 24f;
                }
            }
        }

        // jump multiplier right after a ground slam
        public bool HasSlamJump => superJumpChance > 0f || extraJumpChance > 0f;
        public float SlamJumpMultiplier => slamForce < 5.5f ? 3f + (slamForce - 1f) : 12.5f;

        public void ClearSlamJump()
        {
            superJumpChance = 0f;
            extraJumpChance = 0f;
            slamForce = 0f;
        }

        public static int DodgeDirectionType(Vector3 dodgeDirection, Vector3 aimDirection)
        {
            Vector3 forward = V1CharacterMain.Flat(aimDirection);
            float dot = Vector3.Dot(dodgeDirection.normalized, forward);
            if (dot > 0.999f) return 0;
            if (dot < -0.999f) return 1;
            return 2;
        }

        // RoR2 skills only start from the character's main state. The slide, dash and ground slam
        // take its place, so they pass LMB/RMB/Shift/R presses on themselves (in ULTRAKILL you can always shoot).
        public static void PerformSkillInputs(SkillLocator skills, InputBankTest input)
        {
            if (!skills || !input)
            {
                return;
            }
            Try(skills.primary, input.skill1);
            Try(skills.secondary, input.skill2);
            Try(skills.utility, input.skill3);
            Try(skills.special, input.skill4);
            CheckStaminaFail(skills, input);
        }

        // dash without stamina: StaminaFail sound
        public static void CheckStaminaFail(SkillLocator skills, InputBankTest input)
        {
            if (skills && skills.utility && input && input.skill3.justPressed && skills.utility.stock < 1)
            {
                V1MovementAudio.StaminaFail(skills.gameObject);
            }
        }

        private static void Try(GenericSkill skill, InputBankTest.ButtonState button)
        {
            if (!skill || !skill.skillDef)
            {
                return;
            }
            bool pressed = skill.skillDef.mustKeyPress ? button.justPressed : button.down;
            if (pressed)
            {
                skill.ExecuteIfReady();
            }
        }

        public static V1MovementData Get(GameObject go)
        {
            V1MovementData d = go.GetComponent<V1MovementData>();
            return d ? d : go.AddComponent<V1MovementData>();
        }

        // scale from the Shift chosen in the loadout; the motor's gravity is scaled too
        public void RefreshScale(CharacterBody body)
        {
            SkillLocator skills = body ? body.skillLocator : null;
            bool ror2 = skills && skills.utility && skills.utility.baseSkill == V1Skills.dashRoR2SkillDef;
            scale = ror2 ? RoR2Scale : 1f;
            if (body && body.characterMotor)
            {
                body.characterMotor.CustomGravity = -40f * scale;
            }
        }

        public static float ScaleOf(GameObject go)
        {
            V1MovementData d = go ? go.GetComponent<V1MovementData>() : null;
            return d ? d.scale : 1f;
        }

        // A wall nearby: the closest world point found with horizontal rays within the WallCheck sphere
        public static bool TryGetWall(CharacterBody body, out Vector3 point)
        {
            point = Vector3.zero;
            float range = body.radius + V1CharacterMain.WallCheckExtra;
            Vector3 origin = body.corePosition;
            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < 16; i++)
            {
                float a = i * Mathf.PI * 2f / 16f;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                if (Physics.Raycast(origin, dir, out RaycastHit hit, range, LayerIndex.world.mask, QueryTriggerInteraction.Ignore)
                    && hit.distance < best
                    // only near-vertical walls: ULTRAKILL walls are straight, RoR2 slopes and rocks don't count as walls
                    && Mathf.Abs(hit.normal.y) < 0.35f)
                {
                    best = hit.distance;
                    point = hit.point;
                    found = true;
                }
            }
            return found;
        }

        // whether there is ground within distance meters below the feet
        public static bool GroundBelow(CharacterBody body, float distance)
        {
            return Physics.Raycast(body.footPosition + Vector3.up, Vector3.down, distance, LayerIndex.world.mask, QueryTriggerInteraction.Ignore);
        }
    }
}
