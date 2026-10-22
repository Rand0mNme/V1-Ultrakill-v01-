using EntityStates;
using RoR2;
using UnityEngine;

namespace V1Mod.Survivors.V1.SkillStates
{
    // V1's normal state: walking, air control, jump, wall jumps and wall sliding
    // ported from ULTRAKILL (NewMovement.Move / Jump / WallJump / Cling).
    // The RoR2 motor is kept only for collisions and gravity: body acceleration = 0,
    // so we set the horizontal speed ourselves.
    public class V1CharacterMain : GenericCharacterMain
    {
        // ULTRAKILL: physics step 0.008 s; on the ground the speed closes 25% of the gap to the target every step
        public const float UkTick = 0.008f;
        public const float WalkSpeed = 16.5f;          // walkSpeed 750 * 0.008 * 2.75
        public const float AirAcceleration = 6000f;    // airAcceleration
        public const float Mass = 100f;                // Rigidbody.mass
        public const float JumpPower = 90f;            // jumpPower
        public const float WallJumpPower = 150f;       // wallJumpPower
        public const float MaxFallSpeed = 100f;

        // AddForce(ForceMode.Force) over one 0.008 s step gives speed F * 0.008 / mass
        public static float JumpVelocity(float multiplier) => JumpPower * 1500f * multiplier * UkTick / Mass;
        public const float NormalJump = 2.6f;
        public const float SlideJump = 2f;
        public const float DashJump = 1.5f;
        public static float WallJumpVelocity => 2000f * WallJumpPower * UkTick / Mass; // 24 m/s

        public const int MaxWallJumps = 3;
        public const float WallCheckExtra = 0.4f;      // WallCheck sphere: radius 0.9 vs 0.5 for the player capsule

        private V1MovementData data;

        public override void OnEnter()
        {
            base.OnEnter();
            data = V1MovementData.Get(gameObject);
            data.RefreshScale(characterBody);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!isAuthority || !characterMotor)
            {
                return;
            }
            // ULTRAKILL has no sprint (otherwise RoR2 widens the FOV when running)
            characterBody.isSprinting = false;
            V1MovementData.CheckStaminaFail(skillLocator, inputBank);

            // the RoR2 sprint button (Ctrl) = the ULTRAKILL slide button: slide on the ground, ground slam in the air
            if (inputBank && inputBank.rawSprintInput.justPressed)
            {
                outer.SetNextState(new SlideSlam());
                return;
            }

            float dt = Time.fixedDeltaTime;
            bool grounded = characterMotor.isGrounded;
            data.Tick(dt, grounded);
            float S = data.scale;
            data.launched = false;
            if (data.hookPull.HasValue)
            {
                characterMotor.Motor.ForceUnground();
                characterMotor.velocity = data.hookPull.Value;
                return;
            }
            if (grounded && !data.Jumping)
            {
                data.currentWallJumps = 0;
                data.rocketJumps = 0;
            }

            Vector3 inputDir = Vector3.ClampMagnitude(new Vector3(moveVector.x, 0f, moveVector.z), 1f);
            Vector3 forward = Flat(aimDirection);
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

            Vector3 velocity = characterMotor.velocity;
            float speedScale = characterBody.moveSpeed / WalkSpeed; // speed items

            if (grounded && !data.Jumping)
            {
                Vector3 target = inputDir * WalkSpeed * speedScale * S;
                float t = 1f - Mathf.Pow(0.75f, dt / UkTick);
                Vector3 horizontal = Vector3.Lerp(Flat3(velocity), target, t);
                velocity = new Vector3(horizontal.x, velocity.y, horizontal.z);
            }
            else
            {
                velocity += AirControl(velocity / S, inputDir, forward, right, dt, speedScale) * S;
                velocity = Cling(velocity / S, inputDir, forward, right, dt) * S;
            }

            if (velocity.y < -MaxFallSpeed * S)
            {
                velocity.y = -MaxFallSpeed * S;
            }
            characterMotor.velocity = velocity;
            data.TrackPreSlide(velocity, !grounded, dt);
        }

