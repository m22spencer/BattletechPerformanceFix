using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Harmony;
using BattleTech;
using BattleTech.Save;
using BattleTech.Data;
using System.Reflection;
using static BattletechPerformanceFix.Extensions;

namespace BattletechPerformanceFix
{
    class RemovedFlashpointFix : Feature
    {
        public void Activate()
        {
            "Rehydrate".Pre<SimGameState>();
        }


        static void Rehydrate_Pre(SimGameState __instance, GameInstanceSave gameInstanceSave)
        {
            gameInstanceSave.SimGameSave.AvailableFlashpointList.RemoveAll(item => item.Def == null);
        }

    }
}
