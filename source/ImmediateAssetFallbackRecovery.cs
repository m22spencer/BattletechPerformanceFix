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
    class ImmediateAssetFallbackRecovery : Feature
    {
        public static DataManager DM = null;
        public static TextureManager TM = null;
        public static PrefabCache PC = null;

        public static void SetUnityDataManagers_Post(DataManager __instance) {
            DM = __instance;
            TM = new Traverse(__instance).Property("TextureManager").GetValue<TextureManager>()
                                         .NullCheckError("Unable to locate TextureManager");

            PC = new Traverse(__instance).Property("GameObjectPool").GetValue<PrefabCache>()
                                         .NullCheckError("Unable to locate GameObjectPool");
        }

        public void Activate() {
            "SetUnityDataManagers".Post<DataManager>();

            "PooledInstantiate".Post<PrefabCache>();
            "GetSprite".Post<SpriteCache>();
            "GetAsset".Post<SVGCache>();
            "GetLoadedTexture".Pre<TextureManager>();
            "RequestTexture".Pre<TextureManager>();

            "Contains".Pre<TextureManager>();
        }

        public static bool Contains_Pre(string resourceId, ref bool __result) {
            __result = Locate(resourceId, RT.Texture2D) != null;

            return false;
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

        public static bool GetLoadedTexture_Pre(ref Texture2D __result, string resourceId, Dictionary<string, Texture2D> ___loadedTextures) {
            if (___loadedTextures.TryGetValue(resourceId, out var texture)) {
                __result = texture;
            } else {
                LogWarning($"Request Texture.Get {resourceId} but it does not exist");
                var entry = Locate(resourceId, RT.Texture2D);
                var res = __result;
                entry.LoadTexture2D()
                     .Done( tex => { Spam(() => $"Fallback[{resourceId}:Texture2D] Loaded");
                                     res = tex;
                                     ___loadedTextures.Add(resourceId, tex); }
                          , err => LogWarning(() => $"Fallback[{resourceId}:Texture2D] Failed"));
                __result = res;
            }

            return false;
        }

        public static void RequestTexture_Pre(TextureManager __instance, string resourceId, TextureLoaded loadedCallback, ref LoadFailed error, Dictionary<string, Texture2D> ___loadedTextures) {
            var olderr = error;
            error = (msg) => { var tex = __instance.GetLoadedTexture(resourceId);
                               if (tex == null) olderr(msg);
                               else loadedCallback(tex); };
        }

        public static void GetSprite_Post(ref Sprite __result, string id, Dictionary<string,Sprite> ___cache) {
            if (__result == null) {
                Sprite MkSprite(Texture2D t)
                    => Sprite.Create(t, new UnityEngine.Rect(0f, 0f, (float)t.width, (float)t.height), new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect, Vector4.zero);
                    
                var tex = TM.GetLoadedTexture(id);
                Sprite sprite = null;
                if (tex != null) {
                    sprite = MkSprite(tex);
                }
                // Need some handling for actual sprites here.
                if (sprite != null) { Spam(() => $"Fallback[{id}:Sprite] Loaded");
                                      ___cache[id] = sprite; }
                else LogWarning(() => $"Fallback[{id}:Sprite] Failed. Why? No backing texture entry");
                __result = sprite;
            }
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

        public static void PooledInstantiate_Post(ref GameObject __result, string id
                                                 , Vector3? position = null, Quaternion? rotation = null, Transform parent = null) {
            if (__result == null) {
                var entry = Locate(id);
                var res = __result;
                entry.LoadPrefab()
                     .Then(go => go.NullThrowError("Recieved empty prefab"))
                     .Done(go => { Spam(() => $"Fallback[{id}:Prefab] Loaded {go != null}");
                                   PC.AddPrefabToPool(id, go);
                                   res = PC.PooledInstantiate(id, position, rotation, parent);
                                 }
                          , exn => LogWarning(() => $"Fallback[{id}:Prefab] Failed. Why? {exn.Message}"));
                __result = res;
            }
        }
    }
}
