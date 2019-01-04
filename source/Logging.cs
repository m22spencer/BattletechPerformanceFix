using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using static BattletechPerformanceFix.Extensions;

namespace BattletechPerformanceFix
{
    class Logging
    {
        public static StreamWriter LogStream;
        public static void Init(string logPath) {
            File.Delete(logPath);
            LogStream = File.AppendText(logPath);

            SetLogLevel("spam");
        }

        public static void SetLogLevel(string logLevel) {
            var ll = logLevel.ToLower();
            var levels = List("spam","debug","info","warn","error");
            if (!levels.Contains(ll)) { ll = "info"; }
            Spam  = levels.IndexOf("spam") >= levels.IndexOf(ll);
            Debug = levels.IndexOf("debug") >= levels.IndexOf(ll);
            Info  = levels.IndexOf("info") >= levels.IndexOf(ll);
            Warn  = levels.IndexOf("warn") >= levels.IndexOf(ll);
        }

        internal static void Log(string message, bool flush = false) {
            LogStream.WriteLine(message);
            if (flush) LogStream.Flush();
        }
    }

    public static partial class Extensions {
        public static bool Spam {get; internal set;}
        public static bool Debug {get; internal set;}
        public static bool Info {get; internal set;}
        public static bool Warn {get; internal set;}

        public static void LogException(Exception e) {
            Logging.Log($"[Exception] {e}", true);
            TrapSilently(() => Main.HBSLogger.LogException(e));
        }

        public static void LogError(string message) {
            Logging.Log($"[Error] {message}", true);
            TrapSilently(() => Main.HBSLogger.LogError(message));
        }

        public static void LogWarning(string message) {
            Logging.Log($"[Warning] {message}", true);
            TrapSilently(() => Main.HBSLogger.LogWarning(message));
        }

        public static void LogInfo(string message) {
            Logging.Log($"[Info] {message}", false);
            TrapSilently(() => Main.HBSLogger.Log(message));
        }

        // Can't hit the HBSLogger with debug/spam. It's too slow
        public static void LogDebug(string message) {
            Logging.Log($"[Debug] {message}", false);
        }

        public static void LogSpam(string message) {
            Logging.Log($"[Spam] {message}", false);
        }
    }
}
