using EntityStates;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace V1Mod.Survivors.V1.SkillStates
{
    // Slide from ULTRAKILL (NewMovement.StartSlide / Dodge / StopSlide):
    // - goes one way while the button is held; speed 24 m/s x preSlideSpeed (1 to 3)
    // - preSlideSpeed = speed before the slide / 24, decays smoothly on the ground
    // - A/D add 5 m/s sideways
    // - gravity is 40% weaker when falling against a wall
    // - if it goes slower than 10 m/s for more than 0.1 s, the slide ends by itself
    // - jumping out of a slide: 2 x jump power, the slide speed is kept
    // - stamina doesn't regenerate during a slide
    // - a slide during a dash extends the dash invulnerability up to 1 s
    public class Slide : BaseSkillState
    {
        public const float BaseSlideSpeed = 24f; // walkSpeed 750 * 0.008 * 4
        public const float StrafeSpeed = 5f;

        private V1MovementData data;
        private Vector3 slideDirection;
        private float slideSafety = 1f;
        private float lowSpeedTime;
        private float dashStorage;
        private float frozenStaminaStopwatch;
        private bool keepVelocityOnExit;

        public override void OnEnter()
        {
            base.OnEnter();
            data = V1MovementData.Get(gameObject);

            if (isAuthority)
            {
                Vector3 move = inputBank ? inputBank.moveVector : Vector3.zero;
                move.y = 0f;
                slideDirection = move.sqrMagnitude > 0.01f ? move.normalized : V1CharacterMain.Flat(inputBank ? inputBank.aimDirection : transform.forward);
                if (slideDirection == Vector3.zero)
                {
                    slideDirection = V1CharacterMain.Flat(transform.forward);
                }
                data.dodgeDirectionType = V1MovementData.DodgeDirectionType(slideDirection, inputBank.aimDirection);
            }

            Log.Info("Slide started");
            dashStorage = data.dashing ? 1f : 0f;
            data.dashing = false;
            data.sliding = true;
            if (dashStorage > 0f && NetworkServer.active)
            {
                characterBody.AddTimedBuff(RoR2Content.Buffs.HiddenInvincibility, dashStorage);
            }

            if (skillLocator && skillLocator.utility)
            {
                frozenStaminaStopwatch = skillLocator.utility.rechargeStopwatch;
            }
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();

            if (skillLocator && skillLocator.utility)
            {
                skillLocator.utility.rechargeStopwatch = frozenStaminaStopwatch;
            }

            if (!isAuthority || !characterMotor)
            {
                return;
            }
            V1MovementData.PerformSkillInputs(skillLocator, inputBank);

            float dt = Time.fixedDeltaTime;
            bool grounded = characterMotor.isGrounded;
            data.Tick(dt, grounded);
            if (grounded && !data.Jumping)
            {
                data.currentWallJumps = 0;
                data.rocketJumps = 0;
            }

            if (!inputBank.rawSprintInput.down)
            {
                Log.Info("Slide: Ctrl released after " + fixedAge.ToString("0.00") + " s");
                outer.SetNextStateToMain();
                return;
            }

            float num = 1f;
            if (data.preSlideSpeed > 1f)
            {
                if (data.preSlideSpeed > 3f)
                {
                    data.preSlideSpeed = 3f;
                }
                num = data.preSlideSpeed;
                if (grounded)
                {
                    data.preSlideSpeed -= dt * data.preSlideSpeed;
                }
            }

            float speedScale = characterBody.moveSpeed / V1CharacterMain.WalkSpeed;
            float S = data.scale;
            Vector3 right = Vector3.Cross(Vector3.up, V1CharacterMain.Flat(inputBank.aimDirection)).normalized;
            float strafe = Mathf.Clamp(Vector3.Dot(inputBank.moveVector, right), -1f, 1f);

            Vector3 velocity = (slideDirection * BaseSlideSpeed * num * speedScale + right * strafe * StrafeSpeed) * S;
            float vy = characterMotor.velocity.y;
            if (!grounded && vy < 0f && V1MovementData.TryGetWall(characterBody, out _))
            {
                vy += 40f * S * 0.4f * dt;
            }
            velocity.y = Mathf.Max(vy, -V1CharacterMain.MaxFallSpeed * S);
            characterMotor.velocity = velocity;

            // too slow: the slide ends
            slideSafety -= dt * 5f;
            if (slideSafety <= 0f)
            {
                if (V1CharacterMain.Flat3(velocity).magnitude < 10f * S)
                {
                    lowSpeedTime += dt;
                    if (lowSpeedTime >= 0.1f)
                    {
                        Log.Info("Slide: too slow, ending");
                        outer.SetNextStateToMain();
                        return;
                    }
                }
                else
                {
                    lowSpeedTime = 0f;
                }
            }

            if (dashStorage > 0f)
            {
                dashStorage = Mathf.MoveTowards(dashStorage, 0f, dt);
            }

            HandleJump(grounded);
        }

        private void HandleJump(bool grounded)
        {
            if (!inputBank.jump.justPressed || inputBank.jump.hasPressBeenClaimed || data.jumpCooldown > 0f)
            {
                return;
            }

            if (grounded)
            {
                inputBank.jump.hasPressBeenClaimed = true;
                Vector3 v = characterMotor.velocity;
                v.y = V1CharacterMain.JumpVelocity(V1CharacterMain.SlideJump) * data.scale;
                characterMotor.velocity = v;
                characterMotor.Motor.ForceUnground();
                data.jumping = 0.25f;
                data.jumpCooldown = 0.2f;
                V1MovementAudio.Jump(gameObject);
                keepVelocityOnExit = true;
                outer.SetNextStateToMain();
            }
            else if (data.currentWallJumps < V1CharacterMain.MaxWallJumps && V1MovementData.TryGetWall(characterBody, out Vector3 wallPoint))
            {
                // wall jump during a slide: the slide reflects off the wall and continues
                inputBank.jump.hasPressBeenClaimed = true;
                data.currentWallJumps++;
                Vector3 away = V1CharacterMain.Flat(transform.position - wallPoint);
                Vector3 reflected = V1CharacterMain.Flat(Vector3.Reflect(slideDirection, away));
                slideDirection = (reflected + away * 0.35f).normalized;
                Vector3 v = slideDirection * characterMotor.velocity.magnitude;
                v.y = Mathf.Max(v.y, 15f * data.scale);
                characterMotor.velocity = v;
                characterMotor.Motor.ForceUnground();
                data.jumping = 0.25f;
                data.jumpCooldown = 0.1f;
                V1MovementAudio.WallJump(gameObject, data.currentWallJumps);
            }
        }

        public override void OnExit()
        {
            data.sliding = false;
            V1MovementAudio.SlideStop(gameObject);
            if (isAuthority && characterMotor && !keepVelocityOnExit && characterMotor.isGrounded)
            {
                // StopSlide on the ground: speed resets to walking in the slide direction
                Vector3 v = slideDirection * characterBody.moveSpeed * data.scale;
                v.y = characterMotor.velocity.y;
                characterMotor.velocity = v;
            }
            if (NetworkServer.active && dashStorage > 0f)
            {
                characterBody.ClearTimedBuffs(RoR2Content.Buffs.HiddenInvincibility);
            }
            base.OnExit();
        }

        public override InterruptPriority GetMinimumInterruptPriority()
        {
            return InterruptPriority.Skill;
        }
    }
}
