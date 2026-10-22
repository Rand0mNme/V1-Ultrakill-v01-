using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using EntityStates;
using HarmonyLib;
using RoR2;
using RoR2.Projectile;
using RoR2.Skills;
using UnityEngine;

namespace V1Diag
{
    // Behavior logger for your own character in RoR2, for studying another mod without reading its code.
    // Changes nothing in the game, only writes to BepInEx/V1Diag.log:
    // - the character's stats on spawn and its skills;
    // - every state switch (EntityState) of your character + all state fields (once per type);
    // - skill activations, cooldowns;
    // - bullets, explosions, projectiles (damage coefficient = damage / character damage, radii, spread);
    // - damage dealt (to whom, how much, coefficient, crit, proc).
    [BepInPlugin("com.oleksandr.V1Diag", "V1Diag", "1.0.0")]
    public class V1DiagPlugin : BaseUnityPlugin
    {
        private static StreamWriter writer;
        private static readonly HashSet<Type> dumpedTypes = new HashSet<Type>();
        private static string lastLine;
        private static int repeat;

        private void Awake()
        {
            string path = System.IO.Path.Combine(Paths.BepInExRootPath, "V1Diag.log");
            // don't overwrite the previous log, keep it next to the new one
            try
            {
                if (File.Exists(path))
                {
                    File.Copy(path, System.IO.Path.Combine(Paths.BepInExRootPath, "V1Diag.prev.log"), true);
                }
            }
            catch (Exception)
            {
            }
            writer = new StreamWriter(path, false, Encoding.UTF8) { AutoFlush = true };
            Write("V1Diag started. Time in seconds since game start.");
            // each hook separately: if one fails to apply, the rest still work
            var harmony = new Harmony("com.oleksandr.V1Diag");
            foreach (Type t in typeof(V1DiagPlugin).Assembly.GetTypes())
            {
                if (t.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0)
                {
                    continue;
                }
                try
                {
                    harmony.CreateClassProcessor(t).Patch();
                }
                catch (Exception e)
                {
                    Write("failed to hook " + t.Name + ": " + e.Message);
                }
            }
            Logger.LogInfo("V1Diag: logging to " + path);
        }

        public static void Write(string line)
        {
            if (line == lastLine)
            {
                repeat++;
                return;
            }
            if (repeat > 0)
            {
                writer?.WriteLine($"      (the same {repeat} more times)");
                repeat = 0;
            }
            lastLine = line;
            writer?.WriteLine($"[{Time.time,8:F3}] {line}");
        }

        public static bool MarkDumped(Type t) => dumpedTypes.Add(t);

        // movement: the character's speed whenever it changes noticeably (once per 0.1 s)
        private float velocityTimer;
        private Vector3 lastVelocity;

        private void Update()
        {
            velocityTimer -= Time.deltaTime;
            if (velocityTimer > 0f)
            {
                return;
            }
            velocityTimer = 0.1f;
            foreach (CharacterBody b in CharacterBody.readOnlyInstancesList)
            {

                if (!IsLocal(b) || !b.characterMotor)
                {
                    continue;
                }
                Vector3 v = b.characterMotor.velocity;
                if ((v - lastVelocity).magnitude > 3f)
                {
                    Vector3 flat = new Vector3(v.x, 0f, v.z);
                    Write($"speed: horizontal {flat.magnitude:F1}, up {v.y:F1}, grounded {b.characterMotor.isGrounded}");
                    lastVelocity = v;
                }
            }
        }

        public static bool IsLocal(CharacterBody body)
        {
            return body && body.isPlayerControlled && body.hasEffectiveAuthority;
        }

        public static bool IsLocal(GameObject go)
        {
            return go && IsLocal(go.GetComponent<CharacterBody>());
        }

        public static string Coef(float damage, GameObject owner)
        {
            CharacterBody b = owner ? owner.GetComponent<CharacterBody>() : null;
            return b && b.damage > 0f ? $"{damage / b.damage * 100f:F0}% of character damage" : $"{damage:F1}";
        }

