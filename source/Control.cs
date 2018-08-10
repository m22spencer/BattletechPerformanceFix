using HBS.Logging;
using Harmony;
using System.Reflection;
using BattleTech;
using BattleTech.UI;
using BattleTech.Data;
using System.Diagnostics;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;
using System.Reflection.Emit;
using System.IO;
using HBS.Util;

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

        public static void Log(string msg, params object[] values)
        {
            LogStream.WriteLine(string.Format(msg, values));
            LogStream.Flush();
        }

        public static void Trap(Action f)
        {
            try { f(); } catch (Exception e) { Log("Exception {0}", e); }
        }

        public static T Trap<T>(Func<T> f)
        {
            try { return f(); } catch (Exception e) { Log("Exception {0}", e); return default(T);  }
        }

        public static void Start(string modDirectory, string json)
        {
            var logFile = Path.Combine(ModDir, "BattletechPerformanceFix.log");
            File.Delete(logFile);
            LogStream = File.AppendText(logFile);
            LogStream.AutoFlush = true;
            Log("Initialized {0}", ModFullName);

            lib = mod = new Mod(modDirectory);
            lib.SetupLogging();
            mod.LoadSettings(settings);

            mod.Logger.Log(settings.logLevel);
            mod.Logger.LogDebug("Debug enabled");

			harmony = HarmonyInstance.Create(mod.Name);

            

            var specnames = new List<string> { "LeaveRoom", "InitWidgets" };
            var meths = AccessTools.GetDeclaredMethods(typeof(SGRoomController_MechBay));
            foreach(MethodBase meth in meths)
            {
                try
                {
                    Control.mod.Logger.Log(meth.Name);
                    var sn = specnames.Where(x => meth.Name == x).ToList();
                    var patchfun = sn.Any() ? sn[0] : "Other";
                    mod.Logger.Log(string.Format("methname {0}, patchfun {1}", meth.Name, patchfun));
                    Control.harmony.Patch(meth, new HarmonyMethod(typeof(SGRoomController_MechBay_MakeLazy), patchfun), null);
                } catch (Exception e)
                {
                    Control.mod.Logger.LogException(e);
                }
            }

            harmony.PatchAll(Assembly.GetExecutingAssembly());

            PatchMechlabLimitItems.Initialize();

            // logging output can be found under BATTLETECH\BattleTech_Data\output_log.txt
            // or also under yourmod/log.txt
            mod.Logger.Log("Loaded " + mod.Name);
        }
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
            var logFilePath = Path.Combine(Directory, "log.txt");
            try
            {
                ShutdownLogging();
                AddLogFileForLogger(Name, logFilePath);
            }
            catch (Exception e)
            {
                Logger.Log("BattletechPerformanceFixe: can't create log file", e);
            }
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

    /*
    [HarmonyPatch(typeof(SGRoomController_MechBay), nameof(SGRoomController_MechBay.Init))]
    public static class SGRoomController_MechBay_GuardInitWidgets
    {
        public static bool Prefix()
        {
            //Control.mod.Logger.Log("SGRoomController_MechBayDROP");
            return false;
        }
    }
    */

    public static class SGRoomController_MechBay_MakeLazy
    {
        public static bool allowInit = false;
        public static bool InitWidgets()
        {
            return Control.Trap(() =>
            {
                Control.Log("SGRoomController_MechBay.InitWidgets (want initialize? {0})", allowInit);
                if (!allowInit)
                    return false;
                return true;
            });
        }

        public static bool LeaveRoom(bool ___roomActive, MechBayPanel ___mechBay)
        {
            return Control.Trap(() =>
            {
                Control.Log("SGRoomController_MechBay_LeaveRoom");
                if (___roomActive)
                    return true;
                return false;
            });
        }
        public static void Other(SGRoomController_MechBay __instance, MethodBase __originalMethod, MechBayPanel ___mechBay)
        {
            Control.Trap(() =>
            {
                Control.Log("SGRoomController_MechBay_Log {0}", __originalMethod.Name);

                if (___mechBay == null)
                {
                    Control.Log("Initialize Widgets");
                    allowInit = true;
                    new Traverse(__instance).Method(nameof(SGRoomController_MechBay.InitWidgets)).GetValue();
                    allowInit = false;
                }
            });
        }
    }
}