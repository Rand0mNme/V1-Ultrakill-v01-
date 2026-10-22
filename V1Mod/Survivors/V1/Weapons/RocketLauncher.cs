using System.Collections.Generic;
using EntityStates;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
using UnityEngine.Networking;
using V1Mod.Modules;

namespace V1Mod.Survivors.V1.Weapons
{
    // ULTRAKILL rocket launchers (RocketLauncher.cs, Grenade.cs with rocket = true, Cannonball.cs, WeaponCharges):
    // - hold LMB: a rocket once per second, flies straight at 100 m/s, explosion for 35 / 10 = 3.5 damage (like the shotgun core).
    // - Freezeframe (blue): RMB freezes / unfreezes all your rockets (new ones too). The freeze lasts up to 5 s
    //   and recharges twice as slowly.
    // - S.R.S. Cannon (green): hold RMB (once per 8 s) to charge a cannonball in 1 s; release and it flies at max(15, charge * 150)
    //   with gravity, hitting an enemy for min(5, speed * 0.15); if it kills the enemy it flies on, otherwise it breaks.
    // - Firestarter (red): hold RMB for a stream of gasoline (50 drops per second, a 1.3 s tank, recharges in 8 s).
    //   Soaked enemies ignite from any V1 explosion.
    public static class RocketLauncher
    {
        public const float RateOfFire = 1f;
        public const float RocketSpeed = 100f;
        public const float RocketDamage = 3.5f;
        public const float RocketRadius = 6f;
        public const float FreezeMax = 5f;

        public static void FireRocket(CharacterBody body, Ray aim, Transform muzzle)
        {
            GameObject go = V1Assets.rocket ? Object.Instantiate(V1Assets.rocket, aim.origin, Quaternion.LookRotation(aim.direction)) : new GameObject("V1Rocket");
            if (!V1Assets.rocket)
            {
                go.transform.SetPositionAndRotation(aim.origin, Quaternion.LookRotation(aim.direction));
            }
            V1Rocket r = go.AddComponent<V1Rocket>();
            r.body = body;
            RocketData.Get(body.gameObject).rockets.Add(r);
            if (muzzle && V1Assets.rocketMuzzleFlash)
            {
                V1FlashFx.Spawn(V1Assets.rocketMuzzleFlash, muzzle.position, muzzle.rotation, 4f, 0f, 160f);
            }
        }

        // V1 explosion (ULTRAKILL Explosion.cs):
        // - damage to enemies = damageUnits * enemyMultiplier (multiplier 2 for Explosion Super, 1.25 for Malicious), no falloff;
        // - enemies fly away from the center and up (vector from the explosion with y raised: +0.5, but at least 1), force * push
        //   (push 1.5 for the Knuckleblaster wave); light enemies are launched, heavy ones barely move (force is divided by mass);
        // - enemy projectiles in the sphere become yours and fly away from the explosion at 65 or more;
        // - the player: SelfBlast (the inner sphere hurts and launches; rocketWaveRadius is the rocket's harmless wave for rocket jumps).
        public const float EnemyKnockback = 2500f;

