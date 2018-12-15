using System;
using RSG;
using UnityEngine;
using Harmony;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using Newtonsoft.Json;

namespace BattletechPerformanceFix {
    public static class Extensions {
        public static void Spam(Func<string> msg) {
            LogDebug(msg);
        }

        public static void LogDebug(Func<string> lmsg) {
             if (Main.LogLevel == "Debug") LogDebug(lmsg());
        }
        public static void LogDebug(string msg, params object[] values)
        {
            if (Main.LogLevel == "Debug")
                Main.__Log("[Debug] " + msg, values);
            // Far too much data passes through here to hit the HBS log
            //     it's simply too slow to handle it
        }

        public static void Log(string msg, params object[] values) {
            Main.__Log("[Info]" + msg, values);
            Trap(() => Main.HBSLogger.Log(string.Format(msg, values)));
        }

        public static void LogError(string msg, params object[] values)
        {
            Main.__Log("[Error] " + msg, values);
            Trap(() => Main.HBSLogger.LogError(string.Format(msg, values)));
        }

        public static void LogWarning(Func<string> msg) {
            LogWarning("{0}", msg());
        }

        public static void LogWarning(string msg, params object[] values)
        {
            Main.__Log("[Warning] " + msg, values);
            Trap(() => Main.HBSLogger.LogWarning(string.Format(msg, values)));
        }

        public static void LogException(Exception e)
        {
            Main.__Log("[Exception] {0}", e);
            Trap(() => Main.HBSLogger.LogException(e));
        }


        public static void Trap(Action f)
        { try { f(); } catch (Exception e) { Main.__Log("Exception {0}", e); } }

        public static T Trap<T>(Func<T> f)
        {
            try { return f(); } catch (Exception e) { Main.__Log("Exception {0}", e); return default(T);  }
        }

        public static T TrapAndTerminate<T>(string msg, Func<T> f)
        {
            try {
                return f();
            } catch (Exception e) {
                Main.__Log("PANIC {0} {1}", msg, e);
                TerminateImmediately();
                return default(T);
            }
        }

        public static K SafeCast<K>(this object t) where K : class
            => (t as K).NullCheckError($"Safe cast failed of {t.GetType().FullName} to {typeof(K).FullName}");

        // Do not use for unity Objects! 
        public static T NullCheckError<T>(this T t, string msg) {
            if (t == null) LogError("{0} from {1}", msg, new StackTrace(1).ToString());
            return t;
        }

        public static T NullThrowError<T>(this T t, string msg) {
            if (t == null) throw new System.Exception($"{msg} from {new StackTrace(1).ToString()}");
            return t;
        }
            

        public static GameObject IsDestroyedError(this GameObject t, string msg) {
            if (t == null && t?.GetType() != null) LogError("{0} from {1}", msg, new StackTrace(1).ToString());
            return t;
        }

        public static T[] Array<T>(params T[] p) => p;
        public static List<T> List<T>(params T[] p) => p.ToList();
        public static IEnumerable<T> Sequence<T>(params T[] p) => p;
        public static void ForEach<T>(this IEnumerable<T> xs, Action<T> f) {
           foreach (var x in xs) f(x);
        }

        public static T Or<T>(this T a, Func<T> b)
            => a == null ? b() : a;

        public static T GetWithDefault<K,T>(this Dictionary<K,T> d, K key, Func<T> lazyDefault)
            => d.TryGetValue(key, out var val) ? val : d[key] = lazyDefault();

        public static T Measure<T>(Action<long,TimeSpan> stats, Func<T> f) {
            var tmem = System.GC.GetTotalMemory(false);
            var sw = Stopwatch.StartNew();
            var item = f();
            sw.Stop();
            var delta = System.GC.GetTotalMemory(false) - tmem;
            stats(delta, sw.Elapsed);
            return item;
        }

        public static void TrapAndTerminate(string msg, Action f) => TrapAndTerminate<int>(msg, () => { f(); return 0; });

        // Do not let BattleTech recover anything. Forcibly close.
        // Only for use in dangerous patches which may need to prevent bad save data from being written.
        public static void TerminateImmediately()
            => System.Diagnostics.Process.GetCurrentProcess().Kill();


        public static IPromise AsPromise(this IEnumerator coroutine) {
            var prom = new Promise();
            BPF_CoroutineInvoker.Invoke(coroutine, prom.Resolve);
            return prom;
        }

        public static Func<IPromise<Scene>> LoadSceneAsync(this string name, LoadSceneMode mode = LoadSceneMode.Single) {
            var op = SceneManager.LoadSceneAsync(name, mode);
            op.allowSceneActivation = false;

            var prom = op.AsPromise(name).Then(() => Promise<Scene>.Resolved(SceneManager.GetSceneByName(name)));

            return () => { op.allowSceneActivation = true;
                           return prom; };
        }

