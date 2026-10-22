using EntityStates;
using R2API;
using RoR2;
using RoR2.Skills;
using System.Reflection;
using UnityEngine;
using V1Mod.Survivors.V1.SkillStates;
using V1Mod.Survivors.V1.Weapons;

namespace V1Mod.Survivors.V1
{
    // V1's own skills. So far only Shift (dash) is replaced, the rest is left over from Commando.
    public static class V1Skills
    {
        public static SkillDef dashSkillDef;
        public static SkillDef dashRoR2SkillDef;

        private static readonly FieldInfo skillFamilyField =
            typeof(GenericSkill).GetField("_skillFamily", BindingFlags.NonPublic | BindingFlags.Instance);

        public static void Init(GameObject bodyPrefab)
        {
            ContentAddition.AddEntityState<Dash>(out _);

            SkillLocator skillLocator = bodyPrefab.GetComponent<SkillLocator>();
            Sprite oldIcon = skillLocator.utility.skillFamily.variants[0].skillDef.icon;

            dashSkillDef = ScriptableObject.CreateInstance<SkillDef>();
            dashSkillDef.skillName = "V1Dash";
            (dashSkillDef as ScriptableObject).name = "V1Dash";
            dashSkillDef.skillNameToken = V1Survivor.TOKEN_PREFIX + "UTILITY_DASH_NAME";
            dashSkillDef.skillDescriptionToken = V1Survivor.TOKEN_PREFIX + "UTILITY_DASH_DESCRIPTION";
            dashSkillDef.icon = oldIcon;
            dashSkillDef.activationState = new SerializableEntityStateType(typeof(Dash));
            dashSkillDef.activationStateMachineName = "Body";
            dashSkillDef.interruptPriority = InterruptPriority.PrioritySkill;

            // 3 stamina bars: each one regenerates separately
            dashSkillDef.baseMaxStock = 3;
            dashSkillDef.rechargeStock = 1;
            dashSkillDef.baseRechargeInterval = 100f / 70f; // ULTRAKILL: pool of 300, a dash costs 100, regen 70 per second (Standard)
            dashSkillDef.requiredStock = 1;
            dashSkillDef.stockToConsume = 1;
            dashSkillDef.fullRestockOnAssign = true;
            dashSkillDef.beginSkillCooldownOnSkillEnd = false;
            dashSkillDef.resetCooldownTimerOnUse = false;

            dashSkillDef.isCombatSkill = false;
            dashSkillDef.mustKeyPress = true;
            dashSkillDef.canceledFromSprinting = false;
            dashSkillDef.cancelSprintingOnActivation = false;
            dashSkillDef.forceSprintDuringState = false;

            // alternate Shift: the same dash, but all V1 movement in RoR2 scale (x0.52, as in the other V1 mod)
            dashRoR2SkillDef = Object.Instantiate(dashSkillDef);
            dashRoR2SkillDef.skillName = "V1DashRoR2";
            (dashRoR2SkillDef as ScriptableObject).name = "V1DashRoR2";
            dashRoR2SkillDef.skillNameToken = V1Survivor.TOKEN_PREFIX + "UTILITY_DASH_ROR2_NAME";
            dashRoR2SkillDef.skillDescriptionToken = V1Survivor.TOKEN_PREFIX + "UTILITY_DASH_ROR2_DESCRIPTION";

            SkillFamily utilityFamily = ScriptableObject.CreateInstance<SkillFamily>();
            (utilityFamily as ScriptableObject).name = "V1UtilityFamily";
            utilityFamily.variants = new SkillFamily.Variant[]
            {
                new SkillFamily.Variant
                {
                    skillDef = dashSkillDef,
                    unlockableDef = null,
                    viewableNode = new ViewablesCatalog.Node(dashSkillDef.skillNameToken, false, null),
                },
                new SkillFamily.Variant
                {
                    skillDef = dashRoR2SkillDef,
                    unlockableDef = null,
                    viewableNode = new ViewablesCatalog.Node(dashRoR2SkillDef.skillNameToken, false, null),
                }
            };

            skillFamilyField.SetValue(skillLocator.utility, utilityFamily);

            ContentAddition.AddSkillDef(dashSkillDef);
            ContentAddition.AddSkillDef(dashRoR2SkillDef);
            ContentAddition.AddSkillFamily(utilityFamily);

            LanguageAPI.Add(dashSkillDef.skillNameToken, "Dash");
            LanguageAPI.Add(dashSkillDef.skillDescriptionToken,
                "Quickly <style=cIsUtility>dash</style> in the direction you are moving. You are <style=cIsUtility>invincible</style> while dashing. Holds <style=cIsUtility>3 charges</style> of stamina.");
            LanguageAPI.Add(dashRoR2SkillDef.skillNameToken, "Dash (RoR2 pace)");
            LanguageAPI.Add(dashRoR2SkillDef.skillDescriptionToken,
                "Same <style=cIsUtility>dash</style>, but all of V1's movement is scaled to Risk of Rain 2: walking, jumps, slides, dashes and slams are <style=cIsUtility>48% slower</style> and lower. Holds <style=cIsUtility>3 charges</style> of stamina.");

            InitPiercer(skillLocator);
            InitShotgun(skillLocator);

            // slide and ground slam sit on the sprint button (Ctrl), as in ULTRAKILL, while R stays Commando's
            ContentAddition.AddEntityState<SlideSlam>(out _);
            ContentAddition.AddEntityState<Slide>(out _);
            ContentAddition.AddEntityState<Slam>(out _);

            InitArsenalChoice(bodyPrefab, oldIcon);
        }

