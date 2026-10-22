using System.Collections.Generic;
using RoR2;
using UnityEngine;
using V1Mod.Modules;

namespace V1Mod.Survivors.V1.Weapons
{
    // Sawblade launcher saws (Sawblade Launcher, ULTRAKILL alternate nailguns; Nail.cs with sawblade = true):
    // - flies at 200 m/s without gravity, lives 15 s;
    // - "durability" hitAmount: enemy hit -1, wall bounce -0.25 (-0.1 for the magnet), extra bounce -0.125;
    //   below 1 after a hit (or 0 after a bounce) the saw breaks;
    // - bounces off walls by reflection (the heated saw along the wall normal) and checks up to 3 walls within 5 m ahead at once;
    // - one enemy is hit at most once per 0.15 s; the heated saw (multiHit > 1) stops inside the enemy
    //   and hits it multiHit times with a 0.15 s pause, then flies on;
    // - Attractor magnets swirl the saw around themselves, like nails.
    // Damage relative to a nail as in ULTRAKILL: saw 0.6 / 0.185, Attractor 0.75 / 0.205, heated 1 / 0.185.
    public class V1SawNail : MonoBehaviour
    {
        public enum Kind { Normal, Fodder, Heated }

        public CharacterBody body;
        public Vector3 velocity;
        public float damage;
        public float hitAmount = 2.9f;
        public int multiHitAmount = 1;
        public bool heated;
        public bool bounceToSurfaceNormal;

        private float age;
        private int rotationDirection;
        private HealthComponent currentHitEnemy;
        private float sameEnemyHitCooldown;
        private bool stopped;
        private HurtBox hitTarget;
        private int currentMultiHit;
        private float multiHitCooldown;
        private Vector3 originalVelocity;
        private SoundHandle loop;

        public static float DamageOf(Kind kind)
        {
            switch (kind)
            {
                case Kind.Fodder: return Nailgun.FodderDamage * 0.75f / 0.205f;
                case Kind.Heated: return Nailgun.NailDamage * 1f / 0.185f;
                default: return Nailgun.NailDamage * 0.6f / 0.185f;
            }
        }

        public static V1SawNail Spawn(CharacterBody body, Vector3 pos, Quaternion rot, Kind kind, float hitAmount, int multiHit)
        {
            GameObject prefab = kind == Kind.Heated ? V1Assets.sawNailHeated : kind == Kind.Fodder ? V1Assets.sawNailFodder : V1Assets.sawNail;
            GameObject go = prefab ? Object.Instantiate(prefab, pos, rot) : new GameObject("V1SawNail");
            go.transform.SetPositionAndRotation(pos, rot);
            V1SawNail s = go.AddComponent<V1SawNail>();
            s.body = body;
            s.velocity = rot * Vector3.forward * Nailgun.NailSpeed;
            s.damage = DamageOf(kind);
            s.hitAmount = hitAmount;
            s.multiHitAmount = multiHit;
            s.heated = kind == Kind.Heated;
            s.bounceToSurfaceNormal = kind == Kind.Heated;
            return s;
        }

        private void Start()
        {
            rotationDirection = Random.value < 0.5f ? -1 : 1;
            if (heated)
            {
                loop = V1Sounds.Play("SawThrownLoop", gameObject, 0.4f, 1f, false, loop: true);
            }
            // ForceCheckSawbladeRicochet: a saw fired point-blank into a wall reflects at once
            Bounces(false);
        }

        private void OnDestroy()
        {
            loop?.Stop();
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            age += dt;
            if (!body || age > 15f)
            {
                Destroy(gameObject);
                return;
            }
            if (sameEnemyHitCooldown > 0f)
            {
                sameEnemyHitCooldown = Mathf.MoveTowards(sameEnemyHitCooldown, 0f, dt);
            }
            if (stopped)
            {
                MultiHit(dt);
                return;
            }

            V1Magnet magnet = NearestMagnet();
            if (magnet)
            {
                Vector3 to = (magnet.transform.position - transform.position).normalized;
                Vector3 target = Quaternion.Euler(0f, 85f * rotationDirection, 0f) * to * velocity.magnitude;
                velocity = Vector3.RotateTowards(velocity, target, float.PositiveInfinity, velocity.magnitude);
            }

            // enemies nearby (OverlapSphere 0.5) and on the path
            foreach (Collider c in Physics.OverlapSphere(transform.position, 0.5f, LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Collide))
            {
                HurtBox box = c.GetComponent<HurtBox>();
                if (box && V1Damage.IsEnemy(body, box.healthComponent) && TouchEnemy(box))
                {
                    return;
                }
            }
            Vector3 step = velocity * dt;
            float length = step.magnitude;
            if (length > 0.0001f && V1Physics.Cast(body, transform.position, 0.25f, step, length, out RaycastHit h, out HurtBox enemy))
            {
                if (enemy)
                {
                    if (V1Damage.IsEnemy(body, enemy.healthComponent) && TouchEnemy(enemy))
                    {
                        return;
                    }
                }
                else if (!h.collider.isTrigger)
                {
                    if (hitAmount <= 0f)
                    {
                        Break();
                        return;
                    }
                    transform.position = h.point + h.normal * 0.3f;
                    velocity = bounceToSurfaceNormal ? h.normal * velocity.magnitude : Vector3.Reflect(velocity.normalized, h.normal) * velocity.magnitude;
                    hitAmount -= magnet ? 0.1f : 0.25f;
                    if (magnet)
                    {
                        rotationDirection *= -1;
                    }
                    BounceFx(h.point, h.normal, true);
                    currentHitEnemy = null;
                    sameEnemyHitCooldown = 0f;
                    Bounces(false);
                    return;
                }
            }
            transform.position += step;
            if (velocity.sqrMagnitude > 0.01f)
            {
                transform.rotation = Quaternion.LookRotation(-velocity);
            }
        }

