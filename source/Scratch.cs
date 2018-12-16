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
    class Scratch : Feature
    {
        public void Activate() {
            var fns = List("Add", "Remove", "Clear"); //"Exists", "Get", "TryGet", "GetOrCreate", "Add", "Remove", "Clear", "get_Count", "get_Keys");
            "Add".Pre<DictionaryStore<object>>();
            "Remove".Pre<DictionaryStore<object>>();
            "Clear".Pre<DictionaryStore<object>>();

            "Clear".Pre<BattleTech.Data.DataManager>("DM_CLEAR");
            "RequestResource_Internal".Pre<BattleTech.Data.DataManager>();

            LogDebug("Types to handle: ");
            Assembly.GetAssembly(typeof(ILD))
                    .GetTypes()
                    .Where(ty => ty.GetInterface(typeof(ILD).FullName) != null)
                    .SelectMany(ty => ty.GetProperties(AccessTools.all)
                                        .Where(prop => typeof(UnityEngine.Object).IsAssignableFrom(prop.PropertyType))
                                        .Select(prop => prop.DeclaringType.FullName + "::" + prop.Name + $"(property {prop.PropertyType.FullName})")
                                        .Concat(ty.GetFields()
                                                  .Where(field => typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType))
                                                  .Select(field => field.DeclaringType.FullName + "::" + field.Name + $"(field {field.FieldType.FullName})")))
                    .ToList()
                    .ForEach(str => LogDebug(str));

            var rl = new BattleTechResourceLocator();

            var forFrontload = Assembly.GetAssembly(typeof(RT))
                    .GetTypes()
                    .Where(ty => ty.GetInterface(typeof(HBS.Util.IJsonTemplated).FullName) != null)
                    .Where(ty => Enum.GetNames(typeof(RT)).Contains(ty.Name))
                    .ToList();

            LogDebug("Can frontload the following entries: {0}", forFrontload.Dump(false));


            var kvl = forFrontload.Select(ty => new KeyValuePair<Type,RT>(ty, (RT)Enum.Parse(typeof(RT), ty.Name)))
                                  .ToList();

            var entries = kvl.Select(kv => new KeyValuePair<Type,VersionManifestEntry[]>(kv.Key, rl.AllEntriesOfResource(kv.Value, true)))
                             .ToList();
            var count = entries.SelectMany(kv => kv.Value).Count();

            var flatstr = entries.SelectMany(kv => kv.Value).ToList();

            var strings = Measure( (b,t) => LogDebug($"All def *strings* in {t.TotalSeconds} and {b}b")
                                 , () => flatstr.Select(e => AlternativeLoading.Load.LoadText(e)).ToList());

            var defs = Measure((b,t) => LogDebug($"All defs[{count}] -> class in {t.TotalSeconds} and {b}b")
                              , () => entries.SelectMany(kv => kv.Value.Select(e => AlternativeLoading.Load.LoadJsonD(e, kv.Key)))
                                             .ToList());

            defs.ForEach(d => d.Done(x => {}, LogException));

            LogDebug($"{defs.Count()} defs total");
        }

        public static DataManager DM = null;
        public static MessageCenter MC;
        public static AlternativeLoading.DMGlue.DummyLoadRequest dl = null;

        public static bool RequestResource_Internal_Pre(DataManager __instance, string identifier, RT resourceType) {
            if (DM == null) {
                DM = __instance;
                MC = new Traverse(DM).Property("MessageCenter").GetValue<MessageCenter>();
                MC.AddSubscriber( MessageCenterMessageType.DataManagerLoadCompleteMessage
                                , (msg) => LogDebug("DM Load complete"));

                            }
            if (dl == null) dl = new AlternativeLoading.DMGlue.DummyLoadRequest(DM);
            var t = resourceType;
            if (Has.Contains(identifier)) {
                LogDebug($"HAS {identifier}");
                var item = DM.Get(t, identifier);
                if (item is DataManager.ILoadDependencies) {
                    LogDebug($"Getting dependencies for already loaded item {identifier}");
                    var ild = (item as DataManager.ILoadDependencies);
                    ild.RequestDependencies(DM, () => {}, dl);
                    LogDebug("Check dependencies complete");
                    var hasDeps = ild.DependenciesLoaded(100000);
                    LogDebug($"Deps for {identifier} satisfied? {hasDeps}");
                    /*
                    var dr = new DataManagerRequestCompleteMessage(BattleTechResourceType.MechDef, "dummy");
                    ild.RequestDependencies(DM, () => {
                        }, dl);
                    ////ild.RequestDependencies(DM, () => ild.CheckDependenciesAfterLoad(dr), dl);
                    */
                }
                return false;
            }
            else {
                LogDebug($"DM Load {identifier}");
                return true;
            }
        }

        public static bool WANTCLEARDEFS = true;
        public static void DM_CLEAR(ref bool defs) {
            defs = WANTCLEARDEFS;
            if (defs) LogDebug("Clear defs");
            WANTCLEARDEFS = false;
        }

        public static HashSet<string> Has = new HashSet<string>();

        static int ct = 0;
        public static void Add_Pre(string id) {
            LogDebug($"Adding[{ct++}] {id}");

            LoadDesc["Persistent"] = $"{ct}/??";

            Has.Add(id);
        }

        public static bool Remove_Pre()
            => false;

        public static bool Clear_Pre()
            => false;
    }
}
