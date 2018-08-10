using HBS.Logging;
using Harmony;
using System.Reflection;
using BattleTech;
using BattleTech.UI;
using BattleTech.Data;
using System.Diagnostics;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;
using System.Reflection.Emit;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;

namespace BattletechPerformanceFix {
    public class LoadFixes : Feature
    {
        public void Activate()
        {
            Control.TrapAndTerminate("Patch ModTek.ModTek.ParseGameJOSN", () =>
            {
                Control.harmony.Patch(AccessTools.Method(typeof(ModTek.ModTek), "ParseGameJSON")
                                     , new HarmonyMethod(typeof(MakeModtekUseFasterParse).GetMethod(nameof(MakeModtekUseFasterParse.Prefix)))
                                     , null);
            });
            Control.TrapAndTerminate("Patch HBS.Util.JSONSerializationUtility.StripHBSCommentsFromJSON", () => {
                Control.harmony.Patch(AccessTools.Method(typeof(HBS.Util.JSONSerializationUtility), "StripHBSCommentsFromJSON")
                                     , new HarmonyMethod(typeof(DontStripComments).GetMethod(nameof(DontStripComments.Prefix)))
                                     , null);
            });
        }
    }

    public class MakeModtekUseFasterParse
    {
        public static bool Prefix(string jsonText, JObject __result)
        {
            return Control.TrapAndTerminate("MakeModtekUseFasterParse.Prefix", () =>
            {
                Control.Log("Intercept modtek json: {0}", jsonText.Length);

                var stripped = (string)DontStripComments.stripMeth.Invoke(null, new object[] { jsonText });

                __result = JObject.Parse(new Regex("(\\]|\\}|\"|[A-Za-z0-9])\\s*\\n\\s*(\\[|\\{|\")", RegexOptions.Singleline).Replace(stripped, "$1,\n$2"));
                if (__result == null)
                    throw new System.Exception("StripComments result is null");

                return false;
            });
        }
    }

    public class DontStripComments {
        // TODO: Is this function always called from main thread? We need to patch loadJSON, but it's generic
        public static bool guard = false;
        public static MethodBase stripMeth = null;

        public static string StripComments(MethodBase __originalMethod, string json)
        {
            // Try to parse the json, if it doesn't work, use HBS comment stripping code.
            try
            {
                fastJSON.JSON.Parse(json);
                return json;
            }
            catch (Exception e)
            {
                guard = true;
                var res = (string)__originalMethod.Invoke(null, new object[] { json });
                guard = false;
                return res;
            }
        }

        public static bool Prefix(MethodBase __originalMethod, string json, string __result) {
            return Control.TrapAndTerminate("DontStripComments.Prefix", () =>
            {
                stripMeth = __originalMethod;
                if (guard) return true;
                __result = StripComments(__originalMethod, json);
                if (__result == null)
                    throw new System.Exception("StripComments result is null");
                return false;
            });
        }
    }
}