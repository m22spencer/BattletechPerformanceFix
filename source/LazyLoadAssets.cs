using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SVGImporter;

using HBS.Data;
using BattleTech;
using BattleTech.Data;
using BattleTech.Assetbundles;
using RT = BattleTech.BattleTechResourceType;
using BattleTech.Rendering.MechCustomization;

using UnityEngine;
using Harmony;
using C = BattletechPerformanceFix.CollectSingletons;
using static BattletechPerformanceFix.Extensions;

namespace BattletechPerformanceFix
{
    class LazyLoadAssets : Feature
    {
        public void Activate()
        {
            ActivateExistChecks();
            ActivateGuards();
            ActivateFetches();
        }

        static bool Has(string id) {
            var entry = C.Locate(id);
            var yes = entry != null;
            Spam(() => $"Has on {id}:{entry?.Type}? {yes}");
            return yes;
        }

        /* Every check in the game that looks for a UnityEngine.Object based
         * must return existance based on the manifest
         */
        void ActivateExistChecks() {
            "Contains".Post<TextureManager>(nameof(RID));
            "Contains".Post<SpriteCache>(nameof(ID));
            "Contains".Post<SVGCache>(nameof(ID));
            "IsPrefabInPool".Post<PrefabCache>(nameof(ID));

            "Exists".Post<DataManager>(nameof(DM_Exists));
            "Exists".Post<DictionaryStore<ColorSwatch>>(nameof(Exists_CS));
        }
        public static void RID(string resourceId, ref bool __result) => __result = __result || Has(resourceId);
        public static void ID(string id, ref bool __result) => __result = __result || Has(id);

        public static void DM_Exists(ref bool __result, RT resourceType, string id) {
            if (!__result && resourceType == RT.Prefab) __result = Has(id);
        }

        // FIXME: This recieves all DictionaryStore types, a transpile is likely necessary but increases complexity
        public static void Exists_CS(object __instance, ref bool __result, string id) {
            if (!(__instance is DictionaryStore<ColorSwatch>)) {} //LogWarning($"Exist_CS on {__instance.GetType().FullName}");
            else __result = __result || Has(id);
        }

        // We don't want DataManager loading UnityEngine.Object types.
        void ActivateGuards() {
            "RequestResource_Internal".Pre<DataManager>();
        }

        public static List<RT> ToGuard = List(RT.Texture2D, RT.Sprite, RT.SVGAsset, RT.Prefab, RT.UIModulePrefabs);
        public static bool RequestResource_Internal_Pre(ref bool __result, RT resourceType, string identifier) {
            if (ToGuard.Contains(resourceType)) {
                Spam(() => $"RRI[Drop] {identifier}");
                return false;
            }
            return true;
        }

        public static Texture2D TextureFromBytes(byte[] bytes)
            => new Traverse(C.TM).Method("TextureFromBytes", bytes).GetValue<Texture2D>().NullThrowError($"TextureFromBytes returned null");
        /* Anywhere the game requests a UnityEngine.Object based item, we must load it and provide it.
         * Note: This will cause stutter without a smarter caching system. The goal is to be correct, not fast.
         */
        void ActivateFetches() {
            "GetAsset".Post<SVGCache>();
            "RequestTexture".Pre<TextureManager>();
            "GetLoadedTexture".Pre<TextureManager>();
            "GetSprite".Post<SpriteCache>();
            "PooledInstantiate".Post<PrefabCache>();
            "AddToPoolList".Pre<DataManager>(_ => { Spam(() => "Dropping pool request");
                                                    return false; });
            "Get".Pre<DictionaryStore<ColorSwatch>>(nameof(Get_CS));
        }

        public static void GetAsset_Post(ref SVGAsset __result, string id) {
            if (__result == null) {
                var entry = C.Locate(id, RT.SVGAsset);
                if (entry != null) {
                    __result = entry.Load<SVGAsset, SVGAsset>( null, Identity, Identity);
                }
                if (__result != null)
                    C.SVC.AddSVGAsset(id, __result);
            }
        }

        public static bool RequestTexture_Pre(string resourceId, TextureLoaded loadedCallback, LoadFailed error) {
            var tex = C.TM.GetLoadedTexture(resourceId);
            if (tex == null) error("No texture");
            else loadedCallback(tex);

            return false;
        }

        public static bool GetLoadedTexture_Pre(ref Texture2D __result, string resourceId, Dictionary<string,Texture2D> ___loadedTextures) {
            if (___loadedTextures.TryGetValue(resourceId, out var texture)) __result = texture;
            else { var entry = C.Locate(resourceId, RT.Texture2D);
                   __result = entry.Load<Texture2D,Texture2D>( TextureFromBytes
                                                             , Identity
                                                             , Identity);
                   if (__result != null) {
                       Spam(() => $"GetLoadedTexture[success] for {resourceId}");
                       C.TM.InsertTexture(resourceId, __result);
                   }
            }
            return false;
        }

