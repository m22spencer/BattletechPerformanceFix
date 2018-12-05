﻿using System;
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
using RSG;
using System.Diagnostics;
using System.Reflection;
using BattleTech;
using BattleTech.Portraits;
using BattleTech.Framework;
using RT = BattleTech.BattleTechResourceType;
using BattleTech.Rendering.MechCustomization;
using static BattletechPerformanceFix.Control;

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
                        harmony.Patch(AccessTools.Method(ild, "RequestDependencies"), pre, post);
                    });

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
                
                //harmony.Patch(AccessTools.Method(typeof(DataManager), "Update"), new HarmonyMethod(AccessTools.Method(t, nameof(DataManager_Update))));
            }

            /*
            harmony.Patch(AccessTools.Method(typeof(HBS.Threading.SimpleThreadPool), "Worker"), new HarmonyMethod(drop));
            harmony.Patch(AccessTools.Method(typeof(BattleTech.UI.AVPVideoPlayer), "ForcePlayVideo"), new HarmonyMethod(drop));
            harmony.Patch(AccessTools.Method(typeof(BattleTech.UI.AVPVideoPlayer), "PlayVideo"), new HarmonyMethod(drop));
            harmony.Patch(AccessTools.Method(typeof(BattleTech.UI.SGVideoPlayer), "PlayVideo"), new HarmonyMethod(drop));            
            */
            harmony.Patch(AccessTools.Method(typeof(BattleTech.IntroCinematicLauncher), "OnAddedToHierarchy"), new HarmonyMethod(AccessTools.Method(t, "IntroAdded")));

            //harmony.Patch(AccessTools.Method(typeof(DataManager), "RequestResource_Internal"), new HarmonyMethod(AccessTools.Method(t, nameof(ProcessRequests))));
            harmony.Patch(AccessTools.Method(typeof(DataManager), "RequestResource_Internal"), new HarmonyMethod(AccessTools.Method(t, nameof(RequestResources_Internal2))));


            var ttt = typeof(DictionaryStore<>).MakeGenericType(typeof(MechDef));
            var mde = ttt.GetMethod("Exists", AccessTools.all);
            harmony.Patch(mde,  new HarmonyMethod(AccessTools.Method(t, nameof(MechDef_Exists))));


            harmony.Patch(AccessTools.Method(typeof(SVGCache), "Contains"), new HarmonyMethod(AccessTools.Method(t, nameof(SVGCache_Contains))));
            harmony.Patch(AccessTools.Method(typeof(SpriteCache), "Contains"), new HarmonyMethod(AccessTools.Method(t, nameof(SpriteCache_Contains))));
            harmony.Patch(AccessTools.Method(typeof(TextureManager), "Contains"), new HarmonyMethod(AccessTools.Method(t, nameof(TextureManager_Contains))));
            harmony.Patch(AccessTools.Method(typeof(PrefabCache), "IsPrefabInPool"), new HarmonyMethod(AccessTools.Method(t, nameof(PrefabCache_IsPrefabInPool))));
            harmony.Patch(AccessTools.Method(typeof(DataManager), "Exists"), new HarmonyMethod(AccessTools.Method(t, nameof(DataManager_Exists))));

            return;
            Log("CDAL fix on");

            var resolver = AccessTools.Method(typeof(Resolver<ChassisDef>), "RequestDependencies"); //just using ChassisDef here to reference the static function. It means nothing;

            //harmony.Patch(AccessTools.Method(typeof(DataManager), "PooledInstantiate"), new HarmonyMethod(AccessTools.Method(typeof(ResolveDepsAsync), nameof(PooledInstantiate))));
            //harmony.Patch(AccessTools.Method(typeof(DataManager.QueuedPoolHelper), "SpawnNext"), new HarmonyMethod(AccessTools.Method(typeof(ResolveDepsAsync), nameof(SpawnNext)))); 

            if (WantVanilla)
                return;

            Assembly.GetAssembly(typeof(HeraldryDef))
                .GetTypes()
                .Where(ty => ty.GetInterface(typeof(DataManager.ILoadDependencies).FullName) != null)
                .ForEach(ildtype =>
                {
                    harmony.Patch(AccessTools.Method(ildtype, "CheckDependenciesAfterLoad"), Drop);   // If enabled this will prevent RequestDependency checks that we need temporarily before the data manager is ready.
                    //harmony.Patch(AccessTools.Method(ildtype, "DependenciesLoaded"), Drop);
                    harmony.Patch(AccessTools.Method(ildtype, "RequestDependencies"), new HarmonyMethod(resolver));
                });

            harmony.Patch(AccessTools.Method(typeof(DataManager), "ProcessAsyncRequests"), Drop);

            
            harmony.Patch(AccessTools.Method(typeof(DataManager), "ProcessRequests"), new HarmonyMethod(AccessTools.Method(t, nameof(ProcessRequests))));

            new ChassisDefResolver();
            new HeraldryDefResolver();
            new AbilityDefResolver();
            new BaseComponentRefResolver();
            new MechComponentRefResolver();
            new PilotDefResolver();
            new WeaponDefResolver();
            new MechDefResolver();
            new VehicleDefResolver();
            new MechComponentDefResolver();
            new AmmunitionBoxDefResolver();
            new HeatSinkDefResolver();
            new JumpJetDefResolver();
            new FactionDefResolver();
            new BackgroundDefResolver();
            new UpgradeDefResolver();
        } 

        public static void PrefabCache_IsPrefabInPool(string id, Dictionary<string,object> ___prefabPool)
        {
            Trap("PrefabCache_IsPrefabInPool", () => InterceptAssetChecks.DoCore(id, RT.Prefab, ___prefabPool.ContainsKey, ___prefabPool.GetValueSafe, ___prefabPool.Add));
            return;

            LogDebug("IsPrefabInPool {0}:Texture", id);
            if (Fatal) return;
            if (___prefabPool.ContainsKey(id))
                return;

            if (depsdb != null)
            {
                depsdb.Add(id + ":Prefab");
                return;
            }
        }

        public static void TextureManager_Contains(string resourceId, Dictionary<string, object> ___loadedTextures)
        {
            Trap("TextureManager_Contains", () => InterceptAssetChecks.DoCore(resourceId, RT.Texture2D, ___loadedTextures.ContainsKey, ___loadedTextures.GetValueSafe, ___loadedTextures.Add));
            return;
            LogDebug("Contains {0}:Texture", resourceId);

            if (Fatal) return;
            if (___loadedTextures.ContainsKey(resourceId))
                return;

            if(depsdb != null)
            {
                depsdb.Add(resourceId + ":Texture2D");
                return;
            }


            // Until we can handle files too this needs to just fail.
            return;

            LogDebug("Intercepting texture fetch {0}", resourceId);
            var entry = stuff.dataManager.ResourceLocator.EntryByID(resourceId, RT.Texture2D, false);
            stuff.LoadMapper(entry, RT.Texture2D, resourceId, null, (Texture2D tex) => tex)
                .Done(x => { ___loadedTextures.Add(resourceId, x); LogDebug("Found {0}", resourceId); }
                     , err => LogException(err));
            LogDebug("Complete {0}");
        }

        static List<string> depsdb = null;
        static bool Fatal = false;

        class InterceptAssetChecks
        {
            public static Dictionary<RT, Type> Json = Assembly.GetAssembly(typeof(RT))
                .GetTypes()
                .Where(ty => ty.GetInterface(typeof(HBS.Util.IJsonTemplated).FullName) != null)
                .Where(ty => !Throws(() => ty.Name.ToRT()))
                .ToDictionary(ty => ty.Name.ToRT());

            public static IPromise<object> Ensure(VersionManifestEntry entry, RT type, string id)
            {
                if (Json.TryGetValue(type, out var jtype)) return stuff.LoadJsonD(entry, jtype);
                else if (type == RT.Sprite) return EnsureSprite(entry, type, id).PromiseObject();
                else if (type == RT.Texture2D) return EnsureTexture2D(entry, type, id).PromiseObject();
                else if (type == RT.Prefab) return EnsureTexture2D(entry, type, id).PromiseObject();
                else return Promise<object>.Rejected(new Exception("Unhandled RT type"));
            }

            public static IPromise<Texture2D> EnsureTexture2D(VersionManifestEntry entry, RT type, string id)
            {
                return stuff.LoadMapper(entry, RT.Texture2D, id, null, (Texture2D tex) => tex);
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
                if (Fatal || Check(id))
                {
                    if (depsdb != null) LogDebug("Item {0}:{1} - OK {2}", id, type.AsString(), Get(id).Dump(false));
                }
                else
                {
                    if (depsdb != null) LogDebug("Item {0}:{1} - MISSING", id, type.AsString());
                    if (depsdb != null)
                    {
                        Trap(() => depsdb.Add(id + ":" + type.AsString()));
                    }

                    var entry = stuff.dataManager.ResourceLocator.EntryByID(id, type, false);
                    var Triggered = false;
                    void CheckDependencies(DataManager.ILoadDependencies item)
                    {
                        var yes = Trap("DepsCheck1", () => item.DependenciesLoaded(100000));
                        if (yes)
                        {
                            LogDebug("Checked deps of {0} -- OK", id);
                        }
                        else
                        {
                            var ddbbackup = depsdb;
                            depsdb = new List<string>();
                            Trap("DepsCheck2", () => item.DependenciesLoaded(100000));
                            var depsbak = depsdb;
                            depsdb = ddbbackup;
                            Trap("ReportDeps", () => LogError("Checked deps of {0} -- MISSING: {1}\n{2}\n\n"
                                    , id, depsbak.Dump(false), item.Dump()));
                            if (depsdb == null)
                            {
                                LogError("------------ FATAL ------------");
                                Fatal = true;
                            }
                        }
                    }

                    void Success(object result)
                    {
                        Triggered = true;

                        Trap("SetDataManager", () => new Traverse(result).Field("dataManager").SetValue(stuff.dataManager));
                        Trap("SetDataManager", () => new Traverse(result).Field("DataManager").SetValue(stuff.dataManager));
                        Trap("SetLoadRequest", () => new Traverse(result).Field("loadRequest").SetValue(new DummyLoadRequest(stuff.dataManager)));
                        Trap("AddToDB", () => Set(id, result));

                        if (result is DataManager.ILoadDependencies) Trap("CheckDependencies", () => CheckDependencies(result as DataManager.ILoadDependencies));
                    }

                    void Failure(Exception error)
                    {
                        LogException(error);
                        Triggered = true;
                    }

                    Ensure(entry, type, id)
                        .Done(Success, Failure);

                    if (!Triggered) LogException(new Exception(string.Format("DoCore async load for {0}:{1} :entry {2}", id, type.AsString(), entry.Dump(false))));
                }
            }
        }

        public static bool SVGCache_Contains(string id, Dictionary<string, object> ___cache)
        {
            Trap("SVGCache_Contains", () => InterceptAssetChecks.DoCore(id, RT.Sprite, ___cache.ContainsKey, ___cache.GetValueSafe, ___cache.Add));
            return true;
        }

        public static bool SpriteCache_Contains(string id, Dictionary<string, object> ___cache)
        {
            Trap("SpriteCache_Contains", () => InterceptAssetChecks.DoCore(id, RT.Sprite, ___cache.ContainsKey, ___cache.GetValueSafe, ___cache.Add));
            return true;

            LogDebug("Contains {0}:Sprite", id);
            if (Fatal) return true;
            if (___cache.TryGetValue(id, out var mdef))
            {
                LogDebug("Found sprite");
                return true;
            }
            else
            {
                LogDebug("No sprite");
                if (depsdb != null)
                {
                    LogDebug("dcheck");
                    Trap(() => depsdb.Add(id + ":Sprite"));
                    return true;
                }

                void Add(Sprite s)
                    => Trap("AddSprite", () => { LogDebug("Adding sprite {0}", s == null ? "null" : id); ___cache[id] = s; });

                // FIXME! IMPORTANT! Sprite has Unlocks and such. DLC? 
                Trap("Rest", () =>
                {
                    var texentry = stuff.dataManager.ResourceLocator.EntryByID(id, RT.Texture2D, false);
                    if (texentry != null)
                    {
                        if (stuff.textureManager.Contains(id))
                        {
                            var texture = stuff.textureManager.GetLoadedTexture(id);
                            Add(Sprite.Create(texture, new UnityEngine.Rect(0f, 0f, (float)texture.width, (float)texture.height), new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect, Vector4.zero));
                        }
                    }
                    else
                    {
                        var triggered = false;
                        var entry = stuff.dataManager.ResourceLocator.EntryByID(id, RT.Sprite, false);
                        stuff.LoadMapper(entry, RT.Sprite, id, null, (Sprite s) => s, (yes, no) => yes(Stuff.SpriteFromDisk(entry.FilePath)))
                            .Done(x => { triggered = true; Add(x); }, err => LogException(err));

                        if (!triggered) LogException(new Exception(string.Format("Loading of sprite was async {0} :entry {1}", id, entry.Dump(false))));
                    }
                });
                return true;
            }
        }

        public static void DataManager_Exists(RT resourceType, string id)
        {
            if (resourceType == RT.Prefab)
                stuff.dataManager.IsPrefabInPool(id);
        }
        
        public static bool MechDef_Exists(object __instance, string id, Dictionary<string, object> ___items)
        {
            var gtt = Trap("GetGenericArgs", () => __instance.GetType().GetGenericArguments()[0]);
            gtt.Name.ToRTMap(type => Trap("MDE", () => InterceptAssetChecks.DoCore(id, type, ___items.ContainsKey, ___items.GetValueSafe, ___items.Add))
                            , () => Trap(() => LogWarning("MDE no resolve for {0}", gtt.Name)));
            
            return true;

            if (Fatal) return true;
            LogDebug("Looking for id {0}", id);
            void CheckDeps(DataManager.ILoadDependencies d)
            {

                LogDebug("Check deps");
                var yes = Trap("DepsCheck1", () => d.DependenciesLoaded(100000));
                if (yes) {
                    LogDebug("Checked deps of {0} -- OK", id);
                }
                else
                {
                    depsdb = new List<string>();
                    Trap("DepsCheck2", () => d.DependenciesLoaded(100000));
                    var depsbak = depsdb;
                    depsdb = null;
                    Trap("ReportDeps", () => LogError("Checked deps of {0} -- MISSING: {1}\n{2}\n\n"
                            , id, depsbak.Dump(false), d.Dump()));
                    Fatal = true;
                }
            }

            // We already hook exists, so we can know what something failed to load in the dependency check.
            Trap("Outer", () =>
            {
                if (___items.TryGetValue(id, out var mdef))
                {

                    return;
                }
                else
                {
                    var gt = Trap("GetGenericArgs", () => __instance.GetType().GetGenericArguments()[0]);
                    LogDebug("Tracking: {0}", gt.FullName);
                    if (depsdb != null)
                    {
                        depsdb.Add(id + ":" + gt.Name);
                        return;
                    }
                    
                    if (gt.GetInterface(typeof(HBS.Util.IJsonTemplated).FullName) != null)
                    {
                        LogDebug("IJsonTemplated {0}", gt.Name);
                        var succ = gt.Name.ToRTMap(type =>
                        {
                            LogDebug("Fulltype", type);
                            var entry = stuff.dataManager.ResourceLocator.EntryByID(id, type, false);
                            LogDebug("Derived intercepting lookup of {0}", id);
                            stuff.LoadJsonD(entry, gt).Done(x =>
                            {
                                LogDebug("Loaded {0}={1} of {2}", x.GetType().Name, gt.Name, id);
                                Trap("SetDataManager", () => new Traverse(x).Field("dataManager").SetValue(stuff.dataManager));
                                Trap("SetLoadRequest", () => new Traverse(x).Field("loadRequest").SetValue(new DummyLoadRequest(stuff.dataManager)));
                                Trap("AddToDB", () => ___items.Add(id, x));
                            //x.Chassis = stuff.dataManager.ChassisDefs.Exists(x.ChassisID) ? stuff.dataManager.ChassisDefs.Get(x.ChassisID) : null;
                            if (x is DataManager.ILoadDependencies) CheckDeps(x as DataManager.ILoadDependencies);
                            });
                            LogDebug("Assuming direct control {0}");
                            return true;
                        }
                            , () => { LogError("Failed autoderive for: {0}", gt.Name); return false; }
                            );
                        if (succ)
                            return;
                    }
                    LogDebug("IJSTL failed for {0}", gt.Name);

                    if (__instance is DictionaryStore<MechDef>)
                    {
                        var entry = stuff.dataManager.ResourceLocator.EntryByID(id, RT.MechDef, false);
                        LogDebug("Intercepting lookup of {0}", id);
                        stuff.LoadJson<MechDef>(entry, RT.MechDef, id).Done(x =>
                        {
                            LogDebug("Loaded mechdef of {0}", id);
                            x.DataManager = stuff.dataManager;
                            ___items.Add(id, x);
                            x.Chassis = stuff.dataManager.ChassisDefs.Exists(x.ChassisID) ? stuff.dataManager.ChassisDefs.Get(x.ChassisID) : null;
                            CheckDeps(x);
                        });
                        LogDebug("Assuming direct control {0}");
                    }
                    else if (__instance is DictionaryStore<ChassisDef>)
                    {
                        var entry = stuff.dataManager.ResourceLocator.EntryByID(id, RT.ChassisDef, false);
                        LogDebug("Intercepting lookup of {0}", id);
                        stuff.LoadJson<ChassisDef>(entry, RT.ChassisDef, id).Done(x =>
                        {
                            LogDebug("Loaded of {0}", id);
                            x.DataManager = stuff.dataManager;
                            ___items.Add(id, x);
                            CheckDeps(x);
                        });
                        LogDebug("Assuming direct control");
                    }
                }
            });
            return true;
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

        public static void DataManager_Update(DataManager __instance, Dictionary<string, object> ___poolNextUpdate
            , Dictionary<BattleTechResourceType, Dictionary<string, object>> ___foregroundRequests
            , Dictionary<BattleTechResourceType, Dictionary<string, object>> ___backgroundRequests) {
            int f(Dictionary<BattleTechResourceType, Dictionary<string, object>> req)
                => req.SelectMany(r => r.Value).Count();
            Trap(() => Log("Pool, foreground, background: {0}, {1}, {2}", ___poolNextUpdate.Count, f(___foregroundRequests), f(___backgroundRequests)));
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


        static Dictionary<string, object> resolveMap = new Dictionary<string, object>();

        class Resolver<T>
            where T : DataManager.ILoadDependencies
        {
            public Dictionary<T,IPromise> cache = new Dictionary<T,IPromise>();
            public Resolver()
            {
                if (resolveMap.ContainsKey(typeof(T).FullName)) LogWarning("Resolve map duplicate for {0}", typeof(T).FullName);
                else
                {
                    Log("Add resolver {0}", typeof(T).FullName);
                    resolveMap[typeof(T).FullName] = this;
                }
            }            

            internal virtual IPromise Resolve(T __instance, RT type, string id)
            {
                throw new System.Exception(string.Format("Missing Resolver<T>.Resolve for {0}", typeof(T).FullName));
            }

            public IPromise ResolveSafe(DataManager.ILoadDependencies ild, DataManager dataManager, RT type, string id)
            {
                if (ild == null)
                    LogError("ILD is null");
                if (dataManager == null)
                    LogError("DataManager is null");
                if (id == null)
                    LogError("id is null");
                if (ild.GetType() != typeof(T))
                    LogError("Resolve safe wrong ILD type");
                var __instance = (T)ild;
                __instance.DataManager = ResolveDepsAsync.stuff.dataManager;
                var dummyload = new DummyLoadRequest(dataManager);
                new Traverse(__instance).Field("loadRequest").SetValue(dummyload);
                var idef = string.Format("{0}:{1}", id, Enum.GetName(typeof(RT), type));
                if (cache.TryGetValue(__instance, out var prom))
                {
                    LogDebug("ResolveSafe cached {0}", idef);
                    return prom;
                }
                else
                {
                    LogDebug("ResolveSafe {0}", idef);

                    var np = Trap(() => Resolve(__instance, type, id));
                    LogDebug("Cleanup {0}", idef);
                    
                    np.Done(() =>
                    {
                        LogDebug("Check-Deps {0}", idef);
                        var dl = Trap(() => __instance.DependenciesLoaded(1000000));

                        if (dl) LogDebug("Resolved <?fixme T?> {0}", idef);
                        else
                        {
                            if (ResolveDepsAsync.WantVerify)
                            {
                                RequestDependencies_DryRun = true;
                                dryRun = new List<string>();
                                var lcopy = Trap(() =>
                                {
                                    if (__instance.DataManager == null)
                                        LogError("Can't find DM");
                                    __instance.RequestDependencies(__instance.DataManager, () => { }, dummyload); // Have to create a dummy request only for the stupid request weights.
                                    return string.Join(" ", dryRun.ToArray());
                                });
                                dryRun = null;
                                RequestDependencies_DryRun = false;


                                LogError("{0}desynchronized {1} [{2}]", lcopy.Any() ? "is" : "semi-", idef, lcopy);
                            }
                            else LogError("is-desynchronized {1}", idef);
                        }
                    }); 

                    cache[__instance] = np;
                    return np;
                }
            }

            
            static bool RequestDependencies_DryRun = false;
            /* This is the patch function which harmony calls for all ILoadDependencies types */
            public static bool RequestDependencies(DataManager.ILoadDependencies __instance, DataManager dataManager, Action onDependenciesLoaded, DataManager.DataManagerLoadRequest loadRequest)
            {
                //FIXME: loadRequest is coming through with the wrong ID!

                // Something is running a dependency check, we want to ignore it.
                return Trap(() =>
                {
                    if (RequestDependencies_DryRun)
                    {
                        LogDebug("Depcheck");
                        return true;
                    }

                    var t = __instance.GetType();
                    if (resolveMap.TryGetValue(t.FullName, out var rescls))
                    {
                        // we handle it
                        LogDebug("Resolver<T>.RequestDependencies where T = {0} && {1}", t.FullName, rescls.GetType().FullName);
                        var rs = rescls.GetType()
                            .GetMethod("ResolveSafe");
                        if (rs == null)
                            LogError("Unable to find ResolveSafe function");

                        var prom = (IPromise)rs.Invoke(rescls, new object[] { __instance, dataManager, loadRequest.ResourceType, loadRequest.ResourceId });
                        if (!onDependenciesLoaded.Method.DeclaringType.FullName.StartsWith("BattleTechPerformanceFix"))
                            prom.Done(onDependenciesLoaded);  // This is going to duplicate *a lot* of work.
                        return false;
                    }
                    else
                    {
                        // DM handles it
                        LogDebug("Resolver<T>.RequestDependencies where T = {0} did not resolve and will pass through", t.FullName);
                        return true;
                    }
                });
            }
        }

        class ProxyResolver<T,K> : Resolver<T>
            where T : DataManager.ILoadDependencies
            where K : DataManager.ILoadDependencies
        {
            internal override IPromise Resolve(T __instance, RT type, string id)
            {
                LogDebug("ProxyResolveStart {0}", __instance == null ? "null" : "ok");
                var t = __instance.GetType();
                var k = typeof(K);
                if (resolveMap.TryGetValue(k.FullName, out var rescls))
                {
                    // we handle it
                    LogDebug("ResolverProxy<T->K>.Resolve where T = {0} K = {1} && {2}", t.FullName, k.FullName, rescls.GetType().FullName);
                    var rs = rescls.GetType()
                        .GetMethod("Resolve", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (rs == null)
                        LogError("Unable to find Proxied resolve function function");
                    LogDebug("Found proxied resolve");

                    return Trap(() => (IPromise)rs.Invoke(rescls, new object[] { __instance, type, id }));
                } else
                {
                    return Promise.Rejected(new Exception(string.Format("ResolverProxy<T->K>.Resolve FAILED where T = {0} K = {1} && {2}", t.FullName, k.FullName, rescls.GetType().FullName)));
                }
            }
        }

        class HeraldryDefResolver : Resolver<HeraldryDef>
        {
            internal override IPromise Resolve(HeraldryDef __instance, RT type, string id)
                => Promise.All(Load(RT.Texture2D, __instance.textureLogoID)
                                   , Promise.All(Sequence(__instance.primaryMechColorID, __instance.secondaryMechColorID, __instance.tertiaryMechColorID)
                                                     .Where(color => !string.IsNullOrEmpty(color))
                                                     .Select(color => Load(RT.ColorSwatch, color))));
        }

        class ChassisDefResolver : Resolver<ChassisDef>
        {
            internal override IPromise Resolve(ChassisDef __instance, RT type, string id)
            {
                LogDebug("Chassis-__instance: {0}", __instance == null ? "null" : "ok");
                LogDebug("Chassis-type: {0}", Enum.GetName(typeof(RT), type));
                LogDebug("Chassis-id: {0}", id);

                return Promise.All( __instance.FixedEquipment == null ? Promise.Resolved() : Promise.All(__instance.FixedEquipment.Select(equip => equip.Resolve(__instance.DataManager)))
                                  , __instance.FixedEquipment == null ? Promise.Resolved() : Promise.All(__instance.FixedEquipment.Where(equip => equip.Def != null && !string.IsNullOrEmpty(equip.prefabName)).Select(equip => Load(RT.Prefab, equip.prefabName)))
                                  , Load(RT.Prefab, __instance.PrefabIdentifier)
                                  , string.IsNullOrEmpty(__instance.Description.Icon) ? Promise.Resolved() : Load(RT.Sprite, __instance.Description.Icon)
                                  , Load(RT.HardpointDataDef, __instance.HardpointDataDefID)
                                  , Load(RT.MovementCapabilitiesDef, __instance.MovementCapDefID)
                                  , Load(RT.PathingCapabilitiesDef, __instance.PathingCapDefID))
                              .Then(() => __instance.Refresh());
            }
        }

        class AbilityDefResolver : Resolver<AbilityDef>
        {
            internal override IPromise Resolve(AbilityDef __instance, RT type, string id)
            {
                return Promise.All( string.IsNullOrEmpty(__instance.Description.Icon) ? Promise.Resolved() : Load(RT.SVGAsset, __instance.Description.Icon)
                                  , Promise.All(__instance.EffectData.Where(eff => !string.IsNullOrEmpty(eff.Description.Icon)).Select(eff => Load(RT.SVGAsset, eff.Description.Icon)))
                                  , string.IsNullOrEmpty(__instance.WeaponResource) ? Promise.Resolved() : Load(RT.WeaponDef, __instance.WeaponResource));
            }
        }

        class BaseComponentRefResolver : Resolver<BaseComponentRef>
        {
            internal override IPromise Resolve(BaseComponentRef __instance, RT type, string id)
            {
                return Load<MechComponentRef>(__instance.GetResourceType(), __instance.ComponentDefID)
                    .Then(def =>
                    {
                        new Traverse(__instance).Property("Def").SetValue(def);
                        __instance.Def.Resolve(__instance.DataManager);
                    });
            }
        }

        class PilotDefResolver : Resolver<PilotDef>
        {
            internal override IPromise Resolve(PilotDef __instance, RT type, string id)
            {
                //                                                                                                                             Add LoadAndResolve?
                return Promise.All(__instance.abilityDefNames == null ? Promise.Resolved() : Promise.All(__instance.abilityDefNames.Select(name => Load<AbilityDef>(RT.AbilityDef, name).Then(def => def.Resolve(__instance.DataManager))))
                                  , __instance.PortraitSettings == null ? Promise.Resolved() : Load(RT.PortraitSettings, __instance.PortraitSettings.Description.Id)
                                  , string.IsNullOrEmpty(__instance.Description.Id) ? Promise.Resolved() : Load(RT.Sprite, __instance.Description.Icon));
            }
        }

        class WeaponDefResolver : Resolver<WeaponDef>
        {
            internal override IPromise Resolve(WeaponDef __instance, RT type, string id)
            {
                return Promise.All(Load(RT.Prefab, __instance.WeaponEffectID)
                                  , string.IsNullOrEmpty(__instance.AmmoCategoryToAmmoId) ? Promise.Resolved() : Load(RT.AmmunitionDef, __instance.AmmoCategoryToAmmoId)
                                  , string.IsNullOrEmpty(__instance.AmmoCategoryToAmmoBoxId) ? Promise.Resolved() : Load(RT.AmmunitionBoxDef, __instance.AmmoCategoryToAmmoBoxId)
                                  , string.IsNullOrEmpty(__instance.Description.Icon) ? Promise.Resolved() : Load(RT.SVGAsset, __instance.Description.Icon)
                                  // Status effects could be abstracted out. Used multiple times.
                                  , Promise.All(__instance.statusEffects.Where(eff => !string.IsNullOrEmpty(eff.Description.Icon)).Select(eff => Load(RT.SVGAsset, eff.Description.Icon))));
            }
        }

        class MechDefResolver : Resolver<MechDef>
        {
            internal override IPromise Resolve(MechDef __instance, RT type, string id)
            {
                // FIXME: id is wrong here, because the loadrequest has the wrong id.
                if (__instance.Description.Id != id)
                    LogWarning("ID mismatch {0} but desc says {1}", id, __instance.Description.Id);
                __instance.meleeWeaponRef.DataManager = __instance.dfaWeaponRef.DataManager = __instance.imaginaryLaserWeaponRef.DataManager = __instance.DataManager;
                return Promise.All( __instance.ChassisID.OfString(RT.ChassisDef)
                                              .Then(() => __instance.Refresh())
                                              .Then(() => __instance.Chassis.Resolve(__instance.DataManager))
                                  , __instance.HeraldryID.OfString(RT.HeraldryDef)
                                  , Promise.All(__instance.Inventory.Select(inv => inv.Resolve(__instance.DataManager)))
                                  , __instance.meleeWeaponRef.Resolve(__instance.DataManager)
                                  , __instance.dfaWeaponRef.Resolve(__instance.DataManager)
                                  , __instance.imaginaryLaserWeaponRef.Resolve(__instance.DataManager)
                                  , Promise.All(__instance.Inventory.Select(inv => inv.prefabName.OfString(RT.Prefab))));
            }
        }

        class VehicleDefResolver : Resolver<VehicleDef>
        {
            internal override IPromise Resolve(VehicleDef __instance, RT type, string id)
            {
                if (__instance.Description.Id != id)
                    LogWarning("ID mismatch {0} but desc says {1}", id, __instance.Description.Id);
                __instance.imaginaryLaserWeaponRef.DataManager = __instance.DataManager;
                return Promise.All(__instance.ChassisID.OfString(RT.ChassisDef)
                                             .Then(() => __instance.Refresh())
                                             .Then(() => __instance.Chassis.Resolve(__instance.DataManager))
                                  , __instance.HeraldryID.OfString(RT.HeraldryDef)
                                  , Promise.All(__instance.Inventory.Select(inv => inv.Resolve(__instance.DataManager)))
                                  , __instance.imaginaryLaserWeaponRef.Resolve(__instance.DataManager));
            }
        }

        class MechComponentDefResolver : Resolver<MechComponentDef>
        {
            internal override IPromise Resolve(MechComponentDef __instance, RT type, string id)
            {
                return Promise.All( __instance.Description.Icon.OfString(RT.SVGAsset)
                                  , Promise.All(__instance.statusEffects.Where(eff => !string.IsNullOrEmpty(eff.Description.Icon)).Select(eff => Load(RT.SVGAsset, eff.Description.Icon))));
            }
        }

        class FactionDefResolver : Resolver<FactionDef>
        {
            internal override IPromise Resolve(FactionDef __instance, RT type, string id)
            {
                return Promise.All(__instance.GetSpriteName().OfString(RT.Sprite)
                                  , __instance.DefaultRepresentativeCastDefId.OfString(RT.CastDef)
                                  , HeraldryDef.IsSpecialHeraldryDefId(__instance.heraldryDefId) ? Promise.Resolved() : __instance.heraldryDefId.OfString(RT.HeraldryDef));
            }
        }

        class BackgroundDefResolver : Resolver<BackgroundDef>
        {
            internal override IPromise Resolve(BackgroundDef __instance, RT type, string id)
            {
                return string.IsNullOrEmpty(__instance.Description.Id) ? Promise.Resolved() : Load(RT.Sprite, new Traverse(__instance).Property("TxtrId").GetValue<string>());
            }
        }

        class MechComponentRefResolver : ProxyResolver<MechComponentRef, BaseComponentRef> { }
        class AmmunitionBoxDefResolver : ProxyResolver<AmmunitionBoxDef, MechComponentDef> { }
        class HeatSinkDefResolver : ProxyResolver<HeatSinkDef, MechComponentDef> { }
        class JumpJetDefResolver : ProxyResolver<JumpJetDef, MechComponentDef> { }
        class UpgradeDefResolver : ProxyResolver<UpgradeDef, MechComponentDef> { }

        static Dictionary<string,int> track = new Dictionary<string,int>();

        public static void TrackRequestResource(DataManager __instance, BattleTechResourceType resourceType, PrewarmRequest prewarm)
        {
            var key = string.Format("{0}:{1}", Enum.GetName(typeof(RT), resourceType), prewarm != null);
            if (!track.ContainsKey(key)) track[key] = 0;
            track[key]++;
        }

        public static void IntegrityCheck(BattleTechResourceLocator __instance) {
            Trap(() => { 
                var manifest = new Traverse(__instance).Field("baseManifest").GetValue<Dictionary<BattleTechResourceType, Dictionary<string, VersionManifestEntry>>>();

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


            Control.Log("(File-duplicates {0})", string.Join(" ", counts.ToArray()));
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
        public static bool ProcessRequests(List<DataManager.DataManagerLoadRequest> ___backgroundRequestsList, List<DataManager.DataManagerLoadRequest> ___foregroundRequestsList)
        {
            LogDebug("ProcessRequests from\n{0}\n\n", new StackTrace().ToString());
            LogDebug("FRL {0}", Newtonsoft.Json.JsonConvert.SerializeObject(___foregroundRequestsList, Newtonsoft.Json.Formatting.Indented));
            LogDebug("BRL {0}", Newtonsoft.Json.JsonConvert.SerializeObject(___backgroundRequestsList, Newtonsoft.Json.Formatting.Indented));

            return false;
        }

        public static void LoadCompleteMessage(MessageCenterMessage msg)
        {
            LogDebug("LoadCompleteMessage");
        }

        public static void AsyncLoadCompleteMessage(MessageCenterMessage msg)
        {
            LogDebug("AsyncLoadCompleteMessage");
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
            }

            var entry = stuff.dataManager.ResourceLocator.EntryByID(identifier, resourceType, false);
            if (resourceType == RT.MechDef || resourceType == RT.ChassisDef)
                return false;

            LogDebug("Request {0}:{1}", identifier, resourceType.AsString());

            if (depsdb != null)
            {
                depsdb.Add(identifier);
                return true;
            }

            return true;


            if (dryRun != null)
            {
                dryRun.Add(string.Format("{0}:{1}", identifier, Enum.GetName(typeof(RT), resourceType)));
                return false;
            }
            LogDebug("Request {0}:{1}", identifier, Enum.GetName(typeof(RT), resourceType));

            

            //if (resourceType == RT.MechDef)
            //    return false;

            return true;

            if (stuff.CanHandleType(resourceType)) //resourceType == RT.SimGameConstants || resourceType == RT.MechDef || resourceType == RT.BaseDescriptionDef || resourceType == RT.SimGameMilestoneDef || resourceType == RT.ShipModuleUpgrade || resourceType == RT.PortraitSettings)
            {
                var st = new StackTrace();
                LogDebug("custom request: {0}:{1}", identifier, Enum.GetName(typeof(RT), resourceType));

                // This is glue to keep the DataManagerLoadComplete request intact, until we handle all the calls
                var dlr = new DummyLoadRequest(__instance, string.Format("{0}:{1}", identifier, resourceType.AsString()));
                new Traverse(__instance).Method("AddForegroundLoadRequest", resourceType, identifier, dlr).GetValue();

                stuff.LoadObj(resourceType, identifier)
                     .Done(x =>
                     {
                         LogDebug("stuff.Load for {0}:{1}", identifier, resourceType.AsString());

                         var realType = Trap(() => x.GetType());

                         // Need to use reflection here since we don't know the type of x at compile time.
                         var gt = typeof(DataManagerRequestCompleteMessage<>).MakeGenericType(realType);
                         var ctor = gt.GetConstructors(AccessTools.all).SingleOrDefault();
                         if (ctor == null)
                             LogError("Unable to find DMRCM<?> constructor");
                         var msg = Trap(() => ctor.Invoke(Array(resourceType, identifier, x)));
                         LogDebug("Announce type {0}", msg?.GetType()?.FullName);

                         // FIXME: Instead of all the above, have the dummyloadrequest do it.
                         // Note: This can cause issues since the vent triggers *before* the rest of the dependency chain resolves
                         //   This will be fixed when no longer utilizing DataManager.                      
                         stuff.messageCenter.PublishMessage((MessageCenterMessage)ctor.Invoke(Array(resourceType, identifier, x)));

                         LogDebug("CompleteDummyLoaderRequest: {0}", identifier, dlr.ResourceId);
                        
                         // This is a problem, we need to wait on dependencies since battletech doesn't actually use the Promises.

                         dlr.Complete();
                     }
                     , err => Trap(() => throw err));
                return false;
            }
            return true;
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

            loadDB = FigureItOut();
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

        public Dictionary<RT, KeyValuePair<Func<VersionManifestEntry, IPromise<object>>, Action<string>>> FigureItOut()
        {
            var bttypes = (RT[])Enum.GetValues(typeof(RT));
            var bttypess = Enum.GetNames(typeof(RT));
            var dstores = AccessTools.GetDeclaredFields(typeof(DataManager))
                .Where(field => field.FieldType.FullName.Contains("DictionaryStore"))
                .ToList();

            bool IsJsonBacked(Type t)
            {
                return t.GetInterface(typeof(HBS.Util.IJsonTemplated).FullName) != null;
            }

            var s = dstores.Partition(field => field.FieldType.GetGenericArguments()[0].Let(storeType => IsJsonBacked(storeType) && bttypess.Contains(storeType.Name)));
            var dstores_auto = s.Key;
            var dstores_manual = s.Value;

            var foo = new Dictionary<RT, KeyValuePair<Func<VersionManifestEntry, IPromise<object>>, Action<string>>>();
            void Add(RT ty, Func<VersionManifestEntry, IPromise<object>> load, Action<string> unload)
                => foo[ty] = new KeyValuePair<Func<VersionManifestEntry, IPromise<object>>, Action<string>>(load, unload);


            IPromise<object> AddToStore(string fldName, VersionManifestEntry entry, IPromise<object> x)
            {
                return x.Then(xv =>
                {
                    try
                    {
                        var DS = new Traverse(dataManager).Field(fldName);
                        if (!DS.GetValueType().FullName.Contains("DictionaryStore"))
                            LogError("Failed to fetch dictionary store for :field {0} :got {1}", fldName, DS?.GetValueType()?.FullName);

                        LogDebug("Adding {1} to {0}", fldName, entry.Id);
                        if (DS.Method("Exists", entry.Id).GetValue<bool>())
                        {
                            LogDebug("Item already in DM");
                        }
                        else
                        {
                            LogDebug("Adding Item to DM");
                            Trap(() => DS.Method("Add", entry.Id, xv).GetValue());
                            LogDebug("Added Item to DM");
                        }
                    }
                    catch (Exception e)
                    {
                        LogException(e);
                        LogError("Unable to check if item exists");
                    }
                    return xv;
                });
            }

            dstores_auto.ForEach(field =>
            {
                var ga = field.FieldType.GetGenericArguments()[0];
                if (!IsJsonBacked(ga)) LogError("Non JsonBacked item made it to dstores_auto");
                var bt = (RT)Enum.Parse(typeof(RT), ga.Name);


                var fldName = field.Name;                

                Add(bt
                   , (entry) => AddToStore(fldName, entry, LoadJsonD(entry, ga))
                   , (str) => {/* TODO: remove from DictionaryStore */}
                    );
            });
             
            var auto = foo.Keys.Select(key => key.AsString()).ToArray();
            var manual = bttypes.Where(ty => !foo.Keys.Contains(ty)).Select(key => key.AsString()).ToArray();

            Log("Found[Auto {0}/{1}]: {2}", auto.Length, bttypes.Length, Newtonsoft.Json.JsonConvert.SerializeObject(auto));
            Log("Found[Manual {0}/{1}]: {2}", manual.Length, bttypes.Length, Newtonsoft.Json.JsonConvert.SerializeObject(manual, Newtonsoft.Json.Formatting.Indented));

            //Add(RT.ItemCollectionDef, (entry) => AddToStore("itemCollectionDef", entry, LoadCSVD(entry, typeof(ItemCollectionDef))), (str) => LogError("NYI CSV unload"));
            Add(RT.SimGameConstants
               , (entry) => LoadMapper(entry, entry.Type.ToRT(), entry.Id, Control.Identity, (TextAsset text) => text.text).Then(txt => (object)txt)
               , (str) => LogError("NYI SGC unload"));

            // Prefabs will require special handling as they must be cloned.
            Add(RT.Prefab
               , (entry) => dataManager.IsPrefabInPool(entry.Id) ? Promise<object>.Resolved(dataManager.GetPooledPrefab(entry.Id)) : LoadMapper(entry, entry.Type.ToRT(), entry.Id, null, (GameObject prefab) => { dataManager.AddPrefabToPool(entry.Id, prefab); return (object)prefab; })
               , (str) => LogError("NYI Prefab unload"));

            // FIXME: Requires caching, has DLC checks
            // Odd interaction here.
            Add(RT.Texture2D
                , (entry) =>
                {
                    var prom = new Promise<Texture2D>();
                    if (entry.IsFileAsset) textureManager.RequestTexture(entry.Id, new TextureLoaded(prom.Resolve), new LoadFailed(err => prom.Reject(new System.Exception("Texture2d(manager) failed " + err))));
                    else LoadMapper(entry, entry.Type.ToRT(), entry.Id, null, (Texture2D t) => { textureManager.InsertTexture(entry.Id, t); return t; })
                       .Done(x => prom.Resolve(x), prom.Reject);
                    return prom.Then(x => (object)x);
                }
                , (str) => LogError("NYI Texture unload"));

            Add(RT.SVGAsset
               , (entry) => LoadMapper(entry, entry.Type.ToRT(), entry.Id, null, (SVGAsset svg) => (object)svg)
               , (str) => LogError("NYI SVG unload"));

            // FIXME: Sprite does some funky stuff with texture2d.
            //   it tries to load a texture2d with the exact same id. I don't like this
            Add( RT.Sprite
                , (entry) => LoadMapper(entry, entry.Type.ToRT(), entry.Id, null, (Sprite sprite) => (object)sprite, (yes, no) => SpriteFromDisk(entry.FilePath))
                , (str) => LogError("NYI sprite unload"));

            Trap(() =>
            {
                Add(RT.ColorSwatch
                    , (entry) => AddToStore("colorSwatches", entry, LoadMapper(entry, entry.Type.ToRT(), entry.Id, null, (ColorSwatch cs) => (object)cs))
                    , (str) => LogError("NYI ColorSwatch unload"));
            });

            var manual2 = bttypes.Where(ty => !foo.Keys.Contains(ty)).Select(key => key.AsString()).ToArray();
            Log("!Missing![Manual {0}/{1}]: {2}", manual2.Length, bttypes.Length, Newtonsoft.Json.JsonConvert.SerializeObject(manual2, Newtonsoft.Json.Formatting.Indented));

            return foo;
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
                //FIXME: Verify t here
                HBS.Util.IJsonTemplated a = (HBS.Util.IJsonTemplated)Activator.CreateInstance(t);
                a.FromJSON(json);
                //Log("JSON send");
                if (a == null)
                    LogError("LoadJsonD resulted in a null value for {0} {1}", entry.Id, t.FullName);
                return a;
            }

            return LoadMapper(entry, (RT)Enum.Parse(typeof(RT), entry.Type), entry.Id, Make, (TextAsset r) => Make(r.text));
        }

        // TODO: Looks like resource and bundle are the same type always, if so reduce them into one selector
        public IPromise<T> LoadMapper<T, R>(VersionManifestEntry entry, BattleTechResourceType resourceType, string identifier, Func<string, T> file, Func<R, T> resource, AcceptReject<T> recover = null) 
            where R : UnityEngine.Object 
        {
            var res = new Promise<T>();
            try
            {
                if (entry.IsFileAsset && file != null) dataLoader.LoadResource(entry.FilePath, c => res.Resolve(file(c)));
                else if (entry.IsResourcesAsset && resource != null) res.Resolve(resource(Resources.Load<R>(entry.ResourcesLoadPath)));
                else if (entry.IsAssetBundled && resource != null) bundleManager.RequestAsset<R>(resourceType, identifier, b => res.Resolve(resource(b)));
                else if (recover != null)
                {   try
                    {
                        recover(res.Resolve, res.Reject);
                    } catch (Exception e)
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