        // Pre-run choice (loadout): the whole arsenal at once or only the revolver (the rest from the SmileOS shop)
        public static SkillDef arsenalFullDef;
        public static SkillDef arsenalRevolverDef;

        public static GenericSkill ArsenalSkill(CharacterBody body)
        {
            foreach (GenericSkill s in body.GetComponents<GenericSkill>())
            {
                if (s.skillFamily && (s.skillFamily as ScriptableObject).name == "V1ArsenalFamily")
                {
                    return s;
                }
            }
            return null;
        }

        private static SkillDef MakeChoice(string name, string title, string desc, Sprite icon)
        {
            SkillDef d = ScriptableObject.CreateInstance<SkillDef>();
            d.skillName = name;
            (d as ScriptableObject).name = name;
            d.skillNameToken = V1Survivor.TOKEN_PREFIX + name.ToUpperInvariant() + "_NAME";
            d.skillDescriptionToken = V1Survivor.TOKEN_PREFIX + name.ToUpperInvariant() + "_DESCRIPTION";
            d.icon = icon;
            d.activationState = new SerializableEntityStateType(typeof(EntityStates.Idle));
            d.activationStateMachineName = "Weapon";
            d.baseMaxStock = 0;
            LanguageAPI.Add(d.skillNameToken, title);
            LanguageAPI.Add(d.skillDescriptionToken, desc);
            ContentAddition.AddSkillDef(d);
            return d;
        }

        private static void InitArsenalChoice(GameObject bodyPrefab, Sprite icon)
        {
            arsenalRevolverDef = MakeChoice("V1ArsenalRevolver", "Revolver Only",
                "Start with the <style=cIsUtility>Piercer</style> revolver. Buy the rest of the arsenal at the <style=cIsUtility>SmileOS</style> terminal with <style=cIsUtility>style points</style>.", icon);
            arsenalFullDef = MakeChoice("V1ArsenalFull", "Full Arsenal", "Start with <style=cIsUtility>every weapon</style> unlocked.", icon);

            SkillFamily family = ScriptableObject.CreateInstance<SkillFamily>();
            (family as ScriptableObject).name = "V1ArsenalFamily";
            family.variants = new[]
            {
                new SkillFamily.Variant { skillDef = arsenalRevolverDef, viewableNode = new ViewablesCatalog.Node(arsenalRevolverDef.skillNameToken, false, null) },
                new SkillFamily.Variant { skillDef = arsenalFullDef, viewableNode = new ViewablesCatalog.Node(arsenalFullDef.skillNameToken, false, null) },
            };
            ContentAddition.AddSkillFamily(family);
            GenericSkill slot = bodyPrefab.AddComponent<GenericSkill>();
            slot.skillName = "V1Arsenal";
            skillFamilyField.SetValue(slot, family);
        }

