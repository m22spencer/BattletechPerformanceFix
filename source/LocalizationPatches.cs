using BattleTech;
using HBS.Animation.StateMachine;
using Org.BouncyCastle.Crypto.Tls;
using System;
using System.Diagnostics;
using System.Linq;
using static BattletechPerformanceFix.Extensions;

namespace BattletechPerformanceFix
{
    class LocalizationPatches : Feature
    {
        // If enabled, runs both vanilla & bfix versions, checking for desyncs.
        public static bool Verify = false;
        public void Activate()
        {
            "StringForKeyFormat".Pre<StringsProviderBase<object>>();
            "StringForKeyFormat".Post<StringsProviderBase<object>>();
        }

        // String with non number text between {} is rare, so only do the repair if format fails.
        public static bool StringForKeyFormat_Pre(StringsProviderBase<object> __instance, string key, ref string __result, ref (bool,string)? __state, params object[] args)
        {
            string text = __instance.StringForKey(key);
            try
            {
                var formatted = key == null ? "" : (string.Format(text, args) ?? "");
                __state = (true, __result = formatted);
                return Verify;
            } catch(Exception)
            {
                return true;
            }
        }

        public static void StringForKeyFormat_Post(StringsProviderBase<object> __instance, string key, ref string __result, ref (bool,string)? __state, params object[] args)
        {
            if (Verify && __state != null && __result != __state?.Item2)
                LogError($"StringForKeyFormat.Assertion failed: \ncompare: {__result ?? "null"} == {__state?.Item2 ?? "null"} \nkey: {key ?? "null"} \nstring-for-key: {__instance.StringForKey(key) ?? "null"}\nargs: {args.Dump()}");
        }
    }
}