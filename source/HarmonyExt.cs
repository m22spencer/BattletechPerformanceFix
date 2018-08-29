using System;
using System.Reflection;
using Harmony;


namespace BattletechPerformanceFix
{
    public class Hook : IDisposable {
        readonly MethodBase orig;
        readonly MethodInfo act;
        public void Dispose() {
            Control.harmony.Unpatch(orig, act);
        }

        Hook(MethodBase target, MethodInfo mi) {
            orig = target;
            act = mi;
        }

        public static Hook Prefix(MethodBase target, MethodInfo mi, int priority = Priority.Normal) {
            var h = new Hook(target, mi);
            var m = new HarmonyMethod(mi);
            m.prioritiy = priority;
            Control.harmony.Patch(target, m, null);
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