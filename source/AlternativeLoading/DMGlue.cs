using RSG;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Harmony;
using BattleTech.Data;
using RT = BattleTech.BattleTechResourceType;
using static BattletechPerformanceFix.Extensions;

namespace BattletechPerformanceFix.AlternativeLoading
{
    public static class DMGlue
    {
        public static DataManager DM;
        public static void Initialize() {
            ".ctor".Pre<DataManager>();
            "Update".Pre<DataManager>();
            "ProcessRequests".Pre<DataManager>();
            "RequestResource_Internal".Pre<DataManager>();
        }

        public static void CTOR_Pre(DataManager __instance) {
            LogDebug(() => $"Found DM instance {__instance != null}");
            DM = __instance;
        }

        public static DummyLoadRequest dlr = null;
        public static int waiting = 0;   //FIXME: Don't like this
        public static bool RequestResource_Internal_Pre(RT resourceType, string identifier) {
            if (resourceType == RT.MechDef) {
                var entry = DM.ResourceLocator.EntryByID(identifier, resourceType, true);
                if (entry == null) LogWarning($"No manifest entry for {identifier}:{resourceType.ToString()}");
                else {
                    if (dlr == null) { 
                        dlr = new DummyLoadRequest(DM, "dummmy_DM_BlockQueue", 10u);
                        new Traverse(DM).Method("AddForegroundLoadRequest", dlr.ResourceType, dlr.ResourceId, dlr).GetValue();
                        var tdlr = new Traverse(DM).Method("FindForegroundLoadRequest", dlr.ResourceType, dlr.ResourceId).GetValue<DataManager.DataManagerLoadRequest>();
                        LogDebug($"TDLR ${tdlr.GetType().FullName} ${tdlr?.State.ToString()}");

                    }
                    LogDebug($"Requesting MechDef: {identifier}:{resourceType.ToString()}");
                    waiting++;
                    entry.LoadJson<BattleTech.MechDef>()
                         .DMResolveDependencies()
                         .Done(mdef => { waiting--;
                                         LogDebug($"Got MechDef: {identifier}:{resourceType.ToString()}");
                                         new Traverse(DM).Property("MessageCenter").GetValue<MessageCenter>()
                                                         .PublishMessage(new BattleTech.DataManagerRequestCompleteMessage<BattleTech.MechDef>(resourceType, identifier, mdef));
                                         var mstore = new Traverse(DM).Field("mechDefs").GetValue<HBS.Data.DictionaryStore<BattleTech.MechDef>>();
                                         Measure( (b,t) => LogDebug($"MDEF copy bad: {t.TotalMilliseconds}")
                                                , () => { new BattleTech.MechDef().FromJSON(mdef.ToJSON()); return 0; });
                                         Measure( (b,t) => LogDebug($"MDEF copy fast: {t.TotalMilliseconds}")
                                                , () => { new BattleTech.MechDef(mdef); return 0; });

                                         mstore.Add(identifier, mdef);
                                       }

                              , exn => { waiting--; LogException(exn); });
                }
                return false;
            } else {
                return true;
            }
        }

        public static bool Update_Pre() {
            if (dlr != null && waiting == 0) {
                LogDebug("Clearing DM block");
                dlr.Complete();
            }
                
            if (dlr != null) {
                var tdlr = new Traverse(DM).Method("FindForegroundLoadRequest", dlr.ResourceType, dlr.ResourceId).GetValue<DataManager.DataManagerLoadRequest>();
                if (tdlr != null)
                    LogDebug($"TDLRU[{waiting}] {tdlr?.GetType()?.FullName ?? "nothing"} {tdlr?.State.ToString()} {tdlr?.RequestWeight.RequestWeight}");
            }

            return true;
        }

        public static bool ProcessRequests_Pre() {
            return true;
        }

        public static IPromise<T> DMResolveDependencies<T>(this IPromise<T> p) {
            if (DummyDepsLoader == null) DummyDepsLoader = new DummyLoadRequest(DM);
            return p.Then(maybeDeps => {
                    if (maybeDeps is DataManager.ILoadDependencies) {
                        var ild = maybeDeps as DataManager.ILoadDependencies;
                        var prom = new Promise<T>();
                        ild.RequestDependencies(DMGlue.DM, () => Trap(() => prom.Resolve(maybeDeps)), DummyDepsLoader);
                        return prom;
                    } else {
                        return Promise<T>.Resolved(maybeDeps);
                    }
                });
        }

        // Used for loadrequest weight in DMResolveDependencies, and also to block the load queue in DM.
        public static DummyLoadRequest DummyDepsLoader;
        public class DummyLoadRequest : DataManager.ResourceLoadRequest<object>
        {
            public DummyLoadRequest(DataManager dataManager, string id = "dummy_load_request_for_weight", uint weight = 10000) : base(dataManager, RT.AbilityDef, id, weight, null) {
                this.State = DataManager.DataManagerLoadRequest.RequestState.Processing;
            }
            public override bool AlreadyLoaded { get => true; }

            public void Complete()
            {
                LogDebug("Complete dummy {0}", Enum.GetName(typeof(DataManager.DataManagerLoadRequest.RequestState), this.State));
                this.State = DataManager.DataManagerLoadRequest.RequestState.Complete;
            }

            public override void Load()
            {
                LogDebug("Load dummy");
            }

            public override void NotifyLoadComplete()
            {
                LogDebug("NotifyLoadComplete dummy");
            }

            public override void SendLoadCompleteMessage()
            {
                LogDebug("SendLoadCompleteMessage dummy");
            }

            public override void OnLoaded()
            {
                LogDebug("OnLoaded dummy");
            }
        }
    }
}
