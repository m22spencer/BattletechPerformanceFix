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
using RT = BattleTech.BattleTechResourceType;
using BattleTech.UI;
using static BattletechPerformanceFix.Extensions;

namespace BattletechPerformanceFix {
    public static partial class Extensions {
        public static void AlertUser(string title, string message) {
            GenericPopupBuilder genericPopupBuilder = GenericPopupBuilder.Create(title, message);
            genericPopupBuilder.Render();
        }

        public static void Trap(Action f)
        { try { f(); } catch (Exception e) { LogException(e); }}

        public static T Trap<T>(Func<T> f, Func<T> or = null)
        {
            try { return f(); } catch (Exception e) { LogException(e); return or == null ? default(T) : or(); }
        }

        public static void TrapSilently(Action f)
        { try { f(); } catch {} }


        public static IPromise<T> TrapAsPromise<T>(Func<T> f) {
            var prom = new Promise<T>();
            try { prom.Resolve(f()); } catch (Exception e) { prom.Reject(e); }
            return prom;
        }

        public static T TrapAndTerminate<T>(string msg, Func<T> f)
        {
            try {
                return f();
            } catch (Exception e) {
                LogError(string.Format("PANIC {0} {1}", msg, e));
                TerminateImmediately();
                return default(T);
            }
        }

        public static T Identity<T>(T t) => t;

        public static K SafeCast<K>(this object t) where K : class
            => (t as K).LogIfNull($"Safe cast failed of {t.GetType().FullName} to {typeof(K).FullName}");

        // Do not use for unity Objects! 
        public static T LogIfNull<T>(this T t, string msg) {
            if (t == null) LogError($"{msg} from {new StackTrace(1).ToString()}");
            return t;
        }

        public static T ThrowIfNull<T>(this T t, string msg) {
            if (t == null) throw new System.Exception($"{msg} from {new StackTrace(1).ToString()}");
            return t;
        }
            

        public static GameObject IsDestroyedError(this GameObject t, string msg) {
            if (t == null && t?.GetType() != null) LogError($"{msg} from {new StackTrace(1).ToString()}");
            return t;
        }

        public static RT ToRT(this string name)
            => Trap(() => (RT)Enum.Parse(typeof(RT), name));

        public static T[] Array<T>(params T[] p) => p;
        public static List<T> List<T>(params T[] p) => p.ToList();
        public static IEnumerable<T> Sequence<T>(params T[] p) => p;
        public static void ForEach<T>(this IEnumerable<T> xs, Action<T> f) {
           foreach (var x in xs) f(x);
        }

        public static IEnumerable<IEnumerable<T>> GroupsOf<T>(this IEnumerable<T> items, int count) {
            return items.Select((item,idx) => new { item, idx })
                        .GroupBy(kv => kv.idx / count, kv => kv.item)
                        .Select(x => x.Select(v => v));
        }
       
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

        public static T Measure<T>( string tag, Func<T> f)
            => Measure((b,t) => LogDebug($"Measure[{tag}] :bytes {b} :seconds {t.TotalSeconds}")
                      , f);

        public static void Measure( string tag, Action f)
            => Measure((b,t) => LogDebug($"Measure[{tag}] :bytes {b} :seconds {t.TotalSeconds}")
                      , () => { f(); return 0; });

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

        public static IEnumerable<CodeInstruction> Replace(this CodeInstruction i, IEnumerable<CodeInstruction> ins)
        {
            var first = ins.First();
            var rest = ins.Skip(1);
            i.opcode = first.opcode;
            i.operand = first.operand;
            return Sequence(i).Concat(rest);
        }

        public static IPromise WaitAFrame(int n = 1)
            => Promise.Resolved().WaitAFrame(n);

        public static IPromise WaitAFrame(this IPromise p, int n = 1) {
            IEnumerator OneFrame(int x) { for (var i = 0; i < x; i++) yield return null; }
            var next = new Promise();
            p.Done(() => BPF_CoroutineInvoker.Invoke(OneFrame(n), next.Resolve));
            return next;
        }
        