        // all fields of an object (except references to Unity objects and internal ones), to see the numbers in someone else's code
        public static void DumpFields(object obj, string title)
        {
            var sb = new StringBuilder();
            sb.Append(title).Append(":");
            for (Type t = obj.GetType(); t != null && t != typeof(object) && t != typeof(EntityState) && !(t.Namespace ?? "").StartsWith("UnityEngine"); t = t.BaseType)
            {
                foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (f.Name.Contains("<") || typeof(Delegate).IsAssignableFrom(f.FieldType))
                    {
                        continue;
                    }
                    object v;
                    try
                    {
                        v = f.IsStatic ? f.GetValue(null) : f.GetValue(obj);
                    }
                    catch
                    {
                        continue;
                    }
                    string s = v is UnityEngine.Object uo ? (uo ? uo.name : "null") : v?.ToString() ?? "null";
                    if (s.Length > 80)
                    {
                        s = s.Substring(0, 80) + "…";
                    }
                    sb.Append("\n        ").Append(t.Name).Append('.').Append(f.Name).Append(f.IsStatic ? " (static)" : "").Append(" = ").Append(s);
                }
            }
            Write(sb.ToString());
        }
    }

    [HarmonyPatch(typeof(CharacterBody), "Start")]
    public static class BodyStartPatch
    {
        public static void Postfix(CharacterBody __instance)
        {
            if (!__instance.isPlayerControlled)
            {
                return;
            }
            CharacterBody b = __instance;
            V1DiagPlugin.Write($"=== Character {b.name} ({Language.GetString(b.baseNameToken)}) ===");
            V1DiagPlugin.Write($"    health {b.baseMaxHealth} (+{b.levelMaxHealth}/lvl), damage {b.baseDamage} (+{b.levelDamage}/lvl), speed {b.baseMoveSpeed}, sprint x{b.sprintingSpeedMultiplier}, jump {b.baseJumpPower}, jumps {b.baseJumpCount}, acceleration {b.baseAcceleration}, armor {b.baseArmor}, crit {b.baseCrit}%");
            if (b.characterMotor)
            {
                V1DiagPlugin.Write($"    motor: mass {b.characterMotor.mass}, airControl {b.characterMotor.airControl}, gravity {(b.characterMotor.useGravity ? "on" : "off")}, gravityParameters {b.characterMotor.gravityParameters.environmentalAntiGravityGranterCount}/{b.characterMotor.gravityParameters.antiGravityNeutralizerCount}, Physics.gravity {Physics.gravity}");
            }
            if (b.skillLocator)
            {
                foreach (GenericSkill s in b.GetComponents<GenericSkill>())
                {
                    SkillDef d = s ? s.skillDef : null;
                    if (d)
                    {
                        V1DiagPlugin.Write($"    skill {s.skillName ?? s.name}: {d.skillName} -> {d.activationState.stateType?.FullName}, machine {d.activationStateMachineName}, stock {d.baseMaxStock}, cooldown {d.baseRechargeInterval} s, mustKeyPress {d.mustKeyPress}, interrupt {d.interruptPriority}");
                    }
                }
            }
            foreach (EntityStateMachine m in b.GetComponents<EntityStateMachine>())
            {
                V1DiagPlugin.Write($"    state machine {m.customName}: initial {m.initialStateType.stateType?.FullName}, main {m.mainStateType.stateType?.FullName}");
            }
            foreach (MonoBehaviour c in b.GetComponents<MonoBehaviour>())
            {
                string ns = c.GetType().Namespace ?? "";
                if (!ns.StartsWith("RoR2") && !ns.StartsWith("UnityEngine") && !ns.StartsWith("EntityStates") && !ns.StartsWith("KinematicCharacterController"))
                {
                    V1DiagPlugin.DumpFields(c, "    mod component " + c.GetType().FullName);
                }
            }
        }
    }

    [HarmonyPatch(typeof(EntityStateMachine), nameof(EntityStateMachine.SetState))]
    public static class SetStatePatch
    {
        public static void Prefix(EntityStateMachine __instance, EntityState newState)
        {
            if (newState == null || !V1DiagPlugin.IsLocal(__instance.gameObject))
            {
                return;
            }
            string from = __instance.state != null ? __instance.state.GetType().Name : "-";
            V1DiagPlugin.Write($"state [{__instance.customName}] {from} -> {newState.GetType().FullName}");
            if (V1DiagPlugin.MarkDumped(newState.GetType()))
            {
                V1DiagPlugin.DumpFields(newState, "    fields " + newState.GetType().Name);
            }
        }
    }

    [HarmonyPatch(typeof(GenericSkill), "OnExecute")]
    public static class SkillExecutePatch
    {
        public static void Prefix(GenericSkill __instance)
        {
            if (!V1DiagPlugin.IsLocal(__instance.characterBody))
            {
                return;
            }
            SkillDef d = __instance.skillDef;
            V1DiagPlugin.Write($"skill {__instance.skillName ?? __instance.name} ({(d ? d.skillName : "?")}): stock {__instance.stock}/{__instance.maxStock}, cooldown {__instance.baseRechargeInterval:F2} s");
        }
    }

    [HarmonyPatch(typeof(BulletAttack), nameof(BulletAttack.Fire), new Type[0])]
    public static class BulletPatch
    {
        public static void Prefix(BulletAttack __instance)
        {
            if (!V1DiagPlugin.IsLocal(__instance.owner))
            {
                return;
            }
            BulletAttack a = __instance;
            V1DiagPlugin.Write($"bullet: {V1DiagPlugin.Coef(a.damage, a.owner)} x{a.bulletCount}, spread {a.minSpread}-{a.maxSpread} deg, radius {a.radius}, range {a.maxDistance}, proc {a.procCoefficient}, falloff {a.falloffModel}, force {a.force}, type {a.damageType.damageType}, crit {a.isCrit}");
        }
    }

    [HarmonyPatch(typeof(BlastAttack), nameof(BlastAttack.Fire), new Type[0])]
    public static class BlastPatch
    {
        public static void Prefix(BlastAttack __instance)
        {
            if (!V1DiagPlugin.IsLocal(__instance.attacker))
            {
                return;
            }
            BlastAttack a = __instance;
            float dist = a.attacker ? Vector3.Distance(a.attacker.transform.position, a.position) : 0f;
            V1DiagPlugin.Write($"explosion: {V1DiagPlugin.Coef(a.baseDamage, a.attacker)}, radius {a.radius}, falloff {a.falloffModel}, proc {a.procCoefficient}, force {a.baseForce}, type {a.damageType.damageType}, {dist:F1} m from the character");
        }
    }

    [HarmonyPatch(typeof(ProjectileManager), nameof(ProjectileManager.FireProjectile), new[] { typeof(FireProjectileInfo) })]
    public static class ProjectilePatch
    {
        public static void Prefix(FireProjectileInfo fireProjectileInfo)
        {
            if (!V1DiagPlugin.IsLocal(fireProjectileInfo.owner))
            {
                return;
            }
            FireProjectileInfo p = fireProjectileInfo;
            ProjectileSimple simple = p.projectilePrefab ? p.projectilePrefab.GetComponent<ProjectileSimple>() : null;
            V1DiagPlugin.Write($"projectile {(p.projectilePrefab ? p.projectilePrefab.name : "?")}: {V1DiagPlugin.Coef(p.damage, p.owner)}, speed {(p.useSpeedOverride ? p.speedOverride : (simple ? simple.desiredForwardSpeed : -1f))}, force {p.force}, crit {p.crit}");
        }
    }

    [HarmonyPatch(typeof(HealthComponent), nameof(HealthComponent.TakeDamage))]
    public static class DamagePatch
    {
        public static void Prefix(HealthComponent __instance, DamageInfo damageInfo)
        {
            if (damageInfo == null)
            {
                return;
            }
            bool fromLocal = V1DiagPlugin.IsLocal(damageInfo.attacker);
            bool toLocal = __instance.body && __instance.body.isPlayerControlled;
            if (!fromLocal && !toLocal)
            {
                return;
            }
            string victim = __instance.body ? __instance.body.name : __instance.name;
            string inflictor = damageInfo.inflictor ? damageInfo.inflictor.name : "-";
            if (fromLocal)
            {
                V1DiagPlugin.Write($"damage -> {victim}: {damageInfo.damage:F1} ({V1DiagPlugin.Coef(damageInfo.damage, damageInfo.attacker)}), crit {damageInfo.crit}, proc {damageInfo.procCoefficient}, type {damageInfo.damageType.damageType}, via {inflictor}, target at {__instance.combinedHealth:F0}/{__instance.fullCombinedHealth:F0}");
            }
            else
            {
                V1DiagPlugin.Write($"character took {damageInfo.damage:F1} from {(damageInfo.attacker ? damageInfo.attacker.name : "-")} via {inflictor}, type {damageInfo.damageType.damageType}");
            }
        }
    }
}
