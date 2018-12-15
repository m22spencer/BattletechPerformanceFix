using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RSG;
using Harmony;
using BattleTech;
using UnityEngine;
using BattleTech.Data;
using BattleTech.Assetbundles;
using static BattletechPerformanceFix.Extensions;

// NOTE: Bundles & Resources that are async loaded seem to be force-able

namespace BattletechPerformanceFix.AlternativeLoading
{
    public delegate void AcceptReject<T>(Action<T> accept, Action<Exception> reject);
    public static class Load
    {
        public static IPromise<T> MapSync<T,R>( this VersionManifestEntry entry
                                              , Func<byte[],T> byFile
                                              , Func<R,T> byResource 
                                              , Func<R,T> byBundle
                                              , AcceptReject<T> recover = null) where R : UnityEngine.Object {
            try { if (entry == null) throw new Exception("MapSync: null entry");
                  if (entry.IsAssetBundled && byBundle != null) return LoadAssetFromBundle<R>(entry.Id, entry.AssetBundleName).Then(byBundle);
                  if (entry.IsResourcesAsset && byResource != null) return Promise<T>.Resolved(byResource(Resources.Load<R>(entry.ResourcesLoadPath)));
                  if (entry.IsFileAsset && byFile != null) return Promise<T>.Resolved(byFile(File.ReadAllBytes(entry.FilePath))); }
            catch(Exception e) { return Promise<T>.Rejected(e); }
            return Promise<T>.Rejected(new Exception($"Missing method to load {entry.Id}"));
        }

        public static string AssetBundleNameToFilePath(string assetBundleName)
            => new Traverse(typeof(AssetBundleManager)).Method("AssetBundleNameToFilepath", assetBundleName).GetValue<string>();

        public static AssetBundleManifest manifest;
        public static IEnumerable<string> GetBundleDependencies(string bundleName) {
            if (manifest == null) { manifest = new Traverse(CollectSingletons.BM.NullCheckError("No bundle manager")).Field("manifest").GetValue<AssetBundleManifest>();
                                    manifest.NullCheckError("Unable to find asset bundle manifest"); }
            return manifest.GetAllDependencies(bundleName);
        }

        public static Dictionary<string, AssetBundle> Bundles = new Dictionary<string, AssetBundle>();
        public static List<AssetBundle> LRUBundles = new List<AssetBundle>();
        public static AssetBundle LoadBundle(string bundleName) {
            AssetBundle Load() {
                LogDebug($"Loading bundle {bundleName}");
                GetBundleDependencies(bundleName).ForEach(depName => LoadBundle(depName));
                var path = AssetBundleNameToFilePath(bundleName);
                var newBundle =  Measure( (b,t) => Log($"LoadBundle {b}b in {t.TotalMilliseconds}ms")
                                        , () => AssetBundle.LoadFromFile(path)).NullCheckError($"Missing bundle {bundleName} from {path}");
                Bundles[bundleName] = newBundle;
                return newBundle;
            }


            var bundle = Bundles.GetWithDefault(bundleName, Load);
            LRUBundles.Remove(bundle);
            LRUBundles.Add(bundle);
            return bundle;
        }

        public static IPromise<T> LoadAssetFromBundle<T>(string id, string bundleName) where T : UnityEngine.Object
            => TrapAsPromise<T>(() => LoadBundle(bundleName)?.LoadAsset<T>(id).NullThrowError($"Unable to load {id} from bundle {bundleName}"));

        public static IPromise<string> LoadText(this VersionManifestEntry entry) {
            return MapSync( entry
                          , System.Text.Encoding.UTF8.GetString
                          , (TextAsset t) => t.text
                          , (TextAsset t) => t.text);
        }

        public static IPromise<HBS.Util.IJsonTemplated> LoadJsonD(this VersionManifestEntry entry, Type type) {
            return MapSync( entry
                          , System.Text.Encoding.UTF8.GetString
                          , (TextAsset t) => t.text
                          , (TextAsset t) => t.text)
                .Then(str => { var inst = (HBS.Util.IJsonTemplated)Activator.CreateInstance(type).NullThrowError($"No Activator for {type.FullName}");
                               inst.FromJSON(str);
                               return inst; });
        }

        public static IPromise<T> LoadJson<T>(this VersionManifestEntry entry) where T : class, HBS.Util.IJsonTemplated {
            return LoadJsonD(entry, typeof(T)).Then(x => x.SafeCast<T>());
        }

        public static IPromise<Texture2D> LoadTexture2D(this VersionManifestEntry entry) {
            return MapSync<Texture2D,Texture2D>( entry
                                               , bytes => new Traverse(typeof(TextureManager)).Method("TextureFromBytes", bytes).GetValue<Texture2D>()
                                               , Identity
                                               , Identity);
        }

        public static IPromise<GameObject> LoadPrefab(this VersionManifestEntry entry) {
            return MapSync<GameObject,GameObject>( entry
                                                 , null
                                                 , Identity
                                                 , Identity);
        }
    }
}
