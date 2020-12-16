using System.Collections.Generic;
using System.Linq;
using BattleTech.UI;
using Harmony;
using System.Reflection.Emit;
using HBS;
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

            var cdc = nameof(SGNavigationScreen.CreateDifficultyCallouts);
            cdc.Pre<SGNavigationScreen>();
            cdc.Post<SGNavigationScreen>();
            var rsi = nameof(SGNavigationScreen.RefreshSystemIndicators);
            rsi.Pre<SGNavigationScreen>();
            rsi.Post<SGNavigationScreen>();
        }
        
        // FIXME remove
        // TODO write a wrapper to make these quickly & easily
        public static void CreateDifficultyCallouts_Pre(ref Stopwatch __state)
        { __state = new Stopwatch(); __state.Start(); }
        public static void CreateDifficultyCallouts_Post(ref Stopwatch __state)
        { __state.Stop(); LogDebug("[PROFILE] " + __state.Elapsed + " CreateDifficultyCallouts"); }
        public static void RefreshSystemIndicators_Pre(ref Stopwatch __state)
        { __state = new Stopwatch(); __state.Start(); }
        public static void RefreshSystemIndicators_Post(ref Stopwatch __state)
        { __state.Stop(); LogDebug("[PROFILE] " + __state.Elapsed + " RefreshSystemIndicators"); }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            // cuts out the if/else block in CreateDifficultyCallouts() because CreateDifficultyCallouts()
            // is called later by RefreshSystemIndicators() anyways just after the block
            // FIXME? this technique is v fragile but we don't expect:
            //        - other mods modifying this chunk of IL
            //        - updates to the game (1.9.1 is supposedly the final release)
            // but this is mostly just for test purposes anyway so YOLO
            // FIXME? maybe faster to leave instructions in enumerable form &
            //        not convert it to a list & back
            int startIndex = 7;
            int endIndex = 15;

            LogInfo("Overwriting instructions in SGNavigationScreen.OnDifficultySelectionChanged()" +
                    " at indices " + startIndex + " through " + endIndex + " with nops");
            var codes = new List<CodeInstruction>(instructions);
            for(int i = startIndex; i <= endIndex; i++) {
                LogSpam("overwriting index " + i + " value " + codes[i].opcode + " with nop");
                codes[i].opcode = OpCodes.Nop;
            }

            return codes.AsEnumerable();
        }
    }
}
