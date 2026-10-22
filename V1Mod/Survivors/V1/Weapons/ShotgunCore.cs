using EntityStates;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using V1Mod.Modules;

namespace V1Mod.Survivors.V1.Weapons
{
    // Shotgun, Core Eject variant (ULTRAKILL Shotgun.cs, variation 0; "Shotgun Grenade" prefab):
    // - LMB: 12 pellets with up to 10 degrees of spread on each axis, 1 damage each; point-blank (up to 4 m) the enemy takes 4 more
    //   (Shoot: RaycastAll 4 m, DeliverDamage 4). The next shot comes after the animation, ReadyGun at 1.333 s.
    // - hold RMB: throw force grows to 60 (60 per second), the barrel shakes; release to eject the core:
    //   velocity = (view + up * force * 0.002) * (force + 10). The core explodes on any contact:
    //   Explosion.prefab, damage 35 / 10 = 3.5. The next core no sooner than 0.5 s later.
    // In ULTRAKILL pellets are flying projectiles (speed 75); here they are instant bullets with a trail flying at the same speed.
    public static class ShotgunCore
    {
        public const int Pellets = 12;
        public const float PelletDamage = 0.25f;
        public const float Spread = 10f;
        public const float PointBlankRange = 4f;
        public const float PointBlankDamage = 4f;
        public const float FireCooldown = 1.3333333f;
        public const float CoreCooldown = 0.5f;
        public const float MaxCoreForce = 60f;
        public const float CoreExplosionDamage = 3.5f;
        public const float CoreExplosionRadius = 6f; // approximate: in ULTRAKILL the explosion grows to maxSize 6
        public const float PelletSpeed = 75f;

        public static void FireShot(GameObject owner, CharacterBody body, Ray aim, Transform muzzle)
        {
            FirePellets(owner, body, aim, muzzle, Pellets, Spread);
        }

        // the shot shared by all shotguns: point-blank hit + pellets
        public static void FirePellets(GameObject owner, CharacterBody body, Ray aim, Transform muzzle, int pellets, float spread)
        {
            float damage = Piercer.DamagePerUnit * body.damage;
            // point-blank: everything closer than 4 m along the view line takes 4 more damage
            var seen = new System.Collections.Generic.HashSet<HealthComponent>();
            foreach (RaycastHit h in Physics.RaycastAll(aim, PointBlankRange, LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Collide))
            {
                HurtBox box = h.collider ? h.collider.GetComponent<HurtBox>() : null;
                HealthComponent hc = box ? box.healthComponent : null;
                if (!hc || hc.body == body || !seen.Add(hc) || (hc.body && hc.body.teamComponent && body.teamComponent && hc.body.teamComponent.teamIndex == body.teamComponent.teamIndex))
                {
                    continue;
                }
                FireSingle(owner, body, new Ray(aim.origin, (h.point - aim.origin).normalized), damage * PointBlankDamage, PointBlankRange + 1f, hc);
            }

            // muzzle flash (Shotgun.Shoot: muzzleFlash)
            if (muzzle && V1Assets.shotgunMuzzleFlash)
            {
                V1FlashFx.Spawn(V1Assets.shotgunMuzzleFlash, muzzle.position, Quaternion.LookRotation(aim.direction));
            }
            // for the shotgun parry: a fist at the moment of the shot parries one pellet (Punch.ParryProjectile, playerBullet)
            ShotgunParry.lastShotTime = Time.time;
            ShotgunParry.lastShotBody = body;
            for (int i = 0; i < pellets; i++)
            {
                Quaternion rot = Quaternion.LookRotation(aim.direction) * Quaternion.Euler(Random.Range(-spread, spread), Random.Range(-spread, spread), Random.Range(-spread, spread));
                Ray r = new Ray(aim.origin, rot * Vector3.forward);
                // a pellet is a player projectile (Projectile playerBullet): it deals damage / 4 to an enemy, i.e. 1 / 4 = 0.25
                // a pellet is a real projectile (Projectile): it flies at 75 +- 10 m/s and hits when it arrives
                V1PelletProjectile.Spawn(owner, body, r, muzzle ? muzzle.position : aim.origin, damage * PelletDamage, PelletSpeed + Random.Range(-10f, 10f));
            }
        }