        public static T Let<T>(this T t, Func<T,T> f) => f(t);
        public static void Let<T>(this T t, Action<T> f) => f(t);

        public static HarmonyMethod Drop = new HarmonyMethod(AccessTools.Method(typeof(Extensions), nameof(__Drop)));
        public static bool  __Drop() {
            //LogDebug($"Dropping call to {new StackFrame(1).ToString()}");
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
            LogDebug($"Saying no to to {new StackFrame(1).ToString()}");
            __result = false;
            return false;
        }


        public static void Patch( this MethodBase method
                                , string premethod = null
                                , string postmethod = null
                                , string transpilemethod = null
                                , int priority = Priority.Normal
                                ) {
            var onType = new StackTrace().GetFrames()
                                         .Select(frame => frame.GetMethod().DeclaringType)
                                         .FirstOrDefault(dtype => dtype?.GetInterface(typeof(Feature).FullName) != null)
                                         .LogIfNull($"Unable to find patchable type origin :for {new StackTrace().ToString()}");
            method.LogIfNull($"Cannot patch a null method :from {onType.FullName ?? new StackTrace().ToString()}");
            var pnames = List(premethod, postmethod, transpilemethod);
            var patches = pnames
                .Select(name => name == null ? null : onType.GetMethod(name, AccessTools.all).LogIfNull($"Missing patch method {name} on {onType.FullName}"))
                .Select(meth => meth == null ? null : new HarmonyMethod(meth).Let(h => { h.prioritiy = priority; return h; }))
                .ToArray();

            pnames.Where(p => p != null)
                  .ToList()
                  .ForEach(p => LogInfo($"Patch: {method.DeclaringType.Name}.{method.Name} -> {onType.Name}.{p}"));
            Trap(() => Main.harmony.Patch( method, patches[0], patches[1], patches[2]));

        }
                                    

        public static void Patch<T>( this string method
                                   , string premethod = null
                                   , string postmethod = null
                                   , string transpilemethod = null
                                   , int priority = Priority.Normal
                                   ) {
            MethodBase meth = null;
            if (method.StartsWith("ctor")) meth = (MethodBase)typeof(T).GetConstructors(AccessTools.all)[0];
            else if (method.StartsWith("get_")) meth = (MethodBase)typeof(T).GetProperties(AccessTools.all)
                                                                            .FirstOrDefault(mm => { LogDebug($"{mm.Name}"); return method.EndsWith(mm.Name); })
                                                    ?.GetGetMethod();
            else meth = (MethodBase)typeof(T).GetMethods(AccessTools.all)
                                             .FirstOrDefault(mm => mm.Name == method && mm.GetMethodBody() != null);
            meth.LogIfNull($"Failed to find patchable function {method} on {typeof(T).FullName}");
            meth.Patch(premethod, postmethod, transpilemethod, priority);
        }

        public static void Pre<T>(this string method, string patchmethod = null, int priority = Priority.Normal)
            => method.Patch<T>(patchmethod ?? $"{method}_Pre", null, null, priority);

        public static void Post<T>(this string method, string patchmethod = null, int priority = Priority.Normal)
            => method.Patch<T>(null, patchmethod ?? $"{method}_Post", null, priority);

        public static void Transpile<T>(this string method, string patchmethod = null, int priority = Priority.Normal)
            => method.Patch<T>(null, null, patchmethod ?? $"{method}_Transpile", priority);

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
            LogInfo("BattletechPerformanceFix: Initializing a new coroutine proxy");
            var go = new UnityEngine.GameObject();
            go.name = "BattletechPerformanceFix:CoroutineProxy";
            instance = go.AddComponent<BPF_CoroutineInvoker>();
            UnityEngine.GameObject.DontDestroyOnLoad(go);

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
    