        public static void GetSprite_Post(ref Sprite __result, string id) {
            Sprite MkSprite(Texture2D t)
                => Sprite.Create(t, new UnityEngine.Rect(0f, 0f, (float)t.width, (float)t.height), new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect, Vector4.zero);

            if (__result == null) {
                var entry = C.Locate(id);
                if (entry != null) {
                    var type = entry.Type.ToRT();
                    if (type == RT.Texture2D) {
                        var tex = C.TM.GetLoadedTexture(id);
                        __result = MkSprite(tex);
                    } else if (type == RT.Sprite) {
                        __result = entry.Load<Sprite,Texture2D>( bytes => MkSprite(TextureFromBytes(bytes))
                                                               , MkSprite
                                                               , MkSprite);
                    }
                    // else it's a SVGItem/null and the game wants us to not handle it
                }
                if (__result != null) {
                    Spam(() => $"GetSprite[success] for {id}");
                    C.SC.AddSprite(id, __result);
                }
            }
        }

        public static void PooledInstantiate_Post( ref GameObject __result, string id, Vector3? position = null
                                                 , Quaternion? rotation = null, Transform parent = null) {
            if (__result == null) {
                Spam(() => $"Need prefab {id}");
                var entry = C.Locate(id);
                if (entry != null) {
                    var prefab = entry.Load<GameObject,GameObject>( null
                                                                  , Identity
                                                                  , Identity);
                    if (prefab != null) {
                        Spam(() => $"GetPrefab[success] for {id}");
                        C.PC.AddPrefabToPool(id, prefab);
                        __result = C.PC.PooledInstantiate(id, position, rotation, parent);
                    }
                }
            }
        }

        public static bool Get_CS(object __instance, ref object __result, string id, Dictionary<string,object> ___items) {
            if (!(__instance is DictionaryStore<ColorSwatch>)) {} //LogWarning($"Get_CS on {__instance.GetType().FullName}");
            else {
                if (___items.TryGetValue(id, out var cs)) __result = cs;
                else { var entry = C.Locate(id);
                       __result = entry.Load<ColorSwatch,ColorSwatch>( null
                                                                     , Identity
                                                                     , Identity);
                       if (__result != null) {
                           ___items[id] = __result;
                       }
                }
                return false;
            }
            return true;
        }
    }

    public delegate void AcceptReject<T>(Action<T> accept, Action<Exception> reject);
    static class LoadExtensions {
        public static T Load<T>( this VersionManifestEntry entry
                               , Func<byte[],T> byFile
                               , AcceptReject<T> recover = null)
            => entry.Load<T,UnityEngine.Object>( byFile, null, null, recover);

        public static T Load<T,R>( this VersionManifestEntry entry
                                 , Func<byte[],T> byFile
                                 , Func<R,T> byResource 
                                 , Func<R,T> byBundle
                                 , AcceptReject<T> recover = null) where R : UnityEngine.Object {
            T Wrap() {
                if (entry == null) throw new Exception("MapSync: null entry");
                if (entry.IsFileAsset && byFile != null) return byFile(File.ReadAllBytes(entry.FilePath));
                if (entry.IsResourcesAsset && byResource != null) return byResource(Resources.Load<R>(entry.ResourcesLoadPath));
                if (entry.IsAssetBundled && byBundle != null) return byBundle(entry.LoadFromBundle<R>());
                throw new Exception("Ran out of ways to load asset {entry.Dump(false)}");
            }
            return Trap(Wrap);
        }

        // TODO: Check if this also needs to force dependencies
        public static AssetBundle GetOrLoadBundle(string bundleName) {
            if (C.BM.IsBundleLoaded(bundleName)) return C.BM.GetLoadedAssetBundle(bundleName);

            // BundleManager did not have the bundle, or it is being loaded.
            C.BM.RequestBundle(bundleName, b => {});  // Request it so that an Async op is created

            // Get around all the private method crap
            var l = new Traverse(C.BM).Field("loadOperations").GetValue<Dictionary<string, AssetBundleLoadOperation>>();
            if (l == null) LogError("Couldn't get loadOperation field");
            var op = l[bundleName].NullThrowError($"No load operation for {bundleName}");
            var req = new Traverse(op).Field("loadRequest").GetValue<AssetBundleCreateRequest>().NullThrowError($"No bundle create asyncop for {bundleName}");
            // Force immediate load of bundle by accessing the getter on the async op `req`
            var bundle = req.assetBundle.NullThrowError("Forced the bundle, but got nothing");
            LogDebug($"Was able to get bundle {bundleName} by forcing the load operation");
            return bundle;
        }

        public static T LoadFromBundle<T>( this VersionManifestEntry entry) where T : UnityEngine.Object {
            var bundle = GetOrLoadBundle(entry.AssetBundleName);
            return bundle.LoadAsset<T>(entry.Id);
        }
    }
}