        // one bullet; if a target is given it only hits that target
        internal static Piercer.ShotEnd FireSingle(GameObject owner, CharacterBody body, Ray ray, float damage, float maxDistance, HealthComponent onlyTarget)
        {
            Piercer.ShotEnd end = new Piercer.ShotEnd { point = ray.origin + ray.direction * maxDistance };
            BulletAttack bullet = new BulletAttack
            {
                owner = owner,
                weapon = owner,
                origin = ray.origin,
                aimVector = ray.direction,
                minSpread = 0f,
                maxSpread = 0f,
                bulletCount = 1,
                damage = damage,
                falloffModel = BulletAttack.FalloffModel.None,
                maxDistance = maxDistance,
                force = 200f,
                damageType = V1Damage.Combo(DamageType.Generic, DamageSource.Primary),
                hitMask = LayerIndex.CommonMasks.bullet,
                isCrit = body.RollCrit(),
                procCoefficient = onlyTarget ? 1f : 0.5f,
                radius = 0.1f,
                smartCollision = true,
                tracerEffectPrefab = null,
                hitEffectPrefab = null,
            };
            if (onlyTarget)
            {
                bullet.stopperMask = LayerIndex.world.mask;
            }
            bullet.filterCallback = (BulletAttack attack, ref BulletAttack.BulletHit hit) =>
            {
                if (hit.hitHurtBox && hit.hitHurtBox.healthComponent && !hit.hitHurtBox.healthComponent.alive)
                {
                    return false;
                }
                if (onlyTarget && (!hit.hitHurtBox || hit.hitHurtBox.healthComponent != onlyTarget))
                {
                    return false;
                }
                return BulletAttack.DefaultFilterCallback(attack, ref hit);
            };
            bullet.hitCallback = (BulletAttack attack, ref BulletAttack.BulletHit hit) =>
            {
                bool keepGoing = Piercer.HitWithoutRoR2Effects(attack, ref hit);
                if (!keepGoing || onlyTarget)
                {
                    end = new Piercer.ShotEnd { point = hit.point, normal = hit.surfaceNormal, collider = hit.collider, enemy = hit.hitHurtBox, hit = true };
                }
                return onlyTarget ? false : keepGoing;
            };
            bullet.Fire();
            return end;
        }

        public static void ThrowCore(GameObject owner, CharacterBody body, Ray aim, float force)
        {
            if (!V1Assets.core)
            {
                return;
            }
            Vector3 dir = aim.direction + new Vector3(0f, force * 0.002f, 0f);
            GameObject core = Object.Instantiate(V1Assets.core, aim.origin + aim.direction * 0.5f, Random.rotation);
            V1Core c = core.AddComponent<V1Core>();
            c.owner = owner;
            c.body = body;
            c.velocity = dir * (force + 10f);
        }
    }

    // Core Eject core: flies with ULTRAKILL gravity (-40) and explodes on the first contact with the world or an enemy.
    public class V1Core : MonoBehaviour, IV1Explosive
    {
        public GameObject owner;
        public CharacterBody body;
        public Vector3 velocity;
        private float life;

        public CharacterBody Owner => body;
        private void OnEnable() => V1Explosives.all.Add(this);
        private void OnDisable() => V1Explosives.all.Remove(this);

        // a shot or a coin: a big explosion (Grenade.Explode super = Explosion Super: damage 35, radius 12)
        public void ShotExplode()
        {
            RocketLauncher.Explode(body, transform.position, CoreExplosionDamageSuper, 12f, V1Assets.explosion, 2f, enemyMultiplier: 2f, source: DamageSource.Secondary);
            Destroy(gameObject);
        }
        public const float CoreExplosionDamageSuper = 3.5f;

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            life += dt;
            velocity += Vector3.down * 40f * dt;
            Vector3 step = velocity * dt;
            // the core also explodes on enemies (RoR2 hitboxes are triggers, hence V1Physics.Cast)
            if (V1Physics.Cast(body, transform.position, 0.25f, step, step.magnitude, out RaycastHit hit, out _))
            {
                HurtBox box = hit.collider.GetComponent<HurtBox>();
                if (!(box && box.healthComponent && box.healthComponent.body == body))
                {
                    transform.position = hit.point;
                    Explode();
                    return;
                }
            }
            transform.position += step;
            if (life > 10f)
            {
                Explode();
            }
        }

