using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HBS.DebugConsole;
using UnityEngine;
using static BattletechPerformanceFix.Extensions;

namespace BattletechPerformanceFix
{
    class EnableConsole : Feature
    {
        public void Activate()
        {
            nameof(DebugConsoleHelper.LateUpdate).Post<DebugConsoleHelper>();
            "get_DebugCommandsUnlocked".Post<DebugConsole>();
        }

        public static void get_DebugCommandsUnlocked_Post(ref bool __result)
            => __result = true;

        public static void LateUpdate_Post(DebugConsoleHelper __instance)
        {
            var console = __instance.console;
            if (Input.GetKeyDown(KeyCode.BackQuote))
                __instance.console.SetMode(console.mode == DebugConsole.WindowMode.LogWindow 
                    ? DebugConsole.WindowMode.Hidden 
                    : DebugConsole.WindowMode.LogWindow);
        }
    }
}
