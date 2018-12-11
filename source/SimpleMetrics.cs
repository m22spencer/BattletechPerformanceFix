using System;
using System.Reflection;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Harmony;
using static BattletechPerformanceFix.Extensions;

namespace BattletechPerformanceFix
{
    class SimpleMetrics : Feature
    {
        public static bool Active = false;
        public void Activate() {
            var self = typeof(SimpleMetrics);
            Main.harmony.Patch( AccessTools.Method(typeof(BattleTech.UI.SimGameOptionsMenu), "OnAddedToHierarchy")
                              , new HarmonyMethod(AccessTools.Method(self, "Summary")));
            Main.harmony.Patch( AccessTools.Method(typeof(BattleTech.UI.SGLoadSavedGameScreen), "LoadSelectedSlot")
                              , new HarmonyMethod(AccessTools.Method(self, "Summary")));
            Active = true;
        }


        public static void Instrument(MethodBase meth) {
            if (!Active) return;
            if (meth == null)
                LogError($"Cannot instrument null meth from {new StackTrace().ToString()}");

            LogDebug($"Instrumenting {meth.DeclaringType.FullName}::{meth.ToString()}");

            if(PreHook == null || PostHook == null) {
                Log("Initializing simple metrics hooks");

                var self = typeof(SimpleMetrics);
                PreHook = new HarmonyMethod(AccessTools.Method(self, nameof(__Pre)));
                PreHook.prioritiy = Priority.First;
                PostHook = new HarmonyMethod(AccessTools.Method(self, nameof(__Post)));
                PostHook.prioritiy = Priority.Last;
            }

            if (meth.IsGenericMethod || meth.IsGenericMethodDefinition) {
                LogError($"Cannot instrument a generic method {meth.DeclaringType.FullName}::{meth.ToString()}");
            } else if (meth.GetMethodBody() == null) {
                LogError($"Cannot instrument a method with no body {meth.DeclaringType.FullName}::{meth.ToString()}");
            } else {
                Trap(() => Main.harmony.Patch(meth, PreHook, PostHook));
            }
        }

        public static void Track(MethodBase meth) {
            if (!Active) return;
            if (meth == null)
                LogError($"Cannot instrument null meth from {new StackTrace().ToString()}");

            LogDebug($"Tracking {meth.DeclaringType.FullName}::{meth.ToString()}");

            if(TrackHook == null) {
                Log("Initializing tracking hooks");

                var self = typeof(SimpleMetrics);
                TrackHook = new HarmonyMethod(AccessTools.Method(self, nameof(__Track)));
                TrackHook.prioritiy = Priority.First;
            }

            if (meth.IsGenericMethod || meth.IsGenericMethodDefinition) {
                LogError($"Cannot instrument a generic method {meth.DeclaringType.FullName}::{meth.ToString()}");
            } else if (meth.GetMethodBody() == null) {
                LogError($"Cannot instrument a method with no body {meth.DeclaringType.FullName}::{meth.ToString()}");
            } else {
                Trap(() => Main.harmony.Patch(meth, TrackHook));
            }
        }

        static HarmonyMethod PreHook;
        static HarmonyMethod PostHook;
        static Dictionary<string,Metric> Metrics = new Dictionary<string,Metric>();
        public static void __Pre(ref Metric __state) {
            // Tons of overhead, but it's good enough for what we're doing.
            // FIXME: Change to transpiler doing a lookup into a fixed array of Metrics
            var meth = new StackFrame(1).GetMethod();
            var fullname = meth.DeclaringType.Name + "::" + meth.Name;
            var metric = Metrics.GetWithDefault(fullname, () => new Metric());
            metric.times++;
            metric.timer.Start();
            __state = metric;
        }

        public static void __Post(ref Metric __state) {
            try { 
            __state.timer.Stop();
            } catch(Exception e) { LogException(e); }
        }

        static HarmonyMethod TrackHook;
        public static void __Track() {
            var meth = new StackFrame(1).GetMethod();
            LogDebug($"Tracked {meth.DeclaringType.FullName}::{meth.ToString()}");
        }

        public static void Summary() {
            var buf = "";
            foreach (var kv in Metrics) {
                buf += $":times {kv.Value.times} :ms {kv.Value.timer.Elapsed.TotalMilliseconds} :method {kv.Key}\n";
            }
            Metrics.Clear();
            Log( "SimpleMetrics -------------------------------------- \n{0}\n\n----------------------------------"
               , buf);
        }
    }

    class Metric { public int times = 0; public Stopwatch timer = new Stopwatch(); }
}
