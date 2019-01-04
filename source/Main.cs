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
        public static string LogLevel = "debug";

        public static void Start(string modDirectory, string json)
        {
            ModDir = modDirectory;
            var logFile = Path.Combine(ModDir, "BattletechPerformanceFix-old.log");
            File.Delete(logFile);
            LogStream = File.AppendText(logFile);
            LogStream.AutoFlush = true;

            Logging.Init(Path.Combine(ModDir, "BattletechPerformanceFix.log"));

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

                LogLevel = settings.logLevel.ToLower();

                Logging.SetLogLevel(LogLevel);
                
                harmony = HarmonyInstance.Create(ModFullName);

                var allFeatures = new Dictionary<Type, bool> {
                    //{ typeof(LazyRoomInitialization), false },
                    { typeof(MechlabFix), true },
                    { typeof(LoadFixes), true },
                    { typeof(NoSalvageSoftlock), true },
                    { typeof(MissingAssetsContinueLoad), false },
                    { typeof(DataLoaderGetEntryCheck), true },
                    { typeof(DynamicTagsFix), true },
                    { typeof(BTLightControllerThrottle), false },
                    { typeof(ShopTabLagFix), true },
                    { typeof(MDDB_InMemoryCache), true },
                    { typeof(ContractLagFix), true },
                    //{ typeof(ParallelizeLoad), false },
                    { typeof(SimpleMetrics), false },
                    { typeof(LazyLoadAssets), false },
                    { typeof(EnableLoggingDuringLoads), true },
                    { typeof(DMFix), false },
                    { typeof(ExtraLogging), true },
                };
                               
                Dictionary<Type, bool> want = allFeatures.ToDictionary(f => f.Key, f => settings.features.TryGetValue(f.Key.Name, out var userBool) ? userBool : f.Value);
                settings.features = want.ToDictionary(kv => kv.Key.Name, kv => kv.Value);
                File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(settings, Formatting.Indented));

                var alwaysOn = new Dictionary<Type, bool>
                {
                    { typeof(CollectSingletons), true },
                };

                var allwant = alwaysOn.Concat(want);

                Log("Features ----------");
                foreach (var feature in allwant)
                {
                    Log("Feature {0} is {1}", feature.Key.Name, feature.Value ? "ON" : "OFF");
                }
                Log("Patches ----------");
                foreach (var feature in allwant)
                {
                    if (feature.Value) {
                        try
                        {
                            Log("Feature {0}:", feature.Key.Name);
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
            });
        }

        public static bool MoveNextIntercept(IEnumerator e) {
            LogDebug("MNI");
            return e.MoveNext();
        }


        public static IEnumerable<CodeInstruction> PP_TP(IEnumerable<CodeInstruction> ins) {
            var found = false;
            var nins = ins.Select(i => {
                    if (i.operand is MethodBase) {
                        var amb = i.operand as MethodBase;
                        LogDebug($"Trying {amb.DeclaringType.FullName}::{amb.ToString()}");
                    }
                    if (i.operand is MethodBase && (i.operand as MethodBase).Name == "MoveNext") {
                        LogDebug("Intercepting movenext");
                        i.opcode = OpCodes.Call;
                        i.operand = AccessTools.Method(typeof(Main), "MoveNextIntercept");
                        return i;
                    } else {
                        return i;
                    }

                }).ToList();
            if (!found) LogError("Unable to find MoveNext to intercept");
            return nins;
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
 
