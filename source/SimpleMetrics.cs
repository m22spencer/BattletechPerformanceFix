using System;
using System.Reflection;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Harmony;
using UnityEngine;
using BattleTech.Data;
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

            "Update".Post<BattleTech.UnityGameInstance>();

            var meths = Measure( "AllPatchableTypes"
                               , () => AppDomain
                                 .CurrentDomain.GetAssemblies().SelectMany(asm => asm.GetTypes())
                                 .Where(ty => !ty.IsAbstract && !ty.IsGenericTypeDefinition)
                                 .SelectMany(ty => ty.GetMethods(AccessTools.all))
                                 .Where(meth => !meth.IsGenericMethodDefinition && meth.GetMethodBody() != null)
                                 .ToList());


            var uo = typeof(UnityEngine.Object);
            var uomeths = meths.Where(m => uo.IsAssignableFrom(m.DeclaringType))
                               .ToList();

            // Awakes
            uomeths.Where(m => m.Name == "Awake" && m.ReturnType == typeof(void))
                   .ForEach(m => Trap(() => m.Instrument()));

            // Starts
            uomeths.Where(m => m.Name == "Start" && m.ReturnType == typeof(void))
                   .ForEach(m => Trap(() => m.Instrument()));

            var names = List( "Load", "PoolModule", "PooledInstantiate", "LoadResource", "RequestDependencies"
                            , "CheckDependenciesAfterLoad", "RequestResource_Internal"
                            , "RehydrateObjectFromDictionary");

            meths.Where(m => names.Contains(m.Name))
                 .ForEach(m => m.Instrument());


            Assembly.GetAssembly(typeof(BattleTech.Data.DataManager))
                    .GetTypes()
                    .Where(ty => ty.GetInterface(typeof(BattleTech.Data.DataManager.ILoadDependencies).FullName) != null)
                    .ForEach(ty => { var t = typeof(HBS.Data.DictionaryStore<>).MakeGenericType(ty);
                                     var e = AccessTools.Method(t, "Exists");
                                     e.Instrument(); });
        }

        public static void Update_Post() {
            if (Input.GetKey(KeyCode.LeftShift) && Input.GetKeyDown(KeyCode.M)) {
                Summary();
            }
        }


        public static void Instrument(MethodBase meth) {
            if (meth == null)
                LogError($"Cannot instrument null meth from {new StackTrace().ToString()}");

            LogDebug($"Instrumenting {meth.DeclaringType.FullName}::{meth.ToString()}");

            if(PreHook == null || PostHook == null) {
                LogInfo("Initializing simple metrics hooks");

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
            if (meth == null)
                LogError($"Cannot instrument null meth from {new StackTrace().ToString()}");

            LogDebug($"Tracking {meth.DeclaringType.FullName}::{meth.ToString()}");

            if(TrackHook == null) {
                LogInfo("Initializing tracking hooks");

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
        public static void __Track(object __instance) {
            var meth = new StackFrame(1).GetMethod();
            var hash = __instance.GetHashCode();
            LogDebug($"Tracked[{hash}] {meth.DeclaringType.FullName}::{meth.ToString()}");
        }

        public static void Summary() {
            var buf = "";
            foreach (var kv in Metrics) {
                buf += $":times {kv.Value.times} :ms {kv.Value.timer.Elapsed.TotalMilliseconds} :method {kv.Key}\n";
            }
            Metrics.Clear();
            LogInfo( string.Format("SimpleMetrics -------------------------------------- \n{0}\n\n----------------------------------"
                                  , buf));
        }
    }

    class Metric { public long times = 0; public Stopwatch timer = new Stopwatch(); }
}
