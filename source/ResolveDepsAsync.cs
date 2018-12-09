using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BattleTech.Data;
using BattleTech;
using BattleTech.Assetbundles;
using Harmony;
using Harmony.ILCopying;
using UnityEngine;
using System.IO;
using SVGImporter;
using HBS.Data;
using HBS.Text;
using HBS.Util;
using RSG;
using System.Diagnostics;
using System.Reflection;
using BattleTech;
using BattleTech.UI;
using BattleTech.Portraits;
using BattleTech.Framework;
using UnityEngine;
using RT = BattleTech.BattleTechResourceType;
using BattleTech.Rendering.MechCustomization;
using static BattletechPerformanceFix.Control;
using System.Reflection.Emit;
using O = System.Reflection.Emit.OpCodes;

namespace BattletechPerformanceFix
{
    /* Just to set the load request for the dependency verification */
    class DummyLoadRequest : DataManager.ResourceLoadRequest<object>
    {
        public DummyLoadRequest(DataManager dataManager, string id = "dummy_load_request_for_weight") : base(dataManager, RT.AbilityDef, id, 10000, null) { }
        public override bool AlreadyLoaded { get => true; }

        public void Complete()
        {
            LogDebug("Complete dummy {0}", Enum.GetName(typeof(DataManager.DataManagerLoadRequest.RequestState), this.State));
            this.State = DataManager.DataManagerLoadRequest.RequestState.Complete;
        }

        public override void Load()
        {
            LogDebug("Load dummy");
        }

        public override void NotifyLoadComplete()
        {
            LogDebug("NotifyLoadComplete dummy");
        }

        public override void SendLoadCompleteMessage()
        {
            LogDebug("SendLoadCompleteMessage dummy");
        }

        public override void OnLoaded()
        {
            LogDebug("OnLoaded dummy");
        }
    }

    static class ResolveExt
    {
        public static IPromise<object> PromiseObject<T>(this IPromise<T> p)
            => p.Then(x => (object)x);

        public static IPromise Resolve(this DataManager.ILoadDependencies cls, DataManager dm)
        {
            LogDebug("Attempt to resolve {0}", cls.GetType());
            var prom = new Promise();
            if (dm == null)
                LogError("DM is null");
            cls.DataManager = dm;
            cls.RequestDependencies(cls.DataManager, prom.Resolve, new DummyLoadRequest(dm));
            return prom;
        }
        
        public static IPromise OfString(this string str, RT type)
            => string.IsNullOrEmpty(str) ? Promise.Resolved() : ResolveDepsAsync.Load(type, str);


        public static string AsString(this RT type)
            => Enum.GetName(typeof(RT), type);

        public static RT ToRT(this string s)
            => (RT)Enum.Parse(typeof(RT), s);

        public static T ToRTMap<T>(this string s, Func<RT,T> succ, Func<T> fail)
        {
            try { return succ(s.ToRT()); } catch { return fail(); }
        }

        public static void ToRTMap(this string s, Action<RT> succ, Action fail)
        {
            try { succ(s.ToRT()); } catch { fail(); }
        }
    }