        public static SkillDef piercerShootDef;
        public static SkillDef piercerChargeDef;

        // LMB and RMB: the "Piercer" revolver
        private static void InitPiercer(SkillLocator skillLocator)
        {
            ContentAddition.AddEntityState<PiercerShoot>(out _);
            ContentAddition.AddEntityState<PiercerCharge>(out _);
            Piercer.LoadEffects();

            piercerShootDef = ScriptableObject.CreateInstance<SkillDef>();
            piercerShootDef.skillName = "V1PiercerShoot";
            (piercerShootDef as ScriptableObject).name = "V1PiercerShoot";
            piercerShootDef.skillNameToken = V1Survivor.TOKEN_PREFIX + "PRIMARY_PIERCER_NAME";
            piercerShootDef.skillDescriptionToken = V1Survivor.TOKEN_PREFIX + "PRIMARY_PIERCER_DESCRIPTION";
            piercerShootDef.icon = skillLocator.primary.skillFamily.variants[0].skillDef.icon;
            piercerShootDef.activationState = new SerializableEntityStateType(typeof(PiercerShoot));
            piercerShootDef.activationStateMachineName = "Weapon";
            piercerShootDef.interruptPriority = InterruptPriority.Any;
            piercerShootDef.baseMaxStock = 1;
            piercerShootDef.rechargeStock = 1;
            piercerShootDef.baseRechargeInterval = Piercer.ShotInterval;
            piercerShootDef.requiredStock = 1;
            piercerShootDef.stockToConsume = 1;
            piercerShootDef.fullRestockOnAssign = true;
            piercerShootDef.beginSkillCooldownOnSkillEnd = false;
            piercerShootDef.mustKeyPress = false;
            piercerShootDef.isCombatSkill = true;
            piercerShootDef.canceledFromSprinting = false;
            piercerShootDef.cancelSprintingOnActivation = false;

            piercerChargeDef = ScriptableObject.CreateInstance<SkillDef>();
            piercerChargeDef.skillName = "V1PiercerCharge";
            (piercerChargeDef as ScriptableObject).name = "V1PiercerCharge";
            piercerChargeDef.skillNameToken = V1Survivor.TOKEN_PREFIX + "SECONDARY_PIERCER_NAME";
            piercerChargeDef.skillDescriptionToken = V1Survivor.TOKEN_PREFIX + "SECONDARY_PIERCER_DESCRIPTION";
            piercerChargeDef.icon = skillLocator.secondary.skillFamily.variants[0].skillDef.icon;
            piercerChargeDef.activationState = new SerializableEntityStateType(typeof(PiercerCharge));
            piercerChargeDef.activationStateMachineName = "Weapon";
            piercerChargeDef.interruptPriority = InterruptPriority.Skill;
            piercerChargeDef.baseMaxStock = 1;
            piercerChargeDef.rechargeStock = 1;
            piercerChargeDef.baseRechargeInterval = Piercer.SuperRecharge;
            // the charge isn't spent until the shot (firing removes it itself)
            piercerChargeDef.requiredStock = 1;
            piercerChargeDef.stockToConsume = 0;
            piercerChargeDef.fullRestockOnAssign = true;
            piercerChargeDef.beginSkillCooldownOnSkillEnd = false;
            piercerChargeDef.mustKeyPress = false;
            piercerChargeDef.isCombatSkill = true;
            piercerChargeDef.canceledFromSprinting = false;
            piercerChargeDef.cancelSprintingOnActivation = false;

            ReplaceFamily(skillLocator.primary, "V1PrimaryFamily", piercerShootDef);
            ReplaceFamily(skillLocator.secondary, "V1SecondaryFamily", piercerChargeDef);

            LanguageAPI.Add(piercerShootDef.skillNameToken, "Piercer Revolver");
            LanguageAPI.Add(piercerShootDef.skillDescriptionToken,
                "Fire a hitscan shot for <style=cIsDamage>275% damage</style>. Weak points take <style=cIsDamage>x2</style> damage.");
            LanguageAPI.Add(piercerChargeDef.skillNameToken, "Piercing Shot");
            LanguageAPI.Add(piercerChargeDef.skillDescriptionToken,
                "Hold to charge, release to fire a <style=cIsDamage>piercing</style> beam that hits <style=cIsDamage>3 times for 275% damage</style>. Weak points take <style=cIsDamage>x2.5</style> damage.");
        }

