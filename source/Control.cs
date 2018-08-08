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
            mod.LoadSettings(settings);

            mod.Logger.Log(settings.logLevel);
            mod.Logger.LogDebug("Debug enabled");

			harmony = HarmonyInstance.Create(mod.Name);

            harmony.PatchAll(Assembly.GetExecutingAssembly());

            PatchMechlabLimitItems.Initialize();

            // logging output can be found under BATTLETECH\BattleTech_Data\output_log.txt
            // or also under yourmod/log.txt
            mod.Logger.Log("Loaded " + mod.Name);
        }
    }

    /* Backtracking backtracking backtracking regex is not a good way to strip comments from a string. */
    [HarmonyPatch(typeof(HBS.Util.JSONSerializationUtility), "StripHBSCommentsFromJSON")]
    public class DontStripComments {
        // TODO: Is this function always called from main thread? We need to patch loadJSON, but it's generic
        public static bool guard = false;
        public static bool Prefix(string json, ref string __result) {
            if (guard == true) return true;
            try {
            // Try to parse the json, if it doesn't work, use HBS comment stripping code.
            try { fastJSON.JSON.Parse(json);
                __result = json;
            } catch (Exception e) {
                guard = true;
                __result = new Traverse(typeof(HBS.Util.JSONSerializationUtility)).Method("StripHBSCommentsFromJSON").GetValue<string>(json);
                guard = false;
            }
            } catch(Exception e) {
                Control.mod.Logger.LogException(e);
            }
            return false;
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
        public static Func<A,B,C> fun<A,B,C>(this Func<A,B,C> a) { return a; }
    }
}