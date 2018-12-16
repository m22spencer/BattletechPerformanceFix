using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using HBS.Data;
using BattleTech;
using BattleTech.Data;
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
            var entry = C.Locate(id) != null;
            Spam(() => $"Has on {id}? {entry}");
            return entry;
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
            if (!(__instance is DictionaryStore<ColorSwatch>)) LogWarning($"Exist_CS on {__instance.GetType().FullName}");
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
            "GetLoadedTexture".Pre<TextureManager>();
            "GetSprite".Post<SpriteCache>();
        }

        public static bool GetLoadedTexture(ref Texture2D __result, string resourceId, Dictionary<string,Texture2D> ___loadedTextures) {
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

        public static void GetSprite(ref Sprite __result, string id) {
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
            if (entry == null) throw new Exception("MapSync: null entry");
            if (entry.IsAssetBundled && byBundle != null) throw new Exception("Cannot load from bundles");
            if (entry.IsResourcesAsset && byResource != null) return byResource(Resources.Load<R>(entry.ResourcesLoadPath));
            if (entry.IsFileAsset && byFile != null) return byFile(File.ReadAllBytes(entry.FilePath));
            throw new Exception("Ran out of ways to load asset");
        }
    }
}
