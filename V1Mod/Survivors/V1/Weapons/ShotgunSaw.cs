using EntityStates;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using V1Mod.Modules;

namespace V1Mod.Survivors.V1.Weapons
{
    // Shotgun, Sawed-on variant (ULTRAKILL Shotgun.cs variation 2, Chainsaw.cs, "Shotgun Saw" prefab):
    // - LMB: normal shotgun shot (as Core Eject).
    // - hold RMB (only when the saw is charged): the saw on the barrel spins and cuts everything in front
    //   (HurtZone: 0.25 damage every 0.05 s). Throw force grows to 60. 40 hits without a kill and the saw
    //   overheats, 60 and it breaks and vanishes without a throw.
    // - Release: the saw flies off on a chain at (force + 10) * 1.5, ricochets off walls (losing half its speed),
    //   stops on each enemy and hits it 3 times for 1.5 damage every 0.05 s, then the chain pulls it back (up to 100 m/s).
    // - The saw charge recovers in 4 s (in 8 s while a thrown saw is still flying).
    public static class ShotgunSaw
    {
        public const float ZoneDamage = 0.25f;
        public const float ZoneInterval = 0.05f;
        public const float ZoneRange = 3f;
        public const float ZoneRadius = 1.5f;
        public const float SawDamage = 1.5f;
        public const int SawHits = 3;
        public const float MaxForce = 60f;

