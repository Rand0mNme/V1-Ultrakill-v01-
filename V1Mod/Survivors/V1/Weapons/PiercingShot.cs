using System.Collections.Generic;
using RoR2;
using UnityEngine;
using V1Mod.Modules;

namespace V1Mod.Survivors.V1.Weapons
{
    // ULTRAKILL piercing beam (RevolverBeam with hitAmount 3, PiercingShotOrder / PiercingShotCheck):
    // - the beam always reaches a wall, enemies don't stop it
    // - enemies are searched along the beam with a thick "tube" of radius 0.6 (a normal shot uses 0.4)
    // - 3 hits of 1 damage in total; they go to enemies in order: while an enemy is alive the hits go into it,
    //   if it dies earlier the rest flies on into the next one
    // - every hit on a living enemy: sparks and a 0.05 s hit stop
    // Damage is dealt with ordinary RoR2 bullets (BulletAttack), one per hit, aimed only at their own enemy,
    // so everything also works in multiplayer.
    public static class PiercingShot
    {
        public const float Radius = 0.6f;
        public const int MaxHitsPerTarget = 3;

        // damageUnits: 1 normally; a beam reflected off a coin is stronger by coin power / 4
        public static Vector3 Fire(GameObject owner, CharacterBody body, Ray aim, float damageUnits = 1f)
        {
            // end of the beam: the first wall
            float maxDistance = 1000f;
            Vector3 end = aim.origin + aim.direction * maxDistance;
            Piercer.ShotEnd wallEnd = default;
            if (Physics.Raycast(aim, out RaycastHit wall, maxDistance, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
            {
                maxDistance = wall.distance;
                end = wall.point;
                wallEnd = new Piercer.ShotEnd { point = wall.point, normal = wall.normal, collider = wall.collider, hit = true };
            }

            // enemies along the beam, in order, each once (the body part closest to the beam start)
            RaycastHit[] hits = Physics.SphereCastAll(aim.origin, Radius, aim.direction, maxDistance, LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Collide);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            var seen = new HashSet<HealthComponent>();
            var targets = new List<(HurtBox box, RaycastHit hit)>();
            TeamIndex myTeam = body.teamComponent ? body.teamComponent.teamIndex : TeamIndex.None;
            foreach (RaycastHit h in hits)
            {
                HurtBox box = h.collider ? h.collider.GetComponent<HurtBox>() : null;
                HealthComponent hc = box ? box.healthComponent : null;
                if (!hc || !hc.alive || hc.body == body || seen.Contains(hc))
                {
                    continue;
                }
                if (hc.body && hc.body.teamComponent && hc.body.teamComponent.teamIndex == myTeam)
                {
                    continue;
                }
                seen.Add(hc);
                targets.Add((box, h));
            }

            int remaining = Piercer.SuperHits;
            float damagePerHit = Piercer.DamagePerUnit * body.damage * damageUnits;
            foreach (var (box, h) in targets)
            {
                if (remaining <= 0)
                {
                    break;
                }
                // how many hits it takes to kill the enemy (by the health we can see)
                float perHit = damagePerHit * (box.isSniperTarget ? Piercer.SuperHeadshotMultiplier : 1f);
                int needed = Mathf.Max(1, Mathf.CeilToInt(box.healthComponent.combinedHealth / Mathf.Max(1f, perHit)));
                int n = Mathf.Min(remaining, Mathf.Min(needed, MaxHitsPerTarget));
                Vector3 point = h.point == Vector3.zero ? box.transform.position : h.point;
                Vector3 normal = h.normal.sqrMagnitude > 0f ? h.normal : -aim.direction;
                for (int i = 0; i < n; i++)
                {
                    FireAt(owner, body, aim, box.healthComponent, maxDistance);
                    V1Effects.SpawnHit(V1Assets.superLaserHit, new Piercer.ShotEnd { point = point, normal = normal, collider = h.collider, enemy = true, hit = true });
                    V1HitStop.HitStop(0.05f);
                }
                remaining -= n;
            }

            V1Effects.SpawnHit(V1Assets.superLaserHit, wallEnd);
            return end;
        }

        // one RoR2 bullet that passes through everyone except its target and hits that target once
        private static void FireAt(GameObject owner, CharacterBody body, Ray aim, HealthComponent target, float maxDistance)
        {
            bool done = false;
            BulletAttack bullet = new BulletAttack
            {
                owner = owner,
                weapon = owner,
                origin = aim.origin,
                aimVector = aim.direction,
                minSpread = 0f,
                maxSpread = 0f,
                bulletCount = 1,
                damage = Piercer.DamagePerUnit * body.damage,
                damageColorIndex = DamageColorIndex.Default,
                damageType = V1Damage.Combo(DamageType.Generic, DamageSource.Secondary),
                falloffModel = BulletAttack.FalloffModel.None,
                maxDistance = maxDistance + Radius,
                force = 1500f,
                hitMask = LayerIndex.CommonMasks.bullet,
                stopperMask = LayerIndex.world.mask,
                isCrit = body.RollCrit(),
                procCoefficient = 1f,
                radius = Radius,
                smartCollision = true,
                tracerEffectPrefab = null,
                hitEffectPrefab = null,
            };
            bullet.filterCallback = (BulletAttack attack, ref BulletAttack.BulletHit hit) =>
            {
                if (done || !hit.hitHurtBox || hit.hitHurtBox.healthComponent != target)
                {
                    return false;
                }
                return BulletAttack.DefaultFilterCallback(attack, ref hit);
            };
            bullet.modifyOutgoingDamageCallback = (BulletAttack attack, ref BulletAttack.BulletHit hit, DamageInfo damageInfo) =>
            {
                if (hit.hitHurtBox && hit.hitHurtBox.isSniperTarget)
                {
                    damageInfo.damage *= Piercer.SuperHeadshotMultiplier;
                }
            };
            bullet.hitCallback = (BulletAttack attack, ref BulletAttack.BulletHit hit) =>
            {
                bool keepGoing = Piercer.HitWithoutRoR2Effects(attack, ref hit);
                done = true;
                return keepGoing;
            };
            bullet.Fire();
        }
    }
}
