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
            "get_Keys".Pre<DictionaryStore<object>>();
            "get_Count".Pre<DictionaryStore<object>>();
            "SetUnityDataManagers".Post<DataManager>();

            "ProcessRequests".Pre<DataManager>();
        }

        public static bool Initialized = false;
        public static void SetUnityDataManagers_Post(DataManager __instance) {
            if (Initialized) return;
            Initialized = true;

            var allTheDeps = AllTheThings.Where(thing => thing.Value is DataManager.ILoadDependencies)
                                         .Select(thing => thing.Value as DataManager.ILoadDependencies)
                                         .ToList();
            if (__instance == null) LogError("DM instance is null");

            var dummy = new AlternativeLoading.DMGlue.DummyLoadRequest(__instance, "dummy", 0);
            allTheDeps.ForEach(dep => { dep.DataManager = __instance;
                                        Trap(() => new Traverse(dep).Field("dataManager").SetValue(__instance));
                                        Trap(() => new Traverse(dep).Field("loadRequest").SetValue(dummy));
                                      });

            void Report() {
                var sWithDeps = allTheDeps.Where(thing => !Trap(() => thing.DependenciesLoaded(0), () => false));
                var types = sWithDeps.Select(d => d.GetType().FullName).Distinct().ToArray();
                Log("{0}", $"Need to determine [{sWithDeps.Count()}] dependencies of types {types.Dump()}");

            }

            Report();
            Report();
            Report();
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

        public static bool get_Keys_Pre(object __instance, ref IEnumerable<string> __result) {
            var type = __instance.GetType().GetGenericArguments()[0];
            var rttype = Trap(() => (RT?)type.Name.ToRT(), () => null);
            if (rttype != null) {
                LogWarning($"Something tried to pull the Keys for {type.FullName}, we returned AllManifestEntryKeys");
                __result = CollectSingletons.RL.AllEntriesOfResource(rttype.Value, true).Select(e => e.Id);
                return false;
            } else {
                LogWarning($"Something tried to pull the Keys for {type.FullName}, we returned *nothing*");
                return true;
            }
        }

        public static bool get_Count_Pre(object __instance) {
            var type = __instance.GetType().GetGenericArguments()[0];
            LogWarning($"Something tried to pull the Count for {type.FullName}, we returned *0*");
            return true;
        }


        public static bool RequestResource_Internal_Pre(string identifier) {
            if (AllTheThings.ContainsKey(identifier)) return false;
            else return true;
        }

        public static void ProcessRequests_Pre(DataManager __instance) {
            var fromMethod = new StackFrame(2).GetMethod();
            var isFromExternal = fromMethod.DeclaringType.Name != "DataManager";

            if (!isFromExternal) return;

            var dmlr = new Traverse(__instance).Field("foregroundRequestsList").GetValue<List<DataManager.DataManagerLoadRequest>>();
            if (dmlr.Count > 0) return;

            LogDebug($"ProcessRequests[external? {isFromExternal}] started with an EMPTY queue from {fromMethod.DeclaringType.FullName}.{fromMethod.Name} this will never complete!");
            CollectSingletons.MC.PublishMessage(new DataManagerLoadCompleteMessage());


        }
    }
}
