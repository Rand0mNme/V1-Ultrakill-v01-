using BepInEx.Logging;

namespace V1Mod
{
    internal static class Log
    {
        private static ManualLogSource _logSource;

        internal static void Init(ManualLogSource logSource) => _logSource = logSource;

        internal static void Info(object data) => _logSource.LogInfo(data);
        internal static void Warning(object data) => _logSource.LogWarning(data);
        internal static void Error(object data) => _logSource.LogError(data);
    }
}
