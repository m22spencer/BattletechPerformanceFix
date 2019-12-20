using System.Collections.Generic;
using BattleTech;
using BattleTech.Save;
using BattleTech.Framework;
using HBS.Data;

namespace BattletechPerformanceFix
{
    class RemovedContractsFix : Feature
    {
        public void Activate()
        {
            "Rehydrate".Pre<SimGameState>();
        }

        static void Rehydrate_Pre(SimGameState __instance, GameInstanceSave gameInstanceSave)
        {
            IDataItemStore<string, ContractOverride> contractOverrides = __instance.DataManager.ContractOverrides;
            gameInstanceSave.SimGameSave.ContractBits.RemoveAll(item => !contractOverrides.Exists(item.conName));
        }
    }
}
