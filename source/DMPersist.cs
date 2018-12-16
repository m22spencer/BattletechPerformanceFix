using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BattleTech;
using BattleTech.Data;
using Harmony;
using static BattletechPerformanceFix.Extensions;

namespace BattletechPerformanceFix
{
    class DMPersist : Feature
    {
        public void Activate()
        {
            ".ctor".Pre<DataManager>();
            "RequestResource_Internal".Pre<DataManager>();
        }

        public static MessageCenter mc = null;
        public static Dictionary<string,MessageCenterMessage> Permanent = new Dictionary<string,MessageCenterMessage>();

        public static object itemOf(MessageCenterMessage msg)
            => Trap(() => new Traverse(msg).Property("Resource").GetValue<object>(), () => null);


        public static bool WantCaptureRequest = true;
        public static void CTOR_Pre(MessageCenter messageCenter) {
            mc = messageCenter;
            messageCenter.AddSubscriber( MessageCenterMessageType.DataManagerRequestCompleteMessage
                                       , msg => { var msm = (msg as DataManagerRequestCompleteMessage);
                                                  if (!WantCaptureRequest) { Spam(() => $"Ignoring reannounce of {msm.ResourceId}"); return; }
                                                  var item = itemOf(msm);
                                                  if (item is UnityEngine.Object) Spam(() => $"Ignoring destroyable item {msm.ResourceId}");
                                                  else if (Permanent.ContainsKey(msm.ResourceId)) Spam(() => $"Duplicate load of {msm.ResourceId}");
                                                  else { Spam(() => $"Persisting {msm.ResourceId}");
                                                         Permanent[msm.ResourceId] = msg; }
                                                });
        }

        public static void Republish(MessageCenterMessage msg) {
            WantCaptureRequest = false;
            mc.PublishMessage( msg);
            WantCaptureRequest = true;
        }

        public static bool RequestResource_Internal_Pre(DataManager __instance, string identifier) {
            if (Permanent.TryGetValue(identifier, out var msg)) {
                Spam(() => $"Request for Persisted item {identifier}");
                var item = itemOf(msg);
                if (item is DataManager.ILoadDependencies) {
                    Spam(() => $"Refresh dependencies for {identifier}");
                    (item as DataManager.ILoadDependencies).RequestDependencies(__instance, () => { Spam(() => $"re-deps complete for {identifier}");
                                                                                                    Republish(msg); }
                                                                               , new AlternativeLoading.DMGlue.DummyLoadRequest(__instance));
                } else {
                    Republish( msg);
                }
                return false;
            } else {
                Spam(() => $"DM Load for {identifier}");
                return true;
            }
        }
    }
}
