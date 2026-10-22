using EntityStates;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace V1Mod.Survivors.V1.SkillStates
{
    // Dash as in ULTRAKILL (NewMovement.TryDash / Dodge / Jump):
    // - direction: where WASD points, otherwise forward along the view; horizontal only
    // - speed: 3 x walk speed (16.5 * 3 = 49.5 m/s)
    // - lasts 25 ULTRAKILL physics steps of 0.008 s = 0.2 s, invulnerable the whole time
    // - after the dash the speed drops back to normal walking in the same direction
    // - jumping off the ground during a dash = dash jump: the dash speed is kept,
    //   up 1.5 x jump power, costs one more stamina bar
    public class Dash : BaseSkillState
    {
        public static float duration = 25f * 0.008f;
        public static float speedMultiplier = 3f;

        private Vector3 dashDirection;
        private float walkSpeed;
        private bool keepVelocityOnExit;

        public override void OnEnter()
        {
            base.OnEnter();

            if (isAuthority)
            {
                Vector3 move = inputBank ? inputBank.moveVector : Vector3.zero;
                if (move.sqrMagnitude < 0.01f)
                {
                    move = inputBank ? inputBank.aimDirection : transform.forward;
                }
                move.y = 0f;
                dashDirection = move.sqrMagnitude > 0.0001f ? move.normalized : transform.forward;
                walkSpeed = characterBody.moveSpeed * V1MovementData.ScaleOf(gameObject);
            }
            V1MovementData startData = V1MovementData.Get(gameObject);
            startData.dashing = true;
            if (isAuthority && inputBank)
            {
                startData.dodgeDirectionType = V1MovementData.DodgeDirectionType(dashDirection, inputBank.aimDirection);
            }

            if (NetworkServer.active)
            {
                characterBody.AddTimedBuff(RoR2Content.Buffs.HiddenInvincibility, duration);
            }

            V1MovementAudio.Dash(gameObject);
            if (isAuthority)
            {
                V1MovementFx.Dash(gameObject, dashDirection);
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
            V1MovementData moveData = V1MovementData.Get(gameObject);
            if (moveData.launched)
            {
                // an explosion launch interrupts the dash
                moveData.launched = false;
                keepVelocityOnExit = true;
                outer.SetNextStateToMain();
                return;
            }

            characterMotor.velocity = dashDirection * walkSpeed * speedMultiplier;

            // slide during a dash (extending invulnerability) or ground slam
            if (inputBank && inputBank.rawSprintInput.justPressed)
            {
                outer.SetNextState(new SlideSlam());
                return;
            }

            if (inputBank && inputBank.jump.justPressed && characterMotor.isGrounded)
            {
                inputBank.jump.hasPressBeenClaimed = true;
                DashJump();
                return;
            }

            if (fixedAge >= duration)
            {
                outer.SetNextStateToMain();
            }
        }

        private void DashJump()
        {
            V1MovementData data = V1MovementData.Get(gameObject);
            if (data.jumpCooldown > 0f)
            {
                return;
            }

            Vector3 v = characterMotor.velocity;
            GenericSkill stamina = skillLocator ? skillLocator.utility : null;
            if (stamina && stamina.stock >= 1)
            {
                stamina.DeductStock(1);
            }
            else
            {
                // not enough stamina: horizontal speed drops to walking
                v = dashDirection * walkSpeed;
            }
            v.y = V1CharacterMain.JumpVelocity(V1CharacterMain.DashJump) * data.scale;
            characterMotor.velocity = v;
            characterMotor.Motor.ForceUnground();
            data.jumping = 0.25f;
            data.jumpCooldown = 0.2f;
            V1MovementAudio.DashJump(gameObject);

            keepVelocityOnExit = true;
            data.dashing = false;
            outer.SetNextStateToMain();
        }

        public override void OnExit()
        {
            // if the dash was interrupted by a slide, the slide takes over the dashing flag (invulnerability extension)
            if (fixedAge >= duration)
            {
                V1MovementData.Get(gameObject).dashing = false;
            }
            if (isAuthority && characterMotor && !keepVelocityOnExit)
            {
                // in ULTRAKILL vertical speed is also 0 after a dash
                characterMotor.velocity = dashDirection * walkSpeed;
            }
            base.OnExit();
        }

        public override InterruptPriority GetMinimumInterruptPriority()
        {
            return InterruptPriority.Skill;
        }
    }
}
