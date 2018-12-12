using System;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using O = System.Reflection.Emit.OpCodes;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using Harmony;
using BattleTech;
using BattleTech.UI;
using BattleTech.Assetbundles;
using BattleTech.Data;
using BattleTech.Rendering.MechCustomization;
using SVGImporter;
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

        public static bool NeedsManualAnnounce = false;
        public static bool DataManager_RequestResource_Internal(BattleTechResourceType resourceType, string identifier) {
            if (resourceType == RT.AssetBundle) {
                LogError($"DM RRI Asset bundle passed through, but we didn't handle it {identifier}");
            }

            if (resourceType == RT.Prefab || resourceType == RT.UIModulePrefabs) {
                // It's possible we'll have to resort to a dummy load item which simply immediately completes, but I'd rather not have the message overhead.
                LogDebug($"Blocking DM request of prefab {identifier}");
                NeedsManualAnnounce = true;
                return false;
            }
            return true;
        }

        public static void DataManager_ProcessRequests(DataManager __instance) {
            var dmlr = new Traverse(__instance).Field("foregroundRequestsList").GetValue<List<DataManager.DataManagerLoadRequest>>();
            var byid = string.Join(" ", dmlr.Select(lr => $"{lr.ResourceId}:{lr.ResourceType.ToString()}").Take(10).ToArray());
            Log($"ProcessRequests {NeedsManualAnnounce} :10waiting [{byid}] from {new StackTrace().ToString()}");
            if (NeedsManualAnnounce && new Traverse(__instance).Method("CheckRequestsComplete").GetValue<bool>()) {
                // This might cause double load messages, but we need to inform of a load complete in case we blocked all the requests
                LogDebug("DM queue requires manual announce");
                messageCenter.NullCheckError("MessageCenter is not set yet").PublishMessage(new DataManagerLoadCompleteMessage());
                NeedsManualAnnounce = false;
            }
        }

        public static bool DataManager_PrecachePrefab(string id) {
            LogDebug($"PrecachePrefab? No, no thank you.");
            return false;
        }

        public static bool PrefabCache_GetPooledPrefab(DataManager __instance, ref GameObject __result, string id) {
            LogDebug($"DM GetPooledPrefab: {id}");
            var gop = new Traverse(__instance).Property("GameObjectPool").GetValue<PrefabCache>();
            __result = gop.PooledInstantiate(id);  //FIXME: does this need to be moved to scene?
            return false;
        }

        public static bool PrefabCache_PooledInstantiate(ref GameObject __result, string id, Vector3? position = null, Quaternion? rotation = null, Transform parent = null) {
            LogDebug($"PC PooledInstantiate: {id}");

            GameObject Load() {
                var entry = lookupId(id);
                if (entry == null) {
                    LogError($"Failed to find asset: {id} in the manifest from {new StackTrace().ToString()}");
                    return null;
                } else if (entry.IsResourcesAsset) {
                    LogDebug($"Loading {id} from disk");
                    return Resources.Load(entry.ResourcesLoadPath).SafeCast<GameObject>();
                } else if (entry.IsAssetBundled) {
                    LogDebug($"Loading {id} from bundle {entry.AssetBundleName}");
                    return LoadAssetFromBundle(id, entry.AssetBundleName).SafeCast<GameObject>();
                } else {
                    LogError($"Don't know how to load {entry.Id}");
                    return null;
                }
            }
            
            var prefab = Prefabs.GetWithDefault(id, () => Load());
            if (prefab == null) { LogError($"A prefab({id}) was nulled, but I was never told");
                                  __result = null;
                                  return false; }
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
            LogDebug($"PC IsPrefabInPool: {id}? Sure, why not. from {new StackTrace().ToString()}");
            __result = true;

            return false;
        }

        public static bool DataManager_Exists(RT resourceType, string id, ref bool __result) {
            if (resourceType == RT.Prefab || resourceType == RT.UIModulePrefabs) {
                __result = true;
                return false;
            }
            if (resourceType == RT.AssetBundle) {
                LogDebug("DM E Exists check on asset bundle {id]");
                return Bundles.ContainsKey(id);
            }

            return true;
        }

        public static void RequestAssetProxy<T>(BattleTechResourceType type, string id, Action<object> lc) where T : UnityEngine.Object {
            Action<object> loadedCallback = (a) => WaitAFrame().Done(() => lc(a));
            var entry = Trap(() => lookupId(id, type));
            LogError($"ABM_RequestAsset: {id}:{type.ToString()} (manifest says {entry?.Type})");

            if (entry == null) {
                LogError($"Missing entry for {id}:{type.ToString()}");
                loadedCallback(null);
            } else if (entry.Type != type.ToString()) {
                LogError($"Invalid type fetch, entry is {entry.Type}, but asked for {type.ToString()}");
                loadedCallback(null);
            } else if (!entry.IsAssetBundled) {
                LogError($"ABM asset not bundled: {id}:{type.ToString()}");
                loadedCallback(null);
            } else {
                try {
                var asset = LoadAssetFromBundle(id, entry.AssetBundleName).NullCheckError($"No asset {id}:{type.ToString()}");
                LogDebug($"Recieved asset of type {asset.GetType()}");
                if (asset.GetType() == typeof(Texture2D) && type == RT.Sprite) {
                    // TODO: Not sure why this is happening. Compare against vanilla game. Sick of it, so taking the easy way out temporarily
                    LogError($"Mismatch, load asked for sprite but got texture. We're creating it");
                    var texture = (Texture2D)asset;
                    loadedCallback(Sprite.Create(texture, new UnityEngine.Rect(0f, 0f, (float)texture.width, (float)texture.height), new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect, Vector4.zero));
                } else {
                    loadedCallback(asset);
                }

                //Trap(() => loadedCallback(asset));
                } catch { loadedCallback(null); }
            }
        }

        public static IEnumerable<CodeInstruction> AssetBundleManager_RequestAsset(ILGenerator gen, MethodBase method, IEnumerable<CodeInstruction> _) {
            LogDebug($"Transpiling {method.ToString()}");
            var wcmeth = AccessTools.Method(typeof(OverridePrefabCache), "RequestAssetProxy", null, Array(method.GetGenericArguments()[0]));
            LogDebug($"Redirection to {wcmeth.ToString()}");
            //return Sequence( new CodeInstruction(O.Ret));
            return Sequence( new CodeInstruction(O.Ldarg_1) // type
                           , new CodeInstruction(O.Ldarg_2) // id
                           , new CodeInstruction(O.Ldarg_3) // loadedCallback
                           , new CodeInstruction(O.Call, wcmeth)
                           , new CodeInstruction(O.Ret));
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

        public static T LoadAssetFromBundle<T>(string assetName, string bundleName) where T : UnityEngine.Object
            => LoadBundle(bundleName)?.LoadAsset<T>(assetName).NullCheckError($"Unable to load {assetName} from bundle {bundleName}");
            

        public static UnityEngine.Object LoadAssetFromBundle(string assetName, string bundleName)
            => LoadBundle(bundleName)?.LoadAsset(assetName).NullCheckError($"Unable to load {assetName} from bundle {bundleName}");

        public static VersionManifestEntry lookupId(string id, RT? type = null) {
            if (locator == null)
                locator = new Traverse(typeof(BattleTechResourceLoader)).Field("resourceLocator").GetValue<BattleTechResourceLocator>();
            locator.NullCheckError("Unable to find resource locator");

            if (type == null) {
                var baseManifest = new Traverse(locator).Field("baseManifest").GetValue<Dictionary<RT, Dictionary<string, VersionManifestEntry>>>();
                baseManifest.NullCheckError("Unable to access baseManifest");  //FIXME: Also needs the contents packs manifest if user owns them
                new Traverse(locator).Method("UpdateTypedEntriesIfNeeded");
                // FIXME: Terribly slow
                return baseManifest.SelectMany(idents => idents.Value)
                                   .FirstOrDefault(e => e.Value.Id == id).Value;
            } else {
                return locator.EntryByID(id, type.Value, true);
            }
        }

        public static void DataManager_SetUnityDataManagers(DataManager __instance, AssetBundleManager assetBundleManager) {
            LogDebug("DM SetUnityDataManagers");
            dataManager = __instance;
            bundleManager = assetBundleManager;
            messageCenter = new Traverse(dataManager).Property("MessageCenter").GetValue<MessageCenter>().NullCheckError("Unable to find MessageCenter");
            messageCenter.AddSubscriber(MessageCenterMessageType.OnInitializeContractComplete, msg => LogDebug("OnInitalizeContractComplete message"));
        }

        
        public static MessageCenter messageCenter;
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
            Main.harmony.Patch( AccessTools.Method(typeof(BattleTech.Data.DataManager), "RequestResource_Internal")
                              , new HarmonyMethod(AccessTools.Method(self, nameof(DataManager_RequestResource_Internal))));
            Main.harmony.Patch( AccessTools.Method(typeof(BattleTech.Data.DataManager), "ProcessRequests")
                              , null
                              , new HarmonyMethod(AccessTools.Method(self, nameof(DataManager_ProcessRequests))));
            Main.harmony.Patch( AccessTools.Method(typeof(BattleTech.Data.DataManager), "PrecachePrefab")
                              , new HarmonyMethod(AccessTools.Method(self, nameof(DataManager_PrecachePrefab))));
            Main.harmony.Patch( AccessTools.Method(typeof(BattleTech.Data.DataManager), "Exists")
                              , new HarmonyMethod(AccessTools.Method(self, nameof(DataManager_Exists))));


            var pc = typeof(PrefabCache);
            Main.harmony.Patch( AccessTools.Method(pc, "IsPrefabInPool")
                              , new HarmonyMethod(AccessTools.Method(self, nameof(PrefabCache_IsPrefabInPool))));
            Main.harmony.Patch( AccessTools.Method(pc, "PooledInstantiate")
                              , new HarmonyMethod(AccessTools.Method(self, nameof(PrefabCache_PooledInstantiate))));
            Main.harmony.Patch( AccessTools.Method(pc, "GetPooledPrefab")
                              , new HarmonyMethod(AccessTools.Method(self, nameof(PrefabCache_GetPooledPrefab))));

            var abm = typeof(AssetBundleManager);
            var meth = AccessTools.Method(abm, "RequestAsset");
            meth.NullCheckError("RequestAsset not found");
            Log($"RequestAsset is hookable? {!meth.IsGenericMethodDefinition}");
            Sequence( typeof(ColorSwatch), typeof(TextAsset), typeof(Sprite)
                    , typeof(SVGAsset), typeof(Texture2D))
                .ForEach(inner => Main.harmony.Patch( meth.MakeGenericMethod(inner)
                                                    , null, null
                                                    , new HarmonyMethod(AccessTools.Method(self, nameof(AssetBundleManager_RequestAsset)))));

            Main.harmony.Patch( AccessTools.Method(abm, "IsBundleLoaded", Array(typeof(string))), Yes);
        }

        public static Dictionary<string,GameObject> Prefabs = new Dictionary<string,GameObject>();
    }
}
