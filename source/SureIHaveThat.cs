using System;

using Harmony;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RSG;

using RT = BattleTech.BattleTechResourceType;
using BattleTech;
using BattleTech.Data;
using UnityEngine;
using BattletechPerformanceFix.AlternativeLoading;
using static BattletechPerformanceFix.Extensions;

namespace BattletechPerformanceFix
{
    class SureIHaveThat : Feature
    {
        // FIXME: Copy pasted from IFR fix
        public static DataManager DM = null;
        public static TextureManager TM = null;

        public static void CTOR_Pre(DataManager __instance) {
            DM = __instance;
            TM = new Traverse(__instance).Property("TextureManager").GetValue<TextureManager>();
        }

        public static VersionManifestEntry Locate(string id, RT? type = null) {
            if (DM == null) LogError("DM is null");
            var locator = DM.ResourceLocator;
            if (locator == null) LogError("locator is null");
            if (type != null) return DM.ResourceLocator.EntryByID(id, type.Value, true);

            var baseManifest = new Traverse(locator).Field("baseManifest").GetValue<Dictionary<RT, Dictionary<string, VersionManifestEntry>>>();
            baseManifest.NullCheckError("Unable to access baseManifest");  //FIXME: Also needs the contents packs manifest if user owns them
            new Traverse(locator).Method("UpdateTypedEntriesIfNeeded");
            // FIXME: Terribly slow
            foreach(var types in baseManifest) {
                if (types.Value.TryGetValue(id, out var ent)) return ent;
            }
            return null;
        }

        public static bool Has(string id, RT? type = null) => Locate(id, type) != null;


        public void Activate() {
            ".ctor".Pre<DataManager>();
            "Contains".Post<TextureManager>(nameof(RID));

            "Contains".Post<SpriteCache>(nameof(ID));
            "Contains".Post<SVGCache>(nameof(ID));
            "IsPrefabInPool".Post<PrefabCache>(nameof(ID));
        }

        public static void RID(string resourceId, ref bool __result)
            => __result = __result || Has(resourceId);
        public static void ID(string id, ref bool __result)
            => __result = __result || Has(id);
    }
}