        // Core Eject shotgun: the skills replace LMB/RMB when the shotgun is selected (V1Arsenal)
        private static void InitShotgun(SkillLocator skillLocator)
        {
            ContentAddition.AddEntityState<ShotgunFire>(out _);
            ContentAddition.AddEntityState<ShotgunCoreCharge>(out _);

            SkillDef fire = ScriptableObject.CreateInstance<SkillDef>();
            fire.skillName = "V1ShotgunFire";
            (fire as ScriptableObject).name = "V1ShotgunFire";
            fire.skillNameToken = V1Survivor.TOKEN_PREFIX + "PRIMARY_SHOTGUN_NAME";
            fire.skillDescriptionToken = V1Survivor.TOKEN_PREFIX + "PRIMARY_SHOTGUN_DESCRIPTION";
            fire.icon = skillLocator.primary.skillFamily.variants[0].skillDef.icon;
            fire.activationState = new SerializableEntityStateType(typeof(ShotgunFire));
            fire.activationStateMachineName = "Weapon";
            fire.interruptPriority = InterruptPriority.Any;
            fire.baseMaxStock = 1;
            fire.rechargeStock = 1;
            fire.baseRechargeInterval = ShotgunCore.FireCooldown;
            fire.requiredStock = 1;
            fire.stockToConsume = 1;
            fire.fullRestockOnAssign = true;
            fire.mustKeyPress = false;
            fire.isCombatSkill = true;
            fire.canceledFromSprinting = false;
            fire.cancelSprintingOnActivation = false;

            SkillDef core = ScriptableObject.CreateInstance<SkillDef>();
            core.skillName = "V1ShotgunCore";
            (core as ScriptableObject).name = "V1ShotgunCore";
            core.skillNameToken = V1Survivor.TOKEN_PREFIX + "SECONDARY_SHOTGUN_NAME";
            core.skillDescriptionToken = V1Survivor.TOKEN_PREFIX + "SECONDARY_SHOTGUN_DESCRIPTION";
            core.icon = skillLocator.secondary.skillFamily.variants[0].skillDef.icon;
            core.activationState = new SerializableEntityStateType(typeof(ShotgunCoreCharge));
            core.activationStateMachineName = "Weapon";
            core.interruptPriority = InterruptPriority.Skill;
            core.baseMaxStock = 1;
            core.rechargeStock = 1;
            core.baseRechargeInterval = ShotgunCore.CoreCooldown;
            core.requiredStock = 1;
            core.stockToConsume = 0;
            core.fullRestockOnAssign = true;
            core.mustKeyPress = false;
            core.isCombatSkill = true;
            core.canceledFromSprinting = false;
            core.cancelSprintingOnActivation = false;

            ContentAddition.AddSkillDef(fire);
            ContentAddition.AddSkillDef(core);
            V1Arsenal.shotgunFireDef = fire;
            V1Arsenal.shotgunCoreDef = core;

            LanguageAPI.Add(fire.skillNameToken, "Core Eject Shotgun");
            LanguageAPI.Add(fire.skillDescriptionToken, "Fire 12 pellets for <style=cIsDamage>275% damage</style> each. Point blank hits deal an extra <style=cIsDamage>1100%</style>.");
            LanguageAPI.Add(core.skillNameToken, "Core Eject");
            LanguageAPI.Add(core.skillDescriptionToken, "Hold to charge, release to throw a core that explodes on contact for <style=cIsDamage>962% damage</style>.");

            InitShotgunPump(skillLocator);
        }

