using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Harmony;
using static BattletechPerformanceFix.Extensions;

namespace BattletechPerformanceFix
{
    // BattleTech disables logging during load. Not sure why.
    //   this re-enables it to give more information about errors in log.
    class EnableLoggingDuringLoads : Feature
    {
        public void Activate() {
            Main.harmony.Patch( AccessTools.Method(typeof(BattleTech.LevelLoader), "EnableLogging")
                              , new HarmonyMethod(AccessTools.Method(typeof(EnableLoggingDuringLoads), "EnableLogging")));
        }

        public static void EnableLogging(ref bool enable)
            => enable = true;
    }
}
