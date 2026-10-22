using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace V1Mod.Survivors.V1.Weapons
{
    // Direct damage of V1 projectiles (saws, nails, magnets, coins).
    // On the host the damage is applied immediately. On a client player it is a short RoR2 bullet aimed exactly at the enemy:
    // RoR2 bullets are sent to the host on their own, so the damage also works in multiplayer.
    public static class V1Damage
    {
        public static DamageTypeCombo Combo(DamageType type, DamageSource source)
        {
            DamageTypeCombo c = type;
            c.damageSource = source;
            return c;
        }

        // source: which RoR2 skill slot the hit counts as (for items like Luminous Shot): LMB Primary, RMB Secondary, arm Special
        public static bool Deal(CharacterBody body, HurtBox target, float units, Vector3 position, Vector3 force, float procCoefficient = 1f, DamageType type = DamageType.Generic, DamageSource source = DamageSource.Primary)
        {
            if (!body || !target || !target.healthComponent || !target.healthComponent.alive)
            {
                return false;
            }
            float damage = units * Piercer.DamagePerUnit * body.damage;
            bool crit = body.RollCrit();
            if (NetworkServer.active)
            {
                DamageInfo info = new DamageInfo
                {
                    attacker = body.gameObject,
                    inflictor = body.gameObject,
                    damage = damage,
                    crit = crit,
                    position = position,
                    force = force,
                    procCoefficient = procCoefficient,
                    damageColorIndex = DamageColorIndex.Default,
                    damageType = Combo(type, source),
                };
                target.healthComponent.TakeDamage(info);
                GlobalEventManager.instance.OnHitEnemy(info, target.healthComponent.gameObject);
                GlobalEventManager.instance.OnHitAll(info, target.healthComponent.gameObject);
                return true;
            }
            if (!body.hasEffectiveAuthority)
            {
                return false;
            }
            // client: a bullet from a point next to the enemy straight into its hitbox
            Vector3 aimAt = target.transform.position;
            Vector3 dir = aimAt - position;
            if (dir.sqrMagnitude < 0.0001f)
            {
                dir = force.sqrMagnitude > 0.0001f ? force : Vector3.forward;
            }
            dir.Normalize();
            Vector3 origin = aimAt - dir * 2f;
            HealthComponent only = target.healthComponent;
            BulletAttack bullet = new BulletAttack
            {
                owner = body.gameObject,
                weapon = body.gameObject,
                origin = origin,
                aimVector = dir,
                minSpread = 0f,
                maxSpread = 0f,
                bulletCount = 1,
                damage = damage,
                damageType = Combo(type, source),
                falloffModel = BulletAttack.FalloffModel.None,
                maxDistance = 4f,
                force = force.magnitude,
                hitMask = LayerIndex.entityPrecise.mask,
                stopperMask = 0,
                isCrit = crit,
                procCoefficient = procCoefficient,
                radius = 0.5f,
                smartCollision = false,
                tracerEffectPrefab = null,
                hitEffectPrefab = null,
            };
            bullet.filterCallback = (BulletAttack attack, ref BulletAttack.BulletHit hit) =>
                hit.hitHurtBox && hit.hitHurtBox.healthComponent == only && BulletAttack.DefaultFilterCallback(attack, ref hit);
            bullet.hitCallback = (BulletAttack attack, ref BulletAttack.BulletHit hit) =>
            {
                Piercer.HitWithoutRoR2Effects(attack, ref hit);
                return false;
            };
            bullet.Fire();
            return true;
        }

        public static bool IsEnemy(CharacterBody body, HealthComponent hc)
        {
            return body && hc && hc.body != body && hc.alive && FriendlyFireManager.ShouldDirectHitProceed(hc, body.teamComponent ? body.teamComponent.teamIndex : TeamIndex.Player);
        }
    }
}

namespace V1Mod.Survivors.V1.Weapons
{
    public static class V1Physics
    {
        // First obstacle on a projectile's path: a wall (not a trigger) or an enemy hitbox.
        // RoR2 hitboxes are triggers, so search with Collide and filter out ourselves, allies and other triggers by hand.
        public static bool Cast(CharacterBody body, Vector3 origin, float radius, Vector3 dir, float distance, out RaycastHit hit, out HurtBox enemy)
        {
            hit = default;
            enemy = null;
            if (distance <= 0f || dir.sqrMagnitude < 0.000001f)
            {
                return false;
            }
            dir.Normalize();
            int mask = LayerIndex.world.mask | LayerIndex.entityPrecise.mask;
            RaycastHit[] hits = radius > 0f
                ? Physics.SphereCastAll(origin, radius, dir, distance, mask, QueryTriggerInteraction.Collide)
                : Physics.RaycastAll(origin, dir, distance, mask, QueryTriggerInteraction.Collide);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit h in hits)
            {
                // SphereCastAll returns colliders we started inside with a zero point; that is not a hit
                if (h.distance <= 0f && h.point == Vector3.zero)
                {
                    continue;
                }
                HurtBox box = h.collider.GetComponent<HurtBox>();
                if (box)
                {
                    if (V1Damage.IsEnemy(body, box.healthComponent))
                    {
                        hit = h;
                        enemy = box;
                        return true;
                    }
                    continue;
                }
                if (h.collider.isTrigger)
                {
                    continue;
                }
                hit = h;
                return true;
            }
            return false;
        }
    }
}
