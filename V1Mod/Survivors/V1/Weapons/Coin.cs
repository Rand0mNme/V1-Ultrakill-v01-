using System.Collections.Generic;
using RoR2;
using UnityEngine;
using V1Mod.Modules;

namespace V1Mod.Survivors.V1.Weapons
{
    // V1 explosives that can be detonated with a shot or a coin (Grenade/Cannonball in ULTRAKILL):
    // the shotgun core, rockets, the S.R.S. cannonball.
    public interface IV1Explosive
    {
        CharacterBody Owner { get; }
        Transform transform { get; }
        // detonated by a shot: the shotgun core gives a big explosion (Grenade.Explode super), a rocket a normal one
        void ShotExplode();
    }

    public static class V1Explosives
    {
        public static readonly List<IV1Explosive> all = new List<IV1Explosive>();

        // did the revolver shot hit the explosive before a wall? (RevolverBeam: Grenade -> Explode)
        public static bool TryShoot(CharacterBody body, Ray aim, Vector3 muzzle, float radius)
        {
            float wall = 1000f;
            if (Physics.Raycast(aim, out RaycastHit h, 1000f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
            {
                wall = h.distance;
            }
            IV1Explosive best = null;
            float bestAlong = wall;
            foreach (IV1Explosive e in all)
            {
                if (e == null || !(e as Object) || e.Owner != body)
                {
                    continue;
                }
                Vector3 to = e.transform.position - aim.origin;
                float along = Vector3.Dot(to, aim.direction);
                if (along <= 0f || along >= bestAlong || (to - aim.direction * along).magnitude > radius)
                {
                    continue;
                }
                best = e;
                bestAlong = along;
            }
            if (best == null)
            {
                return false;
            }
            V1Effects.SpawnBeam(V1Assets.revolverBeam, muzzle, best.transform.position);
            best.ShotExplode();
            return true;
        }
    }

    // Shared memory of one reflection chain (CoinChainCache): whoever the chain already hit is not hit a second time
    public class CoinChain
    {
        public readonly HashSet<HealthComponent> beenHit = new HashSet<HealthComponent>();
    }

    // Marksman coin (Coin.cs)
    // - lives 5 s; breaks on touching the ground or a wall (Coin.OnCollisionEnter -> GetDeleted);
    // - double-hit windows: 0.35-0.417 s and after 1 s of flight (hitTimes = 2);
    // - a shot at the coin: 0.1 s later the beam goes on, by priority: another coin (power +1) -> your own explosive
    //   within 100 m (core, rocket, S.R.S. cannonball: they explode) -> the nearest visible enemy the chain hasn't hit yet
    //   (on the weak spot, damage = power, 2 for the first coin) -> a random direction;
    // - on a double hit the coin reflects a second time 0.05 s later, into another target;
    // - piercing beams (charged Piercer, railcannon) aren't spent: the beam itself flies from the coin to the target and gets
    //   stronger by power / 4;
    // - a Feedbacker punch on a coin (Punchflection): the coin hits an enemy (damage = power), and in its place a
    //   new coin appears with power +1 (up to 5), tossed upward at 25.
    public class V1Coin : MonoBehaviour
    {
        public static readonly List<V1Coin> active = new List<V1Coin>();
        public const float HitRadius = 1f;

        public CharacterBody body;
        public Vector3 velocity;
        public float power = 2f;
        public bool shot;
        public CoinChain chain;
        private float age;
        private int hitTimes = 1;
        private bool flashSoundPlayed;
        private float spawnTime;
        private GameObject chargeGlow;

        // for the first 0.1 s the coin collider is off: it can't be hit (StartCheckingSpeed)
        public bool Shootable => !shot && Time.time - spawnTime >= 0.1f;

        public static int WindowHitTimes(float age) => age >= 1f || (age >= 0.35f && age < 0.417f) ? 2 : 1;
        private float stillTime = 1f;
        private SoundHandle spin;
        private GameObject flash;
        // a beam the coin passes on (piercing shots): (from, to, damage bonus)
        private System.Action<Vector3, Vector3, float> beam;

        private void OnEnable()
        {
            active.Add(this);
        }

        private void OnDisable()
        {
            active.Remove(this);
            if (chargeGlow)
            {
                Destroy(chargeGlow);
            }
            spin?.Stop();
        }

        private void Start()
        {
            // in ULTRAKILL the coin spin sound is quiet: volume 0.1
            spin = V1Sounds.Play("CoinSpin", gameObject, 0.1f, 1f, false, loop: true);
        }

        public static V1Coin Spawn(CharacterBody body, Vector3 pos, Vector3 velocity, float power)
        {
            if (!V1Assets.coin)
            {
                return null;
            }
            GameObject go = Object.Instantiate(V1Assets.coin, pos, Random.rotation);
            V1Coin coin = go.AddComponent<V1Coin>();
            coin.body = body;
            coin.velocity = velocity;
            coin.power = power;
            coin.spawnTime = Time.time;
            return coin;
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            age += dt;
            if (shot)
            {
                return;
            }
            // TripleTime (0.35-0.417 s) and DoubleTime (after 1 s): the coin hits twice
            float t = Time.time - spawnTime;
            int newHitTimes = WindowHitTimes(t);
            // the flash sound a little early: audio output lags by about 40 ms and the window is only 67 ms
            if (!flashSoundPlayed && t >= 0.35f - 0.04f)
            {
                flashSoundPlayed = true;
                V1Sounds.Play("CoinFlip", gameObject, 0.5f, Random.Range(1.15f, 1.35f), false);
            }
            // split window (TripleTime, 0.35 s): CoinFlash flash: light, sound and a sprite facing the camera
            if (t >= 0.35f && t < 0.417f && hitTimes != 2 && V1Assets.coinFlash)
            {
                if (flash)
                {
                    Destroy(flash);
                }
                flash = Instantiate(V1Assets.coinFlash, transform.position, Quaternion.identity);
                flash.AddComponent<V1CoinFlashFx>().follow = transform;
            }
            // late window (DoubleTime, from 1 s until it falls): the blinking CoinCharge glow
            if (t >= 1f && !chargeGlow && V1Assets.coinCharge)
            {
                chargeGlow = Instantiate(V1Assets.coinCharge, transform.position, Quaternion.identity);
                V1CoinFlashFx fx = chargeGlow.AddComponent<V1CoinFlashFx>();
                fx.follow = transform;
                fx.blinking = true;
            }
            hitTimes = newHitTimes;
            if (age > 5f)
            {
                Destroy(gameObject);
                return;
            }
            velocity += Vector3.down * Nailgun.NailGravity * dt;
            Vector3 step = velocity * dt;
            float length = step.magnitude;
            // the coin collider turns on 0.1 s after the toss (StartCheckingSpeed)
            if (age > 0.1f && length > 0.0001f && Physics.SphereCast(transform.position, 0.15f, step / length, out RaycastHit h, length, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
            {
                // Coin.OnCollisionEnter: touching the ground or a wall breaks the coin (GetDeleted), the spin sound fades
                transform.position = h.point + h.normal * 0.15f;
                Destroy(gameObject);
                return;
            }
            else
            {
                transform.position += step;
            }
            transform.Rotate(720f * dt, 0f, 0f, Space.Self);
            // lying still: it disappears (Coin.Update: timeToDelete -= 10/s)
            if (age > 0.1f && velocity.magnitude < 1f)
            {
                stillTime -= dt * 10f;
                if (stillTime <= 0f)
                {
                    Destroy(gameObject);
                }
            }
            else
            {
                stillTime = 1f;
            }
        }

        // ---------- shots at coins ----------

        // the first coin on the beam's path before a wall
        public static V1Coin FindOnRay(CharacterBody body, Ray aim, float maxDistance)
        {
            if (active.Count == 0)
            {
                return null;
            }
            float wall = maxDistance;
            if (Physics.Raycast(aim, out RaycastHit h, maxDistance, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
            {
                wall = h.distance;
            }
            V1Coin best = null;
            float bestAlong = wall;
            foreach (V1Coin c in active)
            {
                if (!c || !c.Shootable || c.body != body)
                {
                    continue;
                }
                Vector3 to = c.transform.position - aim.origin;
                float along = Vector3.Dot(to, aim.direction);
                if (along <= 0f || along >= bestAlong || (to - aim.direction * along).magnitude > HitRadius)
                {
                    continue;
                }
                best = c;
                bestAlong = along;
            }
            return best;
        }

        // a normal revolver shot: beam into the coin, then the coin hits by itself
        public static bool TryShootCoin(CharacterBody body, Ray aim, Vector3 muzzle)
        {
            V1Coin coin = FindOnRay(body, aim, 1000f);
            if (!coin)
            {
                return false;
            }
            V1Effects.SpawnBeam(V1Assets.revolverBeam, muzzle, coin.transform.position);
            coin.Hit(coin.power, null, null);
            return true;
        }

        // a piercing beam: the beam goes into the coin, and the coin sends the same beam on (refire)
        public static bool TryBeamCoin(CharacterBody body, Ray aim, Vector3 muzzle, GameObject beamPrefab, System.Action<Vector3, Vector3, float> refire)
        {
            V1Coin coin = FindOnRay(body, aim, 1000f);
            if (!coin)
            {
                return false;
            }
            V1Effects.SpawnBeam(beamPrefab ? beamPrefab : V1Assets.revolverBeamSuper, muzzle, coin.transform.position);
            coin.Hit(coin.power, null, refire);
            return true;
        }

        public void Hit(float newPower, CoinChain sharedChain, System.Action<Vector3, Vector3, float> refire)
        {
            if (shot)
            {
                return;
            }
            shot = true;
            // the window is checked by the exact hit time, not the last physics tick
            hitTimes = WindowHitTimes(Time.time - spawnTime);
            power = newPower;
            chain = sharedChain ?? new CoinChain();
            beam = refire;
            if (chargeGlow)
            {
                Destroy(chargeGlow);
            }
            spin?.Stop();
            if (flash)
            {
                Destroy(flash);
            }
            // StyleHUD: hitting a coin is "fistful of dollar"
            V1Style.AddWeapon(body, 50, "RICOSHOT", 1f);
            V1Sounds.Play("NailRicochet", gameObject, 0.5f, 1f + (power - 2f) / 5f, false);
            Invoke(nameof(Reflect), 0.1f);
        }

        private bool Visible(Vector3 to)
        {
            return !Physics.Linecast(transform.position, to, LayerIndex.world.mask, QueryTriggerInteraction.Ignore);
        }

        private void Reflect()
        {
            if (!body)
            {
                Destroy(gameObject);
                return;
            }
            Vector3 from = transform.position;
            bool done = false;

            // 1. another coin
            V1Coin next = null;
            float best = float.MaxValue;
            foreach (V1Coin c in active)
            {
                if (!c || c == this || !c.Shootable || c.body != body)
                {
                    continue;
                }
                float d = Vector3.Distance(from, c.transform.position);
                if (d < best && Visible(c.transform.position))
                {
                    best = d;
                    next = c;
                }
            }
            if (next)
            {
                V1Effects.SpawnBeam(BeamPrefab(), from, next.transform.position);
                next.Hit(power + 1f, chain, beam);
                done = true;
            }

            // 2. your own explosive within 100 m of the player
            if (!done)
            {
                IV1Explosive bomb = null;
                best = float.MaxValue;
                foreach (IV1Explosive e in V1Explosives.all)
                {
                    if (e == null || !(e as Object) || e.Owner != body)
                    {
                        continue;
                    }
                    float d = Vector3.Distance(from, e.transform.position);
                    if (d < best && Vector3.Distance(body.corePosition, e.transform.position) < 100f && Visible(e.transform.position))
                    {
                        best = d;
                        bomb = e;
                    }
                }
                if (bomb != null)
                {
                    V1Effects.SpawnBeam(BeamPrefab(), from, bomb.transform.position);
                    bomb.ShotExplode();
                    done = true;
                }
            }

            // 3. the nearest visible enemy the chain hasn't hit yet
            if (!done)
            {
                HurtBox target = RevolverAlts.FindTarget(body, from, 1000f, Vector3.forward, 180f, chain.beenHit);
                if (target)
                {
                    chain.beenHit.Add(target.healthComponent);
                    Vector3 to = target.transform.position;
                    if (beam != null)
                    {
                        // a piercing beam flies on by itself and gets stronger (addedDamage += power / 4)
                        beam(from, (to - from).normalized, power / 4f);
                    }
                    else
                    {
                        V1Damage.Deal(body, target, power, to, (to - from).normalized * 1000f);
                        V1Effects.SpawnBeam(BeamPrefab(), from, to);
                        V1Effects.SpawnHit(V1Assets.laserHit, new Piercer.ShotEnd { point = to, normal = (from - to).normalized, enemy = target, hit = true });
                        V1HitStop.HitStop(0.05f);
                    }
                    done = true;
                }
            }

            // 4. nowhere: in a random direction
            if (!done)
            {
                Vector3 dir = Random.onUnitSphere;
                if (beam != null)
                {
                    beam(from, dir, power / 4f);
                }
                else
                {
                    Vector3 end = from + dir * 1000f;
                    if (Physics.Raycast(from, dir, out RaycastHit h, 1000f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
                    {
                        end = h.point;
                    }
                    V1Effects.SpawnBeam(BeamPrefab(), from, end);
                }
            }

            // double hit: once more 0.05 s later, into another target
            hitTimes--;
            if (hitTimes > 0)
            {
                Invoke(nameof(Reflect), 0.05f);
                return;
            }
            Destroy(gameObject);
        }

        private GameObject BeamPrefab()
        {
            return beam != null && V1Assets.revolverBeamSuper ? V1Assets.revolverBeamSuper : V1Assets.revolverBeam;
        }

        // ---------- punching a coin (Punch: alreadyHitCoin, Coin.Punchflection) ----------

        public static bool TryPunch(CharacterBody body, Ray aim)
        {
            V1Coin best = null;
            float bestDistance = 4f;
            foreach (V1Coin c in active)
            {
                if (!c || !c.Shootable || c.body != body)
                {
                    continue;
                }
                Vector3 to = c.transform.position - aim.origin;
                float along = Vector3.Dot(to, aim.direction);
                // SphereCast of radius 1 over 4 m, like a punch on enemies
                if (along < 0f || along > bestDistance || (to - aim.direction * along).magnitude > 1f + HitRadius)
                {
                    continue;
                }
                best = c;
                bestDistance = along;
            }
            if (!best)
            {
                return false;
            }
            best.Punchflection(aim.direction);
            return true;
        }

        private void Punchflection(Vector3 punchDir)
        {
            shot = true;
            CancelInvoke();
            Vector3 from = transform.position;
            Vector3 newCoinPos;
            bool missed = false;
            HurtBox target = RevolverAlts.FindTarget(body, from, 1000f, Vector3.forward, 180f);
            if (target)
            {
                newCoinPos = target.transform.position;
                V1Damage.Deal(body, target, power, newCoinPos, (newCoinPos - from).normalized * 1000f);
                V1Effects.SpawnBeam(V1Assets.revolverBeam, from, newCoinPos);
                V1Effects.SpawnHit(V1Assets.laserHit, new Piercer.ShotEnd { point = newCoinPos, normal = (from - newCoinPos).normalized, enemy = target, hit = true });
            }
            else
            {
                missed = true;
                if (Physics.Raycast(from, punchDir, out RaycastHit h, 1000f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
                {
                    newCoinPos = h.point - punchDir * 0.5f;
                }
                else
                {
                    // flew off into nothing: no new coin
                    V1Effects.SpawnBeam(V1Assets.revolverBeam, from, from + punchDir * 1000f);
                    Destroy(gameObject);
                    return;
                }
                V1Effects.SpawnBeam(V1Assets.revolverBeam, from, newCoinPos);
            }
            V1Sounds.Play("NailRicochet", gameObject, 0.5f, 1f + (power - 2f) / 5f, false);
            // a new coin at the punch spot, tossed upward; power +1 (above 5 only on a hit)
            float newPower = power < 5f || !missed ? power + 1f : power;
            Spawn(body, newCoinPos, Vector3.up * 25f, newPower);
            Destroy(gameObject);
        }
    }
}

namespace V1Mod.Survivors.V1.Weapons
{
    // CoinFlash: AlwaysLookAtCamera on the root, SpriteController on the sprite (random rotation, fades 1/s, shrinks 1/s),
    // RemoveOnTime 0.5 s. The flash stays at the coin.
    public class V1CoinFlashFx : MonoBehaviour
    {
        public Transform follow;
        // CoinCharge: the sprite blinks (SpriteController blinking, fadeSpeed 20) and lives while the coin lives
        public bool blinking;
        private float blinkSpeed = 20f;
        private SpriteRenderer sprite;
        private Light flashLight;
        private float age;

        private void Start()
        {
            sprite = GetComponentInChildren<SpriteRenderer>(true);
            if (sprite)
            {
                sprite.transform.Rotate(Vector3.forward * Random.Range(0, 360));
            }
            flashLight = GetComponentInChildren<Light>(true);
            if (!blinking)
            {
                Destroy(gameObject, 0.5f);
            }
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            age += dt;
            if (!follow && blinking)
            {
                Destroy(gameObject);
                return;
            }
            if (follow)
            {
                transform.position = follow.position;
            }
            Camera cam = null;
            foreach (CameraRigController rig in CameraRigController.readOnlyInstancesList)
            {
                if (rig && rig.sceneCam)
                {
                    cam = rig.sceneCam;
                    break;
                }
            }
            if (cam)
            {
                transform.rotation = Quaternion.LookRotation(cam.transform.position - transform.position);
            }
            if (sprite)
            {
                Color c = sprite.color;
                if (blinking)
                {
                    c.a -= blinkSpeed * dt;
                    if (c.a <= 0f)
                    {
                        blinkSpeed = -Mathf.Abs(blinkSpeed);
                    }
                    else if (c.a >= 1f)
                    {
                        blinkSpeed = Mathf.Abs(blinkSpeed);
                    }
                    c.a = Mathf.Clamp01(c.a);
                }
                else
                {
                    c.a -= dt;
                }
                sprite.color = c;
                Vector3 s = sprite.transform.localScale - Vector3.one * (blinking ? 0f : dt);
                sprite.transform.localScale = Vector3.Max(s, Vector3.zero);
            }
            if (flashLight)
            {
                if (!blinking)
                {
                    flashLight.intensity = Mathf.Max(0f, 20f * (1f - age / 0.5f));
                }
            }
        }
    }
}
