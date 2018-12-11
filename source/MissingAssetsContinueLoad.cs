using System;
using System.Collections.Generic;
using System.Linq;
using Harmony;
using System.IO;
using BattleTech;
using BattleTech.UI;
using BattleTech.Data;
using static BattletechPerformanceFix.Extensions;

namespace BattletechPerformanceFix
{
    // Do you like infinite loading screens?
    // I don't.
    // I REALLY don't.
    public class MissingAssetsContinueLoad : Feature
    {
        public void Activate()
        {
            var rri = Main.CheckPatch(AccessTools.Method(typeof(DataManager), "RequestResource_Internal")
                                        , "0903c09b713e04ac4524e8635c02d61d98c2ec44ed06b961d8b233f320405299");
            var m = new HarmonyMethod(typeof(MissingAssetsContinueLoad), nameof(MissingAssetsContinueLoad.RequestResource_Internal));
            m.prioritiy = Priority.First;
            Main.harmony.Patch( rri
                                 , m
                                 , null);

            Main.harmony.Patch( AccessTools.Method(typeof(DataManager), "Update")
                              , null
                              , new HarmonyMethod(typeof(MissingAssetsContinueLoad), nameof(MissingAssetsContinueLoad.TryAlertUser)));
        }

        public static void TryAlertUser() {
            if (Substitutions.Count > 0 && !InDialog) {
                InDialog = true;

                var msg = "The following substitutions occurred:\n";
                var subs = Substitutions.Take(10); //10 at a time to not flow off the screen
                Substitutions = Substitutions.Skip(10).ToList();

                foreach(var sub in subs)
                    msg += $"{sub}\n";

                GenericPopupBuilder genericPopupBuilder = GenericPopupBuilder.Create("Error: Missing asset", $"<align=\"left\">{msg}</align>");
                genericPopupBuilder.Render();
                genericPopupBuilder.OnClose = () => InDialog = false;
            }
        }
        
        public static bool InDialog = false;
        public static List<string> Substitutions = new List<string>();
        public static void RequestResource_Internal(DataManager __instance, BattleTechResourceType resourceType, string identifier)
        {
            try
            {
                void WithDummyItem(Action<VersionManifestEntry> f) {
                    var manifest = (VersionManifest)new Traverse(__instance.ResourceLocator).Property("manifest").GetValue();
                    var dummies = manifest.Entries.Where(e => resourceType == (BattleTechResourceType)Enum.Parse(typeof(BattleTechResourceType), e.Type));
                    if (dummies.Any()) {
                        f(dummies.First());
                    }
                }

                var versionManifestEntry = __instance.ResourceLocator.EntryByID(identifier, resourceType);
                if (versionManifestEntry == null)
                {
                    // Game wants asset, game doesn't know how to load asset
                    // So we find the first asset of the same type
                    var manifest = (VersionManifest)new Traverse(__instance.ResourceLocator).Property("manifest").GetValue();
                    var dummies = manifest.Entries.Where(e => resourceType == (BattleTechResourceType)Enum.Parse(typeof(BattleTechResourceType), e.Type));
                    WithDummyItem(dummy => {
                            var dummyId = dummy.Id;
                            LogDebug("Missing asset {0}, replacing with dummy {1}", identifier, dummyId);

                            Substitutions.Add($"[id]{identifier} -> {dummyId}");

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
                        });
                }
            }
            catch (Exception e)
            {
                LogException(e);
            }
        }
    }
}
