using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Harmony;
using BattleTech;
using BattleTech.Data;

namespace BattletechPerformanceFix
{
    // Do you like infinite loading screens?
    // I don't.
    // I REALLY don't.
    public class MissingAssetsContinueLoad : Feature
    {
        public void Activate()
        {
            Control.harmony.Patch(AccessTools.Method(typeof(DataManager), "RequestResource_Internal")
                                 , new HarmonyMethod(typeof(MissingAssetsContinueLoad), nameof(MissingAssetsContinueLoad.RequestResource_Internal)), null);
        }
        
        public static void RequestResource_Internal(DataManager __instance, BattleTechResourceType resourceType, ref string identifier)
        {
            try
            {
                var versionManifestEntry = __instance.ResourceLocator.EntryByID(identifier, resourceType, false);
                if (versionManifestEntry == null)
                {
                    // Game wants asset, game doesn't know how to load asset
                    // So we find the first asset of the same type
                    var manifest = (VersionManifest)new Traverse(__instance.ResourceLocator).Property("manifest").GetValue();
                    var dummies = manifest.Entries.Where(e => resourceType == (BattleTechResourceType)Enum.Parse(typeof(BattleTechResourceType), e.Type));
                    if (dummies.Any())
                    {
                        // Hopefully there is one, or you're screwed.
                        var dummy = dummies.First();
                        var dummyId = dummy.Id;
                        Control.LogDebug("Missing asset {0}, replacing with dummy {1}", identifier, dummyId);

                        var d = Traverse.Create(dummy);

                        // Copy the dummy asset, change its identifier and add it to the manifest
                        var ve = new VersionManifestEntry(identifier
                            , d.Field("path").GetValue<string>()
                            , d.Field("type").GetValue<string>()
                            , VersionManifestUtilities.StringToDateTime(d.Field("addedOn").GetValue<string>())
                            , d.Field("version").GetValue<string>()
                            , d.Field("assetBundleName").GetValue<string>());
                        var mfdb = Traverse.Create(__instance.ResourceLocator)
                            .Field("baseManifest")
                            .GetValue<Dictionary<BattleTechResourceType, Dictionary<string, VersionManifestEntry>>>();
                        mfdb[resourceType].Add(identifier, ve);
                    }
                }
            }
            catch (Exception e)
            {
                Control.LogException(e);
            }
        }
    }
}
