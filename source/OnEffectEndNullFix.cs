using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Harmony;
using BattleTech;
using System.Reflection;
using System.Diagnostics;
using static BattletechPerformanceFix.Extensions;

namespace BattletechPerformanceFix
{
    // Integration of JustDieAlready + some extra logging.
    // Ref  https://github.com/rajderks/BattleTech-JustDieAlready/
    // Thank you RD
    class OnEffectEndNullFix : Feature
    {
        public void Activate()
        {
            "initStatisiticEffect".Pre<StatisticEffect>();
            "GetProperStatCollectionOnLoad".Pre<StatisticEffect>();
            "GetProperStatCollectionOnLoad".Post<StatisticEffect>();

            "OnEffectEnd".Pre<StatisticEffect>();
        }

        public static bool OnEffectEnd_Pre(StatisticEffect __instance, object ___target) {
            if (Spam) LogSpam($"OnEffectEnd has been invoked for {__instance.id}");
            var collection = new Traverse(__instance)
                .Property("statCollection")
                .GetValue<StatCollection>();

            var hasCollection = collection != null;
            if (!hasCollection)
            {
                LogWarning($"OnEffectEnd was called with null statCollection for {__instance.id} from {new StackTrace(1).ToString()}");

                // Taken from the base function directly.
                // Need to continue the effect chain as it updates pathing & FOW & other logic
                (___target as ICombatant)?.OnEffectEnd(__instance);
            }

            return hasCollection;
        }

        public static void initStatisiticEffect_Pre(StatisticEffect __instance, EffectData effectData, StatCollection targetStatCollection) {
            if (Spam) LogSpam($"initStatisticEffect has been invoked for {__instance.id}");
            effectData.NullCheckError("EffectData is null for {__instance.id}");
            effectData.NullCheckError("targetStatCollection is null for {__instance.id}");
        }

        public static void GetProperStatCollectionOnLoad_Pre(StatisticEffect __instance) {
            if (Spam) LogSpam($"GetProperStatCollectionOnLoad_Pre has been invoked for {__instance.id}");
            var collection = new Traverse(__instance)
                .Property("statCollection")
                .GetValue<StatCollection>();

            collection
                .NullCheckError($"StatCollection is null at Prefix for {__instance.id}. This should not be possible");
        }

        public static void GetProperStatCollectionOnLoad_Post(StatisticEffect __instance, EffectData ___effectData) {
            if (Spam) LogSpam($"GetProperStatCollectionOnLoad_Post has been invoked for {__instance.id}");
            var collection = new Traverse(__instance)
                .Property("statCollection")
                .GetValue<StatCollection>();

            collection
                .NullCheckError($"StatCollection has been set to null for {__instance.id}. :targetCollection {___effectData?.statisticData?.targetCollection}");
        }
    }
}
