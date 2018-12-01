using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Harmony;
using BattleTech;
using System.Reflection.Emit;

namespace BattletechPerformanceFix
{
    class DisableSensitiveDataLogDump : Feature
    {
        public void Activate()
        {
            Control.harmony.Patch(AccessTools.Method(typeof(UnityGameInstance), nameof(OnInternetConnectivityResult))
                                 , new HarmonyMethod(typeof(DisableSensitiveDataLogDump), nameof(OnInternetConnectivityResult)));

            /* Mods are hooked too late to guard this
            Control.harmony.Patch(AccessTools.Method(typeof(SteamManager), nameof(Awake))
                                 , null, null, new HarmonyMethod(typeof(DisableSensitiveDataLogDump), nameof(Awake)));
                                 */
        }

        public static bool OnInternetConnectivityResult(UnityGameInstance __instance, bool success)
        {
            new Traverse(__instance).Property("InternetAvailable").SetValue(success);
            return false;
        }

        public static IEnumerable<CodeInstruction> Awake(IEnumerable<CodeInstruction> ins)
        {
            var insl = ins.ToList();
            var num = insl.Count;
            return insl.Take(insl.Count - 8).Concat(Control.Sequence(new CodeInstruction(OpCodes.Ret)));
        }
    }
}
