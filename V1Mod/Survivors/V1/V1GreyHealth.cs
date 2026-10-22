using HarmonyLib;
using RoR2;
using UnityEngine;

namespace V1Mod.Survivors.V1
{
    // ULTRAKILL grey health ("hard damage", NewMovement.antiHp):
    // - part of every hit taken (35%) turns grey: health can't be healed above that line;
    //   grey is at most 99% of health;
    // - after a hit the grey part holds: timer +1 s (if it was 0) plus damage / 20 s (damage in points out of V1's 100 health);
    // - when the timer runs out, grey drains at 15% of health per second.
    // A grey arc on the crosshair (AntiHPSlider) shows how much health can't be healed right now.
    public class V1GreyHealth : MonoBehaviour
    {
        public const float HardDamageMultiplier = 0.35f;
        // fraction of health that can't be healed (0..0.99)
        public float antiHp;
        private float cooldown;

        public static void Init()
        {
            GlobalEventManager.onServerDamageDealt += OnDamage;
            GlobalEventManager.onServerDamageDealt += BloodHeal;
            GlobalEventManager.onCharacterDeathGlobal += KillHeal;
            new Harmony(V1Plugin.MODUID + ".greyhealth").PatchAll(typeof(HealPatch));
        }

        private static void OnDamage(DamageReport report)
        {
            if (report == null || !report.victimBody || report.damageDealt <= 0f)
            {
                return;
            }
            V1GreyHealth grey = report.victimBody.GetComponent<V1GreyHealth>();
            HealthComponent hc = report.victimBody.healthComponent;
            if (!grey || !hc || hc.fullCombinedHealth <= 0f)
            {
                return;
            }
            // damage in "points out of 100", like V1 in ULTRAKILL
            float damage100 = report.damageDealt / hc.fullCombinedHealth * 100f;
            grey.antiHp = Mathf.Min(0.99f, grey.antiHp + damage100 * HardDamageMultiplier / 100f);
            if (grey.cooldown == 0f)
            {
                grey.cooldown += 1f;
            }
            grey.cooldown += damage100 / 20f;
        }

        // Blood healing (in ULTRAKILL V1 heals from the blood of nearby enemies). For RoR2, as in the other V1 mod:
        // damage to an enemy closer than BloodHealRadius returns BloodHealFraction of it, a nearby kill gives KillHealFraction of health.
        // All healing is capped by grey health (HealPatch).
        private static bool NearV1(CharacterBody attacker, CharacterBody victim, out HealthComponent hc)
        {
            hc = null;
            if (!attacker || !victim || attacker == victim || !attacker.GetComponent<V1GreyHealth>())
            {
                return false;
            }
            hc = attacker.healthComponent;
            float r = Modules.V1Config.BloodHealRadius.Value;
            return hc && hc.alive && (attacker.corePosition - victim.corePosition).sqrMagnitude <= r * r;
        }

        private static void BloodHeal(DamageReport report)
        {
            if (report == null || report.damageDealt <= 0f || !NearV1(report.attackerBody, report.victimBody, out HealthComponent hc))
            {
                return;
            }
            hc.Heal(report.damageDealt * Modules.V1Config.BloodHealFraction.Value, default(ProcChainMask), true);
        }

        private static void KillHeal(DamageReport report)
        {
            if (!UnityEngine.Networking.NetworkServer.active || report == null || !NearV1(report.attackerBody, report.victimBody, out HealthComponent hc))
            {
                return;
            }
            hc.Heal(hc.fullHealth * Modules.V1Config.KillHealFraction.Value, default(ProcChainMask), true);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (cooldown > 0f)
            {
                cooldown = Mathf.MoveTowards(cooldown, 0f, dt);
            }
            else if (antiHp > 0f)
            {
                antiHp = Mathf.MoveTowards(antiHp, 0f, dt * 0.15f);
            }
        }

        // healing doesn't raise health above (1 - grey)
        [HarmonyPatch(typeof(HealthComponent), nameof(HealthComponent.Heal))]
        public static class HealPatch
        {
            public static void Prefix(HealthComponent __instance, ref float amount)
            {
                if (amount <= 0f || !__instance.body)
                {
                    return;
                }
                V1GreyHealth grey = __instance.body.GetComponent<V1GreyHealth>();
                if (!grey || grey.antiHp <= 0f)
                {
                    return;
                }
                float cap = __instance.fullHealth * (1f - grey.antiHp);
                amount = Mathf.Clamp(cap - __instance.health, 0f, amount);
            }
        }
    }
}
