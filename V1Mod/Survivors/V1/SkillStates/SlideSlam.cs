using EntityStates;

namespace V1Mod.Survivors.V1.SkillStates
{
    // Slide button (like Ctrl in ULTRAKILL): slide on the ground, ground slam in the air.
    public class SlideSlam : BaseSkillState
    {
        public override void OnEnter()
        {
            base.OnEnter();
            if (!isAuthority)
            {
                return;
            }

            V1MovementData data = V1MovementData.Get(gameObject);
            bool grounded = characterMotor && characterMotor.isGrounded;

            Log.Info("Ctrl: grounded " + grounded + ", fallTime " + data.fallTime.ToString("0.00"));
            // NewMovement.HandleInputs: slide on the ground, right after leaving the ground, or when the ground is within 2 m below
            if (grounded || data.fallTime < 0.3f || V1MovementData.GroundBelow(characterBody, 2f))
            {
                outer.SetNextState(new Slide());
                return;
            }

            // TryStartSlam: no ground within 3 m below, airborne for more than 0.1 s, no cooldown
            if (!V1MovementData.GroundBelow(characterBody, 3f) && data.fallTime > 0.5f && data.slamCooldown == 0f && !data.heavyFall)
            {
                outer.SetNextState(new Slam());
                return;
            }

            outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority()
        {
            return InterruptPriority.Skill;
        }
    }
}
