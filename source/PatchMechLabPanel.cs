using HBS.Logging;
using Harmony;
using BattleTech;
using BattleTech.UI;


namespace BattletechPerformanceFix
{
    [HarmonyPatch(typeof(MechLabPanel), "RequestResources")]
    public static class Patch_MechLabPanel_RequestResources {
        public static void Prefix(MechLabPanel __instance) {
            Traverse.Create(__instance)
                    .Field("requestsPerFrame")
                    .SetValue(1000);
        }
    }
}