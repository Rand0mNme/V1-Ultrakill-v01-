using System.Collections.Generic;
using EntityStates;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using V1Mod.Modules;

namespace V1Mod.Survivors.V1.Weapons
{
    // ULTRAKILL railcannons (Railcannon.cs, RevolverBeam.cs, Harpoon.cs, WeaponCharges.raicharge):
    // - one charge shared by all three: 0 -> 5 at 0.25 per second, from 4 it becomes full at once (16 s); LMB fires, hold RMB to zoom (FOV / 2).
    // - Electric (blue): beam through everything up to a wall, hits each enemy 4 times for 2 damage.
    // - Screwdriver (green, Harpoon Malicious): the drill flies at 250 m/s, 3 damage on impact, then drills 116 times for 0.0625 every 0.05 s.
    // - Malicious (red): beam up to the first hit, 2 damage and an explosion for 50 / 10 = 5 damage (x1.25 to enemies).
    public static class Railcannon
    {
        // Electric: 2000% of the character's damage per target in total (as in the other V1 mod), split into 4 hits
        public static readonly float ElectricDamage = 20f / Piercer.DamagePerUnit / 4f;
        public const int ElectricHitsPerTarget = 4;
        public const float BeamRadius = 1f;
        public const float MaliciousBeamDamage = 2f;
        public const float MaliciousExplosionDamage = 5f * 1.25f;
        public const float MaliciousExplosionRadius = 13.5f; // maxSize in Explosion.cs is the sphere radius
        public const float ScrewdriverSpeed = 250f;
        public const float ScrewdriverDamage = 3f;
        public const int DrillHits = 116;
        public const float DrillDamage = 0.0625f;

