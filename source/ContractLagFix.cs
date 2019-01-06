using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BattleTech;
using BattleTech.Data;
using BattleTech.Framework;
using Harmony;
using System.Reflection;
using System.Diagnostics;
using HBS.Util;
using static System.Reflection.Emit.OpCodes;
using static BattletechPerformanceFix.Extensions;

namespace BattletechPerformanceFix
{
    class ContractLagFix : Feature
    {
        public void Activate()
        {
            Assembly.GetAssembly(typeof(ObjectiveRef))
                .GetTypes()
                .Where(ty => ty.BaseType != null && ty.BaseType.FullName.Contains("EncounterObjectRef"))
                .ToList()
                .ForEach(ty => {
                    var meth = AccessTools.Method(ty.BaseType, "UpdateEncounterObjectRef");

                    var tpatch = new HarmonyMethod(typeof(ContractLagFix), nameof(Transpile));
                    tpatch.prioritiy = Priority.First;

                    Main.harmony.Patch(meth
                                         , new HarmonyMethod(typeof(ContractLagFix), nameof(Pre))
                                         , new HarmonyMethod(typeof(ContractLagFix), nameof(Post))
                                         , tpatch);
                });

            LogDebug($"EncounterLayerData ctors {typeof(EncounterLayerData).GetConstructors().Count()}");
            typeof(EncounterLayerData).GetConstructors()
                .ToList()
                .ForEach(con =>
                    Main.harmony.Patch(con
                                         , null, new HarmonyMethod(typeof(ContractLagFix), nameof(EncounterLayerData_Constructor))));

        }

        static int ct = 0;
        static Stopwatch sw = new Stopwatch();

        public static void EncounterLayerData_Constructor(EncounterLayerData __instance)
        {
            eld_cache = eld_cache.Where(c => c != null).ToList();
            eld_cache.Add(__instance);
        }

        static List<EncounterLayerData> eld_cache = new List<EncounterLayerData>();

        public static EncounterLayerData CachedEncounterLayerData()
        {
            return Trap(() =>
            {
                var cached = eld_cache.Where(c => c != null && c.isActiveAndEnabled).FirstOrDefault();
                // integrity check, negates patch. Need to have a Verify flag in the json to enable this.
                //*
                var wants = UnityEngine.Object.FindObjectOfType<EncounterLayerData>();
                if (cached != wants)
                {
                    var inscene = UnityEngine.Object.FindObjectsOfType<EncounterLayerData>();
                    LogError($"eld_cache is out of sync, wants: {wants?.GUID ?? "null"}");
                    LogError($"scene contains ({string.Join(" ", inscene.Select(c => c == null ? "null" : string.Format("(:contractDefId {0} :contractDefIndex {1} :GUID {2})", c.contractDefId, c.contractDefIndex, c.GUID)).ToArray())})");
                    LogError($"current EncounterLayerData ({string.Join(" ", eld_cache.Select(c => c == null ? "null" : string.Format("(:contractDefId {0} :contractDefIndex {1} :GUID {2})", c.contractDefId, c.contractDefIndex, c.GUID)).ToArray())})");
                    AlertUser( "ContractsLagFix: Verify error"
                             , "Please report this to the BT Modding group, and include logs");
                }
                if (cached == null)
                {
                    LogSpam("ContractLagFix: No EncounterLayerData");
                }
                //*/

                return wants;
            });
        }

        public static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> ins)
        {
            return ins.SelectMany(i =>
            {
                if (i.opcode == Call && (i.operand as MethodInfo).Name.StartsWith("FindObjectOfType"))
                {
                    i.operand = AccessTools.Method(typeof(ContractLagFix), "CachedEncounterLayerData");
                    return Sequence(i);
                } else
                {
                    return Sequence(i);
                }
            });
        }

        public static void Pre()
        {
            sw.Start();
        }
        public static void Post()
        {
            sw.Stop();
            //LogDebug("UpdateEncounterObjectRef {0}: {1} ms total", ct++, sw.Elapsed.TotalMilliseconds);
        }
    }
}
