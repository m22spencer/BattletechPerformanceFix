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
            /*
            Control.TrapAndTerminate("Patch ModTek.ModTek.ParseGameJOSN", () =>
            {
                Control.harmony.Patch(AccessTools.Method(typeof(ModTek.ModTek), "ParseGameJSON")
                                     , new HarmonyMethod(typeof(MakeModtekUseFasterParse).GetMethod(nameof(MakeModtekUseFasterParse.Prefix)))
                                     , null);
            });
            */
            Control.TrapAndTerminate("Patch HBS.Util.JSONSerializationUtility.StripHBSCommentsFromJSON", () => {
                Control.harmony.Patch(AccessTools.Method(typeof(HBS.Util.JSONSerializationUtility), "StripHBSCommentsFromJSON")
                                     , new HarmonyMethod(typeof(DontStripComments).GetMethod(nameof(DontStripComments.Prefix)))
                                     , null);
            });
        }
    }

    public class MakeModtekUseFasterParse
    {
        public static bool Prefix(string jsonText, ref JObject __result)
        {
            var res = Control.TrapAndTerminate("MakeModtekUseFasterParse.Prefix", () =>
            {
                Control.Log("Intercept modtek json: {0}", jsonText.Length);

                var stripped = DontStripComments.HBSStripCommentsMirror(jsonText);
                Control.Log("Stripped: {0}", stripped);

                return JObject.Parse(new Regex("(\\]|\\}|\"|[A-Za-z0-9])\\s*\\n\\s*(\\[|\\{|\")", RegexOptions.Singleline).Replace(stripped, "$1,\n$2"));
            });
            __result = res;
            return false;
        }
    }

    public class DontStripComments {
        // TODO: Is this function always called from main thread? We need to patch loadJSON, but it's generic
        public static bool guard = false;

        // Copied from HBS.Utils.JSONSerializationUtility temporarily
        public static string HBSStripCommentsMirror(string json)
        {
            return Control.TrapAndTerminate("HBSStripCommentsMirror", () =>
            {
                var self = new Traverse(typeof(HBS.Util.JSONSerializationUtility));
                var csp = self.Field("commentSurroundPairs").GetValue<Dictionary<string,string>>();

                string str = string.Empty;
                string format = "{0}(.*?)\\{1}";
                foreach (KeyValuePair<string, string> keyValuePair in csp)
                {
                    str = str + string.Format(format, keyValuePair.Key, keyValuePair.Value) + "|";
                }
                string str2 = "\"((\\\\[^\\n]|[^\"\\n])*)\"|";
                string str3 = "@(\"[^\"]*\")+";
                string pattern = str + str2 + str3;
                return Regex.Replace(json, pattern, delegate (Match me)
                {
                    foreach (KeyValuePair<string, string> keyValuePair2 in csp)
                    {
                        if (me.Value.StartsWith(keyValuePair2.Key) || me.Value.EndsWith(keyValuePair2.Value))
                        {
                            return string.Empty;
                        }
                    }
                    return me.Value;
                }, RegexOptions.Singleline);
            });
        }

        public static string StripComments(string json)
        {
            
            // Try to parse the json, if it doesn't work, use HBS comment stripping code.
            try
            {
                fastJSON.JSON.Parse(json);
                return json;
            }
            catch (Exception e)
            {
                return HBSStripCommentsMirror(json);
            }
        }

        public static bool Prefix(string json, ref string __result) {
            var res =  Control.TrapAndTerminate("DontStripComments.Prefix", () =>
            {
                var sc = StripComments(json);
                if (sc == null)
                    throw new System.Exception("StripComments result is null");
                return sc;
            });
            __result = res;
            return false;
        }
    }
}