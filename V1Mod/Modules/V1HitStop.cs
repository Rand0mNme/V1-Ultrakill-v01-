using System.Collections;
using RoR2;
using UnityEngine;

namespace V1Mod.Modules
{
    // ULTRAKILL hit stop (TimeController.HitStop / TrueStop): the game freezes for a fraction of a second of real time.
    // Time can't be stopped for just one player in multiplayer, so hit stop only works in single player.
    public static class V1HitStop
    {
        private static float currentStop;
        private static Coroutine running;

        // HitStop: if the new stop is longer than the current one, time = 0 for length seconds of real time
        public static void HitStop(float length)
        {
            if (!RoR2Application.isInSinglePlayer || PauseManager.isPaused || !V1Plugin.instance)
            {
                return;
            }
            if (length <= currentStop)
            {
                return;
            }
            currentStop = length;
            Time.timeScale = 0f;
            if (running != null)
            {
                V1Plugin.instance.StopCoroutine(running);
            }
            running = V1Plugin.instance.StartCoroutine(Continue(length));
        }

        private static IEnumerator Continue(float length)
        {
            yield return new WaitForSecondsRealtime(length);
            currentStop = 0f;
            running = null;
            // if the player paused during the stop, leave time alone: unpausing restores it
            if (!PauseManager.isPaused && Time.timeScale == 0f)
            {
                Time.timeScale = 1f;
            }
        }
    }
}