    class ResolveDepsAsync : Feature
    {
        public static bool WantVanilla = false;
        public static bool WantVerify = false;
        public void Activate()
        {
            var wantTracking = true;

            var t = typeof(ResolveDepsAsync);
            harmony.Patch(AccessTools.Method(typeof(BattleTechResourceLocator), "RefreshTypedEntries"), null, new HarmonyMethod(AccessTools.Method(t, nameof(IntegrityCheck))));

            if (WantVerify || wantTracking)
            {
                Log("Tracking is ON");
                var pre = new HarmonyMethod(AccessTools.Method(t, "TrackPre"));
                HarmonyMethod post = null; // new HarmonyMethod(AccessTools.Method(t, "TrackPost"));
                Assembly.GetAssembly(typeof(HeraldryDef))
                    .GetTypes()
                    .Where(ty => ty.GetInterface(typeof(DataManager.ILoadDependencies).FullName) != null)
                    .ForEach(ild =>
                    {
                        harmony.Patch(AccessTools.Method(ild, "CheckDependenciesAfterLoad"), pre, post);
                        harmony.Patch(AccessTools.Method(ild, "DependenciesLoaded"), pre, post);
                        //harmony.Patch(AccessTools.Method(ild, "RequestDependencies"), pre, post);

                    });

                //harmony.Patch(AccessTools.Method(typeof(BattleTech.Save.SkirmishUnitsAndLances), "UnMountMemoryStore"), pre, post);


                var mstore = AccessTools.Method(typeof(BattleTech.Save.SkirmishUnitsAndLances), "UnMountMemoryStore");
                harmony.Patch(mstore, new HarmonyMethod(AccessTools.Method(t, "UnmountMemoryStore_Pre")), new HarmonyMethod(AccessTools.Method(t, "UnmountMemoryStore_Post")));



                Assembly.GetAssembly(typeof(DataManager))
                    .GetTypes()
                    .Where(ty => ty.FullName.EndsWith("SpriteLoadRequest"))
                    .ForEach(ty => harmony.Patch(AccessTools.Method(ty, "Load"), pre, post));

                AccessTools.GetDeclaredMethods(typeof(Resources))
                    .Where(meth => meth.Name == "Load" && !meth.IsGenericMethod && !meth.IsGenericMethodDefinition && meth.GetMethodBody() != null)
                    .ForEach(meth => harmony.Patch(meth, new HarmonyMethod(AccessTools.Method(t, nameof(FileHookPath)))));

                AccessTools.GetDeclaredMethods(typeof(AssetBundle))
                    .Where(meth => meth.Name == "LoadAsset" && !meth.IsGenericMethod && !meth.IsGenericMethodDefinition && meth.GetMethodBody() != null)
                    .ForEach(meth => harmony.Patch(meth, new HarmonyMethod(AccessTools.Method(t, nameof(FileHookName)))));
                harmony.Patch(AccessTools.Method(typeof(DataLoader), "CallHandler"), new HarmonyMethod(AccessTools.Method(t, nameof(FileHookPath))));
                // Also check GenerateWebRequest GenerateWebRequest


                harmony.Patch(AccessTools.Method(typeof(BattleTech.UI.SimGameOptionsMenu), "OnAddedToHierarchy"), new HarmonyMethod(AccessTools.Method(t, "Summary")));
                harmony.Patch(AccessTools.Method(typeof(DataManager), "RequestResource_Internal"), new HarmonyMethod(AccessTools.Method(t, nameof(TrackRequestResource))));
                
                harmony.Patch(AccessTools.Method(typeof(DataManager), "Update"), new HarmonyMethod(AccessTools.Method(t, nameof(DataManager_Update))));
            }


            Assembly.GetAssembly(typeof(HeraldryDef))
                    .GetTypes()
                    .Where(ty => ty.GetInterface(typeof(DataManager.ILoadDependencies).FullName) != null)
                    .ForEach(ild =>
                    {
                        var methx = AccessTools.Method(ild, "DependenciesLoaded");
                        if (methx == null)
                            LogError("DependenciesLoaded is null");
                        Log("Hooking {0}.DependenciesLoaded", ild.FullName);
                        Trap(() => harmony.Patch(methx, null, null, new HarmonyMethod(typeof(InterceptAssetChecks), nameof(InterceptAssetChecks.DependenciesLoaded))));
                        if (!harmony.GetPatchInfo(methx).Transpilers.Any())
                            LogError("Transpiler failed to hook");
                    });

            

            /*
            harmony.Patch(AccessTools.Method(typeof(HBS.Threading.SimpleThreadPool), "Worker"), new HarmonyMethod(drop));
            harmony.Patch(AccessTools.Method(typeof(BattleTech.UI.AVPVideoPlayer), "ForcePlayVideo"), new HarmonyMethod(drop));
            harmony.Patch(AccessTools.Method(typeof(BattleTech.UI.AVPVideoPlayer), "PlayVideo"), new HarmonyMethod(drop));
            harmony.Patch(AccessTools.Method(typeof(BattleTech.UI.SGVideoPlayer), "PlayVideo"), new HarmonyMethod(drop));            
            */
            harmony.Patch(AccessTools.Method(typeof(BattleTech.IntroCinematicLauncher), "OnAddedToHierarchy"), new HarmonyMethod(AccessTools.Method(t, "IntroAdded")));

            harmony.Patch(AccessTools.Method(typeof(DataManager), "ProcessRequests"), new HarmonyMethod(AccessTools.Method(t, nameof(ProcessRequests))));
            harmony.Patch(AccessTools.Method(typeof(DataManager), "RequestResource_Internal"), new HarmonyMethod(AccessTools.Method(t, nameof(RequestResources_Internal2))));
            harmony.Patch(AccessTools.Method(typeof(DataManager), "Update"), new HarmonyMethod(AccessTools.Method(t, nameof(Update))));


            var ttt = typeof(DictionaryStore<>).MakeGenericType(typeof(object));
            harmony.Patch(ttt.GetMethod("Exists", AccessTools.all),  new HarmonyMethod(AccessTools.Method(t, nameof(ResolveDepsAsync.DictionaryStore_Exists))));
            harmony.Patch(ttt.GetMethod("Get", AccessTools.all),  new HarmonyMethod(AccessTools.Method(t, nameof(ResolveDepsAsync.DictionaryStore_Exists))));
            harmony.Patch(ttt.GetProperty("Keys", AccessTools.all).GetGetMethod(),  new HarmonyMethod(AccessTools.Method(t, nameof(ResolveDepsAsync.DictionaryStore_Keys))));

            harmony.Patch(AccessTools.Method(typeof(SVGCache), "Contains"), new HarmonyMethod(AccessTools.Method(t, nameof(SVGCache_Contains))));
            harmony.Patch(AccessTools.Method(typeof(SVGCache), "GetAsset"), new HarmonyMethod(AccessTools.Method(t, nameof(SVGCache_Contains))));
            harmony.Patch(AccessTools.Method(typeof(SpriteCache), "Contains"), new HarmonyMethod(AccessTools.Method(t, nameof(SpriteCache_Contains))));
            harmony.Patch(AccessTools.Method(typeof(SpriteCache), "GetSprite"), new HarmonyMethod(AccessTools.Method(t, nameof(SpriteCache_Contains))));
            harmony.Patch(AccessTools.Method(typeof(TextureManager), "Contains"), new HarmonyMethod(AccessTools.Method(t, nameof(TextureManager_Contains))));
            harmony.Patch(AccessTools.Method(typeof(PrefabCache), "IsPrefabInPool"), new HarmonyMethod(AccessTools.Method(t, nameof(PrefabCache_IsPrefabInPool))));
            harmony.Patch(AccessTools.Method(typeof(PrefabCache), "PooledInstantiate"), new HarmonyMethod(AccessTools.Method(t, nameof(PrefabCache_PooledInstantiate))));
            harmony.Patch(AccessTools.Method(typeof(DataManager), "Exists"), new HarmonyMethod(AccessTools.Method(t, nameof(DataManager_Exists))));


            typeof(AssetBundleManager)
                .GetMethods(AccessTools.all)
                .Where(meth => meth.Name == "IsBundleLoaded")
                .ForEach(meth => 
                          harmony.Patch(meth, new HarmonyMethod(AccessTools.Method(t, nameof(AssetBundleManager_IsBundleLoaded)))));


            var tdm = typeof(DataManager);
            harmony.Patch(AccessTools.Method(tdm, "Clear"), new HarmonyMethod(AccessTools.Method(t, nameof(DataManager_Clear))));

            Assembly.GetAssembly(typeof(RT))
                    .GetTypes()
                    .Where(ty => !ty.IsAbstract && !ty.IsInterface && !ty.IsGenericTypeDefinition)
                    .SelectMany(ty => ty.GetMethods())
                    .Where(meth => meth.Name == "Awake" || meth.Name == "Start")
                    .Where(meth => !meth.IsGenericMethod && !meth.IsGenericMethodDefinition)
                    .ForEach(meth => { LogDebug($"Try patch {meth.DeclaringType}.{meth.Name}");
                                       Trap(() => harmony.Patch(meth, new HarmonyMethod(AccessTools.Method(t, nameof(Tag)))));
                                     });
            
            Assembly.GetAssembly(typeof(HeraldryDef))
                .GetTypes()
                .Where(ty => ty.GetInterface(typeof(DataManager.ILoadDependencies).FullName) != null)
                .ForEach(ildtype =>
                {
                    harmony.Patch(AccessTools.Method(ildtype, "CheckDependenciesAfterLoad"), Drop);   // If enabled this will prevent RequestDependency checks that we need temporarily before the data manager is ready.
                    //harmony.Patch(AccessTools.Method(ildtype, "DependenciesLoaded"), Drop);
                    harmony.Patch(AccessTools.Method(ildtype, "RequestDependencies"), Drop);
                });
        } 

