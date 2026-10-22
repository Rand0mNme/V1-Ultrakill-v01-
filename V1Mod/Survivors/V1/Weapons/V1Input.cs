using RoR2;

namespace V1Mod.Survivors.V1.Weapons
{
    // Weapon and arm keys are read directly, so ignore them while the chat, console or a menu is open
    public static class V1Input
    {
        public static bool KeysAllowed(CharacterBody body)
        {
            if (!body || !body.hasEffectiveAuthority || PauseManager.isPaused || !body.healthComponent || !body.healthComponent.alive)
            {
                return false;
            }
            LocalUser user = body.master && body.master.playerCharacterMasterController && body.master.playerCharacterMasterController.networkUser
                ? body.master.playerCharacterMasterController.networkUser.localUser
                : null;
            return user == null || !user.isUIFocused;
        }
    }
}
