using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HBS.Data;
using Harmony;
using BattleTech;
using BattleTech.Data;
using ILD = BattleTech.Data.DataManager.ILoadDependencies;
using Harmony;
using System.Diagnostics;
using RT = BattleTech.BattleTechResourceType;
using UnityEngine;
using static BattletechPerformanceFix.Extensions;


namespace BattletechPerformanceFix
{
    class LoadAllTheThings : Feature
    {
        public static Dictionary<string,object> AllTheThings = new Dictionary<string,object>();
            
        public void Activate() {
            var alljtypes = Assembly.GetAssembly(typeof(RT))
                    .GetTypes()
                    .Where(ty => ty.GetInterface(typeof(HBS.Util.IJsonTemplated).FullName) != null)
                    .Where(ty => Enum.GetNames(typeof(RT)).Contains(ty.Name))
                    .Where(ty => !Array("CombatGameConstants", "SimGameConstants").Contains(ty.Name))
                    .ToList();

            var rl = new BattleTechResourceLocator();

            var allentries = Measure( "Json-Entries"
                                        , () => alljtypes.SelectMany(type => rl.AllEntriesOfResource(type.Name.ToRT(), true)
                                                                               .Select(entry => new { type, entry }))
                                                         .ToList());
                                                                      

            var alljson = Measure( "Load-Json-String"
                                 , () => allentries.Select(te => { string text = null;
                                                                   AlternativeLoading.Load.LoadText(te.entry).Done(x => text = x);
                                                                   if (text == null) LogError("My terrible hack failed");
                                                                   return new { type = te.type
                                                                              , entry = te.entry
                                                                              , text }; })
                                                   .ToList());
            
            var alldefs = Measure( "Deserialize-Json"
                                 , () => alljson.Select(tej => { var inst = (HBS.Util.IJsonTemplated)Activator.CreateInstance(tej.type)
                                                                                                              .NullThrowError("No activator for {trl.type.FullName}");
                                                                 inst.FromJSON(tej.text);
                                                                 return new { entry = tej.entry
                                                                            , def = inst }; })
                                                .ToList());
            alldefs.ForEach(ed => AllTheThings[ed.entry.Id] = ed.def);
            LogDebug($"AllTheThings[{alldefs.Count}] done");

            "RequestResource_Internal".Pre<DataManager>();
            "Exists".Post<DictionaryStore<object>>();
            "Get".Pre<DictionaryStore<object>>();
        }

        public static void Exists_Post(ref bool __result, string id) {
            __result = __result || AllTheThings.ContainsKey(id);
        }

        public static bool Get_Pre(ref object __result, string id) {
            if (AllTheThings.TryGetValue(id, out var thething)) { __result = thething;
                                                                  Spam(() => $"Found the thing[{id}]");
                                                                  return false; }
            return true;
        }

        public static bool RequestResource_Internal_Pre(string identifier) {
            if (AllTheThings.ContainsKey(identifier)) return false;
            else return true;
        }
    }
}
