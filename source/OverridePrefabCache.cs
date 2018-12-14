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
using BattleTech.Save;
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
        // Harmony Glue ----------------------
        // We need three functions:

        // Everything from here to the next section is just glue to hook this into the existing system.
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
            LogDebug($"ProcessRequests {NeedsManualAnnounce} :10waiting [{byid}]"); // from {new StackTrace().ToString()}");
            if (NeedsManualAnnounce && new Traverse(__instance).Method("CheckRequestsComplete").GetValue<bool>()) {
                // This might cause double load messages, but we need to inform of a load complete in case we blocked all the requests
                LogDebug("DM queue requires manual announce");
                messageCenter.NullCheckError("MessageCenter is not set yet").PublishMessage(new DataManagerLoadCompleteMessage());
                NeedsManualAnnounce = false;
            }
        }

        public static bool DataManager_PrecachePrefab(string id) {
            LogDebug($"PrecachePrefab? No, no thank you. {id}");
            return false;
        }

        public static bool PrefabCache_GetPooledPrefab(DataManager __instance, ref GameObject __result, string id) {
            LogDebug($"DM GetPooledPrefab: {id}");
            var gop = new Traverse(__instance).Property("GameObjectPool").GetValue<PrefabCache>();
            __result = gop.PooledInstantiate(id);  //FIXME: does this need to be moved to scene?
            return false;
        }

        public static bool PrefabCache_PoolGameObject(string id, GameObject gameObj) {
            LogDebug($"PC returned: {id}");
            Cache.Return(id, gameObj);
            return false;
        }

        public static bool PrefabCache_PooledInstantiate(ref GameObject __result, string id, Vector3? position = null, Quaternion? rotation = null, Transform parent = null) {
            LogDebug($"PC PooledInstantiate: {id}");
            __result = Cache.Create(id, position, rotation, parent);
            return false;
        }

        // For exists checks, we only care the the item is in the manifest.
        //    as long as the item *can* be loaded, say it's loadable.
        public static bool PrefabCache_IsPrefabInPool(string id, ref bool __result) {
            LogDebug($"PC IsPrefabInPool: {id}? Sure, why not. from {new StackTrace().ToString()}");
            __result = true;  // FIXME: Do a manifest check here, once we speed up the manifest implementation

            return false;
        }

        public static bool DataManager_Exists(RT resourceType, string id, ref bool __result) {
            if (resourceType == RT.Prefab || resourceType == RT.UIModulePrefabs) {
                __result = true;
                return false;
            }
            if (resourceType == RT.AssetBundle) {
                LogDebug("DM E Exists check on asset bundle {id]");
                return Bundles.ContainsKey(id); // FIXME: We should probably report whether the bundle can be loaded here, not whether it is loaded now.
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

        // Ran into some oddity with a prefix patch using object, restorted to a transpiler. FIXME: Consider revisiting the prefix patch.
        public static IEnumerable<CodeInstruction> AssetBundleManager_RequestAsset(ILGenerator gen, MethodBase method, IEnumerable<CodeInstruction> _) {
            LogDebug($"Transpiling {method.ToString()}");
            var wcmeth = AccessTools.Method(typeof(OverridePrefabCache), "RequestAssetProxy", null, Array(method.GetGenericArguments()[0]));
            LogDebug($"Redirection to {wcmeth.ToString()}");

            // return RequestAssetProxy<"T">(type, id, loadedCallback);
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
                foreach(var types in baseManifest) {
                    if (types.Value.TryGetValue(id, out var ent)) return ent;
                }
                return null;
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
            "PooledInstantiate".Pre<PrefabCache>(nameof(PrefabCache_PooledInstantiate));

            Main.harmony.Patch( AccessTools.Method(pc, "GetPooledPrefab")
                              , new HarmonyMethod(AccessTools.Method(self, nameof(PrefabCache_GetPooledPrefab))));
            Main.harmony.Patch( AccessTools.Method(pc, "PoolGameObject")
                              , new HarmonyMethod(AccessTools.Method(self, nameof(PrefabCache_PoolGameObject))));


            var abm = typeof(AssetBundleManager);
            var meth = AccessTools.Method(abm, "RequestAsset");
            meth.NullCheckError("RequestAsset not found");
            LogDebug($"RequestAsset is hookable? {!meth.IsGenericMethodDefinition}");
            Sequence( typeof(ColorSwatch), typeof(TextAsset), typeof(Sprite)
                    , typeof(SVGAsset), typeof(Texture2D))
                .ForEach(inner => Main.harmony.Patch( meth.MakeGenericMethod(inner)
                                                    , null, null
                                                    , new HarmonyMethod(AccessTools.Method(self, nameof(AssetBundleManager_RequestAsset)))));

            Main.harmony.Patch( AccessTools.Method(abm, "IsBundleLoaded", Array(typeof(string))), Yes);


            AccessTools.Method(typeof(SGRoomManager), "OnSimGameInitialize").Track();

            Log("DI fix");
            Main.harmony.Patch( AccessTools.Property(typeof(MechBayMechStorageWidget), "DragItem").GetGetMethod()
                              , new HarmonyMethod(AccessTools.Method(self, nameof(DragItem_get))));

            Log("SLCS fix");

            Main.harmony.Patch( AccessTools.Method(typeof(SGCmdCenterLanceConfigBG), "ShowLanceConfiguratorScreen")
                              , new HarmonyMethod(AccessTools.Method(self, nameof(ShowLanceConfiguratorScreen_Try))));

            AccessTools.Method(typeof(BattleTechResourceLocator), "RefreshTypedEntries").Instrument();
            AccessTools.Method(typeof(SGRoomManager), "OnSimGameInitialize").Instrument();
            AccessTools.Method(typeof(SkirmishUnitsAndLances), "UnMountMemoryStore").Instrument();
            AccessTools.Method(self, "lookupId").Instrument();

            
            AccessTools.Method(typeof(Briefing), "LevelLoaded").Track();
            AccessTools.Method(typeof(LevelLoadRequestListener), "Start").Track();
            AccessTools.Method(typeof(LevelLoadRequestListener), "OnRequestLevelLoad").Track();
            AccessTools.Method(typeof(LevelLoadRequestListener), "BundlesLoaded").Track();
            AccessTools.Method(typeof(LevelLoadRequestListener), "LevelLoaded").Track();



            "OnAddedToHierarchy".Pre<SimGameOptionsMenu>(_ => {
                    var db = Cache.CostDB;
                    var eload = db.OrderByDescending(kv => kv.Value.loadtime / kv.Value.loaded)
                                   .Take(10)
                                   .ToArray();
                    var epool = db.OrderByDescending(kv => kv.Value.alloctime / kv.Value.created)
                                  .Take(10)
                                  .ToArray();
                    var lpool = db.OrderByDescending(kv => kv.Value.leased)
                                  .Take(10)
                                  .ToArray();
                    var cpool = db.OrderByDescending(kv => kv.Value.created)
                                  .Take(10)
                                  .ToArray();

                    var lrubundles = LRUBundles.Take(5).Select(b => b.name);

                    string NiceList(KeyValuePair<string,Cost>[] lst) {
                        return string.Join("", lst.Select(l => "  " + l.Key + "-> " + l.Value.ToString()+ "\n").ToArray());
                    }

                    Log("COST-DB ---------------\nLoad Priority\n{0}\nPool Priority\n{1}\nLease Priority\n{2}\nCreate Priority\n{3}\nLRU Bundles\n  {4}\n"
                       , NiceList(eload)
                       , NiceList(epool)
                       , NiceList(lpool)
                       , NiceList(cpool)
                       , lrubundles.Dump(false));
                });

            BattletechPerformanceFix.AlternativeLoading.DMGlue.Initialize();
        }

        // Anything re-used from the pool with contain the state it was pooled with
        //   room-manger gets set in the wrong order, causing the UI to try something it's not supposed to
        public static void ShowLanceConfiguratorScreen_Try(ref SGRoomManager ___roomManager) {
            if (___roomManager != null && new Traverse(___roomManager).Field("CurrencyWidget").GetValue<SGCurrencyDisplay>() == null) {
                LogWarning("RoomManager is in a bad state, or old ref");
                ___roomManager = null;
            }
        }

        // Prevent an NPE on re-using this widget
        public static bool DragItem_get(IMechLabDropTarget ___parentDropTarget, ref IMechLabDraggableItem __result) {
            __result = ___parentDropTarget?.DragItem;
            return false;
        }

        public static void SGRM(SGRoomManager theManager) {
            LogDebug($"SGRM [{theManager?.GetHashCode()}] from {new StackTrace().ToString()}");
        }

        public static Dictionary<string,GameObject> Prefabs = new Dictionary<string,GameObject>();

        class Cache {
            public static Dictionary<string,Cost> CostDB = new Dictionary<string,Cost>();
            public static Dictionary<string,PrefabCache.RST> DefaultRootData = new Dictionary<string,PrefabCache.RST>();
            public static Dictionary<string,List<GameObject>> Pooled = new Dictionary<string,List<GameObject>>();
            public static GameObject Create(string id, Vector3? position = null, Quaternion? rotation = null, Transform parent = null) {
                var cost = CostDB.GetWithDefault(id, () => new Cost());
                GameObject Load() {
                    GameObject Go() {
                        var entry = lookupId(id);
                        if (entry == null) {
                            LogError($"Failed to find asset: {id} in the manifest from {new StackTrace().ToString()}");
                            return null;
                        } else if (entry.IsResourcesAsset) {
                            LogDebug($"Loading {id} from resources {entry.ResourcesLoadPath}");
                            return Resources.Load(entry.ResourcesLoadPath).SafeCast<GameObject>();
                        } else if (entry.IsAssetBundled) {
                            LogDebug($"Loading {id} from bundle {entry.AssetBundleName}");
                            return LoadAssetFromBundle(id, entry.AssetBundleName).SafeCast<GameObject>();
                        } else {
                            LogError($"Don't know how to load {entry.Id}");
                            return null;
                        }
                    }
                    Measure( (b,t) => { Log($"Load {b}b in {t.TotalMilliseconds}ms");
                                        cost.loadtime += t.TotalSeconds;
                                        cost.loadbytes += b;
                                        cost.loaded++; }
                           , Go);
                    var tmem = System.GC.GetTotalMemory(false);
                    var sw = Stopwatch.StartNew();
                    var item = Go();
                    var delta = System.GC.GetTotalMemory(false) - tmem;
                    return item;
                }

                GameObject RecordRST(GameObject go) {
                    if (go != null) DefaultRootData.GetWithDefault(id, () => new PrefabCache.RST(go));
                    return go;
                }

                GameObject FromPrefab() {
                    Log("FromPrefab");
                    var prefab = Prefabs.GetWithDefault(id, () => RecordRST(Load()));
                    return Measure((b,t) => { Log($"FromPrefab {b}b in {t.TotalMilliseconds}ms");
                                              cost.alloctime += t.TotalSeconds;
                                              cost.allocbytes += b;
                                              cost.created++; }
                                  , () => {
                            if (prefab == null) { LogError($"A prefab({id}) was nulled, but I was never told");
                                                  return null; }
                            else Log($"From Prefab for {id}");
                            var ptransform = prefab.transform;
                            return GameObject.Instantiate( prefab
                                                         , position == null ? ptransform.position : position.Value
                                                         , rotation == null ? ptransform.rotation : rotation.Value);
                        });
                }

                // FIXME: There is some `RST` handling in the existing pooling implementation
                //    looks like it's just for repairing some of the root information.
                GameObject FromPool() {
                    var held = Pooled.GetValueSafe(id);
                    if (held != null && held.Any()) {
                        return Measure((b,t) => { Log($"FromPool {b}b in {t.TotalMilliseconds}ms");
                                                }
                                      , () => {
                                LogDebug($"Leasing existing pooled gameobject for {id}");
                                var first = held[0];
                                if (first == null && first?.GetType() != null) {
                                    LogError($"A gameobject was destroyed while pooled for {id}");
                                    return null;
                                } else if (first == null) {
                                    LogError($"A gameobject is in the pool but null for {id}.");
                                    return null;
                                }
                                held.RemoveAt(0);
                                var t = first?.transform;
                                if (position != null) t.position = position.Value;
                                if (rotation != null) t.rotation = rotation.Value;
                                first.transform.SetParent(null);
                                if (DefaultRootData.TryGetValue(id, out var rd)) {
                                    LogDebug($"Apply RST to recover root data of {id}");
                                    rd.Apply(first);
                                }
                                first.SetActive(true);
                                Log($"FromPool {id}");
                                return first;
                            });
                    } else return null;
                }

                var obj = FromPool() ?? FromPrefab();
                obj.IsDestroyedError($"Both Pool and Prefab have been destroyed!")
                   .NullCheckError($"No prefab or pooled item for {id}");

                //Game tends to request items that don't exist, or can't be fetched. Expects null
                if (obj == null)
                    return obj;

                cost.leased++;

                obj.transform.SetParent(null);
                var pscene = parent?.gameObject?.scene;
                var lscene = obj.scene;
                if (pscene?.name != lscene.name && pscene?.name != "DontDestroyOnLoad") {
                    SceneManager.MoveGameObjectToScene(obj, parent?.gameObject?.scene ?? SceneManager.GetActiveScene());
                }
                obj.transform.SetParent(parent);
                return obj;
            }

            // Use id temporarily just to test things
            public static void Return(string id, GameObject obj) {
                var cost = CostDB.GetWithDefault(id, () => new Cost());
                cost.returned++;

                var held = Pooled.GetWithDefault(id, () => new List<GameObject>());
                obj.SetActive(false);
                obj.transform.SetParent(null);
                GameObject.DontDestroyOnLoad(obj);
                held.Add(obj);
            }

            // create: id, _ -> GameObject  ;; This ensures the id is loaded, and either creates or returns an existing item in the pool
            // hint  : tag                  ;; Mark a phase, using this we can try to be more intelligent about what & how many things are pooled
            // return: GameObject -> ()     ;; Return an item to the pool. Sometimes the game will steal: In this case, some hooks around scene loading or asset unloading can steal it back.

        }

        class Cost {
            public double loaded     = 0;
            public double created    = 0;
            public double leased     = 0;
            public double returned   = 0;
            public double loadbytes  = 0;
            public double loadtime   = 0;
            public double allocbytes = 0;
            public double alloctime  = 0;
            public Cost() {}

            public string ToString() 
                => $":loaded {loaded} :created {created} :leased {leased} :returned {returned} :loadbytes {loadbytes} :loadtime {loadtime} :allocbytes {allocbytes} :alloctime {alloctime}";
        }
    }
}