        // up to 3 walls within 5 m ahead: reflect at once (-0.125 for each)
        private void Bounces(bool sound)
        {
            for (int i = 0; i < 3; i++)
            {
                if (!Physics.Raycast(transform.position, velocity.normalized, out RaycastHit hit, 5f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
                {
                    break;
                }
                transform.position = hit.point + hit.normal * 0.3f;
                velocity = bounceToSurfaceNormal ? hit.normal * velocity.magnitude : Vector3.Reflect(velocity.normalized, hit.normal) * velocity.magnitude;
                hitAmount -= 0.125f;
                BounceFx(hit.point, hit.normal, sound);
                currentHitEnemy = null;
                sameEnemyHitCooldown = 0f;
            }
        }

        private void BounceFx(Vector3 point, Vector3 normal, bool sound)
        {
            if (V1Assets.sawSparks)
            {
                Object.Destroy(Object.Instantiate(V1Assets.sawSparks, point, Quaternion.LookRotation(normal)), 1f);
            }
            if (sound)
            {
                V1Sounds.Play("SawRicochet", gameObject, 0.3f, Random.Range(0.9f, 1.1f), false);
            }
        }

        // true: the saw stopped or broke (don't fly further this step)
        private bool TouchEnemy(HurtBox box)
        {
            HealthComponent hc = box.healthComponent;
            if (sameEnemyHitCooldown > 0f && currentHitEnemy == hc)
            {
                return false;
            }
            if (multiHitAmount > 1)
            {
                stopped = true;
                currentMultiHit = multiHitAmount;
                hitTarget = box;
                currentHitEnemy = hc;
                originalVelocity = velocity;
                sameEnemyHitCooldown = 0.15f;
                multiHitCooldown = 0f;
                return true;
            }
            sameEnemyHitCooldown = 0.15f;
            currentHitEnemy = hc;
            hitAmount -= 1f;
            Damage(box);
            if (hitAmount < 1f)
            {
                Break();
                return true;
            }
            return false;
        }

        private void MultiHit(float dt)
        {
            if (multiHitCooldown > 0f)
            {
                multiHitCooldown = Mathf.MoveTowards(multiHitCooldown, 0f, dt);
                return;
            }
            bool alive = hitTarget && hitTarget.healthComponent && hitTarget.healthComponent.alive;
            if (alive && currentMultiHit > 0)
            {
                currentMultiHit--;
                hitAmount -= 1f;
                Damage(hitTarget);
            }
            alive = hitTarget && hitTarget.healthComponent && hitTarget.healthComponent.alive;
            if (!alive || currentMultiHit <= 0)
            {
                stopped = false;
                velocity = originalVelocity;
                sameEnemyHitCooldown = 0.15f;
                if (hitAmount <= 0f)
                {
                    Break();
                }
                return;
            }
            multiHitCooldown = 0.15f;
        }

        private void Damage(HurtBox box)
        {
            V1Damage.Deal(body, box, damage, transform.position, velocity.normalized * 300f, 0.25f, heated ? DamageType.IgniteOnHit : DamageType.Generic, heated ? DamageSource.Secondary : DamageSource.Primary);
            V1Sounds.Play("SawImpact", gameObject, 0.2f, Random.Range(0.9f, 1.1f), false);
        }

        private void Break()
        {
            V1Sounds.Play("SawRicochet", gameObject, 0.4f, 0.6f, false);
            if (V1Assets.sawSparks)
            {
                Object.Destroy(Object.Instantiate(V1Assets.sawSparks, transform.position, Quaternion.identity), 1f);
            }
            Destroy(gameObject);
        }

        private V1Magnet NearestMagnet()
        {
            NailgunData data = body ? body.GetComponent<NailgunData>() : null;
            if (!data || data.magnets.Count == 0)
            {
                return null;
            }
            V1Magnet best = null;
            float bestDistance = Nailgun.MagnetRadius;
            foreach (V1Magnet m in data.magnets)
            {
                if (!m)
                {
                    continue;
                }
                float d = Vector3.Distance(m.transform.position, transform.position);
                if (d < bestDistance)
                {
                    best = m;
                    bestDistance = d;
                }
            }
            return best;
        }
    }
}