        public static bool AssetBundleManager_IsBundleLoaded(string name, bool __result) {
            return __result == Stuff.Bundles.ContainsKey(name);
        }

        public static string Caller(int c = 2) {
            var frm = new StackFrame(c).GetMethod();
            return $"{frm.DeclaringType.FullName}.{frm.Name}";
        }

        public static void Tag() {
            LogDebug($"Tagged {Caller(3)} {Caller(4)}");
        }


        public static bool DM_ClearLock = false;
        public static bool DataManager_Clear() {
            if (DM_ClearLock) return false;
            DM_ClearLock = true;
            LogDebug("DataManager.Clear is allowed (first init)");

            return true;
        }

        static Stopwatch umms = new Stopwatch();
        public static void UnmountMemoryStore_Pre() {
            umms.Start();
        }

        public static void UnmountMemoryStore_Post() {
            umms.Stop();
        }

        public static void PrefabCache_IsPrefabInPool(string id, Dictionary<string,object> ___prefabPool)
        {
            LogDebug($"IsPrefabInPool {Caller(5)}");
            // FIXME: Critically slow path, but we have to resolve the resource type somehow.
            var type = Trap(() => ResolveDepsAsync.cachedManifest[id].Type.ToRT());
            Trap("PrefabCache_IsPrefabInPool", () => InterceptAssetChecks.DoCore(id, type, ___prefabPool.ContainsKey, ___prefabPool.GetValueSafe, ___prefabPool.Add));
        }

        public static bool PrefabCache_PooledInstantiate(PrefabCache __instance, ref GameObject __result, Dictionary<string, GameObject> ___prefabPool, string id, Vector3? position = null, Quaternion? rotation = null, Transform parent = null) {
            // FIXME: This needs some work.
            __instance.IsPrefabInPool(id); // Load hook
            if (___prefabPool.TryGetValue(id, out var prefab)) {
                var t = prefab.transform;
                var go = (GameObject)GameObject.Instantiate( prefab
                                                           , position == null ? t.position : position.Value
                                                           , rotation == null ? t.rotation : rotation.Value);
                if (parent != null)
                    go.transform.SetParent(parent);
                __result = go;
            } 
            return false;
        }

        public static void TextureManager_Contains(string resourceId, Dictionary<string, object> ___loadedTextures)
        {
            Trap("TextureManager_Contains", () => InterceptAssetChecks.DoCore(resourceId, RT.Texture2D, ___loadedTextures.ContainsKey, ___loadedTextures.GetValueSafe, ___loadedTextures.Add));
        }

        static int depslevel = 0;
        static List<string> depsdb = null;
        static bool Fatal = false;

        class InterceptAssetChecks
        {                   
            public static bool SetWhen(bool singleItemSuccessful, bool everythingSuccessful)
            {
                if (everythingSuccessful == true) return singleItemSuccessful;
                else return false;
            }

            public static IEnumerable<CodeInstruction> DependenciesLoaded(ILGenerator gen, MethodBase method, IEnumerable<CodeInstruction> ins)
            {
                Trap(() => Log("Patching {0}.{1}", method.DeclaringType.FullName, method.Name));

                // var loc;
                var loc = gen.DeclareLocal(typeof(bool));

                var stwhen = AccessTools.Method(typeof(InterceptAssetChecks), nameof(SetWhen));

                return Trap(() =>
                {
                    // loc = true;
                    var start = Sequence(new CodeInstruction(O.Ldc_I4_1), new CodeInstruction(O.Stloc, loc));

                    // return loc;
                    var end = Sequence(new CodeInstruction(O.Ldloc, loc), new CodeInstruction(O.Ret));
                                        
                    var body = ins.SelectMany(i =>
                    {
                        if (i.opcode == O.Ret)
                        {
                            // `return _;`  to  `loc = SetWhen(_, loc);`
                            i.opcode = O.Ldloc;
                            i.operand = loc;
                            var meth = new CodeInstruction(O.Call, stwhen);
                            var stloc = new CodeInstruction(O.Stloc, loc);
                            return Sequence(i, meth, stloc);
                        }
                        else
                        {
                            return Sequence(i);
                        }
                    });

                    return start.Concat(body).Concat(end);
                });
            }

            public static Dictionary<RT, Type> Json = Assembly.GetAssembly(typeof(RT))
                .GetTypes()
                .Where(ty => ty.GetInterface(typeof(HBS.Util.IJsonTemplated).FullName) != null)
                .Where(ty => !Throws(() => ty.Name.ToRT()))
                .ToDictionary(ty => ty.Name.ToRT());

