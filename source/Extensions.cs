using System;
using RSG;
using UnityEngine;
using System.Linq;
using System.Collections;
using System.Collections.Generic;

namespace BattletechPerformanceFix {
    public static class Extensions {
        public static void LogDebug(string msg, params object[] values)
        {
            if (Main.LogLevel == "Debug")
                Main.__Log("[Debug] " + msg, values);
        }

        public static void Log(string msg, params object[] values) {
            Main.__Log("[Info]" + msg, values);
        }

        public static void LogError(string msg, params object[] values)
        {
            Main.__Log("[Error] " + msg, values);
        }

        public static void LogWarning(string msg, params object[] values)
        {
            Main.__Log("[Warning] " + msg, values);
        }

        public static void LogException(params object[] values)
        {
            Main.__Log("[Exception] {0}", values);
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

        public static T[] Array<T>(params T[] p) => p;
        public static List<T> List<T>(params T[] p) => p.ToList();
        public static IEnumerable<T> Sequence<T>(params T[] p) => p;

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

        public static IPromise AsPromise(this AsyncOperation operation) {
            IEnumerator TillDone() { while (!operation.isDone) { yield return null; }
                                     yield return null; } // Post Awake
            return TillDone().AsPromise();
        }

        public static IPromise WaitAFrame(this Promise p) {
            IEnumerator OneFrame() { yield return null; }
            var next = new Promise();
            p.Done(() => BPF_CoroutineInvoker.Invoke(OneFrame(), next.Resolve));
            return next;
        }
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

            return instance;
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
    
