using RoR2;
using RoR2.Skills;
using UnityEngine;
using V1Mod.Modules;

namespace V1Mod.Survivors.V1.Weapons
{
    // Weapon switching as in ULTRAKILL (GunControl): keys from the config (V1Config, 1-5 by default);
    // pressing the same number again gives the next variant of that weapon; the mouse wheel scrolls weapons
    // (each remembers its last variant); Q is the previous weapon.
    // LMB/RMB skills are swapped through RoR2 skill overrides (SkillOverride), each weapon has its own cooldowns.
    public class V1Arsenal : MonoBehaviour
    {
        public static SkillDef shotgunFireDef;
        public static SkillDef shotgunCoreDef;
        public static SkillDef pumpFireDef;
        public static SkillDef pumpChargeDef;
        public static SkillDef sawDef;
        public static SkillDef nailFireDef;
        public static SkillDef nailMagnetDef;
        public static SkillDef nailHeatsinkDef;
        public static SkillDef nailZapperDef;
        public static SkillDef railFireDef;
        public static SkillDef railZoomDef;
        public static SkillDef rocketFireDef;
        public static SkillDef rocketFreezeDef;
        public static SkillDef rocketCannonballDef;
        public static SkillDef rocketNapalmDef;
        public static SkillDef marksmanDef;
        public static SkillDef sharpshooterDef;

        // slots (numbers 1..5) and their variants are V1ViewModel model ids (ULTRAKILL color order: blue, green, red)
        private static readonly int[][] Slots =
        {
            new[] { V1ViewModel.WeaponPiercer, V1ViewModel.WeaponMarksman, V1ViewModel.WeaponSharpshooter },
            new[] { V1ViewModel.WeaponShotgun, V1ViewModel.WeaponShotgunPump, V1ViewModel.WeaponShotgunSaw },
            new[] { V1ViewModel.WeaponNailMagnet, V1ViewModel.WeaponNailOverheat, V1ViewModel.WeaponNailZapper },
            new[] { V1ViewModel.WeaponRailElectric, V1ViewModel.WeaponRailScrewdriver, V1ViewModel.WeaponRailMalicious },
            new[] { V1ViewModel.WeaponRocketFreeze, V1ViewModel.WeaponRocketCannonball, V1ViewModel.WeaponRocketNapalm },
        };

        // sawblade launchers instead of nailguns in slot 3 (toggle in the SmileOS shop)
        private static readonly int[] SawSlot = { V1ViewModel.WeaponSawMagnet, V1ViewModel.WeaponSawOverheat, V1ViewModel.WeaponSawZapper };

        private int[] SlotWeapons(int s) => s == 2 && V1Shop.SawMode(body) ? SawSlot : Slots[s];

        public int Current => current;

        // Drawing a weapon as in ULTRAKILL: the revolver and shotgun fire only after the draw animation
        // (ReadyGun event in PickUp.anim at about 0.36 s, Equip.anim at about 0.44 s), so switching can't bypass the fire rate
        private float readyAt;
        public bool Ready => Time.time >= readyAt;

        private static float EquipDelay(int weapon)
        {
            if (weapon == V1ViewModel.WeaponPiercer || weapon == V1ViewModel.WeaponMarksman || weapon == V1ViewModel.WeaponSharpshooter)
            {
                return 0.36f;
            }
            if (weapon == V1ViewModel.WeaponShotgun || weapon == V1ViewModel.WeaponShotgunPump || weapon == V1ViewModel.WeaponShotgunSaw)
            {
                return 0.44f;
            }
            return 0f;
        }

        public static void InitEquipPatch()
        {
            new HarmonyLib.Harmony(V1Plugin.MODUID + ".equip").PatchAll(typeof(EquipPatch));
        }

        [HarmonyLib.HarmonyPatch(typeof(GenericSkill), nameof(GenericSkill.CanExecute))]
        public static class EquipPatch
        {
            public static void Postfix(GenericSkill __instance, ref bool __result)
            {
                if (!__result || !__instance.characterBody)
                {
                    return;
                }
                SkillLocator skills = __instance.characterBody.skillLocator;
                if (skills && (__instance == skills.primary || __instance == skills.secondary))
                {
                    V1Arsenal a = __instance.characterBody.GetComponent<V1Arsenal>();
                    if (a && !a.Ready)
                    {
                        __result = false;
                    }
                }
            }
        }


