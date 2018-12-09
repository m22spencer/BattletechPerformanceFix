using Harmony;
using BattleTech.UI;
using System;
using System.Reflection;
using System.Diagnostics;
using static BattletechPerformanceFix.Extensions;

namespace BattletechPerformanceFix
{
    [HarmonyPatch(typeof(MechLabPanel), "RequestResources")]
    public static class Patch_MechLabPanel_RequestResources {
        public static void Prefix(MechLabPanel __instance) {
            Traverse.Create(__instance)
                    .Field("requestsPerFrame")
                    .SetValue(3);
            try {
                Control.harmony.Unpatch(WaitForLoads_MoveNext, InterceptMoveNextMI);
                
            } catch(Exception e) {
                LogError(string.Format("[MechLabLoadThrottling] Not patched: {0}", e));
            }
            LogDebug("[MechLabLoadThrottling] Patching WaitForLoads");
            Control.harmony.Patch(WaitForLoads_MoveNext, InterceptMoveNextHM, null);
        }

        // cache a few things for performance reasons.
        public static MethodInfo WaitForLoads_MoveNext = Type.GetType("BattleTech.UI.MechLabPanel+<WaitForLoads>c__Iterator0, Assembly-CSharp", true).GetMethod("MoveNext");
        public static MethodInfo InterceptMoveNextMI = typeof(Patch_MechLabPanel_RequestResources).GetMethod("InterceptMoveNext");
        public static HarmonyMethod InterceptMoveNextHM = new HarmonyMethod(InterceptMoveNextMI);

        public static bool InterceptMoveNext( Object __instance
                                            , MethodBase __originalMethod
                                            , ref bool __result
                                            ) {
            Control.harmony.Unpatch(__originalMethod, InterceptMoveNextMI);
            var hasItems = true;
            Stopwatch sw = new Stopwatch();
            sw.Start();
            var pms = new object[] {};
            // WaitForLoads utilizes a hardcoded # of requestPerFrame (currently 3)
            // This penalizes more powerful machines, as they will load 3 requests instantly, and sit doing nothing till the end
            // of the frame.
            // A quickfix for this, is to spend most of the frame loading, then allow for a render update. (hence < 15ms)
            // A proper fix would account for variable framerates, but it's not very important if there's some UI stutter or framerate drop here.
            while (hasItems && sw.Elapsed.TotalMilliseconds < 15.0) {
                hasItems = (bool)__originalMethod.Invoke(__instance, pms);
            }
            if (!hasItems) {
                __result = false;
                LogDebug(string.Format("[MechLabLoadThrottling] WaitForLoads complete and unpatched"));
            } else {
                Control.harmony.Patch(WaitForLoads_MoveNext, InterceptMoveNextHM, null);
                __result = true;
            }
            return false;
        }
    }
}
