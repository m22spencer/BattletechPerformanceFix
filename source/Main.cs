using Harmony;
using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json;
using HBS.Logging;
using BattleTech;
using BattleTech.Framework;
using static BattletechPerformanceFix.Extensions;

namespace BattletechPerformanceFix
{
    public class Settings {
        public string logLevel = "debug";
        public Dictionary<string,bool> features = new Dictionary<string,bool>();

        public bool WantContractsLagFixVerify = false;
    }

    public static class Main
    {
        public static HarmonyInstance harmony;

        public static readonly string ModName = "BattletechPerformanceFix";
        public static readonly string ModPack = "com.github.m22spencer";
        public static readonly string ModFullName = string.Format("{0}.{1}", ModPack, ModName);
        public static string ModDir;
        public static string SettingsPath { get => Path.Combine(ModDir, "Settings.json"); }

        public static Settings settings;

        public static Type ModTekType = AppDomain.CurrentDomain.GetAssemblies().SelectMany(ty => ty.GetTypes()).FirstOrDefault(ty => ty.FullName == "ModTek.ModTek");

        public static ILog HBSLogger;

        public static void Start(string modDirectory, string json)
        {
            ModDir = modDirectory;

            Logging.Init(Path.Combine(ModDir, "BattletechPerformanceFix.log"));

            HBSLogger = Trap(() => Logger.GetLogger(ModName));

            LogInfo(string.Format("Harmony? {0}", Assembly.GetAssembly(typeof(HarmonyInstance)).GetName().Version));
            LogInfo(string.Format("Unity? {0}", UnityEngine.Application.unityVersion));
            LogInfo(string.Format("Product? {0}-{1}", UnityEngine.Application.productName, UnityEngine.Application.version));
            LogInfo(string.Format("ModTek? {0}", ModTekType?.Assembly?.GetName()?.Version));
            LogInfo(string.Format("Initialized {0} {1}", ModFullName, Assembly.GetExecutingAssembly().GetName().Version));
            LogInfo(string.Format("Mod-Dir? {0}", ModDir));

            Trap(() =>
            {
                var WantHarmonyVersion = "1.2";
                var harmonyVersion = Assembly.GetAssembly(typeof(HarmonyInstance)).GetName().Version;
                if (!harmonyVersion.ToString().StartsWith(WantHarmonyVersion))
                {
                    LogError(string.Format("BattletechPerformanceFix requires harmony version {0}.*, but found {1}", WantHarmonyVersion, harmonyVersion));
                    return;
                }

                var WantVersions = new string[] { "1.9" };
                if (WantVersions.Where(v => VersionInfo.ProductVersion.Trim().StartsWith(v)).Any())
                {
                    LogInfo(string.Format("BattletechPerformanceFix found BattleTech {0} and will now load", VersionInfo.ProductVersion));
                } else
                {
                    LogError(string.Format("BattletechPerformanceFix expected BattleTech ({0}), but found {1}", string.Join(",", WantVersions), VersionInfo.ProductVersion));
                    return;
                }

                settings = new Settings();
                try { settings = JsonConvert.DeserializeObject<Settings>(File.ReadAllText(SettingsPath)); }
                catch { LogWarning("Settings file is invalid or missing, regenerating with defaults"); }

                harmony = HarmonyInstance.Create(ModFullName);

                var allFeatures = new Dictionary<Type, bool> {
                    //{ typeof(LazyRoomInitialization), false },
                    { typeof(HarmonyPatches), true },
                    { typeof(LocalizationPatches), true },
                    { typeof(MechlabFix), true },
                    { typeof(LoadFixes), true },
                    { typeof(NoSalvageSoftlock), true },
                    { typeof(DataLoaderGetEntryCheck), true },
                    { typeof(ShopTabLagFix), true },
                    { typeof(ContractLagFix), true },
                    { typeof(EnableLoggingDuringLoads), true },
                    { typeof(ExtraLogging), true },
                    { typeof(ShaderDependencyOverride), true },
                    { typeof(DisableDeployAudio), false },
                    { typeof(RemovedFlashpointFix), true },
                    { typeof(DisableSimAnimations), false },
                    { typeof(RemovedContractsFix), true },
                    { typeof(VersionManifestPatches), true },
                };

                
                               
                Dictionary<Type, bool> want = allFeatures.ToDictionary(f => f.Key, f => settings.features.TryGetValue(f.Key.Name, out var userBool) ? userBool : f.Value);
                settings.features = want.ToDictionary(kv => kv.Key.Name, kv => kv.Value);
                File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(settings, Formatting.Indented));

                var alwaysOn = new Dictionary<Type, bool>
                {
                };

                var allwant = alwaysOn.Concat(want);

                LogInfo("Features ----------");
                foreach (var feature in allwant)
                {
                    LogInfo(string.Format("Feature {0} is {1}", feature.Key.Name, feature.Value ? "ON" : "OFF"));
                }
                LogInfo("Patches ----------");
                foreach (var feature in allwant)
                {
                    if (feature.Value) {
                        try
                        {
                            LogInfo(string.Format("Feature {0}:", feature.Key.Name));
                            var f = (Feature)AccessTools.CreateInstance(feature.Key);
                            f.Activate();
                        } catch (Exception e)
                        {
                            LogError(string.Format("Failed to activate feature {0} with:\n {1}\n", feature.Key, e));
                        }
                    }
                }
                LogInfo("Runtime ----------");

                harmony.PatchAll(Assembly.GetExecutingAssembly());

                LogInfo("Patch out sensitive data log dumps");
                new DisableSensitiveDataLogDump().Activate();

                LogInfo($"LogLevel {settings.logLevel}");
                Logging.SetLogLevel(settings.logLevel);
            });
        }

        public static MethodBase CheckPatch(MethodBase meth, params string[] sha256s)
        {
            LogSpam("CheckPatch is NYI");
            if (meth == null)
            {
                LogError("A CheckPatch recieved a null method, this is fatal");
            }

            return meth;
        }
    }

    public interface Feature
    {
        void Activate();
    }
}
 