        public static void Explode(CharacterBody body, Vector3 pos, float damageUnits, float radius, GameObject prefab, float scale = 1f, float playerDamage = 35f,
            float rocketWaveRadius = 0f, bool boosted = false, float enemyMultiplier = 1f, float push = 1f, DamageSource source = DamageSource.Primary)
        {
            if (body && body.hasEffectiveAuthority)
            {
                new BlastAttack
                {
                    attacker = body.gameObject,
                    inflictor = body.gameObject,
                    teamIndex = body.teamComponent ? body.teamComponent.teamIndex : TeamIndex.Player,
                    baseDamage = damageUnits * enemyMultiplier * Piercer.DamagePerUnit * body.damage,
                    baseForce = EnemyKnockback * push,
                    bonusForce = Vector3.up * EnemyKnockback * 0.5f * push,
                    damageType = V1Damage.Combo(DamageType.Generic, source),
                    position = pos,
                    radius = radius,
                    falloffModel = BlastAttack.FalloffModel.None,
                    crit = body.RollCrit(),
                    procCoefficient = 1f,
                    attackerFiltering = AttackerFiltering.NeverHitSelf,
                }.Fire();
                V1Doused.IgniteAround(body, pos, radius);
            }
            DeflectProjectiles(body, pos, radius);
            V1ExplosionAnim.Spawn(prefab, pos, Quaternion.identity, scale);
            if (playerDamage > 0f || rocketWaveRadius > 0f || boosted)
            {
                SelfBlast.Apply(body, pos, radius, playerDamage, boosted, rocketWaveRadius);
            }
            V1Sounds.Play("Explosion", body ? body.gameObject : null, 0.8f, Random.Range(0.9f, 1.1f), false);
        }

        // enemy projectiles in the explosion sphere: become yours, lose homing, fly away from the explosion at 65 m/s or more (server only)
        public static void DeflectProjectiles(CharacterBody body, Vector3 pos, float radius)
        {
            if (!NetworkServer.active || !body)
            {
                return;
            }
            TeamIndex team = body.teamComponent ? body.teamComponent.teamIndex : TeamIndex.Player;
            var done = new HashSet<ProjectileController>();
            foreach (Collider c in Physics.OverlapSphere(pos, radius, LayerIndex.projectile.mask, QueryTriggerInteraction.Collide))
            {
                ProjectileController pc = c.GetComponentInParent<ProjectileController>();
                if (!pc || !done.Add(pc) || (pc.teamFilter && pc.teamFilter.teamIndex == team))
                {
                    continue;
                }
                pc.owner = body.gameObject;
                if (pc.teamFilter)
                {
                    pc.teamFilter.teamIndex = team;
                }
                ProjectileTargetComponent ptc = pc.GetComponent<ProjectileTargetComponent>();
                if (ptc)
                {
                    ptc.target = null;
                }
                Vector3 dir = pc.transform.position - pos;
                dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.up;
                pc.transform.rotation = Quaternion.LookRotation(dir);
                ProjectileSimple ps = pc.GetComponent<ProjectileSimple>();
                Rigidbody rb = pc.GetComponent<Rigidbody>();
                float speed = Mathf.Max(65f, ps ? ps.desiredForwardSpeed : (rb ? rb.velocity.magnitude : 0f));
                if (ps)
                {
                    ps.desiredForwardSpeed = speed;
                }
                if (rb)
                {
                    rb.velocity = dir * speed;
                }
            }
        }
    }

    // rocket launcher charges (WeaponCharges.rocket*)
    public class RocketData : MonoBehaviour
    {
        public float cooldown;
        public bool frozen;
        public float freezeTime = RocketLauncher.FreezeMax;
        public float cannonCharge = 1f;
        public float napalmFuel = 1f;
        public readonly List<V1Rocket> rockets = new List<V1Rocket>();
        private float idleFrozen;
        private SoundHandle tick;
        private CharacterBody body;

        public static RocketData Get(GameObject go)
        {
            RocketData d = go.GetComponent<RocketData>();
            return d ? d : go.AddComponent<RocketData>();
        }

        private void Awake()
        {
            body = GetComponent<CharacterBody>();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            rockets.RemoveAll(r => !r);
            if (cooldown > 0f)
            {
                cooldown = Mathf.MoveTowards(cooldown, 0f, dt);
            }
            if (cannonCharge < 1f)
            {
                cannonCharge = Mathf.MoveTowards(cannonCharge, 1f, dt * 0.125f);
            }
            if (napalmFuel < 1f)
            {
                napalmFuel = Mathf.MoveTowards(napalmFuel, 1f, dt * 0.125f);
            }
            freezeTime = Mathf.MoveTowards(freezeTime, frozen ? 0f : RocketLauncher.FreezeMax, frozen ? dt : dt / 2f);
            if (frozen)
            {
                idleFrozen = rockets.Count > 0 ? 0f : idleFrozen + dt;
                if (freezeTime <= 0f || idleFrozen > 0.5f)
                {
                    SetFrozen(false);
                }
            }
        }