        // Pump Charge shotgun: the second shotgun variant (press 2 again)
        private static void InitShotgunPump(SkillLocator skillLocator)
        {
            ContentAddition.AddEntityState<ShotgunPumpFire>(out _);
            ContentAddition.AddEntityState<ShotgunPumpCharge>(out _);

            SkillDef fire = ScriptableObject.CreateInstance<SkillDef>();
            fire.skillName = "V1ShotgunPumpFire";
            (fire as ScriptableObject).name = "V1ShotgunPumpFire";
            fire.skillNameToken = V1Survivor.TOKEN_PREFIX + "PRIMARY_PUMP_NAME";
            fire.skillDescriptionToken = V1Survivor.TOKEN_PREFIX + "PRIMARY_PUMP_DESCRIPTION";
            fire.icon = skillLocator.primary.skillFamily.variants[0].skillDef.icon;
            fire.activationState = new SerializableEntityStateType(typeof(ShotgunPumpFire));
            fire.activationStateMachineName = "Weapon";
            fire.interruptPriority = InterruptPriority.Skill;
            fire.baseMaxStock = 1;
            fire.rechargeStock = 1;
            fire.baseRechargeInterval = 0f;
            fire.requiredStock = 1;
            fire.stockToConsume = 0;
            fire.fullRestockOnAssign = true;
            fire.mustKeyPress = false;
            fire.isCombatSkill = true;
            fire.canceledFromSprinting = false;
            fire.cancelSprintingOnActivation = false;

            SkillDef pump = ScriptableObject.CreateInstance<SkillDef>();
            pump.skillName = "V1ShotgunPump";
            (pump as ScriptableObject).name = "V1ShotgunPump";
            pump.skillNameToken = V1Survivor.TOKEN_PREFIX + "SECONDARY_PUMP_NAME";
            pump.skillDescriptionToken = V1Survivor.TOKEN_PREFIX + "SECONDARY_PUMP_DESCRIPTION";
            pump.icon = skillLocator.secondary.skillFamily.variants[0].skillDef.icon;
            pump.activationState = new SerializableEntityStateType(typeof(ShotgunPumpCharge));
            pump.activationStateMachineName = "Weapon";
            pump.interruptPriority = InterruptPriority.Skill;
            pump.baseMaxStock = 1;
            pump.rechargeStock = 1;
            pump.baseRechargeInterval = 0f;
            pump.requiredStock = 1;
            pump.stockToConsume = 0;
            pump.fullRestockOnAssign = true;
            pump.mustKeyPress = true;
            pump.isCombatSkill = false;
            pump.canceledFromSprinting = false;
            pump.cancelSprintingOnActivation = false;

            ContentAddition.AddSkillDef(fire);
            ContentAddition.AddSkillDef(pump);
            V1Arsenal.pumpFireDef = fire;
            V1Arsenal.pumpChargeDef = pump;

            LanguageAPI.Add(fire.skillNameToken, "Pump Charge Shotgun");
            LanguageAPI.Add(fire.skillDescriptionToken, "Fire 10 pellets for <style=cIsDamage>275% damage</style> each. More pumps: 16, then 24 wider pellets. Three pumps: <style=cIsDamage>overpump explosion</style> for <style=cIsDamage>1375% damage</style>.");
            LanguageAPI.Add(pump.skillNameToken, "Pump");
            LanguageAPI.Add(pump.skillDescriptionToken, "Pump the shotgun to charge the next shot. Pumping three times overloads it.");

            InitShotgunSaw(skillLocator);
        }

