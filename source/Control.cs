using HBS.Logging;
using Harmony;
using System.Reflection;
using BattleTech;
using BattleTech.UI;
using BattleTech.Data;
using System.Diagnostics;
using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;
using System.Reflection.Emit;
using System.IO;
using HBS.Util;
using HBS.Collections;
using HBS.Threading.Coroutine;
using HBS.Threading;
using BattleTech.Save.Core;
using Newtonsoft.Json;
using HBS.Data;
using RSG;

namespace BattletechPerformanceFix
{
    public static class Control
    {
        public static Mod lib;
        public static Mod mod;

        public static ModSettings settings = new ModSettings();

        public static HarmonyInstance harmony;

        public static readonly string ModName = "BattletechPerformanceFix";
        public static readonly string ModPack = "com.github.m22spencer";
        public static readonly string ModFullName = string.Format("{0}.{1}", ModPack, ModName);
        public static readonly string ModDir = "./Mods/BattletechPerformanceFix";

        private static StreamWriter LogStream;

        private static string LogLevel = "Log";

        public static void Log(string msg, params object[] values)
        {
            try { 
                var omsg = string.Format(msg, values);
                LogStream.WriteLine(omsg);
                LogStream.Flush();

                // It's possible to log during a patch that can't access HBS logging tools
                mod.Logger.Log(omsg);
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

        public static void LogDebug(string msg, params object[] values)
        {
            if (LogLevel == "Debug")
                Log("[Debug] " + msg, values);
        }

        public static void LogError(string msg, params object[] values)
        {
            Log("[Error] " + msg, values);
        }

        public static void LogWarning(string msg, params object[] values)
        {
            Log("[Warning] " + msg, values);
        }

        public static void LogException(params object[] values)
        {
            Log("[Exception] {0}", values);
        }

        public static void Trap(Action f)
        {
            try { f(); } catch (Exception e) { Log("Exception {0}", e); }
        }

        public static T Trap<T>(Func<T> f)
        {
            try { return f(); } catch (Exception e) { Log("Exception {0}", e); return default(T);  }
        }

        public static T Trap<T>(string message, Func<T> f)
        {
            try { return f(); } catch (Exception e) { Log("Exception({0}) {1}", message, e); return default(T); }
        }

        public static void Trap(string message, Action f)
        {
            try { f(); } catch (Exception e) { Log("Exception({0}) {1}", message, e); }
        }

        public static bool Throws(Action f)
        {
            try { f(); return false; } catch { return true; }
        }

        public static T TrapAndTerminate<T>(string msg, Func<T> f)
        {
            try {
                return f();
            } catch (Exception e) {
                Log("PANIC {0} {1}", msg, e);
                TerminateImmediately();
                return default(T);
            }
        }

        public static string Dump<T>(this T t, bool indented = true, int depth = 1)
        {
            return JsonConvert.SerializeObject(t, indented ? Formatting.Indented : Formatting.None, new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                MaxDepth = depth,
                Error = (serializer, err) => err.ErrorContext.Handled = true
            });
        }

        public static T[] Array<T>(params T[] p) => p;
        public static List<T> List<T>(params T[] p) => p.ToList();
        public static IEnumerable<T> Sequence<T>(params T[] p) => p;
        public static void ForEach<T>(this IEnumerable<T> xs, Action<T> f)
        {
            foreach (var x in xs) f(x);
        }

        public static IPromise Unit<T>(this IPromise<T> p)
            => p.Then(x => {});

        public static T Or<T>(this T a, T b)
        {
            return a == null ? b : a;
        }

        public static K Let<T,K>(this T t, Func<T, K> f)
            => f(t);

        public static T Identity<T>(this T t)
            => t;

        public static KeyValuePair<List<A>, List<A>> Partition<A>(this IEnumerable<A> xs, Func<A, bool> predicate)
        {
            var yes = new List<A>();
            var no = new List<A>();
            xs.ForEach(elem => { if (predicate(elem)) yes.Add(elem); else no.Add(elem); });
            return new KeyValuePair<List<A>, List<A>>(yes, no);
        }

        public static void TrapAndTerminate(string msg, Action f) => TrapAndTerminate<int>(msg, () => { f(); return 0; });

        public static HarmonyMethod Drop = new HarmonyMethod(AccessTools.Method(typeof(Control), nameof(Drop_Patch)));
        public static bool Drop_Patch() => false;

        public static void Start(string modDirectory, string json)
        {
            var logFile = Path.Combine(ModDir, "BattletechPerformanceFix.log");
            File.Delete(logFile);
            LogStream = File.AppendText(logFile);
            LogStream.AutoFlush = true;
            Log("Initialized {0} {1}", ModFullName, Assembly.GetExecutingAssembly().GetName().Version + "-[REDACTED]-Windows-Edition-(I really am sorry Mac & Linux users :()");
            
            Trap(() =>
            {

                var WantHarmonyVersion = "1.2";
                var harmonyVersion = Assembly.GetAssembly(typeof(HarmonyInstance)).GetName().Version;
                if (!harmonyVersion.ToString().StartsWith(WantHarmonyVersion))
                {
                    LogError("BattletechPerformanceFix requires harmony version {0}.*, but found {1}", WantHarmonyVersion, harmonyVersion);
                    return;
                }

                var WantVersions = new string[] { "1.3." };
                if (WantVersions.Where(v => VersionInfo.ProductVersion.Trim().StartsWith(v)).Any())
                {
                    Log("BattletechPerformanceFix found BattleTech {0} and will now load", VersionInfo.ProductVersion);
                } else
                {
                    LogError("BattletechPerformanceFix requires BattleTech version ({0}), you are on {1}. You are using the wrong version. Check here: https://github.com/m22spencer/BattletechPerformanceFix/releases", string.Join(" or ", WantVersions), VersionInfo.ProductVersion);
                    return;
                }

                lib = mod = new Mod(modDirectory);
                lib.SetupLogging();
                settings = JsonConvert.DeserializeObject<ModSettings>(File.ReadAllText(mod.SettingsPath));

                mod.Logger.Log(settings.logLevel);
                LogLevel = settings.logLevel;
                mod.Logger.LogDebug("Debug enabled");

                harmony = HarmonyInstance.Create(mod.Name);

                var allFeatures = new Dictionary<Type, bool> {
                    //{ typeof(LazyRoomInitialization), false },
                    { typeof(LoadFixes), true },
                    { typeof(NoSalvageSoftlock), true },
                    { typeof(MissingAssetsContinueLoad), true },
                    { typeof(DataLoaderGetEntryCheck), false },  // A bit too dangerous to enable at the moment.
                    { typeof(DynamicTagsFix), true },
                    { typeof(BTLightControllerThrottle), false },
                    { typeof(ShopTabLagFix), true },
                    { typeof(MDDB_InMemoryCache), true },        // Currently don't have a good way to ship sqlite, and ModTek interactions become odd with this patch.
                    //{ typeof(RemoveMDDB), true },
                    { typeof(ContractLagFix), true },
                    { typeof(ResolveDepsAsync), true }
                };
                               
                Dictionary<Type, bool> want = allFeatures.ToDictionary(f => f.Key, f => settings.features.TryGetValue(f.Key.Name, out var userBool) ? userBool : f.Value);
                settings.features = want.ToDictionary(kv => kv.Key.Name, kv => kv.Value);
                File.WriteAllText(mod.SettingsPath, JsonConvert.SerializeObject(settings, Formatting.Indented));

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


                PatchMechlabLimitItems.Initialize();

                // logging output can be found under BATTLETECH\BattleTech_Data\output_log.txt
                // or also under yourmod/log.txt
                mod.Logger.Log("Loaded " + mod.Name);
            });
        }

        public static void TerminateImmediately()
        {
            System.Diagnostics.Process.GetCurrentProcess().Kill();
        }
    }