            public static IPromise<object> Ensure(VersionManifestEntry entry, RT type, string id)
            {
                var mstoreovd = stuff.dataManager.ResourceLocator.GetMemoryStoreContainingEntry(type, id, type.AsString());
                if (mstoreovd != null) { LogDebug($"Override for {id}");
                                         var prom = new Promise<object>();
                                         mstoreovd.GetLoadMethod(type)(id, val => prom.Resolve((object)val));
                                         return prom; }
                else if (Json.TryGetValue(type, out var jtype)) return stuff.LoadJsonD(entry, jtype);
                else if (type == RT.Sprite) return EnsureSprite(entry, type, id).PromiseObject();
                else if (type == RT.Texture2D) return EnsureTexture2D(entry, type, id).PromiseObject();
                else if (type == RT.Prefab || type == RT.UIModulePrefabs) return EnsurePrefab(entry, type, id).PromiseObject();
                else if (type == RT.SVGAsset) return stuff.LoadMapper(entry, type, id, null, (SVGAsset svg) => svg).PromiseObject();
                else if (type == RT.ColorSwatch) return stuff.LoadMapper(entry, type, id, null, (ColorSwatch cs) => cs).PromiseObject();
                else if (type == RT.SimpleText) return stuff.LoadMapper(entry, type, id, System.Text.Encoding.UTF8.GetString, (TextAsset ta) => ta.text).Then(t => new SimpleText(t)).PromiseObject();
                else if (type == RT.SimGameStringList) return stuff.LoadMapper(entry, type, id, System.Text.Encoding.UTF8.GetString
                                                                              , (TextAsset ta) => ta.text).Then(str => new SimGameStringList().Let(ss => { ss.ID = id;
                                                                                                                                                           ss.Load(str);
                                                                                                                                                           return (object)ss; }));
                else if (type == RT.SimGameConversations) return stuff.LoadMapper<isogame.Conversation, GameObject>( entry, type, id
                                                                                                                   , SimGameConversationManager.LoadConversationFromBytes
                                                                                                                   , null).PromiseObject();
                else if (type == RT.SimGameSpeakers) return stuff.LoadMapper<isogame.ConversationSpeakerList, GameObject>( entry, type, id
                                                                                                                         , bytes => new SerializationStream(bytes).Let(SimGameConversationManager.LoadSpeakerListFromStream)
                                                                                                                         , null).PromiseObject();
                else if (type == RT.AssetBundle) return Promise<object>.Resolved((object)Stuff.LoadBundle(id));
                else return Promise<object>.Rejected(new Exception("Unhandled RT type " + type.AsString()));
            }

            public static IPromise<Texture2D> EnsureTexture2D(VersionManifestEntry entry, RT type, string id)
            {
                return stuff.LoadMapper( entry, RT.Texture2D, id
                                       , bytes => new Traverse(typeof(TextureManager)).Method("TextureFromBytes", bytes).GetValue<Texture2D>()
                                       , (Texture2D tex) => tex);
            }

            public static IPromise<Sprite> EnsureSprite(VersionManifestEntry entry, RT type, string id)
            {
                var texentry = stuff.dataManager.ResourceLocator.EntryByID(id, RT.Texture2D, false);
                if (texentry != null)
                {
                    return EnsureTexture2D(entry, RT.Texture2D, id)
                        .Then(texture => Sprite.Create(texture, new UnityEngine.Rect(0f, 0f, (float)texture.width, (float)texture.height), new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect, Vector4.zero));
                }
                else
                {
                    return stuff.LoadMapper(entry, RT.Sprite, id, null, (Sprite s) => s, (yes, no) => yes(Stuff.SpriteFromDisk(entry.FilePath)));
                }
            }

            public static IPromise<GameObject> EnsurePrefab(VersionManifestEntry entry, RT type, string id)
            {
                return stuff.LoadMapper(entry, RT.Prefab, id, null, (GameObject go) => go);
            }

            public static void DoCore(string id, RT type, Func<string,bool> Check, Func<string, object> Get, Action<string, object> Set)
            {
                LogDebug("DoCore {0}:{1}", id, type.AsString());
                if (Fatal)
                    return;
                if (Check(id))
                {
                    //LogDebug("Item {0}:{1} - OK {2}", id, type.AsString(), Get(id) == null ? "null" : "...");
                }
                else
                {
                    //LogDebug("Item {0}:{1} - NEEDSLOAD", id, type.AsString());
                    if (depsdb != null)
                    {
                        LogDebug("DepsCheck :depth {0}", new StackTrace().FrameCount);
                        Trap(() => depsdb.Add(depslevel +" ! " + id + ":" + type.AsString()));
                    }

                    void CheckDependencies(DataManager.ILoadDependencies item)
                    {
                        LogDebug("Load deps of {0}", id);
                        // Dependencies sometimes have to be checked twice as some of them do the refresh code post Deps check
                        var yes = Trap("DepsCheck1", () => item.DependenciesLoaded(100000) ? true : item.DependenciesLoaded(100000));
                        if (yes)
                        {
                            LogDebug("Checked deps of {0} -- OK", id);
                        }
                        else
                        {
                            LogDebug("Check deps of {0}", id);
                            var ddbbackup = depsdb;
                            var root = depsdb == null;
                            if (root) { depsdb = new List<string>();
                                        depslevel = 0; }

                            depslevel++;
                            Trap("DepsCheck2", () => item.DependenciesLoaded(100000));
                            depslevel--;

                            var depsbak = depsdb;
                            depsdb = ddbbackup;

                            var hasDepsList = depsbak.Count() != 0;

                            var depsinfo = string.Format("Checked deps of {0} -- MISSING: {1}"
                                                        , id, depsbak.Dump(false));

                            Trap("ReportDeps", () => LogError("REPORT {0}", depsinfo));
                            if (depsdb == null)
                            {
                                LogError("------------ FATAL ------------");

                                var prettyDeps = "";
                                void AddL(string str)
                                {
                                    // Thanks for the tip about tags @Morphyum
                                    prettyDeps += "<align=\"left\">" + str + "</align>\n";
                                }
                                AddL(id);
                                depsbak.Distinct().ForEach(dep =>
                                {
                                    AddL("<mspace=10>+ </mspace><color=\"red\">" + dep + "</color>");
                                });

                                if (!Fatal) { GenericPopupBuilder genericPopupBuilder = GenericPopupBuilder.Create("Missing Dependencies" + (hasDepsList ? "" : " -- CHECK LOGS"), prettyDeps);
                                              genericPopupBuilder.Render(); }
                                Fatal = true;
                            }
                        }
                    }

                    var Triggered = false;
                    void Success(object result)
                    {
                        Triggered = true;

                        if (result == null)
                            LogWarning("Loaded a null value for {0}:{1}", id, type.AsString());

                        Trap("SetDataManager", () => new Traverse(result).Field("dataManager").SetValue(stuff.dataManager));
                        Trap("SetDataManager", () => new Traverse(result).Field("DataManager").SetValue(stuff.dataManager));
                        Trap("SetLoadRequest", () => new Traverse(result).Field("loadRequest").SetValue(new DummyLoadRequest(stuff.dataManager)));
                        Trap("AddToDB", () => { LogDebug("Adding to db {0}", id);  Set(id, result); LogDebug("Added to db {0}", id); });

                        if (result is DataManager.ILoadDependencies) Trap("CheckDependencies", () => CheckDependencies(result as DataManager.ILoadDependencies));
                    }

                    void Failure(Exception error)
                    {
                        LogException(error);
                        Triggered = true;
                    }

                    var entry = stuff.dataManager.ResourceLocator.EntryByID(type == RT.SimGameConversations ? $"{id}.convo" : id, type, false);
                    if (entry == null) { LogError($"Unable to locate asset for {id}:{type.AsString()} from {new StackTrace().ToString()}");
                                         LogError("Found the following entires with same id {0}", ResolveDepsAsync.stuff.dataManager.ResourceLocator.AllEntries().Where(e => e.Id == id).ToArray().Dump()); }
                    else { Ensure(entry, type, id)
                               .Done(Success, Failure);
                           if (!Triggered) LogException(new Exception(string.Format("DoCore async load for {0}:{1} :entry {2}", id, type.AsString(), entry.Dump(false)))); }
                }
            }
        }

