using System;
using System.Reflection;
using System.Reflection.Emit;
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
    public static partial class Extensions {
        public static void TrackSequence(params string[] qualifiedMethods) {
            LogInfo($"SimpleMetrics.TrackSequence {qualifiedMethods.Dump(false)}");
            var methseq = qualifiedMethods.Select(FindQualifiedMethod).ToList();
            methseq.Zip(methseq.Skip(1))
                   .ForEach(kv => SimpleMetrics.WithEntryAndExit(kv.Key, kv.Value));
        }

    }

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

            TrackSequence( "BattleTech.UI.SGLoadSavedGameScreen::LoadSelectedSlot"
                         , "BattleTech.SimGameState::_OnBeginDefsLoad"
                         , "BattleTech.SimGameState::_OnDefsLoadComplete"
                         , "BattleTech.SimGameState::_OnBeginAttachUX"
                         , "BattleTech.UI.SimGameUXCreator::Awake"
                         , "BattleTech.SimGameState::_OnAttachUXComplete");
        }

        public static void Update_Post() {
            if (Input.GetKey(KeyCode.LeftShift) && Input.GetKeyDown(KeyCode.M)) {
                Summary();
            }
        }

        // This will be broken if entry method is in the stack multiple times
        public static void WithEntryAndExit(MethodBase entry, MethodBase exit) {
            entry.LogIfNull("Cannot instrument null entry");
            exit.LogIfNull("Cannot instrument null exit");

            LogSpam($"Preparing {entry.ToString()} -> {exit.ToString()}");

            var entryIndex = MethodEntryIndexMap.GetWithDefault(entry.QualifiedSignature(), () => MethodEntryIndexMap.Count);
            while (entryIndex >= EntryTimers.Count) EntryTimers.Add(new Stopwatch());

            var exitIndex = MethodExitIndexMap.GetWithDefault(exit.QualifiedSignature(), () => MethodExitIndexMap.Count);
            while (exitIndex >= ExitTimers.Count) ExitTimers.Add(new List<int>());

            ExitTimers[exitIndex].Add(entryIndex);

            var cindex = ((uint)entryIndex << 16) | (uint)exitIndex;
            IdPairToMethods[cindex] = new KeyValuePair<MethodBase,MethodBase>(entry, exit);

            var transpileEntry = new HarmonyMethod(typeof(SimpleMetrics), "TranspileEntry");
            transpileEntry.prioritiy = Priority.First;
            var transpileExit = new HarmonyMethod(typeof(SimpleMetrics), "TranspileExit");
            transpileExit.prioritiy = Priority.Last;


            LogSpam($"Patching {entry.ToString()} -> {exit.ToString()}");

            entry.Patch(null, null, "TranspileEntry", Priority.First);
            exit.Patch(null, null, "TranspileExit", Priority.Last);

            LogSpam($"Patched {entry.ToString()} -> {exit.ToString()}");
        }


        public static Dictionary<uint,KeyValuePair<MethodBase,MethodBase>> IdPairToMethods = new Dictionary<uint, KeyValuePair<MethodBase,MethodBase>>();
        public static void EntryStart(int index) {
            var timer = EntryTimers[index];
            timer.Reset();
            timer.Start();
        }

        public static void EntryStop(int index) {
            ExitTimers[index]
                .ForEach(startIndex => {
                        var timer = EntryTimers[startIndex];
                        var cindex = ((uint)startIndex << 16) | (uint)index;
                        var meths = IdPairToMethods[cindex];
                        LogWarning($"Measured [{meths.Key.QualifiedSignature()} -> {meths.Value.QualifiedSignature()}] in {timer.Elapsed.TotalMilliseconds}");
                    });
        }

        public static Dictionary<string,int> MethodEntryIndexMap = new Dictionary<string,int>();
        public static List<Stopwatch> EntryTimers = new List<Stopwatch>();
        public static IEnumerable<CodeInstruction> TranspileEntry(ILGenerator ilGenerator, MethodBase original, IEnumerable<CodeInstruction> ins) {
            LogInfo($"TranspileEntry {original.QualifiedSignature()}");
            var index = MethodEntryIndexMap[original.QualifiedSignature()];

            // SimpleMetrics.EntryStart(index)
            var start = Sequence( new CodeInstruction(OpCodes.Ldc_I4, index)
                                , new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SimpleMetrics), "EntryStart")));

            return start.Concat(ins);
        }



        public static Dictionary<string,int> MethodExitIndexMap = new Dictionary<string,int>();
        public static List<List<int>> ExitTimers = new List<List<int>>();
        public static IEnumerable<CodeInstruction> TranspileExit(ILGenerator ilGenerator, MethodBase original, IEnumerable<CodeInstruction> ins) {
            LogInfo($"TranspileExit {original.QualifiedSignature()}");
            var index = MethodExitIndexMap[original.QualifiedSignature()];

            var retType = (original as MethodInfo)?.ReturnType ?? typeof(void);

            var tmp = retType == typeof(void) ? null : ilGenerator.DeclareLocal(retType);

            return ins.SelectMany(i => {
                    if (i.opcode == OpCodes.Ret) {
                        var seq = Sequence( new CodeInstruction(OpCodes.Ldc_I4, index)
                                          , new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SimpleMetrics), "EntryStop")));

                        IEnumerable<CodeInstruction> pre;
                        IEnumerable<CodeInstruction> post;
                        if (tmp == null) {
                            pre = Sequence<CodeInstruction>();
                            post = Sequence( new CodeInstruction(OpCodes.Ret));
                        } else {
                            pre = Sequence( new CodeInstruction(OpCodes.Stloc, tmp));
                            post = Sequence( new CodeInstruction(OpCodes.Ldloc, tmp)
                                           , new CodeInstruction(OpCodes.Ret));
                        }

                        var all = pre.Concat(seq).Concat(post);
                        var first = all.First();
                        var rest  = all.Skip(1);
                        // Mutate the current instruction to retain jump offsets.
                        i.opcode = first.opcode;
                        i.operand = first.operand;
                        return Sequence(i).Concat(rest);
                    } else {
                        return Sequence(i);
                    }
                });
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