        private void Explode()
        {
            if (body && body.hasEffectiveAuthority)
            {
                new BlastAttack
                {
                    attacker = owner,
                    inflictor = gameObject,
                    teamIndex = body.teamComponent ? body.teamComponent.teamIndex : TeamIndex.Player,
                    baseDamage = ShotgunCore.CoreExplosionDamage * Piercer.DamagePerUnit * body.damage,
                    baseForce = RocketLauncher.EnemyKnockback,
                    bonusForce = Vector3.up * RocketLauncher.EnemyKnockback * 0.5f,
                    damageType = V1Damage.Combo(DamageType.Generic, DamageSource.Secondary),
                    position = transform.position,
                    radius = ShotgunCore.CoreExplosionRadius,
                    falloffModel = BlastAttack.FalloffModel.None,
                    crit = body.RollCrit(),
                    procCoefficient = 1f,
                    attackerFiltering = AttackerFiltering.NeverHitSelf,
                }.Fire();
                V1Doused.IgniteAround(body, transform.position, ShotgunCore.CoreExplosionRadius);
            }
            if (V1Assets.explosion)
            {
                V1ExplosionAnim.Spawn(V1Assets.explosion, transform.position, Quaternion.identity);
                // the core explosion also hits V1: 35 damage and a launch (core rocket jump)
                SelfBlast.Apply(body, transform.position, ShotgunCore.CoreExplosionRadius, 35f);
                RocketLauncher.DeflectProjectiles(body, transform.position, ShotgunCore.CoreExplosionRadius);
            }
            V1Sounds.Play("Explosion", gameObject, 0.8f, Random.Range(0.9f, 1.1f), false);
            Destroy(gameObject);
        }
    }

    // shotgun LMB
    public class ShotgunFire : BaseSkillState
    {
        public override void OnEnter()
        {
            base.OnEnter();
            StartAimMode(1f);
            if (isAuthority)
            {
                V1ViewModel vm = V1ViewModel.Get(gameObject);
                ShotgunCore.FireShot(gameObject, characterBody, GetAimRay(), vm ? vm.ShootPoint : null);
                Piercer.FovKick(gameObject, V1Camera.DefaultFov / 40f);
                vm?.Trigger("Fire");
            }
            V1Sounds.Play("ShotgunShot", gameObject, 0.45f, Random.Range(0.95f, 1.05f), isAuthority);
            outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Any;
    }

    // shotgun RMB: hold to build force, release to throw the core
    public class ShotgunCoreCharge : BaseSkillState
    {
        private float force;
        private SoundHandle chargeSound;

        public override void OnEnter()
        {
            base.OnEnter();
            chargeSound = V1Sounds.Play("ShotgunCharge", gameObject, 0.5f, 0.01f, isAuthority, loop: true);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!isAuthority)
            {
                return;
            }
            StartAimMode(1f);
            force = Mathf.MoveTowards(force, ShotgunCore.MaxCoreForce, ShotgunCore.MaxCoreForce * Time.fixedDeltaTime);
            ShotgunMeter.Get(gameObject).force = force;
            if (chargeSound != null)
            {
                chargeSound.Pitch = Mathf.Max(0.01f, force / 60f);
            }
            V1ViewModel.Get(gameObject)?.SetShake(force / 3000f);
            if (!inputBank.skill2.down)
            {
                ShotgunCore.ThrowCore(gameObject, characterBody, GetAimRay(), force);
                V1ViewModel.Get(gameObject)?.Trigger("Secondary Fire");
                V1Sounds.Play("CoreEject", gameObject, 0.45f * Mathf.Max(0.3f, force / 60f), Random.Range(0.75f, 0.85f), true);
                skillLocator.secondary.DeductStock(1);
                // after throwing a core the shotgun reloads (resettingCores)
                skillLocator.primary.DeductStock(1);
                outer.SetNextStateToMain();
            }
        }

        public override void OnExit()
        {
            chargeSound?.Stop();
            ShotgunMeter.Get(gameObject).force = 0f;
            V1ViewModel.Get(gameObject)?.SetShake(0f);
            base.OnExit();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
    }
}

namespace V1Mod.Survivors.V1.Weapons
{
    // shotgun RMB charge force (grenadeForce), for the bar on the screen
    public class ShotgunMeter : MonoBehaviour
    {
        public float force;

        public static ShotgunMeter Get(GameObject go)
        {
            ShotgunMeter m = go.GetComponent<ShotgunMeter>();
            return m ? m : go.AddComponent<ShotgunMeter>();
        }
    }
}

namespace V1Mod.Survivors.V1.Weapons
{
    // A pellet in flight (ULTRAKILL Projectile, playerBullet): every physics tick a short RoR2 bullet the length of the step;
    // the first hit (enemy or wall) deals damage and sparks, then the pellet disappears. The model flies out of the barrel and merges with the aim line.
    public class V1PelletProjectile : MonoBehaviour
    {
        private GameObject owner;
        private CharacterBody body;
        private Vector3 position;
        private Vector3 velocity;
        private float damage;
        private float age;
        private Vector3 visualOffset;

        public static void Spawn(GameObject owner, CharacterBody body, Ray ray, Vector3 muzzle, float damage, float speed)
        {
            GameObject go = V1Assets.shotgunPellet ? Object.Instantiate(V1Assets.shotgunPellet, muzzle, Quaternion.LookRotation(ray.direction)) : new GameObject("V1Pellet");
            V1PelletProjectile p = go.AddComponent<V1PelletProjectile>();
            p.owner = owner;
            p.body = body;
            p.position = ray.origin;
            p.velocity = ray.direction * speed;
            p.damage = damage;
            p.visualOffset = muzzle - ray.origin;
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            age += dt;
            if (!body || age > 3f)
            {
                Destroy(gameObject);
                return;
            }
            Vector3 step = velocity * dt;
            Piercer.ShotEnd end = ShotgunCore.FireSingle(owner, body, new Ray(position, velocity.normalized), damage, step.magnitude, null);
            if (end.hit)
            {
                V1Effects.SpawnHit(V1Assets.laserHit, end);
                Destroy(gameObject);
                return;
            }
            position += step;
        }

        private void LateUpdate()
        {
            // the model leaves the barrel and reaches the aim line within 0.1 s
            float k = Mathf.Clamp01(1f - age / 0.1f);
            transform.position = position + visualOffset * k;
            if (velocity.sqrMagnitude > 0.01f)
            {
                transform.rotation = Quaternion.LookRotation(velocity);
            }
        }
    }
}
