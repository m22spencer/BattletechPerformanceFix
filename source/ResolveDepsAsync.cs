using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BattleTech.Data;
using BattleTech;
using BattleTech.Assetbundles;
using Harmony;
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
    static class ResolveExt
    {
        public static IPromise Resolve(this DataManager.ILoadDependencies cls)
        {
            LogError("NYI");
            throw new System.Exception("STOP");
        }
    }

    class ResolveDepsAsync : Feature
    {
        public void Activate()
        {
            var wantTracking = true;

            var t = typeof(ResolveDepsAsync);
            harmony.Patch(AccessTools.Method(typeof(BattleTechResourceLocator), "RefreshTypedEntries"), null, new HarmonyMethod(AccessTools.Method(t, nameof(IntegrityCheck))));

            if (wantTracking)
            {
                Log("Tracking is ON");
                Assembly.GetAssembly(typeof(HeraldryDef))
                    .GetTypes()
                    .Where(ty => ty.GetInterface(typeof(DataManager.ILoadDependencies).FullName) != null)
                    .ForEach(ild =>
                    {
                        var pre = new HarmonyMethod(AccessTools.Method(t, "TrackPre"));
                        HarmonyMethod post = null; // new HarmonyMethod(AccessTools.Method(t, "TrackPost"));
                        harmony.Patch(AccessTools.Method(ild, "CheckDependenciesAfterLoad"), pre, post);
                        harmony.Patch(AccessTools.Method(ild, "DependenciesLoaded"), pre, post);
                        harmony.Patch(AccessTools.Method(ild, "RequestDependencies"), pre, post);
                    });

                harmony.Patch(AccessTools.Method(typeof(BattleTech.UI.SimGameOptionsMenu), "OnAddedToHierarchy"), new HarmonyMethod(AccessTools.Method(t, "Summary")));
                harmony.Patch(AccessTools.Method(typeof(DataManager), "RequestResource_Internal"), new HarmonyMethod(AccessTools.Method(t, nameof(TrackRequestResource))));
            }
            
            Log("CDAL fix on");
            var drop = AccessTools.Method(t, nameof(Drop));

            var resolver = AccessTools.Method(typeof(Resolver<ChassisDef>), "RequestDependencies"); //just using ChassisDef here to reference the static function. It means nothing;

            var c = true;

            Assembly.GetAssembly(typeof(HeraldryDef))
                .GetTypes()
                .Where(ty => ty.GetInterface(typeof(DataManager.ILoadDependencies).FullName) != null)
                .ForEach(ildtype =>
                {
                    harmony.Patch(AccessTools.Method(ildtype, "CheckDependenciesAfterLoad"), new HarmonyMethod(drop));
                    //harmony.Patch(AccessTools.Method(ildtype, "DependenciesLoaded"), new HarmonyMethod(drop));
                    harmony.Patch(AccessTools.Method(ildtype, "RequestDependencies"), new HarmonyMethod(resolver));
                });

            harmony.Patch(AccessTools.Method(typeof(DataManager), "RequestResource_Internal"), new HarmonyMethod(AccessTools.Method(t, nameof(RequestResources_Internal2))));

            new ChassisDefResolver();
            new HeraldryDefResolver();
            new AbilityDefResolver();

            return;
            harmony.Patch(AccessTools.Method(typeof(HeraldryDef), "CheckDependenciesAfterLoad"), new HarmonyMethod(drop));
            //harmony.Patch(AccessTools.Method(typeof(HeraldryDef), "DependenciesLoaded"), new HarmonyMethod(drop));
            harmony.Patch(AccessTools.Method(typeof(HeraldryDef), "RequestDependencies"), new HarmonyMethod(c ? resolver : AccessTools.Method(t, nameof(RequestDependencies_HeraldryDef))));
             
            //*
            harmony.Patch(AccessTools.Method(typeof(ChassisDef), "CheckDependenciesAfterLoad"), new HarmonyMethod(drop));
            //harmony.Patch(AccessTools.Method(typeof(ChassisDef), "DependenciesLoaded"), new HarmonyMethod(drop));
            harmony.Patch(AccessTools.Method(typeof(ChassisDef), "RequestDependencies"), new HarmonyMethod(c ? resolver : AccessTools.Method(t, nameof(RequestDependencies_ChassisDef))));
            //*/

            //
            harmony.Patch(AccessTools.Method(typeof(BaseComponentRef), "CheckDependenciesAfterLoad"), new HarmonyMethod(drop));
            //harmony.Patch(AccessTools.Method(typeof(ChassisDef), "DependenciesLoaded"), new HarmonyMethod(drop));
            harmony.Patch(AccessTools.Method(typeof(BaseComponentRef), "RequestDependencies"), new HarmonyMethod(c ? resolver : AccessTools.Method(t, nameof(RequestDependencies_BaseComponentRef))));
            //*/


            harmony.Patch(AccessTools.Method(typeof(AbilityDef), "CheckDependenciesAfterLoad"), new HarmonyMethod(drop));
            //harmony.Patch(AccessTools.Method(typeof(ChassisDef), "DependenciesLoaded"), new HarmonyMethod(drop));
            harmony.Patch(AccessTools.Method(typeof(AbilityDef), "RequestDependencies"), new HarmonyMethod(c ? resolver : AccessTools.Method(t, nameof(RequestDependencies_AbilityDef))));



        }

        /* Just to set the load request for the dependency verification */
        class DummyLoadRequest : DataManager.ResourceLoadRequest<object>
        {
            public DummyLoadRequest(DataManager dataManager) : base(dataManager, RT.AbilityDef, "", 10000, null) { }
            public override bool AlreadyLoaded { get => true; }
        }

        class Resolver<T>
            where T : DataManager.ILoadDependencies
        {
            public Dictionary<T,IPromise> cache = new Dictionary<T,IPromise>();
            public Resolver()
            {
                if (resolveMap.ContainsKey(typeof(T))) LogWarning("Resolve map duplicate for {0}", typeof(T).FullName);
                else resolveMap[typeof(T)] = this;
            }            

            internal virtual IPromise Resolve(T __instance, RT type, string id)
            {
                throw new System.Exception(string.Format("Missing Resolve<T>.Resolve for {0}", typeof(T).FullName));
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
                __instance.DataManager = dataManager;
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
                        if (__instance.DependenciesLoaded(1000000)) LogDebug("Resolved <?fixme T?> {0}", idef);
                        else
                        {
                            RequestDependencies_DryRun = true;
                            dryRun = new List<string>();
                            var lcopy = Trap(() =>
                            {
                                if (__instance.DataManager == null)
                                    LogError("Can't find DM");
                                __instance.RequestDependencies(__instance.DataManager, () => { }, new DummyLoadRequest(dataManager)); // Have to create a dummy request only for the stupid request weights.
                                return string.Join(" ", dryRun.ToArray());
                            });
                            dryRun = null;
                            RequestDependencies_DryRun = false;

                            LogError("desynchronized {0} [{1}]", idef , lcopy);
                        }
                    });
                    cache[__instance] = np;
                    return np;
                }
            }

            static Dictionary<Type, Resolver<T>> resolveMap = new Dictionary<Type, Resolver<T>>();
            static bool RequestDependencies_DryRun = false;
            /* This is the patch function which harmony calls for all ILoadDependencies types */
            public static bool RequestDependencies(DataManager.ILoadDependencies __instance, DataManager dataManager, Action onDependenciesLoaded, DataManager.DataManagerLoadRequest loadRequest)
            {
                // Something is running a dependency check, we want to ignore it.
                if (RequestDependencies_DryRun)
                    return true;

                var t = __instance.GetType();
                if (resolveMap.TryGetValue(t, out var rescls))
                {
                    // we handle it
                    Log("Resolver<T>.RequestDependencies where T = {0}", t.FullName);
                    rescls.ResolveSafe(__instance, dataManager, loadRequest.ResourceType, loadRequest.ResourceId)
                          .Done(onDependenciesLoaded);
                    return false;
                } else
                {
                    // DM handles it
                    Log("Resolver<T>.RequestDependencies where T = {0} did not resolve and will pass through", t.FullName);
                    return true;
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
                Log("Chassis-__instance: {0}", __instance == null ? "null" : "ok");
                Log("Chassis-type: {0}", Enum.GetName(typeof(RT), type));
                Log("Chassis-id: {0}", id);

                return Promise.All( Promise.Resolved() //__instance.FixedEquipment == null ? Promise.Resolved() : Promise.All(__instance.FixedEquipment.Select(equip => equip.Resolve()))
                                  , Promise.Resolved() //__instance.FixedEquipment == null ? Promise.Resolved() : Promise.All(__instance.FixedEquipment.Where(equip => equip.Def != null && !string.IsNullOrEmpty(equip.prefabName)).Select(equip => Load(RT.Prefab, equip.prefabName)))
                                  , Load(RT.Prefab, __instance.PrefabIdentifier)
                                  , !string.IsNullOrEmpty(__instance.Description.Icon) ? Load(RT.Sprite, __instance.Description.Icon) : Promise.Resolved()
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

        static Dictionary<string,int> track = new Dictionary<string,int>();

        static bool Guard;

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
            track.Clear();
        }

        public static bool Drop() => false;
    
        public static bool RequestDependencies_HeraldryDef(HeraldryDef __instance, DataManager dataManager, Action onDependenciesLoaded, DataManager.DataManagerLoadRequest loadRequest)
        {
            LogDebug("Raw Deprequest for {0}:{1} origin {2}", loadRequest.ResourceId, Enum.GetName(typeof(RT), loadRequest.ResourceType), new StackFrame(1).ToString());
            __instance.DataManager = dataManager;
            Resolve_HeraldryDef(__instance, loadRequest).Done(onDependenciesLoaded);

            return false;
        }
        
        public static void FooTest(List<IPromise> all, string ident, RT type)
        {
            if (!string.IsNullOrEmpty(ident)) all.Add(Load(type, ident));
        }

        class Deps
        {
            List<IPromise> all;
            public Deps()
            {
                all = new List<IPromise>();
            }

            public void FromField(RT type, string ident)
            {
                if (!string.IsNullOrEmpty(ident)) all.Add(Load(type, ident));
            }

            public void FromField<T>(RT type, string ident, Action<T> f)
            {
                if (!string.IsNullOrEmpty(ident)) all.Add(Load<T>(type, ident).Then(f));
            }
        }

        public static Dictionary<HeraldryDef, IPromise> HeraldryDefCache = new Dictionary<HeraldryDef, IPromise>();
        public static IPromise Resolve_HeraldryDef(HeraldryDef __instance, DataManager.DataManagerLoadRequest loadRequest)
        {
            if (HeraldryDefCache.TryGetValue(__instance, out var prom))
            {
                LogDebug("Resolve(cached) {0}:{1}", loadRequest.ResourceId, Enum.GetName(typeof(RT), loadRequest.ResourceType));
                return prom;
            }
            else
            {
                LogDebug("Resolve {0}:{1}", loadRequest.ResourceId, Enum.GetName(typeof(RT), loadRequest.ResourceType));
                
                var np = new HeraldryDefResolver().Resolve(__instance, loadRequest.ResourceType, loadRequest.ResourceId);
                np.Done(() =>
                {
                    if (__instance.DependenciesLoaded(1000000)) LogDebug("Resolved HeraldryDef {0}:{1}", loadRequest.ResourceId, Enum.GetName(typeof(RT), loadRequest.ResourceType));
                    else LogError(string.Format("HeraldryDef desynchronized {0}:{1}", loadRequest.ResourceId, Enum.GetName(typeof(RT), loadRequest.ResourceType)));
                });
                HeraldryDefCache[__instance] = np;
                return np;
            }
        }

        public static bool RD_CD_Guard = true;
        public static bool RequestDependencies_ChassisDef(ChassisDef __instance, DataManager dataManager, Action onDependenciesLoaded, DataManager.DataManagerLoadRequest loadRequest)
        {
            if (!RD_CD_Guard)
                return true;
            LogDebug("Raw Deprequest for {0}:{1}", loadRequest.ResourceId, Enum.GetName(typeof(RT), loadRequest.ResourceType));
            __instance.DataManager = dataManager;
            Resolve_ChassisDef(__instance, loadRequest).Done(onDependenciesLoaded);

            return false;
        }
        public static Dictionary<ChassisDef, IPromise> ChassisDefCache = new Dictionary<ChassisDef, IPromise>();
        public static IPromise Resolve_ChassisDef(ChassisDef __instance, DataManager.DataManagerLoadRequest loadRequest)
        {
            if (ChassisDefCache.TryGetValue(__instance, out var prom))
            {
                LogDebug("Resolve(cached) {0}:{1}", loadRequest.ResourceId, Enum.GetName(typeof(RT), loadRequest.ResourceType));
                return prom;
            }
            else
            {
                LogDebug("Resolve {0}:{1}", loadRequest.ResourceId, Enum.GetName(typeof(RT), loadRequest.ResourceType));

                var np = Trap(() => new ChassisDefResolver().Resolve(__instance, loadRequest.ResourceType, loadRequest.ResourceId));
                np.Done(() =>
                {
                    // Possibility for deps validation
                    if (__instance.DependenciesLoaded(1000000)) LogDebug("Resolved ChassisDef {0}:{1}", loadRequest.ResourceId, Enum.GetName(typeof(RT), loadRequest.ResourceType));
                    else
                    {
                        RD_CD_Guard = false;
                        dryRun = new List<string>();
                        var lcopy = Trap(() =>
                        {
                            if (__instance.DataManager == null)
                                LogError("ChassisDef: Can't find DM");
                            __instance.RequestDependencies(__instance.DataManager, () => { }, loadRequest);
                            return string.Join(" ", dryRun.ToArray());
                        });
                        dryRun = null;
                        RD_CD_Guard = true;

                        LogError("ChassisDef desynchronized {0}:{1} [{2}]", loadRequest.ResourceId, Enum.GetName(typeof(RT), loadRequest.ResourceType), lcopy);
                    }
                });
                ChassisDefCache[__instance] = np;
                return np;
            }
        }

        public static bool RD_AD_Guard = true;
        public static bool RequestDependencies_AbilityDef(AbilityDef __instance, DataManager dataManager, Action onDependenciesLoaded, DataManager.DataManagerLoadRequest loadRequest)
        {
            if (!RD_AD_Guard)
                return true;
            LogDebug("Raw Deprequest for {0}:{1}", loadRequest.ResourceId, Enum.GetName(typeof(RT), loadRequest.ResourceType));
            __instance.DataManager = dataManager;
            Resolve_AbilityDef(__instance, loadRequest).Done(onDependenciesLoaded);

            return false;
        }
        public static Dictionary<AbilityDef, IPromise> AbilityDefCache = new Dictionary<AbilityDef, IPromise>();
        public static IPromise Resolve_AbilityDef(AbilityDef __instance, DataManager.DataManagerLoadRequest loadRequest)
        {
            if (AbilityDefCache.TryGetValue(__instance, out var prom))
            {
                LogDebug("Resolve(cached) {0}:{1}", loadRequest.ResourceId, Enum.GetName(typeof(RT), loadRequest.ResourceType));
                return prom;
            }
            else
            {
                LogDebug("Resolve {0}:{1}", loadRequest.ResourceId, Enum.GetName(typeof(RT), loadRequest.ResourceType));

                var np = Trap(() => new AbilityDefResolver().Resolve(__instance, loadRequest.ResourceType, loadRequest.ResourceId));
                np.Done(() =>
                {
                    // Possibility for deps validation
                    if (__instance.DependenciesLoaded(1000000)) LogDebug("Resolved ChassisDef {0}:{1}", loadRequest.ResourceId, Enum.GetName(typeof(RT), loadRequest.ResourceType));
                    else
                    {
                        RD_CD_Guard = false;
                        dryRun = new List<string>();
                        var lcopy = Trap(() =>
                        {
                            if (__instance.DataManager == null)
                                LogError("AbilityDef: Can't find DM");
                            __instance.RequestDependencies(__instance.DataManager, () => { }, loadRequest);
                            return string.Join(" ", dryRun.ToArray());
                        });
                        dryRun = null;
                        RD_CD_Guard = true;

                        LogError("AbilityDef desynchronized {0}:{1} [{2}]", loadRequest.ResourceId, Enum.GetName(typeof(RT), loadRequest.ResourceType), lcopy);
                    }
                });
                AbilityDefCache[__instance] = np;
                return np;
            }
        }

        public static bool RequestDependencies_BaseComponentRef(BaseComponentRef __instance, DataManager dataManager, Action onDependenciesLoaded, DataManager.DataManagerLoadRequest loadRequest)
        {
            LogDebug("Raw Deprequest for {0}:{1}", loadRequest.ResourceId, Enum.GetName(typeof(RT), loadRequest.ResourceType));
            __instance.DataManager = dataManager;
            Resolve_BaseComponentRef(__instance, loadRequest).Done(onDependenciesLoaded);

            return false;
        }
        public static Dictionary<BaseComponentRef, IPromise> BaseComponentRefCache = new Dictionary<BaseComponentRef, IPromise>();
        public static IPromise Resolve_BaseComponentRef(BaseComponentRef __instance, DataManager.DataManagerLoadRequest loadRequest)
        {
            if (BaseComponentRefCache.TryGetValue(__instance, out var prom))
            {
                LogDebug("Resolve(cached) {0}:{1}", loadRequest.ResourceId, Enum.GetName(typeof(RT), loadRequest.ResourceType));
                return prom;
            }
            else
            {
                LogDebug("Resolve {0}:{1}", loadRequest.ResourceId, Enum.GetName(typeof(RT), loadRequest.ResourceType));

                var np = Trap(() =>
                {
                    var all = new List<IPromise>();

                    all.Add(Load<MechComponentDef>(__instance.GetResourceType(), __instance.ComponentDefID)
                        .Then(def =>
                        {
                            new Traverse(__instance).Property("Def").SetValue(def);
                            //__instance.RefreshComponentDef();
                            var ppm = new Promise();
                            // FIXME: Must do a dispatch to the proper resolve function, we need a lookup table
                            LogDebug("Request dependencies of WeaponDef {0}", def?.Description?.Id);
                            if (__instance.Def != null) __instance.Def.RequestDependencies(__instance.DataManager, ppm.Resolve, loadRequest);
                            else ppm.Resolve();
                            return (IPromise)ppm;
                        }));

                    return Promise.All(all)
                        .Then(() =>
                        {
                            LogDebug("Resolved BaseComponentRef {0}:{1}", loadRequest.ResourceId, Enum.GetName(typeof(RT), loadRequest.ResourceType));
                            LogDebug("Refreshed {0}:{1}", loadRequest.ResourceId, Enum.GetName(typeof(RT), loadRequest.ResourceType));
                        }
                             , err => LogException(err));
                });
                BaseComponentRefCache[__instance] = np;
                return np;
            }
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
            Trap(() => stuff.RequestResource(type, id, new PrewarmRequest(), false, false));
            return Ensure(id).Unit();
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

            //FIXME: need to capture any multiple resolved promise here and do some cache correcting
            try
            {
                Ensure(id)
                    .Resolve(val);
            } catch (Exception e)
            {
                LogError("Asset {0} already dispatched", id);
            }
        }

        public static List<string> dryRun = null;
        public static bool RequestResources_Internal2(MethodInfo __originalMethod, DataManager __instance, BattleTechResourceType resourceType, string identifier, PrewarmRequest prewarm, bool allowRequestStacking, bool filterByOwnership)
        {
            if (dryRun != null)
            {
                dryRun.Add(string.Format("{0}:{1}", identifier, Enum.GetName(typeof(RT), resourceType)));
                return false;
            }
            LogDebug("Request {0}:{1}", identifier, Enum.GetName(typeof(RT), resourceType));
            if (!Initialized)
            {
                stuff = new Stuff(__instance);
                Initialized = true;
                stuff.messageCenter.AddSubscriber(MessageCenterMessageType.DataManagerRequestCompleteMessage, new ReceiveMessageCenterMessage(DispatchAssetLoad));
            }
            return true;
        }

        public static bool CollectDeps = false;
        public static int CollectDepsDepth = 0;
        public static bool Halt = false;
        public static bool RequestResources_Internal(MethodInfo __originalMethod, DataManager __instance, BattleTechResourceType resourceType, string identifier, PrewarmRequest prewarm, bool allowRequestStacking, bool filterByOwnership)
        {
            var stuff = new Stuff(__instance);
            
            // Just temporarily testing the waters here before writing the dependency functions
            if (resourceType == RT.Texture2D || resourceType == RT.SimGameConstants || resourceType == RT.BaseDescriptionDef || resourceType == RT.SimGameMilestoneDef || resourceType == RT.ShipModuleUpgrade || resourceType == RT.PortraitSettings)
            {
                LogDebug("Request {0} {1}", Enum.GetName(typeof(RT), resourceType), identifier);
                stuff.Load<object>(resourceType, identifier)
                     .Done(res =>
                     {
                         LogDebug("Loaded {0} {1}", Enum.GetName(typeof(RT), resourceType), identifier);
                     },
                     (ex) =>
                     {
                         LogException(ex);
                     });
                return false;
            }
            else
            {
                LogDebug("Unhandled {0} {1}", Enum.GetName(typeof(RT), resourceType), identifier);
            }
            return true;
        }
    }

    delegate void AcceptReject<T>(Action<T> accept, Action<Exception> reject);

    class Stuff
    {
        public DataManager dataManager;
        public HBS.Data.DataLoader dataLoader;
        public AssetBundleManager bundleManager;
        public MessageCenter messageCenter;
        public TextureManager textureManager;
        public Stuff(DataManager dataManager)
        {
            this.dataManager = dataManager;
            this.bundleManager = new Traverse(dataManager).Property("AssetBundleManager").GetValue<AssetBundleManager>();
            this.dataLoader = new Traverse(dataManager).Field("dataLoader").GetValue<HBS.Data.DataLoader>();
            this.messageCenter = new Traverse(dataManager).Property("MessageCenter").GetValue<MessageCenter>();
            this.textureManager = new Traverse(dataManager).Property("TextureManager").GetValue<TextureManager>();
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
        
        public void LoadAndPublish<T>(RT resourceType, string identifier)
        {
            Load<T>(resourceType, identifier)
                .Done(res => messageCenter.PublishMessage(new DataManagerRequestCompleteMessage<T>(resourceType, identifier, res)));
        }

        public static Dictionary<string,object> cache = new Dictionary<string,object>();
        public IPromise<T> Load<T>(BattleTechResourceType resourceType, string identifier, bool publish = true)
        {
            // Store item in datamanager
            // It's likely necessary to clear the cache based on the dm field, since items may be removed from dm
            IPromise<T> f<K>(string field, IPromise<K> p)
                where K : new()
            {
                return p.Then(x =>
                {
                    Trap(() => new Traverse(dataManager).Field(field).GetValue<DictionaryStore<K>>().Add(identifier, x));
                    if(publish) messageCenter.PublishMessage(new DataManagerRequestCompleteMessage<K>(resourceType, identifier, x));
                    return (T)(object)x;
                });
            }

            IPromise<T> passthrough<K>(IPromise<K> p)
            {
                return p.Then(val =>
                {
                    if (publish) messageCenter.PublishMessage(new DataManagerRequestCompleteMessage<K>(resourceType, identifier, val));
                    return (T)(object)val;
                });
            }

            IPromise<T> Go()
            {
                var entry = dataManager.ResourceLocator.EntryByID(identifier, resourceType, false);
                switch (resourceType)
                {
                    case RT.ChassisDef: return f("chassisDefs", LoadJson<ChassisDef>(entry, resourceType, identifier));
                    case RT.VehicleChassisDef: return f("vehicleChassisDefs", LoadJson<VehicleChassisDef>(entry, resourceType, identifier));
                    case RT.TurretChassisDef: return f("turretChassisDefs", LoadJson<TurretChassisDef>(entry, resourceType, identifier));
                    case RT.TurretDef: return f("turretDefs", LoadJson<TurretDef>(entry, resourceType, identifier));
                    case RT.BuildingDef: return f("buildingDefs", LoadJson<BuildingDef>(entry, resourceType, identifier));
                    case RT.AmmunitionDef: return f("ammoDefs", LoadJson<AmmunitionDef>(entry, resourceType, identifier));
                    case RT.AmmunitionBoxDef: return f("ammoBoxDefs", LoadJson<AmmunitionBoxDef>(entry, resourceType, identifier));
                    case RT.JumpJetDef: return f("jumpJetDefs", LoadJson<JumpJetDef>(entry, resourceType, identifier));
                    case RT.HeatSinkDef: return f("heatSinkDefs", LoadJson<HeatSinkDef>(entry, resourceType, identifier));
                    case RT.UpgradeDef: return f("upgradeDefs", LoadJson<UpgradeDef>(entry, resourceType, identifier));
                    case RT.WeaponDef: return f("weaponDefs", LoadJson<WeaponDef>(entry, resourceType, identifier));
                    case RT.MechDef: return f("mechDefs", LoadJson<MechDef>(entry, resourceType, identifier));
                    case RT.VehicleDef: return f("vehicleDefs", LoadJson<VehicleDef>(entry, resourceType, identifier));
                    case RT.PilotDef: return f("pilotDefs", LoadJson<PilotDef>(entry, resourceType, identifier));
                    case RT.AbilityDef: return f("abilityDefs", LoadJson<AbilityDef>(entry, resourceType, identifier));
                    case RT.DesignMaskDef: return f("designMaskDefs", LoadJson<DesignMaskDef>(entry, resourceType, identifier));
                    case RT.MovementCapabilitiesDef: return f("movementCapDefs", LoadJson<MovementCapabilitiesDef>(entry, resourceType, identifier));
                    case RT.PathingCapabilitiesDef: return f("pathingCapDefs", LoadJson<PathingCapabilitiesDef>(entry, resourceType, identifier));
                    case RT.HardpointDataDef: return f("hardpointDataDefs", LoadJson<HardpointDataDef>(entry, resourceType, identifier));
                    case RT.LanceDef: return f("lanceDefs", LoadJson<LanceDef>(entry, resourceType, identifier));
                    case RT.CastDef: return f("castDefs", LoadJson<CastDef>(entry, resourceType, identifier));
                    case RT.ConversationContent: return f("conversationDefs", LoadJson<ConversationContent>(entry, resourceType, identifier));
                    case RT.DialogBucketDef: return f("dialogBucketDefs", LoadJson<DialogBucketDef>(entry, resourceType, identifier));
                    case RT.SimGameEventDef: return f("simGameEventDefs", LoadJson<SimGameEventDef>(entry, resourceType, identifier));
                    case RT.SimGameStatDescDef: return f("simGameStatDescDefs", LoadJson<SimGameStatDescDef>(entry, resourceType, identifier));
                    case RT.LifepathNodeDef: return f("lifepathNodeDefs", LoadJson<LifepathNodeDef>(entry, resourceType, identifier));
                    //case RT.SimGameStringList: return f("simGameStringLists", LoadJson<SimGameStringList>(entry, resourceType, identifier));
                    case RT.ContractOverride: return f("contractOverrides", LoadJson<ContractOverride>(entry, resourceType, identifier));
                    case RT.StarSystemDef: return f("systemDefs", LoadJson<StarSystemDef>(entry, resourceType, identifier));
                    case RT.ShopDef: return f("shops", LoadJson<ShopDef>(entry, resourceType, identifier));
                    case RT.MechLabIncludeDef: return f("mechLabIncludeDefs", LoadJson<MechLabIncludeDef>(entry, resourceType, identifier));
                    case RT.FactionDef: return f("factions", LoadJson<FactionDef>(entry, resourceType, identifier));
                    case RT.HeraldryDef: return f("heraldries", LoadJson<HeraldryDef>(entry, resourceType, identifier));
                    //case RT.Conversation: return f("simGameConversations", LoadJson<Conversation>(entry, resourceType, identifier));
                    //case RT.ConversationSpeakerList: return f("simGameSpeakers", LoadJson<ConversationSpeakerList>(entry, resourceType, identifier));
                    case RT.GenderedOptionsListDef: return f("genderedOptionsListDefs", LoadJson<GenderedOptionsListDef>(entry, resourceType, identifier));
                    case RT.AudioEventDef: return f("audioEventDefs", LoadJson<AudioEventDef>(entry, resourceType, identifier));
                    case RT.SimGameMilestoneDef: return f("simGameMilestones", LoadJson<SimGameMilestoneDef>(entry, resourceType, identifier));
                    case RT.BackgroundDef: return f("backgroundDefs", LoadJson<BackgroundDef>(entry, resourceType, identifier));
                    case RT.BackgroundQuestionDef: return f("backgroundQuestionDefs", LoadJson<BackgroundQuestionDef>(entry, resourceType, identifier));
                    case RT.ShipModuleUpgrade: return f("shipUpgradeDefs", LoadJson<ShipModuleUpgrade>(entry, resourceType, identifier));
                    case RT.SimGameSubstitutionListDef: return f("simGameSubstitutionDefLists", LoadJson<SimGameSubstitutionListDef>(entry, resourceType, identifier));
                    case RT.BaseDescriptionDef: return f("baseDescriptionDefs", LoadJson<BaseDescriptionDef>(entry, resourceType, identifier));
                    case RT.PortraitSettings: return f("portraitSettings", LoadJson<PortraitSettings>(entry, resourceType, identifier));
                    case RT.SimGameDifficultySettingList: return f("simGameDifficultySettingLists", LoadJson<SimGameDifficultySettingList>(entry, resourceType, identifier));
                    case RT.FlashpointDef: return f("flashpointDefs", LoadJson<FlashpointDef>(entry, resourceType, identifier));
                    case RT.SimGameMilestoneSet: return f("milestoneSets", LoadJson<SimGameMilestoneSet>(entry, resourceType, identifier));
                    //case RT.ItemCollectionDef: return f("itemCollectionDefs", LoadCSV<ItemCollectionDef>(entry, resourceType, identifier));
                    //case RT.SimpleText: return f("simpleTexts", LoadJson<SimpleText>(entry, resourceType, identifier));

                    /*
                    case RT.Prefab: return LoadMapper(entry, resourceType, identifier, null, (GameObject go) => go);
                    case RT.Sprite:
                    case RT.SVGAsset:
                    case RT.ColorSwatch:
                    */
                    case RT.Texture2D:
                        /* if (this.TextureManager != null && this.TextureManager.Contains(identifier))
                            {
                                obj = this.TextureManager.GetLoadedTexture(identifier);
                            }
                            */
                        return passthrough(LoadMapper(entry, resourceType, identifier
                                                     , null
                                                     , (Texture2D t) => { textureManager.InsertTexture(identifier, t); return t; }
                                                     , (yes, no) => textureManager.RequestTexture(identifier, new TextureLoaded(yes), new LoadFailed(err => no(new Exception(err))))));

                /*
                case RT.UIModulePrefabs:
                case RT.AssetBundle: 
                */

                    // Grouped
                    case RT.BehaviorVariableScope:
                    case RT.ApplicationConstants:
                    case RT.AudioConstants:
                    case RT.CombatGameConstants:
                    case RT.MechStatisticsConstants:
                    case RT.SimGameConstants: return passthrough(LoadMapper(entry, resourceType, identifier, s => s, (TextAsset t) => t.text).Then(x => Promise<string>.Resolved(x)));
                }
                return Promise<T>.Rejected(new Exception(string.Format("Unhandled RT type {0}", Enum.GetName(typeof(RT), resourceType))));
            }

            if (cache.TryGetValue(identifier, out var promise)) return (Promise<T>)promise;
            else
            {
                // FIXME: This caching does not seem to be working, test it.
                LogDebug("Loading {0} {1}", Enum.GetName(typeof(RT), resourceType), identifier);
                var v = Go();
                cache[identifier] = v;
                return v;
            }
        }

        public IPromise<T> LoadJson<T>(VersionManifestEntry entry, BattleTechResourceType resourceType, string identifier)
            where T : class, HBS.Util.IJsonTemplated
        {
            T Make(string json)
            {
                T a = Activator.CreateInstance<T>();
                a.FromJSON(json);
                //Log("JSON send");
                return a;
            }

            return LoadMapper(entry, resourceType, identifier, Make, (TextAsset r) => Make(r.text));
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
                else if (recover != null) recover(res.Resolve, res.Reject);
                else throw new System.Exception(string.Format("Unhandled file, resource, or asset", identifier));
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