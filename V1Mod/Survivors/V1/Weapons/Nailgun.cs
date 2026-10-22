using System.Collections.Generic;
using EntityStates;
using RoR2;
using UnityEngine;
using V1Mod.Modules;

namespace V1Mod.Survivors.V1.Weapons
{
    // ULTRAKILL nailguns (Nailgun.cs, Nail.cs, Magnet.cs, Zapper.cs, WeaponCharges.cs):
    // - shared: hold LMB for a stream of nails; pause between nails fireRate / 100 s (fireRate 4 = 25 nails per second);
    //   a nail flies at 200 m/s with gravity -40, spread +- spread / 3 on each axis, sticks in the enemy.
    // - Attractor (blue, Nailgun Magnet): spread 10, 100 nails (regenerate at 5/s when not firing), damage 0.205.
    //   RMB is a magnet (up to 3, charge 3/s): flies at 100 m/s, sticks to an enemy/wall; nails within 25 m orbit it.
    // - Overheat (green): spread 20, damage 0.185. The barrel heats up (0.55/s) and fires slower past half heat.
    //   RMB (heat from 10%) vents the heatsink: a stream of heated nails (they ignite) with pause 1.5 until the heat is gone.
    //   2 heatsinks, each recharges in 8 s.
    // - JumpStart (red, Nailgun Zapper): spread 10, damage 0.185. RMB is a clamp on a cable (once per 5 s): it latches onto an enemy,
    //   builds charge for 5 s and shocks for 10 damage; it snaps if you move further than 20 m or lose sight of the enemy.
    public static class Nailgun
    {
        public const float NailSpeed = 200f;
        public const float NailGravity = 40f;
        // nail damage as in the other V1 mod: 24% of character damage (in ULTRAKILL 0.185 = 51% at our base of 275%, and there are 25 nails per second)
        public static readonly float NailDamage = 0.24f / Piercer.DamagePerUnit;
        public static readonly float FodderDamage = 0.24f / Piercer.DamagePerUnit;
        public const float FireRate = 4f;
        public const float MagnetRadius = 25f;
        public const float ZapperDamage = 10f;
        public const float ZapperMaxDistance = 20f;