        // Air control from NewMovement.Move (windState is unused, so num8 = 0)
        private static Vector3 AirControl(Vector3 velocity, Vector3 inputDir, Vector3 forward, Vector3 right, float dt, float speedScale)
        {
            if (inputDir.magnitude <= 0f)
            {
                return Vector3.zero;
            }
            float cap = 16.5f * speedScale;
            Vector3 vector4 = Flat3(velocity);

            float num10 = AirAcceleration * dt * (1f / Mass);
            Vector3 vector5 = Vector3.Project(inputDir, right);
            float num11 = Vector3.Dot(vector4, vector5.normalized);
            float num12 = num10;
            if (num11 + num12 > cap)
            {
                num12 = Mathf.Max(cap - num11, 0f);
            }
            Vector3 vector6 = Vector3.Project(inputDir, forward);
            float num13 = Vector3.Dot(vector4, vector6.normalized);
            float num14 = num10;
            if (num13 + num14 > cap)
            {
                num14 = Mathf.Max(cap - num13, 0f);
            }
            Vector3 vector7 = vector5 * num12 + vector6 * num14;
            Vector3 vector8 = (vector4.magnitude > 0.001f) ? vector4.normalized : inputDir;
            Vector3 vector9 = Vector3.ProjectOnPlane(vector7, vector8);
            Vector3 vector10 = Vector3.Project(vector7, vector8);
            float num15 = 0.4f;
            bool flag = Vector3.Dot(vector10, vector8) > 0f;
            bool flag2 = vector4.magnitude > cap;
            Vector3 airDirection = vector9 + vector10 * ((flag && flag2) ? num15 : 1f);

            float f = 1f - airDirection.magnitude / (num10 + 0.0001f);
            f = Mathf.Pow(f, 3f);
            float maxLength = AirAcceleration * (1f / Mass) * dt * f;
            Vector3 vector11 = inputDir * vector4.magnitude - vector4;
            Vector3 vector12 = Vector3.Project(vector11, vector8);
            vector11 = Vector3.ProjectOnPlane(vector11, vector8);
            if (Vector3.Dot(vector12, vector4) < 0f)
            {
                vector11 += vector12 * 0.4f;
            }
            else
            {
                vector11 += vector12;
            }
            airDirection += Vector3.ClampMagnitude(vector11, maxLength);
            return airDirection;
        }

        // Wall sliding (NewMovement.Cling): push into a wall while falling and the fall slows down,
        // then gradually speeds up (clingFade grows to 50).
        private Vector3 Cling(Vector3 velocity, Vector3 inputDir, Vector3 forward, Vector3 right, float dt)
        {
            if (!TryGetWall(out _) || inputDir.sqrMagnitude <= 0f || velocity.y >= -1f)
            {
                return velocity;
            }
            if (!Physics.Raycast(transform.position, inputDir, out RaycastHit clingHit, 1f + characterBody.radius, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
            {
                return velocity;
            }
            // In the original Mathf.Clamp(-1, 1, x) always returns 1; we reproduce it as is
            velocity = right * 1f + forward * 1f + Vector3.down * 2f * data.clingFade;
            data.clingFade = Mathf.MoveTowards(data.clingFade, 50f, dt * 4f);
            V1MovementAudio.Get(gameObject).clinging = true;
            V1MovementFx fx = V1MovementFx.Get(gameObject);
            fx.clinging = true;
            fx.clingPoint = clingHit.point;
            fx.clingNormal = clingHit.normal;
            return velocity;
        }

        public override void ProcessJump()
        {
            if (!jumpInputReceived || !characterMotor)
            {
                return;
            }
            jumpInputReceived = false;
            if (data.jumpCooldown > 0f)
            {
                return;
            }

            if (characterMotor.isGrounded)
            {
                // right after a ground slam the jump is much higher
                bool slamJump = data.HasSlamJump;
                float multiplier = slamJump ? data.SlamJumpMultiplier : NormalJump;
                DoJump(JumpVelocity(multiplier));
                V1MovementAudio.Jump(gameObject, slamJump);
                data.jumpCooldown = 0.2f;
            }
            else if (data.currentWallJumps < MaxWallJumps && TryGetWall(out Vector3 wallPoint))
            {
                data.currentWallJumps++;
                Vector3 away = Flat(transform.position - wallPoint);
                characterMotor.velocity = (away + Vector3.up) * WallJumpVelocity * data.scale;
                characterMotor.Motor.ForceUnground();
                data.jumping = 0.25f;
                data.jumpCooldown = 0.1f;
                V1MovementAudio.WallJump(gameObject, data.currentWallJumps);
            }
        }

        public void DoJump(float verticalSpeed)
        {
            Vector3 v = characterMotor.velocity;
            v.y = verticalSpeed * data.scale; // verticalSpeed in ULTRAKILL units
            characterMotor.velocity = v;
            characterMotor.Motor.ForceUnground();
            data.jumping = 0.25f;
        }

        // A wall nearby: find the closest world point with horizontal rays within the WallCheck sphere
        public bool TryGetWall(out Vector3 point)
        {
            return V1MovementData.TryGetWall(characterBody, out point);
        }

        public static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.zero;
        }

        public static Vector3 Flat3(Vector3 v)
        {
            v.y = 0f;
            return v;
        }
    }

}
