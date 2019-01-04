using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Diagnostics;
using static BattletechPerformanceFix.Extensions;

namespace BattletechPerformanceFix
{
    class Logging
    {
        public static StreamWriter LogStream;
        public static void Init(string logPath) {
            File.Delete(logPath);
            LogStream = File.AppendText(logPath);

            lastFlush = Stopwatch.StartNew();

            SetLogLevel("spam");
        }

        public static void SetLogLevel(string logLevel) {
            var ll = logLevel.ToLower();
            var levels = List("spam","debug","info","warn","error");
            if (!levels.Contains(ll)) { ll = "info"; }
            Spam  = levels.IndexOf("spam") >= levels.IndexOf(ll);
            Extensions.Debug = levels.IndexOf("debug") >= levels.IndexOf(ll);
            Info  = levels.IndexOf("info") >= levels.IndexOf(ll);
            Warn  = levels.IndexOf("warn") >= levels.IndexOf(ll);

            Log($"Levels :Spam {Spam} :Debug {Extensions.Debug} :Info {Info} :Warn {Warn}");
        }

        public static Stopwatch lastFlush;
        internal static void Log(string message, bool flush = false) {
            LogStream.WriteLine(message);
            if (lastFlush.Elapsed.TotalMilliseconds > 100) flush = true;
            if (flush) { lastFlush.Reset();
                         lastFlush.Start();
                         LogStream.Flush(); }
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
            if (!Info) return;
            Logging.Log($"[Info] {message}", false);
            TrapSilently(() => Main.HBSLogger.Log(message));
        }

        // Can't hit the HBSLogger with debug/spam. It's too slow
        public static void LogDebug(string message) {
            if (!Debug) return;
            Logging.Log($"[Debug] {message}", false);
        }

        public static void LogSpam(string message) {
            if (!Spam) return;
            Logging.Log($"[Spam] {message}", false);
        }
    }
}