        public static void FireNail(GameObject owner, CharacterBody body, Ray aim, Transform muzzle, float spread, bool heated, float damage)
        {
            Vector3 pos = aim.origin + aim.direction;
            if (Physics.Raycast(aim, 1f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
            {
                pos = aim.origin;
            }
            Quaternion rot = Quaternion.LookRotation(aim.direction) * Quaternion.Euler(Random.Range(-spread / 3f, spread / 3f), Random.Range(-spread / 3f, spread / 3f), Random.Range(-spread / 3f, spread / 3f));
            GameObject prefab = heated ? V1Assets.nailHeated : V1Assets.nail;
            GameObject go = prefab ? Object.Instantiate(prefab, pos, Quaternion.LookRotation(-(rot * Vector3.forward))) : new GameObject("V1Nail");
            if (!prefab)
            {
                go.transform.SetPositionAndRotation(pos, rot);
            }
            V1Nail nail = go.AddComponent<V1Nail>();
            nail.body = body;
            nail.damage = damage;
            nail.heated = heated;
            nail.velocity = rot * Vector3.forward * NailSpeed;

            if (muzzle && V1Assets.nailMuzzleFlash)
            {
                // nailgun SpriteController: fades and shrinks at 37.5, the light fades at 200/s (5 times faster than the shotgun)
                V1FlashFx.Spawn(V1Assets.nailMuzzleFlash, muzzle.position, muzzle.rotation, 37.5f, 37.5f, 200f);
            }
        }
    }

    // The player's nailgun state (WeaponCharges + Nailgun fields).
    // alt = sawblade launcher (Sawblade Launcher, altVersion): the same three variants, but it fires saws (V1SawNail):
    // - rate 24 (0.24 s pause), the Attractor holds 10 saws (+0.5 per second while not firing);
    // - Overheat: every shot adds 0.125 heat, spread (up to 45) and pause grow with heat; one heatsink (0.125 per second);
    //   RMB is the "super saw": a heated saw that sticks in the enemy and hits round(heat * 3) times;
    // - JumpStart: without the clamp the pause is twice as long.
    public class NailgunData : MonoBehaviour
    {
        // nailgun variant in hand: 0 Attractor, 1 Overheat, 2 JumpStart, -1 another weapon
        public int variant = -1;
        public bool alt;
        public bool triggerHeld;
        public float heatUp;
        public float heatSinks = 2f;
        public float sawHeatSinks = 1f;
        public bool burnOut;
        public float ammo = 100f;
        public float saws = 10f;
        public float magnetCharge = 3f;
        public float zapperRecharge = 5f;
        public readonly List<V1Magnet> magnets = new List<V1Magnet>();
        public V1Zapper zapper;
        private float fireCooldown;
        private int barrel;
        private bool shotSuccessfully;
        private CharacterBody body;
        private SoundHandle steam;

        public const float SawFireRate = 24f;

        // heatsinks of the current nailgun form (the sawblade launcher has its own single one)
        public float Sinks
        {
            get => alt ? sawHeatSinks : heatSinks;
            set
            {
                if (alt)
                {
                    sawHeatSinks = value;
                }
                else
                {
                    heatSinks = value;
                }
            }
        }

        public static NailgunData Get(GameObject go)
        {
            NailgunData d = go.GetComponent<NailgunData>();
            return d ? d : go.AddComponent<NailgunData>();
        }

        private void Awake()
        {
            body = GetComponent<CharacterBody>();
        }

        private bool Authority => body && body.hasEffectiveAuthority;

        private void Update()
        {
            if (!Authority)
            {
                return;
            }
            float dt = Time.deltaTime;
            magnets.RemoveAll(m => !m);
            bool firing = triggerHeld && variant >= 0;
            // venting only runs on the Overheat: switching weapons stops it
            if (burnOut && (variant != 1 || alt))
            {
                burnOut = false;
                steam?.Stop();
                steam = null;
            }

            // heat
            if (variant == 1 && (burnOut || Sinks < 1f))
            {
                heatUp = Mathf.MoveTowards(heatUp, 0f, dt);
                if (burnOut && heatUp <= 0f)
                {
                    burnOut = false;
                    steam?.Stop();
                    steam = null;
                }
            }
            else if (firing && (heatUp < 1f || variant == 2))
            {
                if (variant == 2)
                {
                    heatUp = zapper && zapper.attached ? 1f : 0.33f;
                }
                else if (variant == 0)
                {
                    heatUp = 1f;
                }
                else if (!alt)
                {
                    heatUp = Mathf.MoveTowards(heatUp, 1f, dt * 0.55f);
                }
            }
            else if (heatUp > 0f && !firing)
            {
                // the sawblade launcher cools slowly during the pause between shots
                heatUp = Mathf.MoveTowards(heatUp, 0f, dt * (!alt ? 0.3f : fireCooldown <= 0f ? 0.2f : 0.03f));
            }

            // charges (WeaponCharges.Charge)
            if (heatSinks < 2f && !(variant == 1 && !alt && triggerHeld))
            {
                heatSinks = Mathf.MoveTowards(heatSinks, 2f, dt * 0.125f);
            }
            if (sawHeatSinks < 1f && !(variant == 1 && alt && triggerHeld))
            {
                sawHeatSinks = Mathf.MoveTowards(sawHeatSinks, 1f, dt * 0.125f);
            }
            if (!(variant == 0 && triggerHeld))
            {
                ammo = Mathf.MoveTowards(ammo, 100f, dt * 5f);
                saws = Mathf.MoveTowards(saws, 10f, dt * 0.5f);
            }
            if (magnets.Count < 3 && magnetCharge < 3 - magnets.Count)
            {
                magnetCharge = Mathf.MoveTowards(magnetCharge, 3 - magnets.Count, dt * 3f);
            }
            if (zapperRecharge < 5f)
            {
                zapperRecharge = Mathf.MoveTowards(zapperRecharge, 5f, dt);
            }
        }

        private float CurrentFireRate()
        {
            float rate = alt ? SawFireRate : Nailgun.FireRate;
            if (variant != 1)
            {
                if (alt && variant == 2 && !zapper)
                {
                    return rate * 2f;
                }
                return rate + 3.5f - heatUp * 3.5f;
            }
            if (burnOut)
            {
                return alt ? 3f : rate - 2.5f;
            }
            if (Sinks >= 1f)
            {
                return heatUp < 0.5f ? rate : rate + (heatUp - 0.5f) * (alt ? 65f : 7f);
            }
            return rate + (alt ? 50f : 10f);
        }

        private void FixedUpdate()
        {
            if (!Authority)
            {
                return;
            }
            if (fireCooldown > 0f)
            {
                fireCooldown = Mathf.MoveTowards(fireCooldown, 0f, Time.fixedDeltaTime * 100f);
                if (fireCooldown < 0.01f)
                {
                    fireCooldown = 0f;
                }
            }
            if (variant < 0 || !(triggerHeld || burnOut) || !body.healthComponent || !body.healthComponent.alive)
            {
                if (!triggerHeld)
                {
                    shotSuccessfully = false;
                }
                return;
            }
            if (fireCooldown != 0f)
            {
                return;
            }
            if (variant == 0 && Mathf.RoundToInt(alt ? saws : ammo) <= 0)
            {
                fireCooldown = CurrentFireRate() * 2f;
                V1Sounds.Play("NailNoAmmo", gameObject, shotSuccessfully ? 0.6f : 0.4f, shotSuccessfully ? 1.5f : 1f, true);
                shotSuccessfully = false;
                return;
            }
            Shoot();
        }

        private Ray Aim()
        {
            InputBankTest input = body.inputBank;
            return input ? new Ray(input.aimOrigin, input.aimDirection) : new Ray(transform.position, transform.forward);
        }

        private void Shoot()
        {
            fireCooldown = CurrentFireRate();
            shotSuccessfully = true;
            if (variant == 0)
            {
                if (alt)
                {
                    saws -= 1f;
                }
                else
                {
                    ammo -= 1f;
                }
            }
            V1ViewModel vm = V1ViewModel.Get(gameObject);
            vm?.Trigger("Shoot");
            barrel++;
            Transform muzzle = vm ? vm.NextShootPoint(barrel) : null;
            if (alt)
            {
                ShootSaw(muzzle);
                return;
            }

            float spread = variant == 1 ? 20f : 10f;
            float volume = 0.65f;
            float pitch = 1f;
            if (variant == 1)
            {
                if (burnOut)
                {
                    spread *= 2f;
                    pitch = 2f;
                }
                else if (heatSinks < 1f)
                {
                    pitch = 0.75f;
                    volume = 0.25f;
                }
            }
            float damage = variant == 0 ? Nailgun.FodderDamage : Nailgun.NailDamage;
            Nailgun.FireNail(gameObject, body, Aim(), muzzle, spread, variant == 1 && burnOut, damage);
            V1Sounds.Play("NailShot", gameObject, volume * 0.6f, pitch * Random.Range(0.95f, 1.05f), true);
        }

        // Nailgun.Shoot (altVersion): one saw, the Overheat's spread grows with heat
        private void ShootSaw(Transform muzzle)
        {
            float spread = 0f;
            if (variant == 1)
            {
                spread = Sinks < 1f ? 45f : Mathf.Lerp(0f, 45f, Mathf.Max(0f, heatUp - 0.25f));
            }
            float hitAmount = variant == 0 ? 3.9f : variant == 1 && Sinks >= 1f ? Mathf.Lerp(3f, 1f, heatUp) : 1f;
            Ray aim = Aim();
            Vector3 pos = aim.origin + aim.direction;
            if (Physics.Raycast(aim, 1f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
            {
                pos = aim.origin;
            }
            Quaternion rot = Quaternion.LookRotation(aim.direction) * Quaternion.Euler(Random.Range(-spread / 3f, spread / 3f), Random.Range(-spread / 3f, spread / 3f), Random.Range(-spread / 3f, spread / 3f));
            V1SawNail.Spawn(body, pos, rot, variant == 0 ? V1SawNail.Kind.Fodder : V1SawNail.Kind.Normal, hitAmount, 1);
            if (variant == 1 && Sinks >= 1f)
            {
                heatUp = Mathf.MoveTowards(heatUp, 1f, 0.125f);
            }
            if (muzzle && V1Assets.sawMuzzleFlash)
            {
                V1FlashFx.Spawn(V1Assets.sawMuzzleFlash, muzzle.position, muzzle.rotation, 37.5f, 37.5f, 200f);
            }
            V1Sounds.Play("SawLaunch", gameObject, 0.5f, Random.Range(0.95f, 1.05f), true);
        }

        // Overheat RMB: vent the heatsink; on the sawblade launcher the super saw (Nailgun.SuperSaw)
        public bool TryBurnOut()
        {
            if (burnOut || Sinks < 1f || heatUp < 0.1f)
            {
                return false;
            }
            if (alt)
            {
                Ray aim = Aim();
                Vector3 pos = aim.origin + aim.direction;
                if (Physics.Raycast(aim, 1f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
                {
                    pos = aim.origin;
                }
                V1SawNail.Spawn(body, pos, Quaternion.LookRotation(aim.direction), V1SawNail.Kind.Heated, 20.9f, Mathf.RoundToInt(heatUp * 3f));
                V1ViewModel vm = V1ViewModel.Get(gameObject);
                vm?.Trigger("SuperShoot");
                barrel++;
                Transform muzzle = vm ? vm.NextShootPoint(barrel) : null;
                if (muzzle && V1Assets.superSawMuzzleFlash)
                {
                    V1FlashFx.Spawn(V1Assets.superSawMuzzleFlash, muzzle.position, muzzle.rotation, 37.5f, 37.5f, 200f);
                }
                V1Sounds.Play("SuperSaw", gameObject, 0.7f, 1f, true);
                fireCooldown = CurrentFireRate();
                Sinks -= 1f;
                heatUp = 0f;
                return true;
            }
            burnOut = true;
            fireCooldown = 0f;
            heatSinks -= 1f;
            steam = V1Sounds.Play("Steam", gameObject, 0.5f, 1f, true, loop: true);
            return true;
        }

        private void OnDestroy()
        {
            steam?.Stop();
        }
    }


    // nailgun LMB: NailgunData fires while the button is held
    public class NailgunFire : BaseSkillState
    {
        private NailgunData data;

        public override void OnEnter()
        {
            base.OnEnter();
            data = NailgunData.Get(gameObject);
            data.triggerHeld = true;
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            StartAimMode(0.5f);
            if (isAuthority && (!inputBank || !inputBank.skill1.down))
            {
                outer.SetNextStateToMain();
            }
        }

        public override void OnExit()
        {
            if (data)
            {
                data.triggerHeld = false;
            }
            base.OnExit();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Any;
    }

    // Attractor RMB: magnet
    public class NailgunMagnetShot : BaseSkillState
    {
        public override void OnEnter()
        {
            base.OnEnter();
            if (isAuthority)
            {
                NailgunData data = NailgunData.Get(gameObject);
                if (data.magnetCharge >= 1f && V1Assets.magnet)
                {
                    Ray aim = GetAimRay();
                    Vector3 pos = aim.origin + aim.direction;
                    if (Physics.Raycast(aim, 1f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
                    {
                        pos = aim.origin;
                    }
                    GameObject go = Object.Instantiate(V1Assets.magnet, pos, Quaternion.LookRotation(aim.direction));
                    V1Magnet m = go.AddComponent<V1Magnet>();
                    m.body = characterBody;
                    m.velocity = aim.direction * 100f;
                    data.magnets.Add(m);
                    data.magnetCharge -= 1f;
                    V1ViewModel.Get(gameObject)?.Trigger("Shoot");
                    V1Sounds.Play("MagnetShot", gameObject, 0.6f, 1f, true);
                }
                else
                {
                    V1Sounds.Play("NailNoAmmo", gameObject, 0.4f, 1f, true);
                }
            }
            outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Any;
    }

    // Overheat RMB: vent the heatsink
    public class NailgunHeatsink : BaseSkillState
    {
        public override void OnEnter()
        {
            base.OnEnter();
            if (isAuthority)
            {
                NailgunData.Get(gameObject).TryBurnOut();
            }
            outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Any;
    }

    // JumpStart RMB: clamp on a cable
    public class NailgunZapperShot : BaseSkillState
    {
        public override void OnEnter()
        {
            base.OnEnter();
            if (isAuthority)
            {
                NailgunData data = NailgunData.Get(gameObject);
                if (data.zapperRecharge >= 5f && V1Assets.zapper)
                {
                    if (data.zapper)
                    {
                        data.zapper.Break(false);
                    }
                    Ray aim = GetAimRay();
                    GameObject go = Object.Instantiate(V1Assets.zapper, aim.origin, Quaternion.LookRotation(aim.direction));
                    V1Zapper z = go.AddComponent<V1Zapper>();
                    z.body = characterBody;
                    z.velocity = aim.direction * 100f;
                    data.zapper = z;
                    V1ViewModel.Get(gameObject)?.Trigger("Shoot");
                    V1Sounds.Play("MagnetShot", gameObject, 0.6f, 1.2f, true);
                }
                else
                {
                    V1Sounds.Play("NailNoAmmo", gameObject, 0.4f, 1f, true);
                }
            }
            outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Any;
    }

    // Nail: flies with gravity, sticks in an enemy (damage once), disappears from a wall after 1 s.
    // Near a magnet (25 m) it orbits it, turning its velocity 85 degrees from the direction to the magnet.
    public class V1Nail : MonoBehaviour
    {
        public CharacterBody body;
        public Vector3 velocity;
        public float damage;
        public bool heated;
        private bool hit;
        private float age;
        private int rotationDirection;
        private float removeAt = 5f;

        private void Start()
        {
            rotationDirection = Random.value < 0.5f ? -1 : 1;
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            age += dt;
            if (age > removeAt || age > 60f)
            {
                Destroy(gameObject);
                return;
            }
            if (hit)
            {
                return;
            }
            V1Magnet magnet = NearestMagnet();
            if (magnet)
            {
                removeAt = 60f;
                Vector3 to = (magnet.transform.position - transform.position).normalized;
                Vector3 target = Quaternion.Euler(0f, 85f * rotationDirection, 0f) * to * velocity.magnitude;
                velocity = Vector3.RotateTowards(velocity, target, float.PositiveInfinity, velocity.magnitude);
                // a nail at a magnet hits enemies next to it
                foreach (Collider c in Physics.OverlapSphere(transform.position, 0.5f, LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Collide))
                {
                    HurtBox box = c.GetComponent<HurtBox>();
                    if (box && V1Damage.IsEnemy(body, box.healthComponent))
                    {
                        HitEnemy(box, transform.position);
                        return;
                    }
                }
            }
            else
            {
                velocity += Vector3.down * Nailgun.NailGravity * dt;
            }

            Vector3 step = velocity * dt;
            float length = step.magnitude;
            if (length > 0.0001f && V1Physics.Cast(body, transform.position, 0f, step, length, out RaycastHit h, out _))
            {
                HurtBox box = h.collider.GetComponent<HurtBox>();
                if (box)
                {
                    if (V1Damage.IsEnemy(body, box.healthComponent))
                    {
                        HitEnemy(box, h.point);
                        return;
                    }
                }
                else if (!h.collider.isTrigger && !magnet)
                {
                    // in a wall: sticks out for a second and disappears
                    hit = true;
                    transform.position = h.point;
                    removeAt = age + 1f;
                    V1Sounds.Play("NailRicochet", gameObject, 0.2f * 0.5f, Random.Range(0.9f, 1.1f), false);
                    return;
                }
            }
            transform.position += step;
            if (velocity.sqrMagnitude > 0.01f)
            {
                // as in Nail.Update: the nail model points its tip along -Z, so turn it against the velocity
                transform.rotation = Quaternion.LookRotation(-velocity);
            }
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

        private void HitEnemy(HurtBox box, Vector3 point)
        {
            hit = true;
            V1Damage.Deal(body, box, damage, point, velocity.normalized * 30f, 0.2f, heated ? DamageType.IgniteOnHit : DamageType.Generic, heated ? DamageSource.Secondary : DamageSource.Primary);
            V1Sounds.Play("SawHit", gameObject, 0.2f * 0.5f, Random.Range(0.9f, 1.1f), false);
            // sticks in the body
            transform.position = point - velocity.normalized * 0.5f;
            transform.SetParent(box.transform, true);
            removeAt = age + 5f;
            TrailRenderer trail = GetComponentInChildren<TrailRenderer>();
            if (trail)
            {
                trail.enabled = false;
            }
        }
    }

    // Attractor magnet: flies with gravity, sticks to an enemy or a wall. If it hits nothing it disappears after 5 s,
    // on an enemy it lives while the enemy lives (no longer than 30 s).
    public class V1Magnet : MonoBehaviour
    {
        public CharacterBody body;
        public Vector3 velocity;
        private bool stuck;
        private HealthComponent onEnemy;
        private float age;
        private float notHitTime = 5f;

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            age += dt;
            if (age > 30f || (!stuck && age > notHitTime) || (onEnemy && !onEnemy.alive) || (stuck && !transform.parent && onEnemy))
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
            if (length > 0.0001f && V1Physics.Cast(body, transform.position, 0.2f, step, length, out RaycastHit h, out _))
            {
                HurtBox box = h.collider.GetComponent<HurtBox>();
                if (box && !V1Damage.IsEnemy(body, box.healthComponent))
                {
                    transform.position += step;
                    return;
                }
                if (!box && h.collider.isTrigger)
                {
                    transform.position += step;
                    return;
                }
                stuck = true;
                transform.position = h.point;
                if (box)
                {
                    onEnemy = box.healthComponent;
                    transform.SetParent(box.transform, true);
                }
                return;
            }
            transform.position += step;
        }
    }

    // JumpStart clamp on a cable
    public class V1Zapper : MonoBehaviour
    {
        public CharacterBody body;
        public Vector3 velocity;
        public bool attached;
        private HurtBox target;
        private float charge;
        private float breakTimer;
        private float age;
        private bool broken;
        private LineRenderer line;
        private SoundHandle loop;
        private SoundHandle warning;

        private Vector3 Anchor => body ? body.aimOrigin + Vector3.down * 0.4f : transform.position;
        public float Charge => charge;
        public float Distance => Vector3.Distance(Anchor, transform.position);

        private void Start()
        {
            line = GetComponentInChildren<LineRenderer>(true);
            if (line)
            {
                line.useWorldSpace = true;
                line.positionCount = 2;
            }
        }

        private void Update()
        {
            if (!body)
            {
                Break(false);
                return;
            }
            if (line)
            {
                line.SetPosition(0, Anchor);
                line.SetPosition(1, transform.position);
                float distance = Vector3.Distance(Anchor, transform.position);
                Color color = new Color(0.5f, 0.5f, 0.5f);
                if (breakTimer > 0f)
                {
                    color = breakTimer % 0.1f > 0.05f ? Color.black : Color.white;
                }
                else if (distance > Nailgun.ZapperMaxDistance - 10f)
                {
                    color = Color.Lerp(Color.red, color, (Nailgun.ZapperMaxDistance - distance) / 10f);
                }
                line.startColor = color;
                line.endColor = color;
            }
            if (attached)
            {
                charge = Mathf.MoveTowards(charge, 5f, Time.deltaTime);
                if (loop != null)
                {
                    loop.Pitch = 1f + charge / 5f;
                }
                if (charge >= 5f || !target || !target.healthComponent || !target.healthComponent.alive)
                {
                    Zap();
                }
            }
        }

        private void FixedUpdate()
        {
            if (broken || !body)
            {
                return;
            }
            float dt = Time.fixedDeltaTime;
            age += dt;
            if (!attached)
            {
                if (age > 5f)
                {
                    Break(false);
                    return;
                }
                velocity += Vector3.down * Nailgun.NailGravity * dt;
                Vector3 step = velocity * dt;
                float length = step.magnitude;
                if (length > 0.0001f && V1Physics.Cast(body, transform.position, 0.25f, step, length, out RaycastHit h, out _))
                {
                    HurtBox box = h.collider.GetComponent<HurtBox>();
                    if (box && V1Damage.IsEnemy(body, box.healthComponent))
                    {
                        attached = true;
                        target = box;
                        transform.position = h.point;
                        transform.SetParent(box.transform, true);
                        loop = V1Sounds.Play("ZapperLoop", gameObject, 0.5f, 1f, false, loop: true);
                        return;
                    }
                    if (!box && !h.collider.isTrigger)
                    {
                        Break(false);
                        return;
                    }
                }
                transform.position += step;
                return;
            }
            // the cable holds the player (ConfigurableJoint: limit maxDistance - 5 = 15 m): you can't go further, outward speed is cancelled
            if (body.characterMotor && body.hasEffectiveAuthority)
            {
                Vector3 fromZapper = body.corePosition - transform.position;
                if (fromZapper.magnitude > Nailgun.ZapperMaxDistance - 5f)
                {
                    Vector3 n = fromZapper.normalized;
                    float outward = Vector3.Dot(body.characterMotor.velocity, n);
                    if (outward > 0f)
                    {
                        body.characterMotor.velocity -= n * outward;
                    }
                }
            }
            // the cable snaps: further than 20 m or the enemy is hidden behind a wall
            float distance = Vector3.Distance(Anchor, transform.position);
            bool blocked = Physics.Linecast(Anchor, transform.position, LayerIndex.world.mask, QueryTriggerInteraction.Ignore);
            if (distance > Nailgun.ZapperMaxDistance || blocked)
            {
                if (breakTimer == 0f)
                {
                    warning = V1Sounds.Play("ZapperWarning", gameObject, 0.6f, blocked ? 2f : 1f, true, loop: true);
                }
                breakTimer = Mathf.MoveTowards(breakTimer, 1f, dt * (blocked ? 2f : 1f));
                if (breakTimer >= 1f)
                {
                    Break(false);
                }
                return;
            }
            if (breakTimer != 0f)
            {
                breakTimer = 0f;
                warning?.Stop();
                warning = null;
            }
        }

        private void Zap()
        {
            if (target && target.healthComponent && target.healthComponent.alive)
            {
                V1Damage.Deal(body, target, Nailgun.ZapperDamage, transform.position, Vector3.up * 1000f, 1f, DamageType.Shock5s, DamageSource.Secondary);
                NailgunData data = body ? body.GetComponent<NailgunData>() : null;
                if (data)
                {
                    data.zapperRecharge = 0f;
                }
                V1Sounds.Play("Explosion", gameObject, 0.4f, 1.6f, false);
            }
            Break(true);
        }

        public void Break(bool successful)
        {
            if (broken)
            {
                return;
            }
            broken = true;
            loop?.Stop();
            warning?.Stop();
            Destroy(gameObject);
        }
    }
}
