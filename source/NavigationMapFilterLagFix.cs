using System.Collections.Generic;
using System.Linq;
using BattleTech.UI;
using Harmony;
using System.Reflection;
using System.Reflection.Emit;
using static BattletechPerformanceFix.Extensions;

namespace BattletechPerformanceFix
{
    /* Removes a redundant call for creating difficulty callouts, saves a few seconds per filter selection */
    class NavigationMapFilterLagFix : Feature
    {
        public void Activate()
        {
            var method = AccessTools.Method(typeof(SGNavigationScreen), "OnDifficultySelectionChanged");
            var transpiler = new HarmonyMethod(typeof(NavigationMapFilterLagFix), nameof(Transpiler));
            Main.harmony.Patch(method, null, null, transpiler);
        }
        
        // cuts out the if/else block in OnDifficultySelectionChanged() because CreateDifficultyCallouts()
        // is called later by RefreshSystemIndicators() anyways just after the block
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int startIndex = -1;
            int endIndex = -1;

            // searches for the if block start & end points, in case some other mod is modifying this function
            // (which is why we don't hardcode the indices)
            var code = instructions.ToList();
            for (int i = 0; i < code.Count-1; i++) {
                if (startIndex == -1) {
                    if (code[i].opcode == OpCodes.Ldarg_1 && code[i+1].opcode == OpCodes.Brtrue) {
                        startIndex = i;
                    }
                } else {
                    if (code[i].opcode == OpCodes.Ldarg_1 && code[i+1].opcode == OpCodes.Call &&
                        (code[i+1].operand as MethodInfo).Name == "CreateDifficultyCallouts") {
                        endIndex = i+1;
                        break;
                    }
                }
            }

            if (startIndex != -1 && endIndex != -1) {
                LogDebug("Overwriting instructions in SGNavigationScreen.OnDifficultySelectionChanged()" +
                        " at indices " + startIndex + "-" + endIndex + " with nops");
                for (int i = startIndex; i <= endIndex; i++) {
                    code[i].opcode = OpCodes.Nop;
                }
            } else {
                LogError("Failed to find the code to overwrite in " +
                         "SGNavigationScreen.OnDifficultySelectionChanged(); no changes were made.");
                LogError("NavigationMapFilterLagFix has not been applied, report this as a bug");
            }

            return code.AsEnumerable();
        }
    }
}