        public static string Dump<T>(this T t, bool indented = true)
        {
            return JsonConvert.SerializeObject(t, indented ? Formatting.Indented : Formatting.None, new JsonSerializerSettings
                {
                    ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                    Error = (serializer, err) => err.ErrorContext.Handled = true
                });
        }

        public static IPromise AsPromise(this AsyncOperation operation, string sceneName = null) {
            IEnumerator TillDone() { var sn = sceneName ?? "?";
                                     LogDebug($"Scene[{sn}] load started ----------");
                                     var timer = Stopwatch.StartNew();
                                     while (!operation.isDone && operation.progress < .9f) { LoadDesc["Scene"] = $"{operation.progress:P2}";
                                                                                             LogDebug($"WFF {Time.frameCount} {operation.progress}");
                                                                                             yield return null; }
                                     var loadTime = timer.Elapsed.TotalSeconds;
                                     LogDebug($"Scene[{sn}] ready for activation after {loadTime} seconds");
                                     while (!operation.allowSceneActivation) { yield return null; }
                                     LogDebug($"Scene[{sn}] activation -------");
                                     timer.Reset(); timer.Start();
                                     while (!operation.isDone) { yield return null; }
                                     yield return null;  // Let scene run Awake/Start
                                     var initTime = timer.Elapsed.TotalSeconds;
                                     LogDebug($"Scene[{sn}] fetched in {loadTime + initTime} seconds. :load ({loadTime} seconds) :init ({initTime} seconds)"); }
            return TillDone().AsPromise();
        }


        public static IPromise WaitAFrame(int n = 1)
            => Promise.Resolved().WaitAFrame(n);

        public static IPromise WaitAFrame(this IPromise p, int n = 1) {
            IEnumerator OneFrame(int x) { for (var i = 0; i < x; i++) yield return null; }
            var next = new Promise();
            p.Done(() => BPF_CoroutineInvoker.Invoke(OneFrame(n), next.Resolve));
            return next;
        }

        public static void Instrument(this MethodBase meth)
            => SimpleMetrics.Instrument(meth);

        public static void Track(this MethodBase meth)
            => SimpleMetrics.Track(meth);


        public static HarmonyMethod Drop = new HarmonyMethod(AccessTools.Method(typeof(Extensions), nameof(__Drop)));
        public static bool  __Drop() {
            LogDebug($"Dropping call to {new StackFrame(1).ToString()}");
            return false;
        }

        public static HarmonyMethod Yes = new HarmonyMethod(AccessTools.Method(typeof(Extensions), nameof(__Yes)));
        public static bool  __Yes(ref bool __result) {
            LogDebug($"Saying yes to to {new StackFrame(1).ToString()}");
            __result = true;
            return false;
        }
        public static HarmonyMethod No = new HarmonyMethod(AccessTools.Method(typeof(Extensions), nameof(__No)));
        public static bool  __No(ref bool __result) {
            LogDebug($"Saying yes to to {new StackFrame(1).ToString()}");
            __result = false;
            return false;
        }

        public static void Pre<T>(this string method, string patchmethod = null) {
            var onType = new StackFrame(1).GetMethod().DeclaringType;
            patchmethod = patchmethod ?? (method == ".ctor" ? "CTOR_Pre" : (method+"_Pre"));
            var pmeth = onType.GetMethod(patchmethod, AccessTools.all).NullCheckError($"Missing patch method {patchmethod} on {onType.FullName}");
            if (!pmeth.IsStatic) LogError($"Patch method {patchmethod} must be static");
            MethodBase meth = null;
            if (method == ".ctor") meth = (MethodBase)typeof(T).GetConstructors(AccessTools.all)[0];
            else if (method.StartsWith("get_")) meth = (MethodBase)typeof(T).GetProperties(AccessTools.all)
                                                                          .FirstOrDefault(mm => { LogDebug($"{mm.Name}"); return method.EndsWith(mm.Name); })
                                                  ?.GetGetMethod();
            else meth = (MethodBase)typeof(T).GetMethods(AccessTools.all)
                                             .FirstOrDefault(mm => mm.Name == method && mm.GetMethodBody() != null);
            meth.NullCheckError($"Failed to find patchable function {method} on {typeof(T).FullName}");
            Log($"Prepatch: {meth.DeclaringType.FullName}::{meth.ToString()} -> {onType.FullName}::{pmeth.ToString()}");
            if (meth.IsGenericMethodDefinition) LogError("Can't patch a generic method def for {method} on {meth.DeclaringType.FullName}");
            else Trap(() => Main.harmony.Patch( meth
                                              , new HarmonyMethod(pmeth.DeclaringType, pmeth.Name)));
        }

