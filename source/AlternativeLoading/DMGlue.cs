using RSG;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BattleTech.Data;
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

        public static bool RequestResource_Internal_Pre() {
            return true;
        }

        public static bool Update_Pre() {
            return true;
        }

        public static bool ProcessRequests_Pre() {
            return true;
        }

        public static IPromise<T> DMResolveDependencies<T>(this IPromise<T> p) {
            return p.Then(maybeDeps => {
                    if (maybeDeps is DataManager.ILoadDependencies) {
                        var ild = maybeDeps as DataManager.ILoadDependencies;
                        var prom = new Promise<T>();
                        // FIXME: This will require the DummyLoader from rda branch
                        ild.RequestDependencies(DMGlue.DM, () => prom.Resolve(maybeDeps), null);
                        return prom;
                    } else {
                        return Promise<T>.Resolved(maybeDeps);
                    }
                });
        }
    }

}