        public static void SVGCache_Contains(string id, Dictionary<string, object> ___cache)
        {
            Trap("SVGCache_Contains", () => InterceptAssetChecks.DoCore(id, RT.SVGAsset, ___cache.ContainsKey, ___cache.GetValueSafe, ___cache.Add));
        }

        public static void SpriteCache_Contains(string id, Dictionary<string, object> ___cache)
        {
            Trap("SpriteCache_Contains", () => InterceptAssetChecks.DoCore(id, RT.Sprite, ___cache.ContainsKey, ___cache.GetValueSafe, ___cache.Add));
        }

        public static bool DataManager_Exists(RT resourceType, string id, ref bool __result)
        {
            if (resourceType == RT.Prefab)
            {
                // DM does odd stuff here. Lets just make it do what we want.
                __result = stuff.dataManager.IsPrefabInPool(id);
                return false;
            }

            return true;
        }

        /* TODO: This hook does some odd stuff..
         *    - Single hook works for all generic methods
         *    - Causes DictionaryStore to see object, may have implications
         */
        public static void DictionaryStore_Exists(object __instance, string id, Dictionary<string, object> ___items)
        {
            // FIXME: Custom resolve is required for Conversations. They don't use the asset ID, but instead the convo line ID

            var gtt = Trap("GetGenericArgs", () => __instance.GetType().GetGenericArguments()[0]);
            if (gtt == typeof(isogame.ConversationSpeakerList)) { InterceptAssetChecks.DoCore(id, RT.SimGameSpeakers, ___items.ContainsKey, ___items.GetValueSafe, ___items.Add);
                                                                  return; }
            if (gtt == typeof(isogame.Conversation)) { InterceptAssetChecks.DoCore(id, RT.SimGameConversations, ___items.ContainsKey, ___items.GetValueSafe, ___items.Add);
                                                       return; }
            gtt.Name.ToRTMap(type => Trap("MDE", () => InterceptAssetChecks.DoCore(id, type, ___items.ContainsKey, ___items.GetValueSafe, ___items.Add))
                            , () => Trap(() => LogWarning("MDE no resolve for {0}", gtt.Name)));
                    }

        public static bool DictionaryStore_Keys(object __instance, ref IEnumerable<string> __result)
        {
            var gtt = Trap("GetGenericArgs", () => __instance.GetType().GetGenericArguments()[0]);

            LogWarning($"Strict load of Keys for {gtt.FullName} originated from \n{new StackTrace().ToString()}\n\n");

            RT? rtt = 
                gtt == typeof(FactionDef) ? (RT?)RT.FactionDef
                : gtt == typeof(StarSystemDef) ? (RT?)RT.StarSystemDef
                : gtt == typeof(LifepathNodeDef) ? (RT?)RT.LifepathNodeDef
                : gtt == typeof(SimGameMilestoneDef) ? (RT?)RT.SimGameMilestoneDef
                : gtt == typeof(SimGameStringList) ? (RT?)RT.SimGameStringList
                : gtt == typeof(isogame.ConversationSpeakerList) ? (RT?)RT.SimGameSpeakers
                : gtt == typeof(isogame.Conversation) ? (RT?)RT.SimGameConversations
                : gtt == typeof(SimGameSubstitutionListDef) ? (RT?)RT.SimGameSubstitutionListDef
                : gtt == typeof(PilotDef) ? (RT?)RT.PilotDef
                : gtt == typeof(LanceDef) ? (RT?)RT.LanceDef
                : null;
                
            if(rtt != null) {
                // FIXME: We don't really want to do a full manifest pull here. Some of this requires writing simgame state
                //     but until then, this should be based off the requests made to datamanager
                __result = ResolveDepsAsync.stuff.dataManager.ResourceLocator.AllEntriesOfResource(rtt.Value).Select(entry => entry.Id);   //FIXME: Filter by ownership
                return false;
            }

            LogWarning($"DictionaryStore<{gtt.Name}>.Keys likely needs to be handled");
            __result = Enumerable.Empty<string>();

            return false;
        }


        public static bool SpawnNext(ref bool __result, int ___currentCount, int ___poolCount)
        {
            Log("SpawnNext: {0}/{1}", ___currentCount, ___poolCount);
            __result = true;
            return false;
        }

        public static bool PooledInstantiate(string id)
        {
            Log("PooledInsantiate: {0}", id);
            return true;
        }