        private CharacterBody body;
        private int current = V1ViewModel.WeaponPiercer;
        private int previous = V1ViewModel.WeaponPiercer;
        private readonly int[] slotVariant = new int[Slots.Length];

        private void Start()
        {
            body = GetComponent<CharacterBody>();
            // weapon charges build up even when the weapon isn't in hand (WeaponCharges)
            SawData.Get(gameObject);
            NailgunData.Get(gameObject);
            RailData.Get(gameObject);
            RocketData.Get(gameObject);
            RevolverAltData.Get(gameObject);
        }

        private int SlotOf(int weapon)
        {
            for (int s = 0; s < Slots.Length; s++)
            {
                if (System.Array.IndexOf(Slots[s], weapon) >= 0 || (s == 2 && System.Array.IndexOf(SawSlot, weapon) >= 0))
                {
                    return s;
                }
            }
            return 0;
        }

        private void Update()
        {
            if (!V1Input.KeysAllowed(body))
            {
                return;
            }
            int wanted = current;
            int currentSlot = SlotOf(current);
            bool pressed = false;
            KeyCode[] slotKeys = V1Config.SlotKeys;
            for (int s = 0; s < Slots.Length && s < slotKeys.Length; s++)
            {
                if (V1Config.Down(slotKeys[s]))
                {
                    pressed = true;
                    // same weapon: next owned variant; another weapon: the last selected one (or the first owned)
                    int v = OwnedVariant(s, s == currentSlot ? slotVariant[s] + 1 : slotVariant[s]);
                    if (v >= 0)
                    {
                        slotVariant[s] = v;
                        wanted = SlotWeapons(s)[v];
                    }
                    break;
                }
            }
            if (!pressed)
            {
                if (V1Config.Down(V1Config.PreviousWeapon.Value))
                {
                    if (V1Shop.Owns(body, previous))
                    {
                        wanted = previous;
                    }
                }
                else if (V1Config.MouseWheelSwitch.Value)
                {
                    float wheel = Rewired.ReInput.isReady ? Rewired.ReInput.controllers.Mouse.GetAxis(2) : 0f;
                    if (wheel > 0.01f || wheel < -0.01f)
                    {
                        // scroll, skipping weapons with no owned variant
                        int step = wheel > 0f ? 1 : Slots.Length - 1;
                        for (int s = (currentSlot + step) % Slots.Length; s != currentSlot; s = (s + step) % Slots.Length)
                        {
                            int v = OwnedVariant(s, slotVariant[s]);
                            if (v >= 0)
                            {
                                slotVariant[s] = v;
                                wanted = SlotWeapons(s)[v];
                                break;
                            }
                        }
                    }
                }
            }
            if (wanted != current)
            {
                Switch(wanted);
            }
        }

        // first owned variant of the slot starting at from (wrapping around); -1 = nothing owned (SmileOS shop)
        private int OwnedVariant(int slot, int from)
        {
            int n = SlotWeapons(slot).Length;
            for (int i = 0; i < n; i++)
            {
                int v = ((from + i) % n + n) % n;
                if (V1Shop.Owns(body, SlotWeapons(slot)[v]))
                {
                    return v;
                }
            }
            return -1;
        }

