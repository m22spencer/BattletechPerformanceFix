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
using static BattletechPerformanceFix.Control;

namespace BattletechPerformanceFix
{
    class ResolveDepsAsync : Feature
    {
        public void Activate()
        {
            var t = typeof(ResolveDepsAsync);
            var drop = AccessTools.Method(t, nameof(Drop));
            harmony.Patch(AccessTools.Method(typeof(WeaponDef), "CheckDependenciesAfterLoad"), new HarmonyMethod(drop));
            harmony.Patch(AccessTools.Method(typeof(WeaponDef), "DependenciesLoaded"), new HarmonyMethod(drop));
            harmony.Patch(AccessTools.Method(typeof(WeaponDef), "RequestDependencies"), new HarmonyMethod(AccessTools.Method(t, nameof(RequestDependencies))));
        }

        public static bool Drop() => false;

        public static bool RequestDependencies(WeaponDef __instance, DataManager dataManager, Action onDependenciesLoaded, DataManager.DataManagerLoadRequest loadRequest)
        {
            Trap(() =>
            {
                var s = new Stuff(dataManager);
                //__instance.dataManager = dataManager; ?? needed ??

                var neps = 0;
                var ld = 0;

                var wait = new List<Func<IPromise>>();
                void G<T>(IPromise<T> o) where T : new()
                {
                    wait.Add(() =>
                    {
                        o.Then(_ => Log("ResolveDepsAsync: progress WeaponDef {0} {1}/{2}", loadRequest?.ResourceId, ++ld, wait.Count));
                        return o.Then(x => Promise.Resolved());
                    });
                }

                G(RequestPrefab(s, __instance.WeaponEffectID));
                if (!string.IsNullOrEmpty(__instance.AmmoCategoryToAmmoId)) G(RequestJSON<AmmunitionDef>(s, BattleTechResourceType.AmmunitionDef, __instance.AmmoCategoryToAmmoId));
                if (!string.IsNullOrEmpty(__instance.AmmoCategoryToAmmoBoxId)) G(RequestJSON<AmmunitionBoxDef>(s, BattleTechResourceType.AmmunitionBoxDef, __instance.AmmoCategoryToAmmoBoxId));
                if (!string.IsNullOrEmpty(__instance.Description.Icon)) G(RequestSVGAsset(s, __instance.Description.Icon));
                __instance.statusEffects
                    .Where(effect => !string.IsNullOrEmpty(effect.Description.Icon))
                    .ForEach(effect => G(RequestSVGAsset(s, effect.Description.Icon)));

                neps = wait.Count;


                Log("ResolveDepsAsync request WeaponDef {0} (:dependencies {1})", loadRequest?.ResourceId, neps);

                Trap(() =>
                    Promise.All(wait.Select(f => f()))
                        .Catch(x => LogError("Exn {0}", x)))
                        .Done(() =>
                        {
                            Log("RDA: Complete");
                            onDependenciesLoaded();
                        });
            });
           

            return false;
        }

        // PrefabLoadRequest
        public static IPromise<GameObject> RequestPrefab(Stuff s, string id)
        {
            var sub = new Promise<GameObject>();
            Trap(() =>
            {
                //Log("Requesting prefab");

                var resourceType = BattleTechResourceType.Prefab;
                VersionManifestEntry manifest = s.dataManager.ResourceLocator.EntryByID(id, resourceType, false);
                //Log("manifest {0}", manifest);
                if (manifest.IsAssetBundled)
                {
                    //Log("Bundled {0}", id);
                    s.bundleManager.RequestAsset<GameObject>(BattleTechResourceType.Prefab, id, v => sub.Resolve(v));
                }
                else
                {
                    //Log("Not bundled");
                    // We already have a type...
                    // Why exactly are we doing this if we already have a VME?
                    sub.Resolve((GameObject)Resources.Load(manifest.ResourcesLoadPath));
                }
                //Log("Retsub");
            });

            return sub;
        }

        //SpriteLoadRequest
        public static IPromise<Sprite> RequestSprite(Stuff s, string id)
        {
            var sub = new Promise<Sprite>();

            VersionManifestEntry manifest = Trap(() => s.dataManager.ResourceLocator.EntryByID(id, BattleTechResourceType.Texture2D, false));
            if (manifest.IsAssetBundled)
            {
                Trap(() => s.bundleManager.RequestAsset<Sprite>(BattleTechResourceType.Sprite, id, (sprite) => sub.Resolve(sprite)));
            }
            else if (manifest.IsResourcesAsset)
            {
                var tex = Resources.Load<Texture2D>(manifest.ResourcesLoadPath);
                sub.Resolve(Sprite.Create(tex, new UnityEngine.Rect(0f, 0f, (float)tex.width, (float)tex.height), new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect, Vector4.zero));
            } else
            {
                sub.Resolve(Stuff.SpriteFromDisk(manifest.FilePath));
            }

            return sub;
        }

        public static IPromise<SVGAsset> RequestSVGAsset(Stuff s, string id)
        {
            var sub = new Promise<SVGAsset>();

            VersionManifestEntry manifest = Trap(() => s.dataManager.ResourceLocator.EntryByID(id, BattleTechResourceType.SVGAsset, false));
            if (manifest.IsAssetBundled)
            {
                Trap(() => s.bundleManager.RequestAsset<SVGAsset>(BattleTechResourceType.SVGAsset, id, (svg) => sub.Resolve(svg)));
            }
            else if (manifest.IsResourcesAsset)
            {
                Trap(() => sub.Resolve(Resources.Load<SVGAsset>(manifest.ResourcesLoadPath)));
            }

            return sub;
        }

        public static IPromise<T> RequestJSON<T>(Stuff s, BattleTechResourceType type, string id) where T : class, HBS.Util.IJsonTemplated
        {
            var sub = new Promise<T>();

            void Go(string json)
            {
                T a = Activator.CreateInstance<T>();
                a.FromJSON(json);
                //Log("JSON send");
                sub.Resolve(a);
            }
            
            VersionManifestEntry manifest = s.dataManager.ResourceLocator.EntryByID(id, type, false);
            if (manifest.IsAssetBundled)
            {
                s.bundleManager.RequestAsset<TextAsset>(type, id, (txt) => Go(txt.text));
            }
            else if (manifest.IsResourcesAsset)
            {
                var txt = Resources.Load<TextAsset>(manifest.ResourcesLoadPath);
                Go(txt.text);
            } else if (manifest.IsFileAsset)
            {
                s.dataLoader.LoadResource(manifest.FilePath, txt => Go(txt));
            }

            return sub;
        }
    }

    class Stuff
    {
        public DataManager dataManager;
        public HBS.Data.DataLoader dataLoader;
        public AssetBundleManager bundleManager;
        public Stuff(DataManager dataManager)
        {
            this.dataManager = dataManager;
            this.bundleManager = new Traverse(dataManager).Property("AssetBundleManager").GetValue<AssetBundleManager>();
            this.dataLoader = new Traverse(dataManager).Field("dataLoader").GetValue<HBS.Data.DataLoader>();
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