using BepInEx.Configuration;
using UnityEngine;

namespace V1Mod.Modules
{
    // Key settings. File: BepInEx/config/com.oleksandr.V1Mod.cfg (in r2modman: Config editor -> com.oleksandr.V1Mod).
    // Changes to the file are picked up without restarting the game.
    // Values are Unity key names (KeyCode): Alpha1, Q, F, Mouse3, Mouse4, LeftAlt, None (disabled), etc.
    // Whiplash sits on the RoR2 special skill (key from the game settings); an extra key can be set here.
    public static class V1Config
    {
        public static ConfigEntry<KeyCode> Revolvers;
        public static ConfigEntry<KeyCode> Shotguns;
        public static ConfigEntry<KeyCode> Nailguns;
        public static ConfigEntry<KeyCode> Railcannons;
        public static ConfigEntry<KeyCode> RocketLaunchers;
        public static ConfigEntry<KeyCode> PreviousWeapon;
        public static ConfigEntry<KeyCode> Punch;
        public static ConfigEntry<KeyCode> SwapArm;
        public static ConfigEntry<KeyCode> Whiplash;
        public static ConfigEntry<bool> MouseWheelSwitch;
        public static ConfigEntry<bool> UnlockAllWeapons;
        public static ConfigEntry<float> StylePriceMultiplier;
        public static ConfigEntry<float> BloodHealRadius;
        public static ConfigEntry<float> BloodHealFraction;
        public static ConfigEntry<float> KillHealFraction;

        public static void Init(ConfigFile config)
        {
            const string keys = "Keys";
            Revolvers = config.Bind(keys, "Revolvers", KeyCode.Alpha1, "Revolvers (press again for the next variant)");
            Shotguns = config.Bind(keys, "Shotguns", KeyCode.Alpha2, "Shotguns (press again for the next variant)");
            Nailguns = config.Bind(keys, "Nailguns", KeyCode.Alpha3, "Nailguns (press again for the next variant)");
            Railcannons = config.Bind(keys, "Railcannons", KeyCode.Alpha4, "Railcannons (press again for the next variant)");
            RocketLaunchers = config.Bind(keys, "RocketLaunchers", KeyCode.Alpha5, "Rocket launchers (press again for the next variant)");
            PreviousWeapon = config.Bind(keys, "PreviousWeapon", KeyCode.Q, "Previous weapon. RoR2 uses Q for equipment by default, so set another key here or None");
            Punch = config.Bind(keys, "Punch", KeyCode.F, "Punch (hold with the Knuckleblaster for a blast wave)");
            SwapArm = config.Bind(keys, "SwapArm", KeyCode.G, "Swap arm: Feedbacker / Knuckleblaster");
            Whiplash = config.Bind(keys, "Whiplash", KeyCode.None, "Whiplash hook (hold). It is also on the RoR2 special skill (R by default); this key is an extra one. If you set R here, unbind R from the special skill in the RoR2 settings");
            MouseWheelSwitch = config.Bind(keys, "MouseWheelSwitch", true, "Scroll through weapons with the mouse wheel");

            UnlockAllWeapons = config.Bind("Shop", "UnlockAllWeapons", false, "All weapons unlocked from the start (no SmileOS shop needed). Otherwise a run starts with the Piercer and the rest is bought at the terminal near the landing spot");
            StylePriceMultiplier = config.Bind("Shop", "StylePriceMultiplier", 0.1f, "Shop prices in style points = ULTRAKILL price x this number (Marksman 7500 P x 0.1 = 750 P)");
            const string blood = "Blood";
            BloodHealRadius = config.Bind(blood, "BloodHealRadius", 6f, "Blood healing: distance (m) from which V1 absorbs the blood of wounded and killed enemies");
            BloodHealFraction = config.Bind(blood, "BloodHealFraction", 0.05f, "Fraction of damage dealt up close that is returned as health");
            KillHealFraction = config.Bind(blood, "KillHealFraction", 0.075f, "Fraction of V1's max health restored by a kill up close");
        }

        public static KeyCode[] SlotKeys => new[] { Revolvers.Value, Shotguns.Value, Nailguns.Value, Railcannons.Value, RocketLaunchers.Value };

        // RoR2 reads input through Rewired, so keyboard keys and mouse buttons are queried from it
        public static bool Down(KeyCode key)
        {
            if (key == KeyCode.None || !Rewired.ReInput.isReady)
            {
                return false;
            }
            int mouse = MouseIndex(key);
            if (mouse >= 0)
            {
                return Rewired.ReInput.controllers.Mouse.GetButtonDown(mouse);
            }
            return Rewired.ReInput.controllers.Keyboard.GetKeyDown(key);
        }

        public static bool Held(KeyCode key)
        {
            if (key == KeyCode.None || !Rewired.ReInput.isReady)
            {
                return false;
            }
            int mouse = MouseIndex(key);
            if (mouse >= 0)
            {
                return Rewired.ReInput.controllers.Mouse.GetButton(mouse);
            }
            return Rewired.ReInput.controllers.Keyboard.GetKey(key);
        }

        private static int MouseIndex(KeyCode key)
        {
            return key >= KeyCode.Mouse0 && key <= KeyCode.Mouse6 ? key - KeyCode.Mouse0 : -1;
        }
    }
}