        // FIXME: DM update is a problem, it doesn't keep the loads coming fast enough.
        //      Consider broadcasting the loadcomplete from ProcessRequests with a stack depth check to avoid overflows
        public static void DataManager_Update() {
            LogDebug($"Spin :frame {Time.frameCount} :renderFrame {Time.renderedFrameCount} :ms {Time.unscaledTime} :fps {Application.targetFrameRate}");
        }

        public static bool IntroAdded(IntroCinematicLauncher __instance)
        {
            return false;
        }

        static List<string> allfiles = new List<string>();
        public static void FileHookPath(string path)
        {
            allfiles.Add(path);
        }

        public static void FileHookName(string name)
        {
            allfiles.Add(name);
        }

        static Dictionary<string,int> track = new Dictionary<string,int>();

        public static void TrackRequestResource(DataManager __instance, BattleTechResourceType resourceType, PrewarmRequest prewarm)
        {
            var key = string.Format("{0}:{1}", Enum.GetName(typeof(RT), resourceType), prewarm != null);
            if (!track.ContainsKey(key)) track[key] = 0;
            track[key]++;
        }

        public static Dictionary<string, VersionManifestEntry> cachedManifest = null;
        public static void IntegrityCheck(BattleTechResourceLocator __instance) {
            Trap(() => { 
                var manifest = new Traverse(__instance).Field("baseManifest").GetValue<Dictionary<BattleTechResourceType, Dictionary<string, VersionManifestEntry>>>();
                cachedManifest = new Dictionary<string, VersionManifestEntry>();
                manifest.AsEnumerable().ForEach(types => types.Value.AsEnumerable().ForEach(kv => cachedManifest[kv.Key] = kv.Value));

                Control.Log("----------------- Manifest integrity check ---------------------------");
                var wrongIdents = manifest.SelectMany(type => type.Value.Where(entry => entry.Value.Id != entry.Key));
                var wrongTypes = manifest.SelectMany(type => type.Value.Where(entry => (RT)Enum.Parse(typeof(RT), entry.Value.Type) != type.Key));


                string f(VersionManifestEntry vme)
                    => string.Format("{0}:{1}", vme.Id, (RT)Enum.Parse(typeof(RT), vme.Type));


                Control.Log(string.Format("Wrong ids   ({0})", string.Join(" ", wrongIdents.Select(x => f(x.Value)).ToArray())));
                Control.Log(string.Format("Wrong types ({0})", string.Join(" ", wrongTypes.Select(x => f(x.Value)).ToArray())));
                manifest.SelectMany(types => types.Value.Select(entries => entries.Value))
                    .GroupBy(entry => entry.Id)
                    .Where(group => group.Count() > 1)
                    .ForEach(collision =>
                    {
                        string.Format("ID collision: ({0})", string.Join(" ", collision.Select(f).ToArray()));
                    });
            });
        }

        public static void TrackPre()
        {
            var frm = new StackFrame(1).GetMethod();
            var key = string.Format("{0}.{1}", frm.DeclaringType.Name, frm.Name);
            if (!track.ContainsKey(key)) track[key] = 0;
            if (!track.ContainsKey("total")) track["total"] = 0;
            track[key]++;
            track["total"]++;
        }

        public static void Summary()
        {
            Control.Log("(Track {0})", string.Join(" ", track.Select(kv => string.Format(":{0} {1}", kv.Key, kv.Value)).ToArray()));

            var counts = allfiles.GroupBy(s => s)
                .Where(g => g.Count() > 1)
                .Select(g => string.Format("{0}:{1}", g.First(), g.Count()));


            Control.Log("(File-duplicates {0})", counts.Dump());
            track.Clear();

            var counts2 = Stuff.LoadMapper_hits.GroupBy(s => s)
               .Where(g => g.Count() > 1)
               .Select(g => string.Format("{0}:{1}", g.First(), g.Count()));


            Control.Log("(Load-duplicates {0})", counts2.Dump());


            Control.Log($"Umms: {umms.Elapsed.TotalMilliseconds}");
            umms = new Stopwatch();

            track.Clear();
        }

       
        public static Dictionary<string, Promise<object>> promises = new Dictionary<string, Promise<object>>();
        public static bool Initialized = false;
        public static Stuff stuff;

        public static Promise<object> Ensure(string id)
        {
            if (promises.TryGetValue(id, out var prom))
            {
                return prom;
            }
            else
            {
                var p = new Promise<object>();
                promises[id] = p;
                return p;
            }
        }

        public static IPromise Load(RT type, string id)
        {
            LogDebug("ResolveDepsAsync.Load {0}:{1}", id, type.AsString());
            Trap(() => stuff.RequestResource(type, id, new PrewarmRequest(), false, false));
            var onLoad = Ensure(id);
            onLoad.Done(x => LogDebug("ResolveDepsAsync.Loaded {0}:{1}", id, type.AsString()));
            return onLoad.Unit();
        }

        public static IPromise<T> Load<T>(RT type, string id)
        {
            Trap(() => stuff.RequestResource(type, id, new PrewarmRequest(), false, false));
            return Ensure(id)
                .Then(x => x is T ? (T)x : throw new Exception(string.Format("Load<T> Wanted {0}, but {1}:{2} is a {3}", typeof(T).FullName, id, Enum.GetName(typeof(RT), type), x.GetType().FullName)));
        }

        public static void DispatchAssetLoad(MessageCenterMessage msg)
        {
            var t = new Traverse(msg);
            var val = t.Property("Resource").GetValue();
            var id = t.Property("ResourceId").GetValue<string>();

            if (val is MechDef)
            {
                var mdv = val as MechDef;
                LogDebug("itc Mechdef[{0}] wanting {1}", id, mdv.ChassisID);
            }

            if (val is DataManager.ILoadDependencies)
            {
                var deps = val as DataManager.ILoadDependencies;
            }

            //FIXME: need to capture any multiple resolved promise here and do some cache correcting
            try
            {
                Ensure(id)
                    .Resolve(val);
            } catch
            {
                LogWarning("Asset {0} already dispatched", id);
            }
        }

