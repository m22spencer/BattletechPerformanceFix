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
            Feature.Initialize();

            Trap(() =>
            {

                lib = mod = new Mod(modDirectory);
                lib.SetupLogging();
                mod.LoadSettings(settings);

                mod.Logger.Log(settings.logLevel);
                mod.Logger.LogDebug("Debug enabled");

                harmony = HarmonyInstance.Create(mod.Name);

                if (settings.experimentalLazyRoomInitialization) LazyRoomInitialization.Activate();
                else Log("ExperimentalLazyRoomInitialization is OFF", settings.experimentalLazyRoomInitialization);

                var loadFixes = new LoadFixes();
                if (settings.experimentalLoadFixes) {
                    Log("experimentalLoadFixes is ON");
                    //loadFixes.Activate();
                    loadFixes.TryAndActivateFeature();
                } else
                {
                    Log("experimentalLoadFixes is OFF");
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

    abstract public class Feature
    {
        public static bool WantGenerate;
        public static Dictionary<string, int> CRCCache;

        public static void Initialize()
        {
            if (CRCCache == null)
            {
                var crcstore_path = Path.Combine(Control.ModDir, "PATCHCRC");
                if (File.Exists(crcstore_path))
                {
                    // Confirm crc mode
                    var json = new StreamReader(crcstore_path).ReadToEnd();
                    CRCCache = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, int>>(json);
                    WantGenerate = false;
                } else
                {
                    // Generate crc mode
                    CRCCache = new Dictionary<string, int>();
                    WantGenerate = true;
                }
            }
        }

        public static void WriteCRCData()
        {
            var crcstore_path = Path.Combine(Control.ModDir, "PATCHCRC");
            File.WriteAllText(crcstore_path, Newtonsoft.Json.JsonConvert.SerializeObject(CRCCache));
        }

        public static bool AddHash(MethodBase meth)
        {
            var details = ""
                + meth.ContainsGenericParameters.ToString()
                + meth.IsGenericMethod.ToString()
                + meth.IsGenericMethodDefinition.ToString()
                + meth.IsVirtual.ToString()
                + meth.IsStatic.ToString()
                + meth.IsSpecialName.ToString()
                + meth.IsFinal.ToString()
                + meth.IsAssembly.ToString()
                + meth.IsFamily.ToString()
                + string.Join("", meth.GetParameters().Select(pi => pi.ToString()).ToArray())
                + (meth.GetMethodBody().GetILAsByteArray() == null
                ? ""
                : string.Join("", meth.GetMethodBody().GetILAsByteArray().Select(b => b.ToString()).ToArray()));

            var hash = details.GetHashCode();
            var fullName = meth.DeclaringType.FullName + ":" + meth.Name;
            if (WantGenerate)
            {
                // If exists, needs to compare hash
                CRCCache.Add(fullName, hash);
                WriteCRCData();
                return true;
            } else
            {
                if (CRCCache.TryGetValue(fullName, out var known) && known == hash) { }
                else 
                {
                    Control.Log("Unable to patch {0} as the method does not match the DB crc", fullName);
                    return false;
                } 
            }
            return true;
        }

        public abstract void Activate(Patcher patcher);

        public void TryAndActivateFeature()
        {
            var p = new Patcher(this.GetType());
            Activate(p);
            //Here need to check if p is valid.
            if (p.Valid)
            {
                Control.Log("Activating feature");
                new Traverse(p).Field("patchable").GetValue<List<Patchable>>()
                    .ForEach(pb => new Traverse(pb).Field("thunks").GetValue<List<Action>>().ForEach(act => act()));
            } else
            {
                Control.Log("Invalid feature will not activate");
            }
        }
    }

    public class Patcher
    {
        private Type feature;
        public bool Valid { get; private set; }
        private List<Patchable> patchable = new List<Patchable>();
        public Patcher(Type feature)
        {
            this.feature = feature;
            this.Valid = true;
        }
        public Patchable GetPatchableMethod(Type t, string methodName)
        {
            var meth = AccessTools.Method(t, methodName);
            var validcrc = Feature.AddHash(meth);
            if (!validcrc) Valid = false;
            var p = new Patchable(feature, meth);
            patchable.Add(p);
            return p;
        }

        public void OnSuccessfulActivation(Action f)
        {
            throw new System.Exception("NYI");
        }
    }

    public class Patchable
    {
        Type feature;
        MethodBase method;
        private List<Action> thunks = new List<Action>();
        public Patchable(Type feature, MethodBase method)
        {
            this.feature = feature;
            this.method = method;
        }

        HarmonyMethod ResolvePatch(string name)
        {
            var method = AccessTools.Method(feature, name);
            if (method == null)
                Control.Log("Failed to resolve patch {0}", name);
            return new HarmonyMethod(method);
        }
        public Patchable Prefix(string methodName)
        {
            var patch = ResolvePatch(methodName);
            // Do checks for public/static, and ensure harmony doesn't crash
            thunks.Add(() => { Control.Log("Apply prefix {0}.{1}", feature.FullName, methodName); Control.harmony.Patch(method, patch, null); });
            return this;
        }

        public Patchable Postfix(string methodName) {
            var patch = ResolvePatch(methodName);
            // Do checks for public/static, and ensure harmony doesn't crash
            thunks.Add(() => Control.harmony.Patch(method, null, patch));
            return this;
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
 