    public interface Feature
    {
        void Activate();
    }

    public class Mod
    {
        public Mod(string directory)
        {
            Directory = directory;
            Name = Path.GetFileName(directory);
        }

        public string Name { get; }
        public string Directory { get; }

        public string SourcePath => Path.Combine(Directory, "source");
        public string SettingsPath => Path.Combine(Directory, "Settings.json");
        public string ModsPath => Path.GetDirectoryName(Directory);
        public string InfoPath => Path.Combine(Directory, "mod.json");

        public ILog Logger => HBS.Logging.Logger.GetLogger(Name);
        private FileLogAppender logAppender;

        public void LoadSettings<T>(T settings) where T : ModSettings
        {
            if (!File.Exists(SettingsPath))
            {
                return;
            }
            
            using (var reader = new StreamReader(SettingsPath))
            {
                var json = reader.ReadToEnd();
                settings = JsonConvert.DeserializeObject<T>(json);
            }

            var logLevelString = settings.logLevel;
            DebugBridge.StringToLogLevel(logLevelString, out var level);
            if (level == null)
            {
                level = LogLevel.Debug;
            }
            HBS.Logging.Logger.SetLoggerLevel(Name, level);

            
        }

        public void SaveSettings<T>(T settings) where T : ModSettings
        {
            using (var writer = new StreamWriter(SettingsPath))
            {
                var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                writer.Write(json);
            }
        }
        
        internal string AssemblyPath => string.IsNullOrEmpty(ModTekInfo.DLL) ? null : Path.Combine(Directory, ModTekInfo.DLL);

        private ModTekInfo _modTekInfo;
        internal ModTekInfo ModTekInfo
        {
            get
            {
                if (_modTekInfo == null)
                {
                    using (var reader = new StreamReader(InfoPath))
                    {
                        var info = new ModTekInfo();
                        var json = reader.ReadToEnd();
                        JSONSerializationUtility.FromJSON(info, json);
                        _modTekInfo = info;
                    }
                }

                return _modTekInfo;
            }
        }

        internal void SetupLogging()
        {
        }

        internal void ShutdownLogging()
        {
            if (logAppender == null)
            {
                return;
            }

            try
            {
                HBS.Logging.Logger.ClearAppender(Name);
                logAppender.Flush();
                logAppender.Close();
            }
            catch
            {
            }

            logAppender = null;
        }

        private void AddLogFileForLogger(string name, string logFilePath)
        {
            logAppender = new FileLogAppender(logFilePath, FileLogAppender.WriteMode.INSTANT);

            HBS.Logging.Logger.AddAppender(name, logAppender);
        }

        public override string ToString()
        {
            return $"{Name} ({Directory})";
        }
    }

    public class ModSettings
    {
        public string logLevel = "Log";
        public Dictionary<string, bool> features = new Dictionary<string, bool>();
    }

    internal class ModTekInfo
    {
        public string[] DependsOn = { };
        public string DLL = null;
    }

    public class Adapter<T>
    {
        public readonly T instance;
        public readonly Traverse traverse;

        protected Adapter(T instance)
        {
            this.instance = instance;
            traverse = Traverse.Create(instance);
        }
    }
}
 