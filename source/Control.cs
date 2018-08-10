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
                    loadFixes.Activate();
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
    [HarmonyPatch(typeof(Logger), "LogImpl")]
    public static class No_Logs
    {
        public static bool Prefix()
        {
            return false;
        }
    }
    */

    /*
    [HarmonyPatch(typeof(DictionaryPersistentStore), nameof(DictionaryPersistentStore.Save))]
    public static class DictionaryPersistentStore_Save_NoThreading
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> body)
        {

            var ethis = new CodeInstruction(OpCodes.Ldarg_0);
            var ecall = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(DictionaryPersistentStore), "BackgroundSave"));
            var eret = new CodeInstruction(OpCodes.Ret);

            var newBody = new CodeInstruction[] { ethis, ecall, eret };
            return newBody.AsEnumerable();
        }
    }

    [HarmonyPatch(typeof(Task), "MoveNextAsync")]
    public static class NoThreaded_MoveNextAsync
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> body)
        {

            var ethis = new CodeInstruction(OpCodes.Ldarg_0);
            var ecall = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Task), "MoveNextUnity"));
            var eret = new CodeInstruction(OpCodes.Ret);

            var newBody = new CodeInstruction[] { ethis, ecall, eret };
            return newBody.AsEnumerable();
        }
    }
    */

    /*
        private int AddRequest(ThreadedSaveManagerRequest request)
    {
    	threadedRequsts.Add(request);
	    LazySingletonBehavior<ThreadPoolManager>.Instance.QueueTask(request.ThreadPoolManagerTask);
	    return request.RequestID;
    }
    */
    /*
    [HarmonyPatch(typeof(SaveSystem), "ProcessRequests")]
    public static class Foo
    {
        static bool guard = false;
        public static void Prefix()
        {
            if (guard)
                return;
            Control.Log("MMInitHook");

            guard = true;
            NoThreaded_SaveSystem_AddRequest.EmptyQueue();
            guard = false;
        }
    }
    */

        /*
    [HarmonyPatch(typeof(SimpleThreadPool), nameof(SimpleThreadPool.QueueTask))]
    public static class QueueTaskInCSThreadPool
    {
        public static bool Prefix(Action task)
        {
            Control.Trap(() =>
            {
                Control.Log("Queuing");
                System.Threading.ThreadPool.QueueUserWorkItem(x => task());
            });
            return false;
        }
    }
    */

    //[HarmonyPatch(typeof(SimpleThreadPool), "Worker")]
    public static class SimpleThreadPool_Worker_Drop { 
        public static bool Prefix(BlockingQueue<Action> ___tasks, ref bool ___disposed)
        {
            Queue<Action> queue = new Traverse(___tasks).Field("queue").GetValue<Queue<Action>>();
            while(true && !___disposed)
            {
                //Control.Log("Sleeping");
                while(queue.Any())
                {
                    Control.Log("Thread do work");
                    var act = queue.Dequeue();
                    Control.Trap(() => act());
                }
                System.Threading.Thread.Sleep(100);
            }

            Control.Log("Worker shutting down");

            return false;
        }
    }
}
 