using BattleTech.UI;
using Harmony;

namespace BattletechPerformanceFix
{
    public class DisableCombatTurnDelays : Feature
    {
        public void Activate()
        {
            var fadeOut = "FadeOutNotificationOverSecondsCoroutine";
            var showTeam = "ShowTeamNotification";
            fadeOut.Pre<TurnEventNotification>();
            showTeam.Pre<TurnEventNotification>();
        }

        [HarmonyPatch(typeof(TurnEventNotification), "FadeOutNotificationOverSecondsCoroutine")]
        public class FadeOutNotificationOverSecondsCoroutine_Pre
        {
            public static void Prefix(ref float seconds)
            {
                seconds = 0;
            }
        }
    }
}
