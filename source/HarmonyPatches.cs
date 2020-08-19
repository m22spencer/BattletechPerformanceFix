using System.Collections.Generic;
using System.Linq;
using BattleTech;
using BattleTech.Framework;
using Harmony;
using System.Reflection;
using System.Diagnostics;
using static System.Reflection.Emit.OpCodes;
using static BattletechPerformanceFix.Extensions;
using System.Reflection.Emit;
using BestHTTP.ServerSentEvents;
using BattleTech.Data;
using System;
using System.Security.Permissions;

namespace BattletechPerformanceFix
{
    class HarmonyPatches : Feature
    {
        public void Activate()
        {
            typeof(MethodInvoker)
                .GetMethods(AccessTools.all)
                .First(m => m.GetParameters().Length == 1)
                .Patch("GetHandler_Pre");

            typeof(AccessTools)
                .GetMethods(AccessTools.all)
                .First(m => m.Name == "MakeDeepCopy" && !m.IsGenericMethodDefinition)
                .Patch(null, null, "MakeDeepCopy_Transpile");
        }

        static Dictionary<MethodInfo, FastInvokeHandler> FastInvokeCache = new Dictionary<MethodInfo, FastInvokeHandler>();
        public static bool GetHandler_Pre(MethodInfo methodInfo, ref FastInvokeHandler __result)
        {
            __result = FastInvokeCache.TryGetValue(methodInfo, out var finvoke)
                ? finvoke
                : FastInvokeCache[methodInfo] = MethodInvoker.Handler(methodInfo, methodInfo.DeclaringType.Module, false);

            return false;
        }


        public static MethodInfo GetAddMethodFaster(Type type, Func<MethodInfo, bool> _)
        {
            return type.GetMethod("Add", AccessTools.all);
        }

        public static IEnumerable<CodeInstruction> MakeDeepCopy_Transpile(IEnumerable<CodeInstruction> ins)
        {
            return ins.Select(i =>
            {
                if ((i.operand as MethodBase)?.Name == "FirstMethod")
                {
                    i.operand = typeof(HarmonyPatches).GetMethod("GetAddMethodFaster");
                }
                return i;
            });
        }
    }
}