        public void SetFrozen(bool value)
        {
            if (frozen == value)
            {
                return;
            }
            frozen = value;
            idleFrozen = 0f;
            bool local = body && body.hasEffectiveAuthority;
            if (frozen)
            {
                V1Sounds.Play("FreezeStart", gameObject, 0.7f, 1f, local);
                tick = V1Sounds.Play("FreezeTick", gameObject, 0.5f, 1f, local, loop: true);
            }
            else
            {
                V1Sounds.Play("FreezeStop", gameObject, 0.7f, 1f, local);
                tick?.Stop();
                tick = null;
            }
        }

        private void OnDestroy()
        {
            tick?.Stop();
        }
    }

    // rocket launcher LMB: a rocket every second while held
    public class RocketLauncherFire : BaseSkillState
    {
        public override void OnEnter()
        {
            base.OnEnter();
            RocketData data = RocketData.Get(gameObject);
            if (isAuthority && data.cooldown <= 0f)
            {
                data.cooldown = RocketLauncher.RateOfFire;
                StartAimMode(1f);
                V1ViewModel vm = V1ViewModel.Get(gameObject);
                RocketLauncher.FireRocket(characterBody, GetAimRay(), vm ? vm.ShootPoint : null);
                vm?.Trigger("Fire");
                V1Sounds.Play("RocketFire", gameObject, 0.6f, Random.Range(0.9f, 1.1f), true);
                Piercer.FovKick(gameObject, V1Camera.DefaultFov / 40f);
            }
            outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Any;
    }

    // Freezeframe RMB
    public class RocketFreeze : BaseSkillState
    {
        public override void OnEnter()
        {
            base.OnEnter();
            if (isAuthority)
            {
                RocketData data = RocketData.Get(gameObject);
                if (data.frozen)
                {
                    data.SetFrozen(false);
                }
                else if (data.freezeTime > 0f)
                {
                    data.SetFrozen(true);
                }
            }
            outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Any;
    }

    // S.R.S. Cannon RMB: hold to charge, release to fire the cannonball
    public class RocketCannonball : BaseSkillState
    {
        private float charge;
        private SoundHandle sound;
        private bool done;