        public static void FireElectric(CharacterBody body, Ray aim, Transform muzzle, float bonus = 0f)
        {
            float maxDistance = 1000f;
            Vector3 end = aim.origin + aim.direction * maxDistance;
            if (Physics.Raycast(aim, out RaycastHit wall, maxDistance, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
            {
                maxDistance = wall.distance;
                end = wall.point;
                V1Effects.SpawnHit(V1Assets.superLaserHit, new Piercer.ShotEnd { point = wall.point, normal = wall.normal, collider = wall.collider, hit = true });
            }
            RaycastHit[] hits = Physics.SphereCastAll(aim.origin, BeamRadius, aim.direction, maxDistance, LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Collide);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            var seen = new HashSet<HealthComponent>();
            bool any = false;
            foreach (RaycastHit h in hits)
            {
                HurtBox box = h.collider ? h.collider.GetComponent<HurtBox>() : null;
                HealthComponent hc = box ? box.healthComponent : null;
                if (!V1Damage.IsEnemy(body, hc) || !seen.Add(hc))
                {
                    continue;
                }
                Vector3 point = h.point == Vector3.zero ? box.transform.position : h.point;
                for (int i = 0; i < ElectricHitsPerTarget; i++)
                {
                    V1Damage.Deal(body, box, ElectricDamage + bonus, point, aim.direction * 400f, 1f, i == 0 ? DamageType.Shock5s : DamageType.Generic);
                }
                V1Effects.SpawnHit(V1Assets.superLaserHit, new Piercer.ShotEnd { point = point, normal = -aim.direction, collider = h.collider, enemy = box, hit = true });
                any = true;
            }
            if (any)
            {
                V1HitStop.HitStop(0.05f);
            }
            V1Effects.SpawnBeam(V1Assets.railBeam, muzzle ? muzzle.position : aim.origin, end, muzzle);
        }

        public static void FireMalicious(CharacterBody body, Ray aim, Transform muzzle, float bonus = 0f)
        {
            Vector3 end = aim.origin + aim.direction * 1000f;
            if (V1Physics.Cast(body, aim.origin, 0.4f, aim.direction, 1000f, out RaycastHit h, out _)
                || Physics.Raycast(aim, out h, 1000f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
            {
                end = h.point;
                HurtBox box = h.collider.GetComponent<HurtBox>();
                if (box && V1Damage.IsEnemy(body, box.healthComponent))
                {
                    V1Damage.Deal(body, box, MaliciousBeamDamage + bonus, h.point, aim.direction * 400f);
                }
                Explode(body, h.point - aim.direction * 0.5f);
            }
            V1Effects.SpawnBeam(V1Assets.railBeamMalicious, muzzle ? muzzle.position : aim.origin, end, muzzle);
        }

        private static void Explode(CharacterBody body, Vector3 pos)
        {
            if (body.hasEffectiveAuthority)
            {
                new BlastAttack
                {
                    attacker = body.gameObject,
                    inflictor = body.gameObject,
                    teamIndex = body.teamComponent ? body.teamComponent.teamIndex : TeamIndex.Player,
                    baseDamage = MaliciousExplosionDamage * Piercer.DamagePerUnit * body.damage,
                    baseForce = RocketLauncher.EnemyKnockback,
                    bonusForce = Vector3.up * RocketLauncher.EnemyKnockback * 0.5f,
                    damageType = V1Damage.Combo(DamageType.Generic, DamageSource.Primary),
                    position = pos,
                    radius = MaliciousExplosionRadius,
                    falloffModel = BlastAttack.FalloffModel.None,
                    crit = body.RollCrit(),
                    procCoefficient = 1f,
                    attackerFiltering = AttackerFiltering.NeverHitSelf,
                }.Fire();
                V1Doused.IgniteAround(body, pos, MaliciousExplosionRadius);
            }
            if (V1Assets.maliciousExplosion)
            {
                V1ExplosionAnim.Spawn(V1Assets.maliciousExplosion, pos, Quaternion.identity);
                SelfBlast.Apply(body, pos, MaliciousExplosionRadius, 50f);
                RocketLauncher.DeflectProjectiles(body, pos, MaliciousExplosionRadius);
            }
            V1Sounds.Play("MaliciousExplosion", body.gameObject, 0.9f, Random.Range(0.9f, 1.1f), false);
        }

        public static void FireScrewdriver(CharacterBody body, Ray aim)
        {
            GameObject go = V1Assets.screwdriver ? Object.Instantiate(V1Assets.screwdriver, aim.origin + aim.direction, Quaternion.LookRotation(aim.direction)) : new GameObject("V1Screwdriver");
            V1Screwdriver s = go.AddComponent<V1Screwdriver>();
            s.body = body;
            s.velocity = aim.direction * ScrewdriverSpeed;
        }
    }

    // railcannon charge (WeaponCharges.raicharge)
    public class RailData : MonoBehaviour
    {
        public float charge = 5f;
        private CharacterBody body;

        public static RailData Get(GameObject go)
        {
            RailData d = go.GetComponent<RailData>();
            return d ? d : go.AddComponent<RailData>();
        }

        private void Awake()
        {
            body = GetComponent<CharacterBody>();
        }

        private void Update()
        {
            if (charge < 5f)
            {
                charge = Mathf.MoveTowards(charge, 5f, Time.deltaTime * 0.25f);
                if (charge >= 4f)
                {
                    charge = 5f;
                    if (body && body.hasEffectiveAuthority)
                    {
                        V1Sounds.Play("RailFullCharge", gameObject, 0.6f, 1f, true);
                    }
                }
            }
            V1ViewModel vm = V1ViewModel.Get(gameObject);
            if (vm)
            {
                bool full = charge >= 5f;
                vm.SetPartActive(V1ViewModel.WeaponRailElectric, "FullCharge", full);
                vm.SetPartActive(V1ViewModel.WeaponRailScrewdriver, "FullCharge", full);
                vm.SetPartActive(V1ViewModel.WeaponRailMalicious, "FullCharge", full);
            }
        }
    }

    // railcannon LMB
    public class RailcannonFire : BaseSkillState
    {
        public override void OnEnter()
        {
            base.OnEnter();
            RailData data = RailData.Get(gameObject);
            if (isAuthority && data.charge >= 5f)
            {
                data.charge = 0f;
                StartAimMode(1f);
                V1Arsenal arsenal = GetComponent<V1Arsenal>();
                int weapon = arsenal ? arsenal.Current : V1ViewModel.WeaponRailElectric;
                V1ViewModel vm = V1ViewModel.Get(gameObject);
                Transform muzzle = vm ? vm.ShootPoint : null;
                Ray aim = GetAimRay();
                if (weapon == V1ViewModel.WeaponRailMalicious)
                {
                    CharacterBody b = characterBody;
                    if (!V1Coin.TryBeamCoin(b, aim, muzzle ? muzzle.position : aim.origin, V1Assets.railBeamMalicious, (o, d, bonus) => Railcannon.FireMalicious(b, new Ray(o, d), null, bonus)))
                    {
                        Railcannon.FireMalicious(b, aim, muzzle);
                    }
                    V1Sounds.Play("RailMaliciousFire", gameObject, 0.8f, 1f, true);
                }
                else if (weapon == V1ViewModel.WeaponRailScrewdriver)
                {
                    Railcannon.FireScrewdriver(characterBody, aim);
                    V1Sounds.Play("RailFire", gameObject, 0.8f, 1.1f, true);
                }
                else
                {
                    CharacterBody b = characterBody;
                    // coin: the railcannon reflects off it into a target (RevolverBeam: Coin -> DelayedReflectRevolver)
                    if (!V1Coin.TryBeamCoin(b, aim, muzzle ? muzzle.position : aim.origin, V1Assets.railBeam, (o, d, bonus) => Railcannon.FireElectric(b, new Ray(o, d), null, bonus)))
                    {
                        Railcannon.FireElectric(b, aim, muzzle);
                    }
                    V1Sounds.Play("RailFire", gameObject, 0.8f, 1f, true);
                }
                vm?.Trigger("Shoot");
                Piercer.FovKick(gameObject, V1Camera.DefaultFov / 10f);
            }
            outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Any;
    }

    // railcannon RMB: hold to zoom
    public class RailcannonZoom : BaseSkillState
    {
        private V1CameraEffects fx;

        public override void OnEnter()
        {
            base.OnEnter();
            fx = GetComponent<V1CameraEffects>();
            if (fx)
            {
                fx.zooming = true;
            }
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (isAuthority && (!inputBank || !inputBank.skill2.down))
            {
                outer.SetNextStateToMain();
            }
        }

        public override void OnExit()
        {
            if (fx)
            {
                fx.zooming = false;
            }
            base.OnExit();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Any;
    }

    // Screwdriver drill: flies with gravity, sticks in an enemy and drills it
    public class V1Screwdriver : MonoBehaviour
    {
        public CharacterBody body;
        public Vector3 velocity;
        private HurtBox target;
        private int drillsLeft;
        private float drillCooldown;
        private float age;
        private bool stuck;
        private SoundHandle drill;

        private void Update()
        {
            if (!target)
            {
                return;
            }
            if (!target.healthComponent || !target.healthComponent.alive)
            {
                Destroy(gameObject);
                return;
            }
            drillCooldown = Mathf.MoveTowards(drillCooldown, 0f, Time.deltaTime);
            if (drillCooldown > 0f)
            {
                return;
            }
            drillCooldown = 0.05f;
            V1Damage.Deal(body, target, Railcannon.DrillDamage, transform.position, Vector3.zero, 0.1f);
            if (drill != null)
            {
                drill.Pitch = 1.5f - (float)drillsLeft / Railcannon.DrillHits / 2f;
            }
            drillsLeft--;
            if (drillsLeft <= 0)
            {
                Destroy(gameObject);
            }
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            age += dt;
            if (!body || age > 30f || (stuck && !target && age > 5f))
            {
                Destroy(gameObject);
                return;
            }
            if (stuck)
            {
                return;
            }
            velocity += Vector3.down * Nailgun.NailGravity * dt;
            Vector3 step = velocity * dt;
            float length = step.magnitude;
            if (length > 0.0001f && V1Physics.Cast(body, transform.position, 0.3f, step, length, out RaycastHit h, out _))
            {
                HurtBox box = h.collider.GetComponent<HurtBox>();
                if (box && V1Damage.IsEnemy(body, box.healthComponent))
                {
                    stuck = true;
                    target = box;
                    drillsLeft = Railcannon.DrillHits;
                    transform.position = h.point;
                    transform.SetParent(box.transform, true);
                    V1Damage.Deal(body, box, Railcannon.ScrewdriverDamage, h.point, velocity.normalized * 500f);
                    drill = V1Sounds.Play("Drill", gameObject, 0.6f, 1f, false, loop: true);
                    return;
                }
                if (!box && !h.collider.isTrigger)
                {
                    stuck = true;
                    transform.position = h.point;
                    age = 0f;
                    return;
                }
            }
            transform.position += step;
            if (velocity.sqrMagnitude > 0.01f)
            {
                transform.rotation = Quaternion.LookRotation(velocity);
            }
        }

        private void OnDestroy()
        {
            drill?.Stop();
        }
    }
}
