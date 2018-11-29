using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Harmony;
using BattleTech;
using BattleTech.Data;
using HBS.Data;
using System.Diagnostics;
using System.IO;
using System.Reflection.Emit;

namespace BattletechPerformanceFix
{
    // Shaves off about a second of load time due to no file exists check or atime read
    public class DataLoaderGetEntryCheck : Feature
    {
        public void Activate()
        {
            var p = nameof(GetEntry);
            var m = new HarmonyMethod(typeof(DataLoaderGetEntryCheck), p);
            Control.harmony.Patch(AccessTools.Method(typeof(DataLoader), p)
                                 , null
                                 , null
                                 , m);
        }

        static DateTime dummyTime = DateTime.UtcNow;

        public static DateTime GetLastWriteTimeUtcStub(string path) => dummyTime;
        public static bool ExistsStub(string path) => true;
   
        public static IEnumerable<CodeInstruction> GetEntry(IEnumerable<CodeInstruction> ins)
        {
            return ins.MethodReplacer( AccessTools.Method(typeof(File), nameof(File.Exists))
                                     , AccessTools.Method(typeof(DataLoaderGetEntryCheck), nameof(ExistsStub)))
                      .MethodReplacer( AccessTools.Method(typeof(File), nameof(File.GetLastWriteTimeUtc))
                                     , AccessTools.Method(typeof(DataLoaderGetEntryCheck), nameof(GetLastWriteTimeUtcStub)));
        }
    }
}
