using BattleTech;
using System;

namespace BattletechPerformanceFix
{
    class LocalizationPatches : Feature
    {
        public void Activate()
        {
            "StringForKeyFormat".Pre<StringsProviderBase<object>>();
        }

        // String with non number text between {} is rare, so only do the repair if format fails.
        public static bool StringForKeyFormat_Pre(StringsProviderBase<object> __instance, string key, ref string __result, params object[] args)
        {
            string text = __instance.StringForKey(key);
            try
            {
                __result = string.Format(text, args);
                return false;
            } catch(Exception)
            {
                return true;
            }
        }
    }
}