        // the weapon's LMB/RMB skills; the revolver uses the character's own skills
        private static void DefsOf(int weapon, out SkillDef primary, out SkillDef secondary)
        {
            switch (weapon)
            {
                case V1ViewModel.WeaponShotgun:
                    primary = shotgunFireDef;
                    secondary = shotgunCoreDef;
                    break;
                case V1ViewModel.WeaponShotgunPump:
                    primary = pumpFireDef;
                    secondary = pumpChargeDef;
                    break;
                case V1ViewModel.WeaponShotgunSaw:
                    primary = shotgunFireDef;
                    secondary = sawDef;
                    break;
                case V1ViewModel.WeaponNailMagnet:
                case V1ViewModel.WeaponSawMagnet:
                    primary = nailFireDef;
                    secondary = nailMagnetDef;
                    break;
                case V1ViewModel.WeaponNailOverheat:
                case V1ViewModel.WeaponSawOverheat:
                    primary = nailFireDef;
                    secondary = nailHeatsinkDef;
                    break;
                case V1ViewModel.WeaponRailElectric:
                case V1ViewModel.WeaponRailScrewdriver:
                case V1ViewModel.WeaponRailMalicious:
                    primary = railFireDef;
                    secondary = railZoomDef;
                    break;
                case V1ViewModel.WeaponRocketFreeze:
                    primary = rocketFireDef;
                    secondary = rocketFreezeDef;
                    break;
                case V1ViewModel.WeaponRocketCannonball:
                    primary = rocketFireDef;
                    secondary = rocketCannonballDef;
                    break;
                case V1ViewModel.WeaponRocketNapalm:
                    primary = rocketFireDef;
                    secondary = rocketNapalmDef;
                    break;
                case V1ViewModel.WeaponMarksman:
                    primary = null;
                    secondary = marksmanDef;
                    break;
                case V1ViewModel.WeaponSharpshooter:
                    primary = null;
                    secondary = sharpshooterDef;
                    break;
                case V1ViewModel.WeaponNailZapper:
                case V1ViewModel.WeaponSawZapper:
                    primary = nailFireDef;
                    secondary = nailZapperDef;
                    break;
                default:
                    primary = null;
                    secondary = null;
                    break;
            }
        }

        // the shop toggled nailguns/sawblade launchers: if a nailgun is in hand, switch to the other form of the same variant
        public void RefreshNailForm()
        {
            if (V1ViewModel.IsNailgun(current))
            {
                Switch(SlotWeapons(2)[V1ViewModel.NailVariant(current)]);
            }
        }

        private void Switch(int weapon)
        {
            SkillLocator skills = body.skillLocator;
            if (!skills)
            {
                return;
            }
            // switching weapons interrupts what the previous one was doing (charging, saw, burst, zoom)
            EntityStateMachine weaponMachine = EntityStateMachine.FindByCustomName(gameObject, "Weapon");
            if (weaponMachine && !(weaponMachine.state is EntityStates.Idle) && !weaponMachine.IsInMainState())
            {
                weaponMachine.SetNextStateToMain();
            }
            // remove the previous weapon's overrides
            DefsOf(current, out SkillDef oldPrimary, out SkillDef oldSecondary);
            if (oldPrimary)
            {
                skills.primary.UnsetSkillOverride(this, oldPrimary, GenericSkill.SkillOverridePriority.Replacement);
            }
            if (oldSecondary)
            {
                skills.secondary.UnsetSkillOverride(this, oldSecondary, GenericSkill.SkillOverridePriority.Replacement);
            }
            DefsOf(weapon, out SkillDef newPrimary, out SkillDef newSecondary);
            if (newPrimary)
            {
                skills.primary.SetSkillOverride(this, newPrimary, GenericSkill.SkillOverridePriority.Replacement);
            }
            if (newSecondary)
            {
                skills.secondary.SetSkillOverride(this, newSecondary, GenericSkill.SkillOverridePriority.Replacement);
            }
            int slot = SlotOf(weapon);
            slotVariant[slot] = Mathf.Max(0, System.Array.IndexOf(SlotWeapons(slot), weapon));
            previous = current;
            current = weapon;
            readyAt = Time.time + EquipDelay(weapon);
            NailgunData nail = NailgunData.Get(gameObject);
            nail.variant = V1ViewModel.IsNailgun(weapon) ? V1ViewModel.NailVariant(weapon) : -1;
            nail.alt = V1ViewModel.IsSawLauncher(weapon);
            V1ViewModel.Get(gameObject)?.SetWeapon(weapon);
            V1Sounds.Play("Footstep1", gameObject, 0.2f, 1.6f, true);
        }
    }
}
