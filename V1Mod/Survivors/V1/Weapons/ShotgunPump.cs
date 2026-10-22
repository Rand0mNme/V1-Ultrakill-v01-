using EntityStates;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using V1Mod.Modules;

namespace V1Mod.Survivors.V1.Weapons
{
    // Shotgun, Pump Charge variant (ULTRAKILL Shotgun.cs, variation 1; "Shotgun Pump" prefab):
    // - RMB: pump (Pump animation, ready after 0.64 s), charge 0 -> 1 -> 2 -> 3
    // - LMB: charge 0 = 10 pellets with spread /1.5; 1 = 16 pellets; 2 = 24 pellets with spread x2;
    //   3 (overpump) = an explosion at the barrel instead of pellets: damage 50 / 10 = 5, explosion 1.5 times bigger than normal.
    //   The charge resets after a shot. Ready again 0.993 s after a shot (FireWithPump).
    // In ULTRAKILL the overpump also hurts the player; not here yet.
    public static class ShotgunPump
    {
        public const float FireCooldown = 0.99346405f;
        public const float PumpCooldown = 0.64052284f;
        public const float OverpumpDamage = 5f;
        public const float OverpumpRadius = 9f; // approximate: maxSize 6 x 1.5

        public static void FireShot(GameObject owner, CharacterBody body, Ray aim, Transform muzzle, int charge)
        {
            if (charge >= 3)
            {
                // explosion at the barrel (or at the wall if it is closer than a meter)
                Vector3 pos = aim.origin + aim.direction;
                if (Physics.Raycast(aim, out RaycastHit wall, 1f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
                {
                    pos = aim.origin + aim.direction * Mathf.Max(0f, wall.distance - 0.1f);
                }
                if (body.hasEffectiveAuthority)
                {
                    new BlastAttack
                    {
                        attacker = owner,
                        inflictor = owner,
                        teamIndex = body.teamComponent ? body.teamComponent.teamIndex : TeamIndex.Player,
                        baseDamage = OverpumpDamage * Piercer.DamagePerUnit * body.damage,
                        baseForce = RocketLauncher.EnemyKnockback,
                    bonusForce = Vector3.up * RocketLauncher.EnemyKnockback * 0.5f,
                    damageType = V1Damage.Combo(DamageType.Generic, DamageSource.Primary),
                        position = pos,
                        radius = OverpumpRadius,
                        falloffModel = BlastAttack.FalloffModel.None,
                        crit = body.RollCrit(),
                        procCoefficient = 1f,
                        attackerFiltering = AttackerFiltering.NeverHitSelf,
                    }.Fire();
                    V1Doused.IgniteAround(body, pos, OverpumpRadius);
                }
                V1ExplosionAnim.Spawn(V1Assets.explosion, pos, Quaternion.LookRotation(aim.direction), 1.5f);
                // the overpump explodes right in your hands: 50 damage and a strong knockback
                SelfBlast.Apply(body, pos, OverpumpRadius, 50f);
                RocketLauncher.DeflectProjectiles(body, pos, OverpumpRadius);
                V1Sounds.Play("Explosion", owner, 0.9f, Random.Range(0.9f, 1.1f), true);
                return;
            }

            int pellets = charge == 0 ? 10 : charge == 1 ? 16 : 24;
            float spread = charge == 0 ? ShotgunCore.Spread / 1.5f : charge == 1 ? ShotgunCore.Spread : ShotgunCore.Spread * 2f;
            ShotgunCore.FirePellets(owner, body, aim, muzzle, pellets, spread);
        }
    }

    // pump state shared by LMB and RMB
    public class PumpData : MonoBehaviour
    {
        public int charge;
        public static PumpData Get(GameObject go)
        {
            PumpData d = go.GetComponent<PumpData>();
            return d ? d : go.AddComponent<PumpData>();
        }
    }

    public class ShotgunPumpFire : BaseSkillState
    {
        public override void OnEnter()
        {
            base.OnEnter();
            StartAimMode(1f);
            PumpData data = PumpData.Get(gameObject);
            if (isAuthority)
            {
                V1ViewModel vm = V1ViewModel.Get(gameObject);
                ShotgunPump.FireShot(gameObject, characterBody, GetAimRay(), vm ? vm.ShootPoint : null, data.charge);
                Piercer.FovKick(gameObject, V1Camera.DefaultFov / 40f);
                vm?.Trigger("PumpFire");
            }
            // shot pitch: 1.15-1.25 / 0.95-1.05 / 0.75-0.85 by charge
            float pitch = data.charge == 0 ? Random.Range(1.15f, 1.25f) : data.charge == 1 ? Random.Range(0.95f, 1.05f) : Random.Range(0.75f, 0.85f);
            if (data.charge < 3)
            {
                V1Sounds.Play("ShotgunShot", gameObject, 0.45f, pitch, isAuthority);
            }
            data.charge = 0;
        }

        // can't pump while the shot animation plays (ReadyGun 0.993 s)
        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (fixedAge >= ShotgunPump.FireCooldown && isAuthority)
            {
                outer.SetNextStateToMain();
            }
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
    }

    public class ShotgunPumpCharge : BaseSkillState
    {
        private bool secondSound;

        public override void OnEnter()
        {
            base.OnEnter();
            PumpData data = PumpData.Get(gameObject);
            if (data.charge < 3)
            {
                data.charge++;
            }
            if (isAuthority)
            {
                V1ViewModel.Get(gameObject)?.Trigger("Pump");
            }
            // Pump1Sound (0.10 s): pump sound + charge build-up sound
            V1Sounds.Play("Pump1", gameObject, 1f, Random.Range(0.95f, 1.05f), isAuthority);
            V1Sounds.Play("PumpCharge", gameObject, 0.6f, 1f + 0.15f * data.charge, isAuthority);
            // on overpump (3) the shotgun screen beeps a warning every 0.2 s (Shotgun.UpdateMeter)
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            // Pump2Sound after ~0.4 s
            if (!secondSound && fixedAge >= 0.4f)
            {
                secondSound = true;
                V1Sounds.Play("Pump2", gameObject, 1f, Random.Range(0.95f, 1.05f), isAuthority);
            }
            if (fixedAge >= ShotgunPump.PumpCooldown && isAuthority)
            {
                outer.SetNextStateToMain();
            }
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
    }
}