        // Sawed-on shotgun: LMB as Core Eject, RMB is the saw
        private static void InitShotgunSaw(SkillLocator skillLocator)
        {
            ContentAddition.AddEntityState<ShotgunSawCharge>(out _);

            SkillDef saw = ScriptableObject.CreateInstance<SkillDef>();
            saw.skillName = "V1ShotgunSaw";
            (saw as ScriptableObject).name = "V1ShotgunSaw";
            saw.skillNameToken = V1Survivor.TOKEN_PREFIX + "SECONDARY_SAW_NAME";
            saw.skillDescriptionToken = V1Survivor.TOKEN_PREFIX + "SECONDARY_SAW_DESCRIPTION";
            saw.icon = skillLocator.secondary.skillFamily.variants[0].skillDef.icon;
            saw.activationState = new SerializableEntityStateType(typeof(ShotgunSawCharge));
            saw.activationStateMachineName = "Weapon";
            saw.interruptPriority = InterruptPriority.Skill;
            saw.baseMaxStock = 1;
            saw.rechargeStock = 1;
            saw.baseRechargeInterval = 0f;
            saw.requiredStock = 1;
            saw.stockToConsume = 0;
            saw.fullRestockOnAssign = true;
            saw.mustKeyPress = false;
            saw.isCombatSkill = true;
            saw.canceledFromSprinting = false;
            saw.cancelSprintingOnActivation = false;

            ContentAddition.AddSkillDef(saw);
            V1Arsenal.sawDef = saw;

            InitNailguns(skillLocator);

            LanguageAPI.Add(saw.skillNameToken, "Sawed-on");
            LanguageAPI.Add(saw.skillDescriptionToken, "Hold to rev a chainsaw that shreds anything in front of you, release to throw it on a chain. Each enemy it passes is hit 3 times for <style=cIsDamage>412% damage</style>.");
        }

        // arsenal weapon skill: no RoR2 cooldown (the weapon sets its own pace, as in ULTRAKILL)
        private static SkillDef WeaponDef(string name, System.Type state, Sprite icon, string title, string description, bool mustKeyPress)
        {
            ContentAddition.AddEntityState(state, out _);
            SkillDef def = ScriptableObject.CreateInstance<SkillDef>();
            def.skillName = name;
            (def as ScriptableObject).name = name;
            def.skillNameToken = V1Survivor.TOKEN_PREFIX + name.ToUpperInvariant() + "_NAME";
            def.skillDescriptionToken = V1Survivor.TOKEN_PREFIX + name.ToUpperInvariant() + "_DESCRIPTION";
            def.icon = icon;
            def.activationState = new SerializableEntityStateType(state);
            def.activationStateMachineName = "Weapon";
            def.interruptPriority = InterruptPriority.Skill;
            def.baseMaxStock = 1;
            def.rechargeStock = 1;
            def.baseRechargeInterval = 0f;
            def.requiredStock = 1;
            def.stockToConsume = 0;
            def.fullRestockOnAssign = true;
            def.mustKeyPress = mustKeyPress;
            def.isCombatSkill = true;
            def.canceledFromSprinting = false;
            def.cancelSprintingOnActivation = false;
            ContentAddition.AddSkillDef(def);
            LanguageAPI.Add(def.skillNameToken, title);
            LanguageAPI.Add(def.skillDescriptionToken, description);
            return def;
        }

