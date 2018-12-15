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
using isogame;
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

            "RequestResource_Internal".Pre<DataManager>();
            "Exists".Post<DictionaryStore<object>>();
            "Get".Pre<DictionaryStore<object>>();
            "TryGet".Pre<DictionaryStore<object>>();
            "get_Keys".Post<DictionaryStore<object>>();
            "get_Count".Pre<DictionaryStore<object>>();
            "SetUnityDataManagers".Post<DataManager>();

            "ProcessRequests".Pre<DataManager>();
        }

        public static bool Initialized = false;
        public static void SetUnityDataManagers_Post(DataManager __instance) {
            if (Initialized) return;
            Initialized = true;



            //For debugger hook
            //System.Threading.Thread.Sleep(10000);

            var alljtypes = Assembly.GetAssembly(typeof(RT))
                                    .GetTypes()
                                    .Where(ty => ty.GetInterface(typeof(HBS.Util.IJsonTemplated).FullName) != null)
                                    .Where(ty => Enum.GetNames(typeof(RT)).Contains(ty.Name))
                                    .Where(ty => !Array("CombatGameConstants", "SimGameConstants").Contains(ty.Name))
                                    .ToList();

            Log("Autoresolve {0}", alljtypes.Select(ty => ty.Name).ToArray().Dump());

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

            Measure( "All speakerlists"
                   , () => rl.AllEntriesOfResource(RT.SimGameSpeakers, true)
                             .ForEach(entry => AlternativeLoading.Load.MapSync<ConversationSpeakerList,GameObject>( entry, bytes => SimGameConversationManager.LoadSpeakerListFromStream(new HBS.Util.SerializationStream(bytes)), null, null)
                                                                 .Done(list => { LogDebug($"ADDSPEAKER: {entry.Id} as {entry.Name}");
                                                                                 AllTheThings[entry.Name] = list; }
                                                                      , LogException)));

            Measure( "All conversations"
                   , () => rl.AllEntriesOfResource(RT.SimGameConversations, true)
                             .ForEach(entry => AlternativeLoading.Load.MapSync<Conversation,GameObject>( entry, bytes => SimGameConversationManager.LoadConversationFromStream(new HBS.Util.SerializationStream(bytes)), null, null)
                                                                 .Done(convo => { LogDebug($"ADDCONVO: {entry.Id} as {convo.idRef.id}");
                                                                                  AllTheThings[convo.idRef.id] = convo;
                                                                                  //FIXME:
                                                                                }
                                                                      , LogException)));


            Spam(() => $"LiterallyAllTheThings {AllTheThings.Keys.ToArray().Dump()}");


            var allTheDeps = AllTheThings.Where(thing => thing.Value is DataManager.ILoadDependencies)
                                         .Select(thing => thing.Value as DataManager.ILoadDependencies)
                                         .ToList();
            if (__instance == null) LogError("DM instance is null");

            var dummy = new AlternativeLoading.DMGlue.DummyLoadRequest(__instance, "dummy", 0);
            allTheDeps.ForEach(dep => { dep.DataManager = __instance;
                                        Trap(() => new Traverse(dep).Field("dataManager").SetValue(__instance));
                                        Trap(() => new Traverse(dep).Field("loadRequest").SetValue(dummy));
                                      });

            var dmrc = new DataManagerRequestCompleteMessage(0, null);
            bool CheckDeps(DataManager.ILoadDependencies ild) {
                Trap(() => ild.CheckDependenciesAfterLoad(dmrc)); //Crashes for a few things like MechDef
                return ild.DependenciesLoaded(0);
            }

            void Report() {
                var sWithDeps = allTheDeps.Where(thing => !Trap(() => CheckDeps(thing), () => false));
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

        public static bool TryGet_Pre(ref bool __result, string id, out object t) {
            if (AllTheThings.TryGetValue(id, out var thething)) { __result = true;
                                                                  t = thething;
                                                                  Spam(() => $"Found the thing[{id}]");
                                                                  return false; }
            t = null;
            return true;
        }

        public static void get_Keys_Post(object __instance, ref IEnumerable<string> __result) {
            var type = __instance.GetType().GetGenericArguments()[0];

            __result = __result.Concat(AllTheThings.Where(thing => thing.Value.GetType() == type).Select(thing => thing.Key)).Distinct();
        }

        public static bool get_Count_Pre(object __instance) {
            var type = __instance.GetType().GetGenericArguments()[0];
            LogWarning($"Something tried to pull the Count for {type.FullName}, we returned *0*");
            return true;
        }


        public static bool RequestResource_Internal_Pre(string identifier) {
            if (AllTheThings.ContainsKey(identifier)) {
                var item = AllTheThings[identifier];
                if (item is DesignMaskDef)
                    CollectSingletons.MC.PublishMessage(new DataManagerRequestCompleteMessage<DesignMaskDef>(RT.DesignMaskDef, identifier, item as DesignMaskDef));
                return false;
            }
            else return true;
        }

        public static void ProcessRequests_Pre(DataManager __instance) {
            var fromMethod = new StackFrame(2).GetMethod();
            var isFromExternal = fromMethod.DeclaringType.Name != "DataManager";

            if (!isFromExternal) return;

            var dmlr = new Traverse(__instance).Field("foregroundRequestsList").GetValue<List<DataManager.DataManagerLoadRequest>>();
            if (dmlr.Count > 0) {
                var byid = string.Join(" ", dmlr.Select(lr => $"{lr.ResourceId}:{lr.ResourceType.ToString()}").Take(10).ToArray());
                LogDebug($"ProcessRequests started with: {byid}");
            } else {
            LogDebug($"ProcessRequests[external? {isFromExternal}] started with an EMPTY queue from {fromMethod.DeclaringType.FullName}.{fromMethod.Name} this will never complete!");
            CollectSingletons.MC.PublishMessage(new DataManagerLoadCompleteMessage());
            }
        }
    }
}
