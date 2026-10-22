using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using V1Mod.Survivors.V1.SkillStates;

namespace V1Mod.Survivors.V1.Weapons
{
    // ULTRAKILL explosions also hit the player (Explosion.Collide -> NewMovement):
    // - the inner sphere (maxSize, damage > 0) hurts and launches: damage out of V1's 100 health points (35 for the core and rocket,
    //   50 for Malicious and the overpump) is converted into a fraction of RoR2 max health.
    //   Launch (NewMovement.LaunchFromPoint, force 200, mass 100, multiplier 8): horizontal away from the explosion
    //   (max(0, maxSize - distance) * 200), up at least half of that; all capped at 1000, speed = that * 8 / 100.
    //   If V1 is in a jump (0.5 s after a jump/launch), the force doesn't depend on distance.
    //   If the explosion is almost exactly below/above V1 (horizontally closer than 0.25 m), the launch is straight up.
    // - the outer wave of normal explosions (damage 0) hits only enemies in ULTRAKILL (canHit = EnemiesOnly) and leaves the player alone.
    // - the exception is a rocket that hit a wall/floor ("Explosion Rocket Harmless"): its outer wave (radius 8) launches
    //   the player without damage with force 100 / ((n + 3) / 3), where n = rocket jumps since last touching the ground (100, 75, 60, 50...),
    //   and for 0.5 s after it other harmless waves don't launch (safeExplosionLaunchCooldown).
    // - parried pellets and the like (boosted): launch at a speed of at least 60 (LaunchFromPointAtSpeed).
    // Walls don't shield the player from explosions (in ULTRAKILL the ray only checks layer 11, not the environment).
    public static class SelfBlast
    {
        public const float LaunchStrength = 200f;

        public static void Apply(CharacterBody body, Vector3 pos, float damageRadius, float playerDamage, bool boosted = false, float rocketWaveRadius = 0f)
        {
            if (!body || !body.hasEffectiveAuthority || !body.characterMotor)
            {
                return;
            }
            V1MovementData data = V1MovementData.Get(body.gameObject);
            float distance = Vector3.Distance(body.corePosition, pos);
            if (distance <= damageRadius)
            {
                if (boosted)
                {
                    LaunchAtSpeed(body, pos, Mathf.Max(60f * data.scale, body.characterMotor.velocity.magnitude));
                }
                else
                {
                    Launch(body, pos, damageRadius, LaunchStrength);
                }
                if (playerDamage > 0f && NetworkServer.active && body.healthComponent)
                {
                    body.healthComponent.TakeDamage(new DamageInfo
                    {
                        attacker = null,
                        inflictor = null,
                        damage = body.healthComponent.fullCombinedHealth * playerDamage / 100f,
                        position = pos,
                        force = Vector3.zero,
                        procCoefficient = 0f,
                        damageType = DamageType.NonLethal,
                        damageColorIndex = DamageColorIndex.Default,
                    });
                }
            }
            else if (rocketWaveRadius > 0f && distance <= rocketWaveRadius && data.safeLaunchCooldown <= 0f)
            {
                float strength = Mathf.Round(100f / ((data.rocketJumps + 3) / 3f));
                data.rocketJumps++;
                Launch(body, pos, rocketWaveRadius, strength);
                data.safeLaunchCooldown = 0.5f;
            }
        }

        // explosion point almost exactly below/above the player: launch straight up
        private static Vector3 Direction(CharacterBody body, Vector3 from)
        {
            Vector3 offset = from - body.corePosition;
            if (new Vector3(offset.x, 0f, offset.z).sqrMagnitude < 0.0625f || offset.sqrMagnitude < 0.0001f)
            {
                return Vector3.up;
            }
            return -offset.normalized;
        }

        public static void LaunchAtSpeed(CharacterBody body, Vector3 from, float speed)
        {
            CharacterMotor motor = body.characterMotor;
            Vector3 v = Direction(body, from) * speed;
            // up at least half the speed
            v.y += Mathf.Max(0f, 0.5f * speed - v.y);
            motor.Motor.ForceUnground();
            motor.velocity = v;
            AfterLaunch(body);
        }

        public static void Launch(CharacterBody body, Vector3 from, float maxSize, float strength)
        {
            CharacterMotor motor = body.characterMotor;
            V1MovementData data = V1MovementData.Get(body.gameObject);
            float power = data.Jumping ? maxSize * strength : Mathf.Max(0f, maxSize - Vector3.Distance(body.corePosition, from)) * strength;
            Vector3 v = Direction(body, from) * power;
            Vector3 horizontal = new Vector3(v.x, 0f, v.z);
            // the player's vertical speed is compared with the force as is (that's how ULTRAKILL does it)
            float up = Mathf.Max(motor.velocity.y / data.scale, 0.5f * power);
            Vector3 launch = Vector3.ClampMagnitude(horizontal + Vector3.up * up, 1000f) * 8f / 100f * data.scale;
            if (launch.sqrMagnitude < 0.01f)
            {
                return;
            }
            motor.Motor.ForceUnground();
            motor.velocity = launch;
            AfterLaunch(body);
        }

        // NewMovement.Launch: jumping for 0.5 s, the dash is interrupted, the ground slam is cancelled
        private static void AfterLaunch(CharacterBody body)
        {
            V1MovementData data = V1MovementData.Get(body.gameObject);
            data.jumping = 0.5f;
            data.jumpCooldown = Mathf.Max(data.jumpCooldown, 0.2f);
            data.heavyFall = false;
            data.launched = true;
        }
    }
}
