using System;

using Harmony;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using RT = BattleTech.BattleTechResourceType;
using BattleTech;
using BattleTech.Data;
using UnityEngine;
using static BattletechPerformanceFix.Extensions;

namespace BattletechPerformanceFix
{
    class ImmediateAssetFallbackRecovery : Feature
    {
        public static DataManager DM = null;

        public static void CTOR_Pre(DataManager __instance) {
            DM = __instance;
        }

        public void Activate() {
            ".ctor".Pre<DataManager>();

            "PooledInstantiate".Post<PrefabCache>();
            "GetSprite".Post<SpriteCache>();
            "GetAsset".Post<SVGCache>();
            "GetLoadedTexture".Post<TextureManager>();
            "RequestTexture".Pre<TextureManager>();
        }

        public static VersionManifestEntry Locate(string id, RT? type = null) {
            var locator = DM.ResourceLocator;
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

        public static void GetLoadedTexture_Post(ref Texture2D __result, string resourceId) {
            if (__result == null) {
                LogWarning($"Request Texture.Get {resourceId} but it does not exist");
            }
        }

        public static void RequestTexture_Pre(string resourceId, TextureLoaded loadedCallback, ref LoadFailed error, Dictionary<string, Texture2D> ___loadedTextures) {
            error = (msg) => LogWarning($"Request Texture.Async {resourceId} but it does not exist :with [{msg}]");

        }

        public static void GetSprite_Post(ref Sprite __result, string id) {
            if (__result == null)
                LogWarning($"Request Sprite {id} but it does not exist");
        }

        public static void GetAsset_Post(ref SVGImporter.SVGAsset __result, string id) {
            if (__result == null) {
                var entry = Locate(id, RT.SVGAsset);
                SVGImporter.SVGAsset asset = null;
                Exception ex = null;
                AlternativeLoading.Load.MapSync(entry, null
                                               , (SVGImporter.SVGAsset x) => x
                                               , (SVGImporter.SVGAsset y) => y)
                                  .Done( svg => asset = svg
                                       , exn => ex = exn);

                LogWarning(() => $"Request missing SVGAsset {id}. Can we get it? {entry != null}. Did we get it? {asset != null}. Why? {ex?.Message}");
            }
        }

        public static void PooledInstantiate_Post(ref GameObject __result, string id) {
            if (__result == null)
                LogWarning($"Request Prefab {id} but it does not exist");
        }
    }
}
