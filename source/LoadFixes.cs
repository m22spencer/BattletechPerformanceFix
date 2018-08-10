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
    //[HarmonyPatch(typeof(ModTek.ModTek), "ParseGameJSON")]
    public class MakeModtekUseFasterParse
    {
        public static bool Prefix(string jsonText, JObject __result)
        {
            Control.Log("Intercept modtek json: {0}", jsonText.Length);

            var stripped = DontStripComments.StripComments(jsonText);

            __result = JObject.Parse(new Regex("(\\]|\\}|\"|[A-Za-z0-9])\\s*\\n\\s*(\\[|\\{|\")", RegexOptions.Singleline).Replace(stripped, "$1,\n$2"));

            return false;
        }
    }


    //[HarmonyPatch(typeof(HBS.Util.JSONSerializationUtility), "StripHBSCommentsFromJSON")]
    public class DontStripComments {
        // TODO: Is this function always called from main thread? We need to patch loadJSON, but it's generic
        public static bool guard = false;

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
                guard = true;
                var res = new Traverse(typeof(HBS.Util.JSONSerializationUtility)).Method("StripHBSCommentsFromJSON").GetValue<string>(json);
                guard = false;
                return res;
            }
        }

        public static bool Prefix(string json, string __result) {
            if (guard) return true;
            __result = StripComments(json);
            return false;
        }
    }
}