        private static void InitNailguns(SkillLocator skillLocator)
        {
            Sprite primaryIcon = skillLocator.primary.skillFamily.variants[0].skillDef.icon;
            Sprite secondaryIcon = skillLocator.secondary.skillFamily.variants[0].skillDef.icon;
            V1Arsenal.nailFireDef = WeaponDef("V1NailgunFire", typeof(NailgunFire), primaryIcon, "Nailgun",
                "Hold to fire 25 nails per second for <style=cIsDamage>51% damage</style> each.", false);
            V1Arsenal.nailMagnetDef = WeaponDef("V1NailgunMagnet", typeof(NailgunMagnetShot), secondaryIcon, "Attractor",
                "Shoot a magnet (up to 3). Nails within 25m orbit it.", true);
            V1Arsenal.nailHeatsinkDef = WeaponDef("V1NailgunHeatsink", typeof(NailgunHeatsink), secondaryIcon, "Overheat",
                "Vent a heatsink: fire a stream of <style=cIsDamage>igniting</style> nails until the barrel cools.", true);
            V1Arsenal.nailZapperDef = WeaponDef("V1NailgunZapper", typeof(NailgunZapperShot), secondaryIcon, "JumpStart",
                "Shoot a tethered zapper. After 5 seconds it shocks the enemy for <style=cIsDamage>2750% damage</style>. Breaks beyond 20m.", true);

            // railcannons
            V1Arsenal.railFireDef = WeaponDef("V1RailcannonFire", typeof(RailcannonFire), primaryIcon, "Railcannon",
                "Fire when fully charged (16s, shared by all railcannons). Electric: pierces everything for <style=cIsDamage>4x550% damage</style>. Screwdriver: drills into an enemy. Malicious: <style=cIsDamage>huge explosion</style>.", true);
            V1Arsenal.railZoomDef = WeaponDef("V1RailcannonZoom", typeof(RailcannonZoom), secondaryIcon, "Zoom",
                "Hold to zoom in.", false);

            // revolvers
            V1Arsenal.marksmanDef = WeaponDef("V1MarksmanCoin", typeof(MarksmanCoin), secondaryIcon, "Marksman",
                "Toss a coin (4 charges). Shoot it to ricochet the shot into the nearest enemy for <style=cIsDamage>550% damage</style>, more when chained.", true);
            V1Arsenal.sharpshooterDef = WeaponDef("V1SharpshooterCharge", typeof(SharpshooterCharge), secondaryIcon, "Sharpshooter",
                "Hold to spin (3 charges), release to fire a piercing shot that bounces off walls up to 3 times.", false);

            // Whiplash instead of the special skill (R)
            SkillDef whip = WeaponDef("V1Whiplash", typeof(WhiplashThrow), skillLocator.special.skillFamily.variants[0].skillDef.icon, "Whiplash",
                "Throw a hook. Light enemies are pulled to you, you are pulled to heavy ones. F punches (Feedbacker parries projectiles), G swaps arms.", false);
            whip.activationStateMachineName = "Hook";
            whip.baseRechargeInterval = 0.5f;
            whip.stockToConsume = 1;
            whip.interruptPriority = InterruptPriority.Any;
            ReplaceFamily(skillLocator.special, "V1SpecialFamily", whip);

            // rocket launchers
            V1Arsenal.rocketFireDef = WeaponDef("V1RocketFire", typeof(RocketLauncherFire), primaryIcon, "Rocket Launcher",
                "Hold to fire a rocket every second, exploding for <style=cIsDamage>962% damage</style>.", false);
            V1Arsenal.rocketFreezeDef = WeaponDef("V1RocketFreeze", typeof(RocketFreeze), secondaryIcon, "Freezeframe",
                "Freeze or unfreeze all your rockets in mid-air (up to 5 seconds).", true);
            V1Arsenal.rocketCannonballDef = WeaponDef("V1RocketCannonball", typeof(RocketCannonball), secondaryIcon, "S.R.S. Cannon",
                "Hold to charge, release to fire a cannonball for up to <style=cIsDamage>1375% damage</style>. Kills let it keep going.", false);
            V1Arsenal.rocketNapalmDef = WeaponDef("V1RocketNapalm", typeof(RocketNapalm), secondaryIcon, "Firestarter",
                "Hold to spray gasoline. Doused enemies <style=cIsDamage>ignite</style> from your explosions.", false);
        }

        private static void ReplaceFamily(GenericSkill slot, string familyName, SkillDef def)
        {
            SkillFamily family = ScriptableObject.CreateInstance<SkillFamily>();
            (family as ScriptableObject).name = familyName;
            family.variants = new SkillFamily.Variant[]
            {
                new SkillFamily.Variant
                {
                    skillDef = def,
                    unlockableDef = null,
                    viewableNode = new ViewablesCatalog.Node(def.skillNameToken, false, null),
                }
            };
            skillFamilyField.SetValue(slot, family);
            ContentAddition.AddSkillDef(def);
            ContentAddition.AddSkillFamily(family);
        }
    }
}
