using System;
using System.Collections.Generic;
using Harmony;
using HBS.Data;
using System.IO;

namespace BattletechPerformanceFix
{
    // Shaves off about a second of load time due to no file exists check or atime read
    public class DataLoaderGetEntryCheck : Feature
    {
        public void Activate()
        {
            var p = nameof(GetEntry);

            var getentry = Control.CheckPatch( AccessTools.Method(typeof(DataLoader), p)
                                             , "17104bca745ea14636548c5a2647770edfdeb28808986dafcd721ae0b6971e54");

            var m = new HarmonyMethod(typeof(DataLoaderGetEntryCheck), p);
            Control.harmony.Patch( getentry
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
