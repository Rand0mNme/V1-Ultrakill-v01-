using BepInEx;
using R2API;
using R2API.Utils;
using System.Security;
using System.Security.Permissions;
using V1Mod.Survivors.V1;

[module: UnverifiableCode]
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]

namespace V1Mod
{
    [BepInDependency(PrefabAPI.PluginGUID)]
    [BepInDependency(LanguageAPI.PluginGUID)]
    [BepInDependency("com.bepis.r2api.content_management")]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.EveryoneNeedSameModVersion)]
    [BepInPlugin(MODUID, MODNAME, MODVERSION)]
    public class V1Plugin : BaseUnityPlugin
    {
        public const string MODUID = "com.oleksandr.V1Mod";
        public const string MODNAME = "V1Mod";
        public const string MODVERSION = "0.1.0";

        // prefix for language tokens so they don't clash with other mods
        public const string DEVELOPER_PREFIX = "OLEKSANDR";

        public static V1Plugin instance;

        private float configCheckTimer;
        private System.DateTime configWriteTime;

        void Update()
        {
            Modules.V1Sounds.UpdateVolume();
            // the config file changed (e.g. in r2modman): reload it without restarting the game
            configCheckTimer -= UnityEngine.Time.unscaledDeltaTime;
            if (configCheckTimer <= 0f)
            {
                configCheckTimer = 1f;
                try
                {
                    System.DateTime t = System.IO.File.GetLastWriteTimeUtc(Config.ConfigFilePath);
                    if (configWriteTime != default && t != configWriteTime)
                    {
                        Config.Reload();
                        Log.Info("Config reloaded");
                    }
                    configWriteTime = t;
                }
                catch (System.Exception)
                {
                }
            }
        }

        void Awake()
        {
            instance = this;
            Log.Init(Logger);
            Modules.V1Config.Init(Config);

            // asset bundle and sounds first: the survivor takes its crosshair and effects from them
            Modules.V1Sounds.LoadAll(System.IO.Path.GetDirectoryName(Info.Location));
            Modules.V1Assets.Load(System.IO.Path.GetDirectoryName(Info.Location));

            V1Survivor.Init();
        }
    }
}