        // General idea: Build a queue as RR_I is called
        //    waits for the queue to complete + 1 frame and then dispatches a datamanagerloaded message
        public static void ProcessRequests(DataManager __instance, List<DataManager.DataManagerLoadRequest> ___backgroundRequestsList, List<DataManager.DataManagerLoadRequest> ___foregroundRequestsList)
        {
            if (!__instance.IsLoading)
            {
                LogDebug("Process requests started empty queue message");
                stuff.messageCenter.PublishMessage(new DataManagerLoadCompleteMessage());
            }

            LogDebug($"ProcessRequests from: {new StackTrace().ToString()}");
        }

        public static void LoadCompleteMessage(MessageCenterMessage msg)
        {
            LogDebug("LoadCompleteMessage");
        }

        public static void AsyncLoadCompleteMessage(MessageCenterMessage msg)
        {
            LogDebug("AsyncLoadCompleteMessage");
        }

        public static bool Update()
        {
            if (Fatal)
                return false;
            return true;
        }

        public static List<string> dryRun = null;     
        public static bool RequestResources_Internal2(MethodInfo __originalMethod, DataManager __instance, BattleTechResourceType resourceType, string identifier, PrewarmRequest prewarm, bool allowRequestStacking, bool filterByOwnership)
        {
            if (!Initialized)
            {
                stuff = new Stuff(__instance);
                Initialized = true;
                //stuff.messageCenter.AddSubscriber(MessageCenterMessageType.DataManagerRequestCompleteMessage, new ReceiveMessageCenterMessage(DispatchAssetLoad));
                //stuff.messageCenter.AddSubscriber(MessageCenterMessageType.DataManagerLoadCompleteMessage, new ReceiveMessageCenterMessage(LoadCompleteMessage));
                //stuff.messageCenter.AddSubscriber(MessageCenterMessageType.DataManagerAsyncLoadCompleteMessage, new ReceiveMessageCenterMessage(AsyncLoadCompleteMessage));


                stuff.messageCenter.AddSubscriber(MessageCenterMessageType.LevelLoadComplete, new ReceiveMessageCenterMessage(msg => LogDebug("Load complete")));
            }

            var mstoreovd = stuff.dataManager.ResourceLocator.GetMemoryStoreContainingEntry(resourceType, identifier, resourceType.AsString());
            if (mstoreovd != null)
                LogWarning($"Override for {identifier}:{resourceType.AsString()}");

            LogDebug($"{Caller(3)} RRI: {identifier}:{resourceType.AsString()}");

            if (resourceType == RT.SimGameConstants || resourceType == RT.CombatGameConstants || resourceType == RT.MechStatisticsConstants)
                return true;

            return false;
        }

        public static bool CollectDeps = false;
        public static int CollectDepsDepth = 0;
        public static bool Halt = false;
        public static Dictionary<string, Promise<object>> reqCache = new Dictionary<string, Promise<object>>();
    }

    delegate void AcceptReject<T>(Action<T> accept, Action<Exception> reject);

    class Stuff
    {
        public DataManager dataManager;
        public HBS.Data.DataLoader dataLoader;
        public AssetBundleManager bundleManager;
        public MessageCenter messageCenter;
        public TextureManager textureManager;

        public Dictionary<RT, KeyValuePair<Func<VersionManifestEntry, IPromise<object>>, Action<string>>> loadDB;

        public Stuff(DataManager dataManager)
        {
            this.dataManager = dataManager;
            this.bundleManager = new Traverse(dataManager).Property("AssetBundleManager").GetValue<AssetBundleManager>();
            this.dataLoader = new Traverse(dataManager).Field("dataLoader").GetValue<HBS.Data.DataLoader>();
            this.messageCenter = new Traverse(dataManager).Property("MessageCenter").GetValue<MessageCenter>();
            this.textureManager = new Traverse(dataManager).Property("TextureManager").GetValue<TextureManager>();
        }

        public bool CanHandleType(RT type)
        {
            return loadDB.ContainsKey(type);
        }

        public bool RequestResource(RT type, string id, PrewarmRequest p, bool stack, bool own)
        {
            return dataManager.RequestResource(type, id);
        }

        public void Add<T>(string field, string key, T item) where T : new()
        {
            Trap(() =>
            {
                new Traverse(dataManager)
                    .Field(field)
                    .GetValue<DictionaryStore<T>>()
                    .Add(key, item);
            });
        }

        public static Dictionary<string,IPromise<object>> cache = new Dictionary<string,IPromise<object>>();
        public IPromise<object> LoadObj(RT type, string identifier)
        {
            // FIXME: If prefab, pull it out of the cache so it can load again.
            //    or preferably duplicate it without going to disk (this works right?)

            if (cache.TryGetValue(identifier, out var cached))
            {
                LogDebug("cached Obj {0}:{1}", identifier, type.AsString());
                return cached;
            }
            var entry = dataManager.ResourceLocator.EntryByID(identifier, type, false);
            LogDebug("Found entry {0}:{1}", identifier, type.AsString());
            if (loadDB.TryGetValue(type, out var kvld))
            {
                LogDebug("Custom load-db2: {0}:{1}", identifier, Enum.GetName(typeof(RT), type));
                var prom = Trap(() => kvld.Key(entry)
                    .Then(x =>
                    {
                        LogDebug("load-db2-success: {0}:{1}", identifier, Enum.GetName(typeof(RT), type));
                        if (x is DataManager.ILoadDependencies)
                            return (x as DataManager.ILoadDependencies).Resolve(ResolveDepsAsync.stuff.dataManager).Then(() => Promise<object>.Resolved(x));
                        LogDebug("load-db2-not-ild");
                        var reso = Promise<object>.Resolved(x);
                        LogDebug("load-db2-after-ild");
                        return reso;
                    }));
                cache[identifier] = prom;
                return prom;
            } else
            {
                return Promise<object>.Rejected(new Exception(string.Format("Don't know how to load {0} yet", type.AsString())));
            }
        }