        public static void Post<T>(this string method, string patchmethod = null) {
            var onType = new StackFrame(1).GetMethod().DeclaringType;
            patchmethod = patchmethod ?? (method == ".ctor" ? "CTOR_Post" : (method+"_Post"));
            var pmeth = onType.GetMethod(patchmethod, AccessTools.all).NullCheckError($"Missing patch method {patchmethod} on {onType.FullName}");
            if (!pmeth.IsStatic) LogError($"Patch method {patchmethod} must be static");
            MethodBase meth = null;
            if (method == ".ctor") meth = (MethodBase)typeof(T).GetConstructors(AccessTools.all)[0];
            else if (method.StartsWith("get_")) meth = (MethodBase)typeof(T).GetProperties(AccessTools.all)
                                                                          .FirstOrDefault(mm => { LogDebug($"{mm.Name}"); return method.EndsWith(mm.Name); })
                                                  ?.GetGetMethod();
            else meth = (MethodBase)typeof(T).GetMethods(AccessTools.all)
                                             .FirstOrDefault(mm => mm.Name == method && mm.GetMethodBody() != null);
            meth.NullCheckError($"Failed to find patchable function {method} on {typeof(T).FullName}");
            Log($"Postpatch: {meth.DeclaringType.FullName}::{meth.ToString()} -> {onType.FullName}::{pmeth.ToString()}");
            if (meth.IsGenericMethodDefinition) LogError("Can't patch a generic method def for {method} on {meth.DeclaringType.FullName}");
            else Trap(() => Main.harmony.Patch( meth
                                              , null
                                              , new HarmonyMethod(pmeth.DeclaringType, pmeth.Name)));
        }


        // C# macros when...
        public static void Pre<T>(this string method, Action<T> f) where T : class 
            => method.Pre<T>(x => { f(x); return true; });

        public static void Pre<T>(this string method, Func<T,bool> f) where T : class
            => AccessTools.Method(typeof(T), method).Pre(f);

        public static void Pre<T>(this MethodBase meth, Func<T,bool> f) where T : class {
            var key = meth.DeclaringType.FullName + "::" + meth.Name;
            __PreDB[key] = v => f(v.SafeCast<T>());
            Main.harmony.Patch( meth
                              , new HarmonyMethod(typeof(Extensions), nameof(__Pre)));
        }
        public static Dictionary<string,Func<object,bool>> __PreDB = new Dictionary<string,Func<object,bool>>();
        public static bool __Pre(object __instance) {
            var sf = new StackFrame(1).GetMethod();
            var name = sf.Name;
            var mname = name.Substring(0, name.LastIndexOf('_'));
            var key = sf.DeclaringType.FullName + "::" + mname;
            return __PreDB[key](__instance);
        }

        public static Dictionary<string,string> LoadDesc = new Dictionary<string,string>();
    }

    class BPF_CoroutineInvoker : UnityEngine.MonoBehaviour {
        static BPF_CoroutineInvoker instance = null;
        public static BPF_CoroutineInvoker Instance { get => instance ?? Init(); }

        static BPF_CoroutineInvoker Init() {
            Extensions.Log("[BattletechPerformanceFix: Initializing a new coroutine proxy");
            var go = new UnityEngine.GameObject();
            go.name = "BattletechPerformanceFix:CoroutineProxy";
            instance = go.AddComponent<BPF_CoroutineInvoker>();
            UnityEngine.GameObject.DontDestroyOnLoad(go);


            instance.StartCoroutine(UpdateLoadScreen());

            return instance;
        }

        public static IEnumerator UpdateLoadScreen() {
            while(true) {
                var text = Extensions.LoadDesc.Aggregate("", (acc,a) => $"{a.Key}: {a.Value}\n{acc}");
                Extensions.Trap(() => new Traverse(typeof(BattleTech.UI.LoadingCurtain)).Field("activeInstance")
                                                                                        .Field("spinnerAndTipWidget")
                                                                                        .Field("tipText")
                                                                                        .Method("SetText", text, new object[0])
                                                                                        .GetValue());

                yield return null;
            }
        }

        public static void Invoke(IEnumerator coroutine, Action done) {
            Instance.StartCoroutine(Proxy(coroutine, done));
        }

        static IEnumerator Proxy(IEnumerator coroutine, Action done) {
            yield return Instance.StartCoroutine(coroutine);
            done();
        }
    }
}
    
