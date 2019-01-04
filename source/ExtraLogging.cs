using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Harmony;
using BattleTech;
using BattleTech.UI;
using BattleTech.Framework;
using static BattletechPerformanceFix.Extensions;

namespace BattletechPerformanceFix
{
    class ExtraLogging : Feature
    {
        public void Activate() {
            "RequestLance".Pre<LanceOverride>();
            "PrepContract".Pre<SimGameState>();
            "PrepContract".Post<SimGameState>();
            "InitializeTaggedLance".Pre<LanceSpawnerGameLogic>();
        }

        public static void RequestLance_Pre(int requestedDifficulty, Contract contract, int ___lanceDifficultyAdjustment) {
            LogInfo($"(CL) LanceOverride::RequestLance :contract.Name {contract?.Name} :requestedDifficulty {requestedDifficulty} :lanceDifficultyAdjustment {___lanceDifficultyAdjustment} :contract.Difficulty {contract?.Difficulty}");
        }

        public static void PrepContract_Pre(SimGameState __instance, Contract contract, StarSystem system) {
            var GD = system.Def.GetDifficulty(__instance.SimGameMode);
            LogInfo($"(CL) SimGameState::PrepContract(pre) :contract.Name {contract?.Name} :contract.Difficulty {contract?.Difficulty} :GetDifficulty {GD} :GlobalDifficulty {__instance.GlobalDifficulty} :ContractDifficultyVariance {__instance.Constants.Story.ContractDifficultyVariance}");
        }

        public static void PrepContract_Post(SimGameState __instance, Contract contract, StarSystem system) {
            var fd = Trap(() => new Traverse(contract?.Override).Field("finalDifficulty").GetValue<int>());
            LogInfo($"(CL) SimGameState::PrepContract(post) :contract.Name {contract?.Name} :contract.Difficulty {contract?.Difficulty} :contract.Override.finalDifficulty {fd} :UIDifficulty {contract?.Override?.GetUIDifficulty()}");
        }

        public static void InitializeTaggedLance_Pre() {
            LogInfo($"(CL) Initialize tagged lance (hardcoded difficulty 5)");
        }
    }
}
