using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using Harmony;
using BattleTech;
using BattleTech.Assetbundles;
using BattleTech.Data;
using static BattletechPerformanceFix.Extensions;
using RT = BattleTech.BattleTechResourceType;

namespace BattletechPerformanceFix
{
    class OverridePrefabCache : Feature
    {
        public static bool DataManager_PooledInstantiate(DataManager __instance, ref GameObject __result, string id, RT resourceType, Vector3? position = null, Quaternion? rotation = null, Transform parent = null) {
            LogDebug($"DM PooledInstantiate: {id}");
            var gop = new Traverse(__instance).Property("GameObjectPool").GetValue<PrefabCache>();
            __result = gop.PooledInstantiate(id, position, rotation, parent);
            return false;
        }

        public static bool PrefabCache_PooledInstantiate(ref GameObject __result, string id, Vector3? position = null, Quaternion? rotation = null, Transform parent = null) {
            LogDebug($"PC PooledInstantiate: {id}");

            GameObject Load() {
                var entry = lookupId(id);
                if (entry.IsResourcesAsset) {
                    LogDebug($"Loading {id} from disk");
                    return Resources.Load(entry.ResourcesLoadPath).SafeCast<GameObject>();
                } else if (entry.IsAssetBundled) {
                    LogDebug($"Loading {id} from bundle {entry.AssetBundleName}");
                    return LoadAssetFromBundle(id, entry.AssetBundleName).SafeCast<GameObject>();
                } else {
                    LogError("Don't know how to load {entry.id}");
                    return null;
                }
            }
            
            var prefab = Prefabs.GetWithDefault(id, Load);
            if (prefab == null) LogError($"A prefab({id}) was nulled, but I was never told");
            else LogDebug($"Found prefab for {id}");
            var ptransform = prefab.transform;
            var obj = GameObject.Instantiate( prefab
                                            , position == null ? ptransform.position : position.Value
                                            , rotation == null ? ptransform.rotation : rotation.Value);

            obj.transform.SetParent(parent);
            if (parent == null) SceneManager.MoveGameObjectToScene(obj, SceneManager.GetActiveScene());
            __result = obj;

            return false;
        }

        public static bool PrefabCache_IsPrefabInPool(string id, ref bool __result) {
            LogDebug($"PC IsPrefabInPool: {id}? Sure, why not.");
            __result = true;

            return false;
        }

        public static string AssetBundleNameToFilePath(string assetBundleName)
            => new Traverse(typeof(AssetBundleManager)).Method("AssetBundleNameToFilepath", assetBundleName).GetValue<string>();

        public static IEnumerable<string> GetBundleDependencies(string bundleName) {
            if (manifest == null) { manifest = new Traverse(bundleManager.NullCheckError("No bundle manager")).Field("manifest").GetValue<AssetBundleManifest>();
                                    manifest.NullCheckError("Unable to find asset bundle manifest"); }
            return manifest.GetAllDependencies(bundleName);
        }

        public static Dictionary<string, AssetBundle> Bundles = new Dictionary<string, AssetBundle>();
        public static AssetBundle LoadBundle(string bundleName) {
            if (Bundles.TryGetValue(bundleName, out var bundle)) return bundle;
            else { LogDebug($"Loading bundle {bundleName}");
                   GetBundleDependencies(bundleName).ForEach(depName => LoadBundle(depName));
                   var path = AssetBundleNameToFilePath(bundleName);
                   var newBundle =  AssetBundle.LoadFromFile(path).NullCheckError($"Missing bundle {bundleName} from {path}");
                   Bundles[bundleName] = newBundle;
                   return newBundle; }
        }

        public static UnityEngine.Object LoadAssetFromBundle(string assetName, string bundleName)
            => LoadBundle(bundleName)?.LoadAsset(assetName).NullCheckError($"Unable to load {assetName} from bundle {bundleName}");

        public static VersionManifestEntry lookupId(string id) {
            if (locator == null)
                locator = new Traverse(typeof(BattleTechResourceLoader)).Field("resourceLocator").GetValue<BattleTechResourceLocator>();
            locator.NullCheckError("Unable to find resource locator");
            // FIXME: Terribly slow
            return locator.AllEntries().Single(e => e.Id == id);
        }

        public static void DataManager_SetUnityDataManagers(DataManager __instance, AssetBundleManager assetBundleManager) {
            LogDebug("DM SetUnityDataManagers");
            dataManager = __instance;
            bundleManager = assetBundleManager;
        }

        public static DataManager dataManager;
        public static AssetBundleManager bundleManager;
        public static AssetBundleManifest manifest;
        public static BattleTechResourceLocator locator;
        public void Activate() {
            var self = typeof(OverridePrefabCache);
            Main.harmony.Patch( AccessTools.Method(typeof(BattleTech.Data.DataManager), "SetUnityDataManagers")
                              , new HarmonyMethod(AccessTools.Method(self, nameof(DataManager_SetUnityDataManagers))));

            Main.harmony.Patch( AccessTools.Method(typeof(BattleTech.Data.DataManager), "PooledInstantiate")
                              , new HarmonyMethod(AccessTools.Method(self, nameof(DataManager_PooledInstantiate))));
            Main.harmony.Patch( AccessTools.Method(typeof(PrefabCache), "IsPrefabInPool")
                              , new HarmonyMethod(AccessTools.Method(self, nameof(PrefabCache_IsPrefabInPool))));
            Main.harmony.Patch( AccessTools.Method(typeof(PrefabCache), "PooledInstantiate")
                              , new HarmonyMethod(AccessTools.Method(self, nameof(PrefabCache_PooledInstantiate))));

        }

        public static Dictionary<string,GameObject> Prefabs = new Dictionary<string,GameObject>();
    }
}
