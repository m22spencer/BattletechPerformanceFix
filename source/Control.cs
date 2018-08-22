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

        public static void LogDebug(string msg, params object[] values)
        {
            if (LogLevel == "Debug")
                Log("[Debug] " + msg, values);
        }

        public static void LogError(string msg, params object[] values)
        {
            Log("[Error] " + msg, values);
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
        public static void TrapAndTerminate(string msg, Action f) => TrapAndTerminate<int>(msg, () => { f(); return 0; });

        public static void Start(string modDirectory, string json)
        {
            var logFile = Path.Combine(ModDir, "BattletechPerformanceFix.log");
            File.Delete(logFile);
            LogStream = File.AppendText(logFile);
            LogStream.AutoFlush = true;
            Log("Initialized {0}", ModFullName);

            Trap(() =>
            {

                var WantHarmonyVersion = "1.2";
                var harmonyVersion = Assembly.GetAssembly(typeof(HarmonyInstance)).GetName().Version;
                if (!harmonyVersion.ToString().StartsWith(WantHarmonyVersion))
                {
                    LogError("BattletechPerformanceFix requires harmony version {0}.*, but found {1}", WantHarmonyVersion, harmonyVersion);
                    return;
                }

                var WantVersion = "1.2.0";
                if (VersionInfo.ProductVersion != WantVersion)
                {
                    LogError("BattletechPerformanceFix expected BattleTech {0}, but found {1}", WantVersion, VersionInfo.ProductVersion);
                    return;
                } else
                {
                    Log("BattletechPerformanceFix found BattleTech {0} and will now load", WantVersion);
                }

                lib = mod = new Mod(modDirectory);
                lib.SetupLogging();
                mod.LoadSettings(settings);

                mod.Logger.Log(settings.logLevel);
                LogLevel = settings.logLevel;
                mod.Logger.LogDebug("Debug enabled");

                harmony = HarmonyInstance.Create(mod.Name);

                if (settings.experimentalLazyRoomInitialization) LazyRoomInitialization.Activate();
                else Log("ExperimentalLazyRoomInitialization is OFF", settings.experimentalLazyRoomInitialization);

                var loadFixes = new LoadFixes();
                if (settings.experimentalLoadFixes) {
                    Log("experimentalLoadFixes is ON");
                    loadFixes.Activate();
                } else
                {
                    Log("experimentalLoadFixes is OFF");
                }

                var noSalvageSoftlock = new NoSalvageSoftlock();
                if (settings.experimentalSalvageSoftlockFix) {
                    Log("experimentalSalvageSoftlockFix is ON");
                    noSalvageSoftlock.Activate();
                } else
                {
                    Log("experimentalSalvageSoftlockFix is OFF");
                }


                harmony.PatchAll(Assembly.GetExecutingAssembly());

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
                JSONSerializationUtility.FromJSON(settings, json);
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
                var json = JSONSerializationUtility.ToJSON(settings);
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
        public bool experimentalLazyRoomInitialization = false;
        public bool experimentalLoadFixes = false;
        public bool experimentalSalvageSoftlockFix = false;
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
 