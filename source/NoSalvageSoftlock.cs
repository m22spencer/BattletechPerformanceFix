using Harmony;
using System;
using BattleTech;
using BattleTech.UI;
using static BattletechPerformanceFix.Extensions;

namespace BattletechPerformanceFix
{
    public class NoSalvageSoftlock : Feature
    {
        public void Activate()
        {
            var hap = Main.CheckPatch(AccessTools.Method(typeof(AAR_SalvageChosen), nameof(AAR_SalvageChosen.HasAllPriority))
                                        , "80d43f27b8537a10099fd1ebceb4b6961549f30518c00de53fcf38c27623f7ec");
            Main.harmony.Patch(hap
                                 , new HarmonyMethod(typeof(NoSalvageSoftlock), nameof(NoSalvageSoftlock.HasAllPriority)), null);
        }

        public static bool HasAllPriority(AAR_SalvageChosen __instance, ref bool __result)
        {
            try
            {
                int negotiated = __instance.contract.FinalPrioritySalvageCount;
                int totalSalvageMadeAvailable = __instance.parent.TotalSalvageMadeAvailable;
                int count = __instance.PriorityInventory.Count;
                int num = negotiated;
                if (num > totalSalvageMadeAvailable)
                {
                    num = totalSalvageMadeAvailable;
                }
                if (num > 7)
                {
                    num = 7;
                }
                LogDebug(string.Format("HasAllPriority :negotiated {0} :available {1} :selected {2} :clamped {3}", negotiated, totalSalvageMadeAvailable, count, num));
                __result = count >= num;
                return false;
            } catch (Exception e)
            {
                LogException(e);
                return true;
            }
        }
    }
}