        public static void ThrowSaw(GameObject owner, CharacterBody body, Ray aim, float force)
        {
            if (!V1Assets.chainsaw)
            {
                return;
            }
            Vector3 pos = aim.origin + aim.direction * 0.5f;
            if (Physics.Raycast(aim, out RaycastHit wall, 0.5f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
            {
                pos = aim.origin + aim.direction * Mathf.Max(0f, wall.distance - 0.5f);
            }
            GameObject go = Object.Instantiate(V1Assets.chainsaw, pos, Random.rotation);
            V1Chainsaw saw = go.AddComponent<V1Chainsaw>();
            saw.owner = owner;
            saw.body = body;
            saw.velocity = aim.direction * (force + 10f) * 1.5f;
        }
    }

    // saw charge (WeaponCharges.shoSawCharge)
    public class SawData : MonoBehaviour
    {
        public float charge = 1f;
        public int sawsOut;

        public static SawData Get(GameObject go)
        {
            SawData d = go.GetComponent<SawData>();
            return d ? d : go.AddComponent<SawData>();
        }

        private void Update()
        {
            if (charge < 1f)
            {
                charge = Mathf.MoveTowards(charge, 1f, Time.deltaTime * (sawsOut == 0 ? 0.25f : 0.125f));
            }
            // the saw on the barrel is visible only when charged
            V1ViewModel.Get(gameObject)?.SetPartActive(V1ViewModel.WeaponShotgunSaw, "ChainSawAttachPoint", charge >= 1f);
        }
    }

    // Sawed-on RMB: hold to saw, release to throw the saw
    public class ShotgunSawCharge : BaseSkillState
    {
        private float force;
        private float zoneTimer;
        private int nonlethalHits;
        private bool broken;
        private float brokenPitchTarget = 1f;
        private SoundHandle loop;
        private SoundHandle grind;
        private GameObject sparks;
        private bool thrown;

        public override void OnEnter()
        {
            base.OnEnter();
            SawData data = SawData.Get(gameObject);
            if (data.charge < 1f)
            {
                thrown = true;
                if (isAuthority)
                {
                    outer.SetNextStateToMain();
                }
                return;
            }
            loop = V1Sounds.Play("ChainsawLoop", gameObject, 0.5f, 0.5f, isAuthority, loop: true);
            V1ViewModel.Get(gameObject)?.SetBool("Sawing", true);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (thrown || !isAuthority)
            {
                return;
            }
            StartAimMode(1f);
            force = Mathf.MoveTowards(force, ShotgunSaw.MaxForce, ShotgunSaw.MaxForce * Time.fixedDeltaTime);
            ShotgunMeter.Get(gameObject).force = force;
            V1ViewModel.Get(gameObject)?.SetShake(force / 12000f);
            if (loop != null)
            {
                if (broken)
                {
                    loop.Pitch = Mathf.MoveTowards(loop.Pitch, brokenPitchTarget, Time.fixedDeltaTime * 2f);
                    if (loop.Pitch == brokenPitchTarget)
                    {
                        brokenPitchTarget = 1f + Random.Range(-0.33f, 0.33f);
                    }
                }
                else
                {
                    loop.Pitch = (force / 2f + 30f) / 60f;
                }
            }

            Ray aim = GetAimRay();
            Saw(aim);
            GrindWalls(aim);

            if (nonlethalHits > 60)
            {
                // the saw broke: it vanishes without a throw
                SawData.Get(gameObject).charge = 0f;
                thrown = true;
                SpawnSparks(V1ViewModel.Get(gameObject)?.ShootPoint ? V1ViewModel.Get(gameObject).ShootPoint.position : aim.origin + aim.direction);
                V1ViewModel.Get(gameObject)?.PlayState("FireNoReload");
                outer.SetNextStateToMain();
                return;
            }
            if (nonlethalHits > 40 && !broken)
            {
                broken = true;
                brokenPitchTarget = 1f;
            }

            if (!inputBank.skill2.down)
            {
                SawData.Get(gameObject).charge = 0f;
                ShotgunSaw.ThrowSaw(gameObject, characterBody, aim, force);
                thrown = true;
                V1ViewModel.Get(gameObject)?.PlayState("FireNoReload");
                V1Sounds.Play("SawShot", gameObject, 0.45f * Mathf.Max(0.5f, force / 60f), Random.Range(0.75f, 0.85f), true);
                outer.SetNextStateToMain();
            }
        }

        // HurtZone on the barrel: hits every enemy in front of your face for 0.25 every 0.05 s
        private void Saw(Ray aim)
        {
            zoneTimer += Time.fixedDeltaTime;
            if (zoneTimer < ShotgunSaw.ZoneInterval)
            {
                return;
            }
            zoneTimer = 0f;
            var seen = new System.Collections.Generic.HashSet<HealthComponent>();
            Vector3 center = aim.origin + aim.direction * (ShotgunSaw.ZoneRange - ShotgunSaw.ZoneRadius);
            foreach (Collider c in Physics.OverlapSphere(center, ShotgunSaw.ZoneRadius, LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Collide))
            {
                HurtBox box = c.GetComponent<HurtBox>();
                HealthComponent hc = box ? box.healthComponent : null;
                if (!hc || !seen.Add(hc) || !V1Damage.IsEnemy(characterBody, hc))
                {
                    continue;
                }
                V1Damage.Deal(characterBody, box, ShotgunSaw.ZoneDamage, box.transform.position, aim.direction * 100f, source: DamageSource.Secondary);
                if (hc.alive)
                {
                    nonlethalHits++;
                }
            }
        }

        // saw against a wall: sparks and a scraping sound
        private void GrindWalls(Ray aim)
        {
            if (Physics.Raycast(aim, out RaycastHit hit, 3f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
            {
                if (!sparks && V1Assets.sawSparks)
                {
                    sparks = Object.Instantiate(V1Assets.sawSparks);
                }
                if (sparks)
                {
                    sparks.transform.position = hit.point;
                    sparks.transform.rotation = Quaternion.LookRotation(hit.normal);
                }
                if (grind == null)
                {
                    grind = V1Sounds.Play("Scrape", gameObject, 0.6f, 1.2f, true, loop: true);
                }
            }
            else
            {
                StopGrind();
            }
        }

        private void StopGrind()
        {
            if (sparks)
            {
                Object.Destroy(sparks);
                sparks = null;
            }
            grind?.Stop();
            grind = null;
        }

        private static void SpawnSparks(Vector3 pos)
        {
            if (V1Assets.sawSparks)
            {
                Object.Destroy(Object.Instantiate(V1Assets.sawSparks, pos, Quaternion.identity), 1f);
            }
        }

        public override void OnExit()
        {
            loop?.Stop();
            ShotgunMeter.Get(gameObject).force = 0f;
            StopGrind();
            V1ViewModel vm = V1ViewModel.Get(gameObject);
            if (vm)
            {
                vm.SetShake(0f);
                vm.SetBool("Sawing", false);
            }
            base.OnExit();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
    }

    // Thrown saw on a chain (Chainsaw.cs)
    public class V1Chainsaw : MonoBehaviour
    {
        public GameObject owner;
        public CharacterBody body;
        public Vector3 velocity;

        private float age;
        private bool stopped;
        private HealthComponent currentEnemy;
        private HurtBox hitTarget;
        private int hitsLeft;
        private float multiHitCooldown;
        private float sameEnemyCooldown;
        private Vector3 originalVelocity;
        private float blockedTimer;
        private float ignorePlayer;
        private LineRenderer chain;
        private SoundHandle sound;
        private SawData data;

        private Vector3 Anchor => body ? body.aimOrigin + Vector3.down * 0.4f : transform.position;

        private void Start()
        {
            chain = GetComponentInChildren<LineRenderer>(true);
            if (chain)
            {
                chain.useWorldSpace = true;
                chain.positionCount = 2;
            }
            data = owner ? SawData.Get(owner) : null;
            if (data)
            {
                data.sawsOut++;
            }
            sound = V1Sounds.Play("ChainsawLoop", gameObject, 0.5f, 0.85f, false, loop: true);
        }

        public static readonly System.Collections.Generic.List<V1Chainsaw> active = new System.Collections.Generic.List<V1Chainsaw>();
        private void OnEnable() => active.Add(this);
        private void OnDisable() => active.Remove(this);

        // the saw breaks off the chain and flies as a disc at 105 m/s (Nail sawblade: damage 1.5, 4 hits per enemy, durability 5.9)
        public void TurnIntoSawblade(Vector3 dir)
        {
            if (dir.sqrMagnitude < 0.0001f)
            {
                dir = transform.forward;
            }
            V1Sawblade.Spawn(body, transform.position, dir.normalized * 105f);
            V1Sounds.Play("SawHit", gameObject, 0.4f, 0.7f, false);
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (data)
            {
                data.sawsOut--;
            }
            sound?.Stop();
        }

        private void Update()
        {
            if (!body)
            {
                Destroy(gameObject);
                return;
            }
            transform.LookAt(transform.position + (transform.position - Anchor));
            if (chain)
            {
                chain.SetPosition(0, Anchor);
                chain.SetPosition(1, transform.position);
            }
            if (sameEnemyCooldown > 0f && !stopped)
            {
                sameEnemyCooldown = Mathf.MoveTowards(sameEnemyCooldown, 0f, Time.deltaTime);
                if (sameEnemyCooldown <= 0f)
                {
                    currentEnemy = null;
                }
            }
            if (multiHitCooldown > 0f)
            {
                multiHitCooldown = Mathf.MoveTowards(multiHitCooldown, 0f, Time.deltaTime);
            }
            else if (stopped)
            {
                if (currentEnemy && currentEnemy.alive && hitsLeft > 0)
                {
                    hitsLeft--;
                    DamageEnemy();
                }
                if (!currentEnemy || !currentEnemy.alive || hitsLeft <= 0)
                {
                    stopped = false;
                    velocity = originalVelocity.normalized * Mathf.Max(originalVelocity.magnitude, 35f);
                }
                else
                {
                    multiHitCooldown = 0.05f;
                }
            }
            if (sound != null)
            {
                sound.Pitch = stopped ? 1.1f : 0.85f;
                sound.Volume = stopped ? 0.75f : 0.5f;
            }
        }

        private void FixedUpdate()
        {
            if (!body)
            {
                return;
            }
            float dt = Time.fixedDeltaTime;
            age += dt;
            if (stopped)
            {
                return;
            }
            Vector3 toGun = Anchor - transform.position;
            float distance = toGun.magnitude;
            Vector3 dir = distance > 0.001f ? toGun / distance : Vector3.up;
            bool retracting = Vector3.Dot(velocity.normalized, dir) >= 0.5f;
            if (!retracting)
            {
                velocity = Vector3.MoveTowards(velocity, dir * 100f, dt * distance * 10f);
            }
            else
            {
                velocity = dir * Mathf.Min(100f, Mathf.MoveTowards(velocity.magnitude, 100f, dt * Mathf.Max(10f, distance) * 10f));
            }
            ignorePlayer += dt;
            if (ignorePlayer > 0.66f && distance < 1.5f)
            {
                // back in the hand
                Destroy(gameObject);
                return;
            }
            // the chain is blocked by a wall for more than 0.25 s: in ULTRAKILL the saw breaks off and turns into a disc; here it just vanishes
            blockedTimer = Physics.Raycast(transform.position, dir, distance, LayerIndex.world.mask, QueryTriggerInteraction.Ignore) ? blockedTimer + dt : 0f;
            if (blockedTimer >= 0.25f || age > 20f)
            {
                // Chainsaw.TurnIntoSawblade: the chain is blocked by a wall, so the saw breaks off and flies on as a disc
                if (blockedTimer >= 0.25f)
                {
                    TurnIntoSawblade(stopped ? originalVelocity : velocity);
                    return;
                }
                Destroy(gameObject);
                return;
            }

            Vector3 step = velocity * dt;
            float length = step.magnitude;
            if (length > 0.0001f)
            {
                RaycastHit[] hits = Physics.SphereCastAll(transform.position, 0.5f, step / length, length, LayerIndex.world.mask | LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Collide);
                System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                foreach (RaycastHit h in hits)
                {
                    HurtBox box = h.collider.GetComponent<HurtBox>();
                    if (box)
                    {
                        HealthComponent hc = box.healthComponent;
                        if (V1Damage.IsEnemy(body, hc) && !(sameEnemyCooldown > 0f && currentEnemy == hc))
                        {
                            stopped = true;
                            hitsLeft = ShotgunSaw.SawHits;
                            hitTarget = box;
                            currentEnemy = hc;
                            originalVelocity = velocity;
                            sameEnemyCooldown = 0.25f;
                            transform.position += step.normalized * h.distance;
                            return;
                        }
                        continue;
                    }
                    if (h.collider.isTrigger || h.distance <= 0f && h.point == Vector3.zero)
                    {
                        continue;
                    }
                    // ricochet off a wall at half speed
                    transform.position = h.point + h.normal * 0.5f;
                    velocity = Vector3.Reflect(velocity.normalized, h.normal) * (velocity.magnitude / 2f);
                    ignorePlayer = 1f;
                    if (V1Assets.sawSparks)
                    {
                        Destroy(Instantiate(V1Assets.sawSparks, h.point, Quaternion.LookRotation(h.normal)), 1f);
                    }
                    return;
                }
            }
            transform.position += step;
        }

        private void DamageEnemy()
        {
            V1Sounds.Play("SawHit", gameObject, 0.2f, Random.Range(0.9f, 1.1f), false);
            if (hitTarget)
            {
                V1Damage.Deal(body, hitTarget, ShotgunSaw.SawDamage, transform.position, (hitTarget.transform.position - transform.position).normalized * 300f, source: DamageSource.Secondary);
                if (V1Assets.laserHit)
                {
                    V1Effects.SpawnHit(V1Assets.laserHit, new Piercer.ShotEnd { point = transform.position, normal = -velocity.normalized, enemy = hitTarget, hit = true });
                }
            }
        }
    }
}

namespace V1Mod.Survivors.V1.Weapons
{
    // Saw disc (Nail with sawblade = true, NailAltChainsaw): flies straight at 105 m/s without gravity, bounces off walls;
    // stops on an enemy and hits it 4 times for 1.5 damage every 0.05 s; durability 5.9: -1 per living enemy,
    // -0.125 per bounce; below 1 it breaks. Lives up to 15 s.
    public class V1Sawblade : MonoBehaviour
    {
        public CharacterBody body;
        public Vector3 velocity;
        private float hitAmount = 5.9f;
        private float age;
        private bool stopped;
        private HurtBox target;
        private HealthComponent lastEnemy;
        private float sameEnemyCooldown;
        private int hitsLeft;
        private float multiHitCooldown;
        private Vector3 resumeVelocity;
        private SoundHandle sound;

        public static V1Sawblade Spawn(CharacterBody body, Vector3 pos, Vector3 velocity)
        {
            GameObject go = V1Assets.chainsaw ? Object.Instantiate(V1Assets.chainsaw, pos, Quaternion.LookRotation(velocity)) : new GameObject("V1Sawblade");
            go.transform.position = pos;
            foreach (LineRenderer lr in go.GetComponentsInChildren<LineRenderer>(true))
            {
                lr.enabled = false;
            }
            V1Sawblade s = go.AddComponent<V1Sawblade>();
            s.body = body;
            s.velocity = velocity;
            return s;
        }

        private void Start()
        {
            sound = V1Sounds.Play("ChainsawLoop", gameObject, 0.5f, 0.85f, false, loop: true);
        }

        private void OnDestroy()
        {
            sound?.Stop();
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            age += dt;
            if (!body || age > 15f || hitAmount < 1f)
            {
                Destroy(gameObject);
                return;
            }
            if (sameEnemyCooldown > 0f)
            {
                sameEnemyCooldown -= dt;
            }
            if (stopped)
            {
                multiHitCooldown -= dt;
                if (multiHitCooldown > 0f)
                {
                    return;
                }
                if (target && target.healthComponent && target.healthComponent.alive && hitsLeft > 0)
                {
                    hitsLeft--;
                    V1Damage.Deal(body, target, 1.5f, transform.position, velocity.normalized * 300f, source: DamageSource.Secondary);
                    V1Sounds.Play("SawHit", gameObject, 0.2f, Random.Range(0.9f, 1.1f), false);
                    multiHitCooldown = 0.05f;
                    return;
                }
                stopped = false;
                velocity = resumeVelocity;
                sameEnemyCooldown = 0.15f;
                return;
            }
            Vector3 step = velocity * dt;
            if (V1Physics.Cast(body, transform.position, 0.5f, step, step.magnitude, out RaycastHit h, out HurtBox enemy))
            {
                if (enemy)
                {
                    if (!(sameEnemyCooldown > 0f && lastEnemy == enemy.healthComponent))
                    {
                        stopped = true;
                        target = enemy;
                        lastEnemy = enemy.healthComponent;
                        hitsLeft = 4;
                        multiHitCooldown = 0f;
                        resumeVelocity = velocity;
                        hitAmount -= 1f;
                        transform.position += step.normalized * h.distance;
                        return;
                    }
                }
                else
                {
                    transform.position = h.point + h.normal * 0.5f;
                    velocity = Vector3.Reflect(velocity, h.normal);
                    hitAmount -= 0.125f;
                    if (V1Assets.sawSparks)
                    {
                        Object.Destroy(Object.Instantiate(V1Assets.sawSparks, h.point, Quaternion.LookRotation(h.normal)), 1f);
                    }
                    return;
                }
            }
            transform.position += step;
            transform.rotation = Quaternion.LookRotation(velocity);
        }
    }
}
