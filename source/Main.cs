using Harmony;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Reflection;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json;
using HBS.Logging;
using static BattletechPerformanceFix.Extensions;

namespace BattletechPerformanceFix
{
    public class Settings {
        public string logLevel = "Debug";
        public Dictionary<string,bool> features = new Dictionary<string,bool>();
    }

    public static class Main
    {
        public static HarmonyInstance harmony;

        public static readonly string ModName = "BattletechPerformanceFix";
        public static readonly string ModPack = "com.github.m22spencer";
        public static readonly string ModFullName = string.Format("{0}.{1}", ModPack, ModName);
        public static string ModDir;
        public static string SettingsPath { get => Path.Combine(ModDir, "Settings.json"); }

        public static Type ModTekType = AppDomain.CurrentDomain.GetAssemblies().SelectMany(ty => ty.GetTypes()).FirstOrDefault(ty => ty.FullName == "ModTek.ModTek");

        private static StreamWriter LogStream;

        public static ILog HBSLogger;
        public static string LogLevel = "Debug";

        public static void Start(string modDirectory, string json)
        {
            ModDir = modDirectory;
            var logFile = Path.Combine(ModDir, "BattletechPerformanceFix.log");
            File.Delete(logFile);
            LogStream = File.AppendText(logFile);
            LogStream.AutoFlush = true;

            HBSLogger = Trap(() => Logger.GetLogger(ModName));

            Log("Harmony? {0}", Assembly.GetAssembly(typeof(HarmonyInstance)).GetName().Version);
            Log("Unity? {0}", UnityEngine.Application.unityVersion);
            Log("Product? {0}-{1}", UnityEngine.Application.productName, UnityEngine.Application.version);
            Log("ModTek? {0}", ModTekType.Assembly.GetName().Version);
            Log("Initialized {0} {1}", ModFullName, Assembly.GetExecutingAssembly().GetName().Version);
            Log("Mod-Dir? {0}", ModDir);
            
            Trap(() =>
            {
                var WantHarmonyVersion = "1.2";
                var harmonyVersion = Assembly.GetAssembly(typeof(HarmonyInstance)).GetName().Version;
                if (!harmonyVersion.ToString().StartsWith(WantHarmonyVersion))
                {
                    LogError("BattletechPerformanceFix requires harmony version {0}.*, but found {1}", WantHarmonyVersion, harmonyVersion);
                    return;
                }

                var WantVersions = new string[] { "1.2.", "1.3." };
                if (WantVersions.Where(v => VersionInfo.ProductVersion.Trim().StartsWith(v)).Any())
                {
                    Log("BattletechPerformanceFix found BattleTech {0} and will now load", VersionInfo.ProductVersion);
                } else
                {
                    LogError("BattletechPerformanceFix expected BattleTech ({0}), but found {1}", string.Join(",", WantVersions), VersionInfo.ProductVersion);
                    return;
                }

                var settings = new Settings();
                try { settings = JsonConvert.DeserializeObject<Settings>(File.ReadAllText(SettingsPath)); }
                catch { LogWarning("Settings file is invalid or missing, regenerating with defaults"); }

                Log($"LogLevel {settings.logLevel}");
                LogLevel = settings.logLevel;

                harmony = HarmonyInstance.Create(ModFullName);

                var allFeatures = new Dictionary<Type, bool> {
                    //{ typeof(LazyRoomInitialization), false },
                    { typeof(LoadFixes), true },
                    { typeof(NoSalvageSoftlock), true },
                    { typeof(MissingAssetsContinueLoad), true },
                    { typeof(DataLoaderGetEntryCheck), true },
                    { typeof(DynamicTagsFix), true },
                    { typeof(BTLightControllerThrottle), false },
                    { typeof(ShopTabLagFix), true },
                    { typeof(MDDB_InMemoryCache), true },
                    { typeof(ContractLagFix), true },
                    { typeof(ParallelizeLoad), false },
                    { typeof(SimpleMetrics), false }
                };
                               
                Dictionary<Type, bool> want = allFeatures.ToDictionary(f => f.Key, f => settings.features.TryGetValue(f.Key.Name, out var userBool) ? userBool : f.Value);
                settings.features = want.ToDictionary(kv => kv.Key.Name, kv => kv.Value);
                File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(settings, Formatting.Indented));

                Log("Features ----------");
                foreach (var feature in want)
                {
                    Log("Feature {0} is {1}", feature.Key.Name, feature.Value ? "ON" : "OFF");
                }
                Log("Patches ----------");
                foreach (var feature in want)
                {
                    if (feature.Value) {
                        try
                        {
                            var f = (Feature)AccessTools.CreateInstance(feature.Key);
                            f.Activate();
                        } catch (Exception e)
                        {
                            LogError("Failed to activate feature {0} with:\n {1}\n", feature.Key, e);
                        }
                    }
                }
                Log("Runtime ----------");

                harmony.PatchAll(Assembly.GetExecutingAssembly());

                Log("Patch out sensitive data log dumps");
                new DisableSensitiveDataLogDump().Activate();


                Trap(() => PatchMechlabLimitItems.Initialize());
            });
        }

        public static void __Log(string msg, params object[] values)
        {
            try { 
                var omsg = Trap(() => string.Format(msg, values));
                LogStream.WriteLine(omsg);
                LogStream.Flush();

                // It's possible to log during a patch that can't access HBS logging tools
            } catch { }
        } 

        public static string HashMethod(MethodBase meth)
        {
            var body = meth.GetMethodBody();
            //ilbytes is different between battletech versions for same code. Jump/label maybe?
            // TODO: push this through harmony api to get CodeInstructions and then hash that.
            var ilbytes = body.GetILAsByteArray();
            var methsig = meth.ToString();
            var lvs = string.Join(":", body.LocalVariables.Select(lvi => lvi.ToString()).ToArray());

            
            var allbytes = Encoding.UTF8.GetBytes(methsig + ":" + lvs + ":").Concat(ilbytes).ToArray();

            var s = System.Security.Cryptography.SHA256.Create();
            //return string.Join("", s.ComputeHash(allbytes).Select(b => b.ToString("x2")).ToArray());

            return methsig + ":" + lvs + ":" + string.Join("", ilbytes.Select(b => b.ToString("x2")).ToArray());
        }

        public static MethodBase CheckPatch(MethodBase meth, params string[] sha256s)
        {
            if (meth == null)
            {
                LogError("A CheckPatch recieved a null method, this is fatal");
            }
            /*
            var h = HashMethod(meth);
            if (!sha256s.Contains(h))
            {
                LogWarning(":method {0}::{1} :hash {2} does not match any specified :hash ({3})", meth.DeclaringType.FullName, meth.ToString(), h, string.Join(" ", sha256s));
            }
            */

            return meth;
        }
    }

    public interface Feature
    {
        void Activate();
    }
}
 
