using System.Collections.Generic;
using EntityStates;
using RoR2;
using UnityEngine;
using V1Mod.Modules;

namespace V1Mod.Survivors.V1.Weapons
{
    // Marksman and Sharpshooter revolvers (Revolver.cs gunVariation 1 and 2, Coin.cs, RevolverBeam.cs, WeaponCharges.rev1/rev2charge).
    // LMB on both is the normal revolver shot.
    // - Marksman (green): RMB tosses a coin (4 charges, 4 s each). A shot at a coin reflects into the nearest
    //   other coin (+1 power) or an enemy: damage = coin power (2, more in a chain). In the 0.35-0.417 s window and after 1 s
    //   of flight the coin hits twice.
    // - Sharpshooter (red): hold RMB (3 charges, 6.7 s each) to spin up at 75 per second; release (from 25) or LMB fires a beam
    //   that pierces all enemies for 1 damage and bounces off walls up to 3 times (one per 25 spin),
    //   after a bounce it homes on the nearest visible enemy.
    public static class RevolverAlts
    {
        public const float CoinRadius = 1f;
        public const float SharpChargeRate = 75f;

        public static HurtBox FindTarget(CharacterBody body, Vector3 from, float maxDistance, Vector3 preferDir, float maxAngle, ICollection<HealthComponent> exclude = null)
        {
            HurtBox best = null;
            float bestScore = float.MaxValue;
            TeamIndex team = body.teamComponent ? body.teamComponent.teamIndex : TeamIndex.Player;
            foreach (TeamComponent tc in TeamComponent.GetTeamMembers(TeamIndex.Monster))
            {
                Consider(tc);
            }
            foreach (TeamComponent tc in TeamComponent.GetTeamMembers(TeamIndex.Void))
            {
                Consider(tc);
            }
            foreach (TeamComponent tc in TeamComponent.GetTeamMembers(TeamIndex.Lunar))
            {
                Consider(tc);
            }
            return best;

            void Consider(TeamComponent tc)
            {
                if (!tc || tc.teamIndex == team || !tc.body || !tc.body.healthComponent || !tc.body.healthComponent.alive || !tc.body.hurtBoxGroup || (exclude != null && exclude.Contains(tc.body.healthComponent)))
                {
                    return;
                }
                // weak spot (head), otherwise the main hitbox
                HurtBox box = tc.body.hurtBoxGroup.mainHurtBox;
                foreach (HurtBox h in tc.body.hurtBoxGroup.hurtBoxes)
                {
                    if (h && h.isSniperTarget)
                    {
                        box = h;
                        break;
                    }
                }
                if (!box)
                {
                    return;
                }
                Vector3 to = box.transform.position - from;
                float d = to.magnitude;
                if (d > maxDistance || (maxAngle < 180f && Vector3.Angle(preferDir, to) > maxAngle))
                {
                    return;
                }
                if (Physics.Linecast(from, box.transform.position, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
                {
                    return;
                }
                if (d < bestScore)
                {
                    bestScore = d;
                    best = box;
                }
            }
        }

        // Sharpshooter beam (RevolverBeam with ricochetAmount): pierces enemies, bounces off walls; each next
        // segment appears 0.1 s later, hits each enemy one more time (maxHitsPerTarget++) and aims
        // at a coin or an enemy in a "tube" of radius 5 m along the bounce direction (RicochetAimAssist)
        public static void FireSharp(CharacterBody body, Ray aim, Vector3 muzzle, int ricochets, int hitsPerTarget = 1)
        {
            var go = new GameObject("V1SharpBeam");
            V1SharpBeam beam = go.AddComponent<V1SharpBeam>();
            beam.body = body;
            beam.Fire(aim.origin, aim.direction, muzzle, ricochets, hitsPerTarget);
        }
    }

    // charges (WeaponCharges.rev1charge / rev2charge)
    public class RevolverAltData : MonoBehaviour
    {
        public float coinCharge = 400f;
        public float sharpCharge = 300f;
        public float twirlCharge;

        public static RevolverAltData Get(GameObject go)
        {
            RevolverAltData d = go.GetComponent<RevolverAltData>();
            return d ? d : go.AddComponent<RevolverAltData>();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (coinCharge < 400f)
            {
                coinCharge = Mathf.MoveTowards(coinCharge, 400f, 25f * dt);
            }
            if (sharpCharge < 300f)
            {
                sharpCharge = Mathf.MoveTowards(sharpCharge, 300f, 15f * dt);
            }
        }
    }

    // Marksman RMB: toss a coin
    public class MarksmanCoin : BaseSkillState
    {
        public override void OnEnter()
        {
            base.OnEnter();
            if (isAuthority)
            {
                RevolverAltData data = RevolverAltData.Get(gameObject);
                if (data.coinCharge >= 100f && V1Assets.coin)
                {
                    data.coinCharge -= 100f;
                    Ray aim = GetAimRay();
                    Vector3 up = Vector3.up;
                    Vector3 pos = aim.origin - Vector3.Cross(Vector3.Cross(aim.direction, up), aim.direction).normalized * 0.5f;
                    Vector3 playerVelocity = characterMotor ? characterMotor.velocity : Vector3.zero;
                    V1Coin.Spawn(characterBody, pos, aim.direction * 20f + up * 15f + playerVelocity, 2f);
                    V1Sounds.Play("CoinFlip", gameObject, 0.6f, Random.Range(0.9f, 1.1f), true);
                    // Punch.CoinFlip: the hand flips the coin
                    V1ViewModel.Get(gameObject)?.ArmTrigger("CoinFlip");
                }
            }
            outer.SetNextStateToMain();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Any;
    }

    // Sharpshooter RMB: hold to spin up, release or LMB for a beam with bounces
    public class SharpshooterCharge : BaseSkillState
    {
        private RevolverAltData data;
        private SoundHandle twirl;
        private bool fired;

        public override void OnEnter()
        {
            base.OnEnter();
            data = RevolverAltData.Get(gameObject);
            if (data.sharpCharge < 100f)
            {
                fired = true;
                if (isAuthority)
                {
                    outer.SetNextStateToMain();
                }
                return;
            }
            data.twirlCharge = 0f;
            twirl = V1Sounds.Play("Twirling", gameObject, 0.4f, 0.5f, isAuthority, loop: true);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (fired || !isAuthority)
            {
                return;
            }
            StartAimMode(1f);
            data.twirlCharge = Mathf.Min(100f, data.twirlCharge + RevolverAlts.SharpChargeRate * Time.fixedDeltaTime);
            float twirlLevel = Mathf.Min(3f, Mathf.Floor(data.twirlCharge / 25f)) + 1f;
            if (twirl != null)
            {
                twirl.Pitch = 0.5f + twirlLevel / 2f;
            }
            V1ViewModel.Get(gameObject)?.SetTwirl(true, data.twirlCharge);
            bool released = !inputBank.skill2.down;
            if ((released || inputBank.skill1.down) && data.twirlCharge >= 25f)
            {
                Fire();
                outer.SetNextStateToMain();
                return;
            }
            if (released)
            {
                outer.SetNextStateToMain();
            }
        }

        private void Fire()
        {
            fired = true;
            int ricochets = Mathf.Min(3, Mathf.FloorToInt(data.twirlCharge / 25f));
            data.sharpCharge -= 100f;
            data.twirlCharge = 0f;
            Ray aim = GetAimRay();
            V1ViewModel vm = V1ViewModel.Get(gameObject);
            CharacterBody b = characterBody;
            Vector3 muzzle = vm?.ShootPointPosition ?? aim.origin;
            // coins on the path and bounces are handled by the beam itself (V1SharpBeam)
            RevolverAlts.FireSharp(b, aim, muzzle, ricochets);
            vm?.Trigger("ChargeShoot");
            Piercer.FovKick(gameObject, V1Camera.DefaultFov / 20f);
            V1Sounds.Play("TwirlShot", gameObject, 0.6f, 1f, true);
            V1Sounds.Play("Shoot2b3", gameObject, 0.4f, 1.2f, true);
        }

        public override void OnExit()
        {
            twirl?.Stop();
            V1ViewModel.Get(gameObject)?.SetTwirl(false, 0f);
            base.OnExit();
        }

        public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
    }

}

namespace V1Mod.Survivors.V1.Weapons
{
    // Sharpshooter beam segments with a 0.1 s delay between bounces (DelayedActivationManager)
    public class V1SharpBeam : MonoBehaviour
    {
        public CharacterBody body;

        public void Fire(Vector3 origin, Vector3 dir, Vector3 visualStart, int ricochets, int hitsPerTarget)
        {
            StartCoroutine(Run(origin, dir, visualStart, ricochets, hitsPerTarget));
        }

        private System.Collections.IEnumerator Run(Vector3 origin, Vector3 dir, Vector3 visualStart, int ricochets, int hitsPerTarget)
        {
            while (body)
            {
                // a coin on the path: the beam goes into it and the coin sends it on (a ricocheting beam gets +2 hits, at most 3)
                Ray ray = new Ray(origin, dir);
                V1Coin coin = V1Coin.FindOnRay(body, ray, 1000f);
                if (coin)
                {
                    V1Effects.SpawnBeam(V1Assets.revolverBeamSharp ? V1Assets.revolverBeamSharp : V1Assets.revolverBeamSuper, visualStart, coin.transform.position);
                    int rico = ricochets;
                    int hits = Mathf.Min(hitsPerTarget + 2, 3);
                    CharacterBody b = body;
                    coin.Hit(coin.power, null, (o, d, bonus) => RevolverAlts.FireSharp(b, new Ray(o, d), o, rico, hits));
                    break;
                }
                float length = 1000f;
                bool wallHit = Physics.Raycast(origin, dir, out RaycastHit wall, 1000f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore);
                if (wallHit)
                {
                    length = wall.distance;
                }
                // every segment hits all enemies on its path again (hitEids.Clear)
                var hitEnemies = new HashSet<HealthComponent>();
                bool any = false;
                foreach (RaycastHit h in Physics.SphereCastAll(origin, 0.4f, dir, length, LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Collide))
                {
                    HurtBox box = h.collider.GetComponent<HurtBox>();
                    if (!box || !V1Damage.IsEnemy(body, box.healthComponent) || !hitEnemies.Add(box.healthComponent))
                    {
                        continue;
                    }
                    Vector3 point = h.point == Vector3.zero ? box.transform.position : h.point;
                    for (int i = 0; i < hitsPerTarget; i++)
                    {
                        V1Damage.Deal(body, box, 1f, point, dir * 300f, source: DamageSource.Secondary);
                    }
                    V1Effects.SpawnHit(V1Assets.laserHit, new Piercer.ShotEnd { point = point, normal = -dir, collider = h.collider, enemy = box, hit = true });
                    any = true;
                }
                if (any)
                {
                    V1HitStop.HitStop(0.05f);
                }
                Vector3 end = origin + dir * length;
                V1Effects.SpawnBeam(V1Assets.revolverBeamSharp ? V1Assets.revolverBeamSharp : V1Assets.revolverBeamSuper, visualStart, end);
                if (!wallHit || ricochets <= 0)
                {
                    break;
                }
                // bounce
                V1Effects.SpawnHit(V1Assets.laserHit, new Piercer.ShotEnd { point = wall.point, normal = wall.normal, collider = wall.collider, hit = true });
                V1Sounds.Play("NailRicochet", body.gameObject, 0.6f, Random.Range(0.9f, 1.1f), false);
                ricochets--;
                hitsPerTarget++;
                origin = wall.point + wall.normal * 0.05f;
                dir = Vector3.Reflect(dir, WallNormal(wall, dir));
                dir = AimAssist(origin, dir);
                visualStart = origin;
                yield return new WaitForSeconds(0.1f);
            }
            Destroy(gameObject);
        }

        // In ULTRAKILL almost all walls are straight (at 90 degrees), while RoR2 has uneven rocks and slopes: the beam bounced off them
        // anywhere. So the wall normal is smoothed (averaged over 5 rays around the hit point) and snapped:
        // a near-vertical surface counts as a flat wall, a near-horizontal one as a flat floor or ceiling.
        private static Vector3 WallNormal(RaycastHit hit, Vector3 dir)
        {
            Vector3 n = hit.normal;
            Vector3 side = Vector3.Cross(dir, Vector3.up);
            if (side.sqrMagnitude < 0.001f)
            {
                side = Vector3.Cross(dir, Vector3.right);
            }
            side.Normalize();
            Vector3 up = Vector3.Cross(side, dir).normalized;
            Vector3[] offsets = { side, -side, up, -up };
            int count = 1;
            foreach (Vector3 o in offsets)
            {
                Vector3 start = hit.point - dir * 1.5f + o * 0.75f;
                if (Physics.Raycast(start, dir, out RaycastHit h, 3f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
                {
                    n += h.normal;
                    count++;
                }
            }
            n = (n / count).normalized;
            float vertical = Mathf.Abs(Vector3.Dot(n, Vector3.up));
            if (vertical < 0.42f)
            {
                // wall (up to ~25 degrees from vertical): a strictly horizontal normal
                Vector3 flat = new Vector3(n.x, 0f, n.z);
                if (flat.sqrMagnitude > 0.0001f)
                {
                    n = flat.normalized;
                }
            }
            else if (vertical > 0.9f)
            {
                // floor or ceiling (up to ~25 degrees from horizontal)
                n = n.y > 0f ? Vector3.up : Vector3.down;
            }
            return n;
        }

        // RicochetAimAssist: SphereCast with radius 5 along the bounce; a coin beats an enemy; the target must be visible
        private Vector3 AimAssist(Vector3 origin, Vector3 dir)
        {
            Vector3 best = Vector3.zero;
            float bestDistance = float.MaxValue;
            bool coinFound = false;
            foreach (V1Coin c in V1Coin.active)
            {
                if (!c || !c.Shootable || c.body != body)
                {
                    continue;
                }
                Vector3 to = c.transform.position - origin;
                float along = Vector3.Dot(to, dir);
                if (along > 0f && (to - dir * along).magnitude <= 5f && along < bestDistance && !Physics.Linecast(origin, c.transform.position, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
                {
                    best = c.transform.position;
                    bestDistance = along;
                    coinFound = true;
                }
            }
            if (!coinFound)
            {
                foreach (RaycastHit h in Physics.SphereCastAll(origin, 5f, dir, 1000f, LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Collide))
                {
                    HurtBox box = h.collider.GetComponent<HurtBox>();
                    if (!box || !V1Damage.IsEnemy(body, box.healthComponent) || h.distance >= bestDistance)
                    {
                        continue;
                    }
                    Vector3 target = box.transform.position;
                    if (Physics.Linecast(origin, target, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
                    {
                        continue;
                    }
                    best = target;
                    bestDistance = h.distance;
                }
            }
            return bestDistance < float.MaxValue ? (best - origin).normalized : dir;
        }
    }
}
