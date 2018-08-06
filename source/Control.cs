using HBS.Logging;
using Harmony;
using System.Reflection;
using DynModLib;
using BattleTech;
using BattleTech.UI;
using BattleTech.Data;
using System.Diagnostics;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace BattletechPerformanceFix
{
    public static class Control
    {
        public static Mod mod;

        public static ModSettings settings = new ModSettings();

        public static HarmonyInstance harmony;

        public static void Start(string modDirectory, string json)
        {
            mod = new Mod(modDirectory);
            Logger.SetLoggerLevel(mod.Logger.Name, LogLevel.Log);

            mod.LoadSettings(settings);
			
			harmony = HarmonyInstance.Create(mod.Name);
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            PatchMechlabLimitItems.Initialize();
            
            // logging output can be found under BATTLETECH\BattleTech_Data\output_log.txt
            // or also under yourmod/log.txt
            mod.Logger.Log("Loaded " + mod.Name);
        }
    }

    public class Hook : IDisposable {
        readonly MethodBase orig;
        readonly MethodInfo act;
        public void Dispose() {
            Control.harmony.RemovePatch(orig, act);
        }

        Hook(MethodBase target, MethodInfo mi) {
            orig = target;
            act = mi;
        }

        public static Hook Prefix(MethodBase target, MethodInfo mi) {
            var h = new Hook(target, mi);
            Control.harmony.Patch(target, new HarmonyMethod(mi), null);
            return h;
        }

        public static Hook Postfix(MethodBase target, MethodInfo mi) {
            var h = new Hook(target, mi);
            Control.harmony.Patch(target, null, new HarmonyMethod(mi));
            return h;
        }
    }

    public static class Fun {
        public static Action fun (this Action a) { return a; }
        public static Action<A> fun<A>(this Action<A> a) { return a; }

        
        public static Func<A> fun<A>(this Func<A> a) { return a; }
        public static Func<A,B> fun<A,B>(this Func<A,B> a) { return a; }
    }
}