        public IPromise<T> LoadJson<T>(VersionManifestEntry entry, BattleTechResourceType resourceType, string identifier)
            where T : class, HBS.Util.IJsonTemplated
        {
            return LoadJsonD(entry, typeof(T)).Then(x => x as T);
        }

        public IPromise<object> LoadJsonD(VersionManifestEntry entry, Type t)
        {
            object Make(string json)
            {
                if (t == null)
                    LogError($"LoadJsonD: type is null");
                //FIXME: Verify t here
                HBS.Util.IJsonTemplated a = (HBS.Util.IJsonTemplated)Activator.CreateInstance(t);
                if (a == null)
                    LogError($"LoadJsonD: No type from activator for type {t.FullName}");
                a.FromJSON(json);
                //Log("JSON send");
                if (a == null)
                    LogError("LoadJsonD resulted in a null value for {0} {1}", entry.Id, t.FullName);
                return a;
            }

            return LoadMapper(entry, (RT)Enum.Parse(typeof(RT), entry.Type), entry.Id, bytes => Make(System.Text.Encoding.UTF8.GetString(bytes)), (TextAsset r) => Make(r.text));
        }

        public static readonly string BundlesPath = Path.Combine(Application.streamingAssetsPath, "data/assetbundles");
        public static string AssetBundleNameToFilePath(string assetBundleName)
            => new Traverse(typeof(AssetBundleManager)).Method("AssetBundleNameToFilepath", assetBundleName).GetValue<string>();

        public static IEnumerable<string> GetBundleDependencies(string bundleName)
            => new Traverse(ResolveDepsAsync.stuff.bundleManager).Field("manifest").GetValue<AssetBundleManifest>().GetAllDependencies(bundleName);

        public static Dictionary<string, AssetBundle> Bundles = new Dictionary<string, AssetBundle>();
        public static AssetBundle LoadBundle(string bundleName) {
            if (Bundles.TryGetValue(bundleName, out var bundle)) return bundle;
            else { GetBundleDependencies(bundleName).ForEach(depName => LoadBundle(depName));
                   var path = AssetBundleNameToFilePath(bundleName);
                   var newBundle =  AssetBundle.LoadFromFile(path).NullCheckError($"Missing bundle {bundleName} from {path}");
                   Bundles[bundleName] = newBundle;
                   return newBundle; }
        }

        public static UnityEngine.Object LoadAssetFromBundle(string assetName, string bundleName)
            => LoadBundle(bundleName)?.LoadAsset(assetName).NullCheckError($"Unable to load {assetName} from bundle {bundleName}");

        public static Dictionary<string, int> LoadMapper_hits = new Dictionary<string,int>();
        // TODO: Looks like resource and bundle are the same type always, if so reduce them into one selector
        public IPromise<T> LoadMapper<T, R>(VersionManifestEntry entry, BattleTechResourceType resourceType, string identifier, Func<byte[], T> file, Func<R, T> resource, AcceptReject<T> recover = null) 
            where R : UnityEngine.Object 
        {
            if (LoadMapper_hits.ContainsKey(identifier)) LoadMapper_hits[identifier]++;
            LoadMapper_hits[identifier] = 0;
            var res = new Promise<T>();
            try
            {
                if (entry == null)
                {
                    res.Reject(new Exception(string.Format("Null entry for {0}:{1}", identifier, resourceType.AsString())));
                    return res;
                }
                if (entry.IsFileAsset && file != null) File.ReadAllBytes(entry.FilePath).Let(c => res.Resolve(file(c)));
                else if (entry.IsResourcesAsset && resource != null) res.Resolve(resource(Resources.Load<R>(entry.ResourcesLoadPath)));
                else if (entry.IsAssetBundled && resource != null)
                {
                    res.Resolve(resource(LoadAssetFromBundle(entry.Id, entry.AssetBundleName).SafeCast<R>()));
                }
                else if (recover != null) { try {
                        recover(res.Resolve, res.Reject);
                    }
                    catch (Exception e)
                    {
                        res.Reject(e);
                    }
                }
                else throw new System.Exception(string.Format("Unhandled file, resource, or asset {0}:{1} via {2}", identifier, resourceType.AsString(), entry.IsFileAsset ? "file" : entry.IsResourcesAsset ? "resource" : entry.IsAssetBundled ? "bundle" : "unknown"));
            } catch (Exception e)
            {
                res.Reject(e);
            }
            return res;
        }

        public DataManager.DataManagerLoadRequest CreateRequest(BattleTechResourceType resourceType, string identifier)
        {
            if (dataManager == null)
                LogError("DM null & CreateRequest");
            var meth = AccessTools.Method(typeof(DataManager), "CreateByResourceType");
            if (meth == null)
                LogError("DM missing CreateResourceByType");
            return Trap(() => (DataManager.DataManagerLoadRequest)meth.Invoke(dataManager, new object[] { resourceType, identifier, new PrewarmRequest() }));
        }

        public static Sprite SpriteFromDisk(string assetPath)
        {
            if (!File.Exists(assetPath))
            {
                return null;
            }
            Sprite result;
            try
            {
                byte[] array = File.ReadAllBytes(assetPath);
                Texture2D texture2D;
                if (TextureManager.IsDDS(array))
                {
                    texture2D = TextureManager.LoadTextureDXT(array);
                }
                else
                {
                    if (!TextureManager.IsPNG(array) && !TextureManager.IsJPG(array))
                    {
                        LogWarning(string.Format("Unable to load unknown file type from disk (not DDS, PNG, or JPG) at: {0}", assetPath));
                        return null;
                    }
                    texture2D = new Texture2D(2, 2, TextureFormat.DXT5, false);
                    if (!texture2D.LoadImage(array))
                    {
                        return null;
                    }
                }
                result = Sprite.Create(texture2D, new UnityEngine.Rect(0f, 0f, (float)texture2D.width, (float)texture2D.height), new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect, Vector4.zero);
            }
            catch (Exception ex)
            {
                LogError(string.Format("Unable to load image at: {0}\nExceptionMessage:\n{1}", assetPath, ex.Message));
                result = null;
            }
            return result;
        }
    }
}