        public override void OnEnter()
        {
            base.OnEnter();
            if (RocketData.Get(gameObject).cannonCharge < 1f)
            {
                done = true;
                if (isAuthority)
                {
                    outer.SetNextStateToMain();
                }
                return;
            }
            sound = V1Sounds.Play("CannonCharge", gameObject, 0.6f, 0.5f, isAuthority, loop: true);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (done || !isAuthority)
            {
                return;
            }
            StartAimMode(1f);
            charge = Mathf.MoveTowards(charge, 1f, Time.fixedDeltaTime);
            if (sound != null)
            {
                sound.Pitch = charge + 0.5f;
            }
            V1ViewModel.Get(gameObject)?.SetShake(charge / 100f);
            if (!inputBank.skill2.down)
            {
                done = true;
                RocketData data = RocketData.Get(gameObject);
                data.cannonCharge = 0f;
                data.cooldown = RocketLauncher.RateOfFire;
                Ray aim = GetAimRay();
                Vector3 pos = aim.origin + aim.direction;
                if (Physics.Raycast(aim, 1f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
                {
                    pos = aim.origin;
                }
                GameObject go = V1Assets.cannonball ? Object.Instantiate(V1Assets.cannonball, pos, Quaternion.LookRotation(aim.direction)) : new GameObject("V1Cannonball");
                go.transform.position = pos;
                V1Cannonball c = go.AddComponent<V1Cannonball>();
                c.body = characterBody;
                Vector3 playerVelocity = characterMotor ? characterMotor.velocity : Vector3.zero;
                c.velocity = (1f - charge) * playerVelocity + aim.direction * Mathf.Max(15f, charge * 150f);
                V1ViewModel.Get(gameObject)?.Trigger("Fire");
                V1Sounds.Play("RocketFire", gameObject, 0.6f, Random.Range(0.6f, 0.8f), true);
                outer.SetNextStateToMain();
            }
        }

        public override void OnExit()
        {
            sound?.Stop();
            V1ViewModel.Get(gameObject)?.SetShake(0f);
            base.OnExit();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }

    // Firestarter RMB: hold for a stream of gasoline
    public class RocketNapalm : BaseSkillState
    {
        private float projectileCooldown;
        private SoundHandle loop;
        private bool firing;

        public override void OnEnter()
        {
            base.OnEnter();
            RocketData data = RocketData.Get(gameObject);
            if (data.napalmFuel < 0.25f)
            {
                V1Sounds.Play("NailNoAmmo", gameObject, 0.4f, 1f, isAuthority);
                if (isAuthority)
                {
                    outer.SetNextStateToMain();
                }
                return;
            }
            firing = true;
            V1Sounds.Play("NapalmStart", gameObject, 0.6f, Random.Range(0.9f, 1.1f), isAuthority);
            loop = V1Sounds.Play("NapalmLoop", gameObject, 0.5f, Random.Range(0.9f, 1.1f), isAuthority, loop: true);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!firing || !isAuthority)
            {
                return;
            }
            RocketData data = RocketData.Get(gameObject);
            if (!inputBank.skill2.down || data.napalmFuel <= 0f)
            {
                outer.SetNextStateToMain();
                return;
            }
            StartAimMode(0.5f);
            projectileCooldown = Mathf.MoveTowards(projectileCooldown, 0f, Time.fixedDeltaTime);
            if (projectileCooldown > 0f)
            {
                return;
            }
            projectileCooldown = 0.02f;
            data.napalmFuel -= 0.015f;
            Ray aim = GetAimRay();
            Vector3 pos = aim.origin + aim.direction;
            if (Physics.Raycast(aim, 1f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
            {
                pos = aim.origin;
            }
            Quaternion rot = Quaternion.LookRotation(aim.direction) * Quaternion.Euler(Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.Range(-1f, 1f));
            GameObject go = V1Assets.gasoline ? Object.Instantiate(V1Assets.gasoline, pos, rot) : new GameObject("V1Gasoline");
            go.transform.SetPositionAndRotation(pos, rot);
            V1Gasoline g = go.AddComponent<V1Gasoline>();
            g.body = characterBody;
            g.velocity = rot * Vector3.forward * 150f;
            V1ViewModel.Get(gameObject)?.Trigger("Spray");
        }

        public override void OnExit()
        {
            if (firing)
            {
                loop?.Stop();
                V1Sounds.Play("NapalmStop", gameObject, 0.6f, Random.Range(0.9f, 1.1f), isAuthority);
            }
            base.OnExit();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
    }

    // Rocket: flies straight, stays still while frozen, explodes on contact
    public class V1Rocket : MonoBehaviour, IV1Explosive
    {
        public CharacterBody body;
        private float age;
        private SoundHandle loop;
        private RocketData data;

        public CharacterBody Owner => body;
        private void OnEnable() => V1Explosives.all.Add(this);
        private void OnDisable() => V1Explosives.all.Remove(this);
        public void ShotExplode() => Explode(transform.position);

        private void Start()
        {
            data = body ? RocketData.Get(body.gameObject) : null;
            loop = V1Sounds.Play("RocketLoop", gameObject, 0.4f, 1f, false, loop: true);
        }

        private void FixedUpdate()
        {
            if (!body)
            {
                Destroy(gameObject);
                return;
            }
            float dt = Time.fixedDeltaTime;
            bool frozen = data && data.frozen;
            if (loop != null)
            {
                loop.Pitch = frozen ? 0.5f : 1f;
            }
            if (frozen)
            {
                return;
            }
            age += dt;
            if (age > 20f)
            {
                Explode(transform.position);
                return;
            }
            Vector3 step = transform.forward * RocketLauncher.RocketSpeed * dt;
            if (V1Physics.Cast(body, transform.position, 0.4f, transform.forward, step.magnitude, out RaycastHit h, out _))
            {
                HurtBox box = h.collider.GetComponent<HurtBox>();
                if (box && V1Damage.IsEnemy(body, box.healthComponent))
                {
                    // Grenade.Collision (rocket): an airborne (non-flying) enemy gets a big explosion (Explosion Super)
                    CharacterBody enemy = box.healthComponent.body;
                    bool airborne = enemy && enemy.characterMotor && !enemy.characterMotor.isGrounded;
                    Explode(h.point - transform.forward * 0.3f, airborne ? Kind.Super : Kind.Normal);
                    return;
                }
                if (!box && !h.collider.isTrigger)
                {
                    // into a wall or floor: a "harmless" rocket explosion whose outer wave gives a rocket jump
                    Explode(h.point - transform.forward * 0.3f, Kind.Harmless);
                    return;
                }
            }
            transform.position += step;
        }

        public enum Kind { Normal, Harmless, Super }

        // Grenade.Explode for a rocket: Explosion (6/8), Explosion Rocket Harmless (6 + wave 8 for the rocket jump),
        // Explosion Super (12, damage x2 to enemies); a frozen rocket explodes 1.5 times bigger
        private void Explode(Vector3 pos, Kind kind = Kind.Normal)
        {
            float size = data && data.frozen ? 1.5f : 1f;
            if (kind == Kind.Super)
            {
                RocketLauncher.Explode(body, pos, RocketLauncher.RocketDamage, 12f * size, V1Assets.explosion, 2f * size, 35f, enemyMultiplier: 2f);
            }
            else
            {
                RocketLauncher.Explode(body, pos, RocketLauncher.RocketDamage, RocketLauncher.RocketRadius * size, V1Assets.explosion, size, 35f,
                    kind == Kind.Harmless ? 8f * size : 0f);
            }
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            loop?.Stop();
        }
    }

    // S.R.S. cannonball: flies with gravity, hits enemies on the way
    public class V1Cannonball : MonoBehaviour, IV1Explosive
    {
        public CharacterBody Owner => body;
        private void OnEnable() => V1Explosives.all.Add(this);
        private void OnDisable() => V1Explosives.all.Remove(this);

        // a shot or a coin: the cannonball explodes (Cannonball.Explode)
        public void ShotExplode()
        {
            RocketLauncher.Explode(body, transform.position, RocketLauncher.RocketDamage, RocketLauncher.RocketRadius, V1Assets.explosion, source: DamageSource.Secondary);
            Destroy(gameObject);
        }
        public CharacterBody body;
        public Vector3 velocity;
        private float age;
        private readonly HashSet<HealthComponent> hitEnemies = new HashSet<HealthComponent>();
        // durability 2: kills use it up, at zero the cannonball breaks (Cannonball.durability)
        private int durability = 2;
        private bool launched;

        // a Feedbacker punch on the cannonball (Punch -> Cannonball.Launch): flies straight at 150 m/s without gravity
        public void Launch(Vector3 dir)
        {
            launched = true;
            hitEnemies.Clear();
            velocity = dir.normalized * 150f;
            age = 0f;
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            age += dt;
            if (!body || age > 15f)
            {
                Break();
                return;
            }
            if (!launched)
            {
                velocity += Vector3.down * Nailgun.NailGravity * dt;
            }
            Vector3 step = velocity * dt;
            float length = step.magnitude;
            if (length > 0.0001f)
            {
                RaycastHit[] hits = Physics.SphereCastAll(transform.position, 0.6f, step / length, length, LayerIndex.world.mask | LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Collide);
                System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                foreach (RaycastHit h in hits)
                {
                    HurtBox box = h.collider.GetComponent<HurtBox>();
                    if (box)
                    {
                        HealthComponent hc = box.healthComponent;
                        if (!V1Damage.IsEnemy(body, hc) || !hitEnemies.Add(hc))
                        {
                            continue;
                        }
                        float speed = velocity.magnitude;
                        if (speed > 10f)
                        {
                            V1Damage.Deal(body, box, Mathf.Min(5f, speed * 0.15f), transform.position, velocity.normalized * 3000f, source: DamageSource.Secondary);
                        }
                        V1Sounds.Play("SawHit", gameObject, 0.5f, 0.6f, false);
                        // killed: flies on; otherwise it breaks
                        if (hc.alive)
                        {
                            Break();
                            return;
                        }
                        durability--;
                        if (durability <= 0)
                        {
                            Break();
                            return;
                        }
                        continue;
                    }
                    if (!h.collider.isTrigger)
                    {
                        transform.position = h.point;
                        Break();
                        return;
                    }
                }
            }
            transform.position += step;
        }

        private void Break()
        {
            if (V1Assets.dustBig)
            {
                Object.Destroy(Object.Instantiate(V1Assets.dustBig, transform.position, Quaternion.identity), 2f);
            }
            V1Sounds.Play("LandingHeavy", gameObject, 0.6f, 1.2f, false);
            Destroy(gameObject);
        }
    }

    // Gasoline drop: a soaked enemy ignites from explosions
    public class V1Gasoline : MonoBehaviour
    {
        public CharacterBody body;
        public Vector3 velocity;
        private float age;

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            age += dt;
            if (!body || age > 5f)
            {
                Destroy(gameObject);
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
                    V1Doused.Douse(box.healthComponent);
                    Destroy(gameObject);
                    return;
                }
                if (!box && !h.collider.isTrigger)
                {
                    Destroy(gameObject);
                    return;
                }
            }
            transform.position += step;
        }
    }

    // "Soaked in gasoline" mark (Flammable in ULTRAKILL)
    public class V1Doused : MonoBehaviour
    {
        private float time;
        private static readonly List<V1Doused> all = new List<V1Doused>();
        private HealthComponent health;

        public static void Douse(HealthComponent hc)
        {
            if (!hc)
            {
                return;
            }
            V1Doused d = hc.GetComponent<V1Doused>();
            if (!d)
            {
                d = hc.gameObject.AddComponent<V1Doused>();
                d.health = hc;
                all.Add(d);
            }
            d.time = 15f;
        }

        // the explosion ignites soaked enemies within its radius
        public static void IgniteAround(CharacterBody attacker, Vector3 pos, float radius)
        {
            if (!NetworkServer.active)
            {
                return;
            }
            IgniteAroundServer(attacker, pos, radius);
        }

        private static void IgniteAroundServer(CharacterBody attacker, Vector3 pos, float radius)
        {
            for (int i = all.Count - 1; i >= 0; i--)
            {
                V1Doused d = all[i];
                if (!d || !d.health || !d.health.alive)
                {
                    continue;
                }
                if (Vector3.Distance(d.health.transform.position, pos) <= radius + 3f)
                {
                    InflictDotInfo dot = new InflictDotInfo
                    {
                        victimObject = d.health.gameObject,
                        attackerObject = attacker.gameObject,
                        dotIndex = DotController.DotIndex.Burn,
                        duration = 4f,
                        damageMultiplier = 2f,
                    };
                    DotController.InflictDot(ref dot);
                    Destroy(d);
                }
            }
        }

        private void Update()
        {
            time -= Time.deltaTime;
            if (time <= 0f)
            {
                Destroy(this);
            }
        }

        private void OnDestroy()
        {
            all.Remove(this);
        }
    }
}
