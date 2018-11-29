using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BattleTech.Rendering;
using Harmony;
using BattleTech.Framework;
using BattleTech;
using BattleTech.Data;

namespace BattletechPerformanceFix
{
    // Shaves off about a second of load time due to no file exists check or atime read
    public class DynamicTagsFix : Feature
    {
        public void Activate()
        {
            var p = nameof(GenerateTeam);
            var m = new HarmonyMethod(typeof(DynamicTagsFix), p);
            Control.harmony.Patch(AccessTools.Method(typeof(ContractOverride), p)
                                 , m
                                 , null
                                 , null);
        }

        public static int lastUpdate = int.MinValue;
        public static int ct = 0;
        public static void GenerateTeam(TeamOverride teamOverride, Contract ___contract, DataManager ___dataManager)
        {
            teamOverride.RunMadLibs(___contract, ___dataManager);
        }
    }
}
