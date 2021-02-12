using System;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Harmony;
using static BattletechPerformanceFix.Extensions;
using BattleTech;
using BattleTech.Analytics.Sim;
using BattleTech.Data;
using BattleTech.Framework;
using BattleTech.Framework.Save;
using BattleTech.Save;
using BattleTech.Save.Test;
using BattleTech.UI;
using BattleTech.UI.Tooltips;
using BattleTech.UI.TMProWrapper;
using Localize;
using HBS.Collections;
using HBS.FSM;
using HBS.Util;
using UnityEngine;

namespace BattletechPerformanceFix
{
    class MemoryLeakFix: Feature
    {
        private static Type self = typeof(MemoryLeakFix);

        public void Activate() {
            // fixes group 1: occurs on save file load
            // fix 1.1: allow the BattleTechSimAnalytics class to properly remove its message subscriptions
            "BeginSession".Transpile<BattleTechSimAnalytics>("Session_Transpile");
            "EndSession".Transpile<BattleTechSimAnalytics>("Session_Transpile");
            // fix 1.2: add a RemoveSubscriber() for a message type that never had one to begin with
            "OnSimGameInitializeComplete".Post<SimGameUXCreator>();
            // fix 1.3.1: clear InterpolatedText objects that aren't supposed to live forever
            "Destroy".Post<SimGameState>();
            // fix 1.3.2: patch methods making an InterpolatedText object and doesn't store it anywhere
            "RunMadLibs".Transpile<LanceOverride>("MadLib_Transpile");
            "RunMadLibsOnLanceDef".Transpile<LanceOverride>("MadLib_Transpile");
            "RunMadLib".Transpile<UnitSpawnPointOverride>("MadLib_Transpile");
            "UpdateTMPText".Transpile<LocalizableText>("ToTMP_Transpile");
            // these finalizers could never run to begin with, and they only did RemoveSubscriber; nop them
            "Finalize".Transpile<Contract>("Nop_Transpile");
            "Finalize".Transpile<ContractObjectiveOverride>("Nop_Transpile");
            "Finalize".Transpile<ObjectiveOverride>("Nop_Transpile");
            "Finalize".Transpile<DialogueContentOverride>("Nop_Transpile");
            "Finalize".Transpile<InterpolatedText>("Nop_Transpile");
            // fix 1.4: when a savefile is created, two copies of each contract are created and stored;
            //          when that savefile is loaded, one set of copies overwrites the other, and both sets register subs.
            //          this patch unregisters subs for the about-to-be-overwritten contracts
            "Rehydrate".Pre<StarSystem>("SS_Rehydrate_Pre");
            // fix 1.5: when the ContractOverrides are read from JSON, subs are created, but these COs get Copy()ed
            //          before getting attached to a Contract, and Copy also creates subs. the first set of subs
            //          are never seen by the user, so modify the JSON deserialization process to immediately unsub
            "FullRehydrate".Post<ContractOverride>("CO_JSON_Post");
            "FromJSON".Post<ContractOverride>("CO_JSON_Post");
            // fix 1.6: when loading a campaign save, contracts & subs are created for all previously completed
            //          story missions (why? dunno) and then overwritten later on (ugh...) with the globalContracts
            //          defined in the file. completed contract subs were created and must be removed
            "Rehydrate".Pre<SimGameState>("SGS_Rehydrate_Pre");
            "Rehydrate".Post<SimGameState>("SGS_Rehydrate_Post");
            "Rehydrate".Transpile<SimGameState>("SGS_Rehydrate_Transpile");

            // fixes group 2: occurs on entering/exiting a contract
            // fix 2.1: when a contract completes, remove its OnLanguageChanged subs
            "ResolveCompleteContract".Pre<SimGameState>();
            // fix 2.2: none of these classes need to store a CombatGameState
            "ContractInitialize".Post<DialogueContent>("DialogueContent_ContractInitialize_Post");
            "ContractInitialize".Post<ConversationContent>("ConversationContent_ContractInitialize_Post");
            "ContractInitialize".Post<DialogBucketDef>("DialogBucketDef_ContractInitialize_Post");

            // fixes group 3: occurs on transiting between star systems
            // fix 3.1: when a star system removes its contracts, remove those contracts' OnLanguageChanged subs
            "ResetContracts".Pre<StarSystem>();
            // fix 3.2: see below

            // fixes group 4: occurs on accepting & completing a travel contract
            // fix 4.1: don't let the Contract constructor make a copy of its given ContractOverride,
            //          let the caller handle it...
            var paramTypes = new Type[]{ typeof(string), typeof(string), typeof(string), typeof(ContractTypeValue),
                                     typeof(GameInstance), typeof(ContractOverride), typeof(GameContext),
                                     typeof(bool), typeof(int), typeof(int), typeof(int?)};
            var ctor = AccessTools.Constructor(typeof(Contract), paramTypes);
            var patch = new HarmonyMethod(AccessTools.Method(self, "Contract_Transpile"));
            Main.harmony.Patch(ctor, null, null, patch);
            //          ...and perform a Copy() of the ObjectiveOverride in the one spot that needs it
            // NOTE there's no clear way for Harmony to transpile an IEnumerator-returning method directly
            //      (something about having to patch *all* instances of IEnumerator.MoveNext() [lol no]),
            //      so instead just reimplement the method and patch its callers to use the reimplementation
            "GeneratePotentialContracts".Transpile<SimGameState>();

            // fixes group 5: occurs when completing and/or cancelling a travel contract
            // fix 5.1: when arriving at a travel contract's destination,
            //            remove the breadcrumb's ("pointer" to destination contract's) subs
            "FinishCompleteBreadcrumbProcess".Pre<SimGameState>();
            "FinishCompleteBreadcrumbProcess".Post<SimGameState>();
            // fix 5.2.1: when backing out of a travel contract proper (ie not breadcrumb), remove its subs
            "OnLanceConfigurationCancelled".Pre<SimGameState>();
            // fix 5.2.2: same but for campaign mission
            "CancelStoryOrConsecutiveLanceConfiguration".Pre<SimGameState>();
            // fix 5.3: when cancelling a travel contract, remove its breadcrumb's subs
            "FailBreadcrumb".Pre<SimGameState>();

            // fixes group 6: occurs on creating a new savefile
            // fix 6.1: clean up the GameInstanceSave.references after serialization is complete
            "PostSerialization".Post<GameInstanceSave>();
        }

        private static IEnumerable<CodeInstruction> Session_Transpile(IEnumerable<CodeInstruction> ins)
        {
            var meth = AccessTools.Method(self, "_UpdateMessageSubscriptions");
            return TranspileReplaceCall(ins, "UpdateMessageSubscriptions", meth);
        }

        private static void _UpdateMessageSubscriptions(BattleTechSimAnalytics __instance, bool subscribe)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            var mc = __instance.messageCenter;
            if (mc != null) {
                mc.Subscribe(MessageCenterMessageType.OnReportMechwarriorSkillUp,
                             new ReceiveMessageCenterMessage(__instance.ReportMechWarriorSkilledUp), subscribe);
                mc.Subscribe(MessageCenterMessageType.OnReportMechwarriorHired,
                             new ReceiveMessageCenterMessage(__instance.ReportMechWarriorHired), subscribe);
                mc.Subscribe(MessageCenterMessageType.OnReportMechWarriorKilled,
                             new ReceiveMessageCenterMessage(__instance.ReportMechWarriorKilled), subscribe);
                mc.Subscribe(MessageCenterMessageType.OnReportShipUpgradePurchased,
                             new ReceiveMessageCenterMessage(__instance.ReportShipUpgradePurchased), subscribe);
                mc.Subscribe(MessageCenterMessageType.OnSimGameContractComplete,
                             new ReceiveMessageCenterMessage(__instance.ReportContractComplete), subscribe);
                mc.Subscribe(MessageCenterMessageType.OnSimRoomStateChanged,
                             new ReceiveMessageCenterMessage(__instance.ReportSimGameRoomChange), subscribe);
            }
        }

        private static void OnSimGameInitializeComplete_Post(SimGameUXCreator __instance)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            __instance.sim.MessageCenter.RemoveSubscriber(
                    MessageCenterMessageType.OnSimGameInitialized,
                    new ReceiveMessageCenterMessage(__instance.OnSimGameInitializeComplete));
        }

        private static void Destroy_Post(SimGameState __instance)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            if (__instance == null) {
                LogSpam("SimGameState was null (ok if first load)");
                return;
            }

            var contracts = __instance.GetAllCurrentlySelectableContracts();
            LogSpam($"removing subscriptions for {contracts.Count} contracts");
            foreach (var contract in contracts) {
                RemoveContractSubscriptions(__instance.MessageCenter, contract);
            }
        }

        private static void RemoveContractSubscriptions(MessageCenter mc, Contract contract)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            LogSpam($"removing subs for contract {contract.GetHashCode()}");
            mc.RemoveSubscriber(MessageCenterMessageType.OnLanguageChanged,
                                new ReceiveMessageCenterMessage(contract.OnLanguageChanged));

            if (contract.Override != null) {
                RemoveContractOverrideSubscriptions(mc, contract.Override);
            }
        }

        private static void
        RemoveContractOverrideSubscriptions(MessageCenter mc, ContractOverride contractOverride)
        {
            LogSpam($"removing subs for contract override {contractOverride.GetHashCode()}");
            foreach (var dialogue in contractOverride.dialogueList) {
                foreach (var content in dialogue.dialogueContent) {
                    RemoveTextSubscriber(mc, (InterpolatedText) content._Words);
                    mc.RemoveSubscriber(MessageCenterMessageType.OnLanguageChanged,
                                        new ReceiveMessageCenterMessage(content.OnLanguageChanged));
                }
            }

            foreach (var objective in contractOverride.contractObjectiveList) {
                RemoveTextSubscriber(mc, (InterpolatedText) objective._Title);
                RemoveTextSubscriber(mc, (InterpolatedText) objective._Description);
                mc.RemoveSubscriber(MessageCenterMessageType.OnLanguageChanged,
                                    new ReceiveMessageCenterMessage(objective.OnLanguageChanged));
            }

            foreach (var objective in contractOverride.objectiveList) {
                RemoveTextSubscriber(mc, (InterpolatedText) objective._Title);
                RemoveTextSubscriber(mc, (InterpolatedText) objective._Description);
                mc.RemoveSubscriber(MessageCenterMessageType.OnLanguageChanged,
                                    new ReceiveMessageCenterMessage(objective.OnLanguageChanged));
            }
        }

        private static void RemoveTextSubscriber(MessageCenter mc, InterpolatedText text) {
            if (text != null) {
                mc.RemoveSubscriber(MessageCenterMessageType.OnLanguageChanged,
                                    new ReceiveMessageCenterMessage(text.OnLanguageChanged));
            }
        }

        private static IEnumerable<CodeInstruction> MadLib_Transpile(IEnumerable<CodeInstruction> ins)
        {
            var methString = AccessTools.Method(self, "_Contract_RunMadLib",
                                                new Type[]{typeof(Contract), typeof(string)});
            var methTagSet = AccessTools.Method(self, "_Contract_RunMadLib",
                                                new Type[]{typeof(Contract), typeof(TagSet)});
            var firstPass = TranspileReplaceOverloadedCall(ins, typeof(Contract), "RunMadLib",
                                                           new Type[]{typeof(string)}, methString);
            return TranspileReplaceOverloadedCall(firstPass, typeof(Contract), "RunMadLib",
                                                  new Type[]{typeof(TagSet)}, methTagSet);
        }

        private static IEnumerable<CodeInstruction>
        TranspileReplaceCall(IEnumerable<CodeInstruction> ins, string originalMethodName,
                             MethodInfo replacementMethod)
        {
            LogInfo($"TranspileReplaceCall: {originalMethodName} -> {replacementMethod.ToString()}");
            return ins.SelectMany(i => {
                if (i.opcode == OpCodes.Call &&
                   (i.operand as MethodInfo).Name.StartsWith(originalMethodName)) {
                    i.operand = replacementMethod;
                }
                return Sequence(i);
            });
        }

        private static IEnumerable<CodeInstruction>
        TranspileReplaceOverloadedCall(IEnumerable<CodeInstruction> ins, Type originalMethodClass,
                                       string originalMethodName, Type[] originalParamTypes,
                                       MethodInfo replacementMethod)
        {
            LogInfo($"TranspileReplaceOverloadedCall: {originalMethodClass.ToString()}.{originalMethodName}" +
                     $"({String.Concat(originalParamTypes.Select(x => x.ToString()))}) -> {replacementMethod.ToString()}");
            return ins.SelectMany(i => {
                var methInfo = i.operand as MethodInfo;
                if (i.opcode == OpCodes.Callvirt &&
                    methInfo.DeclaringType == originalMethodClass &&
                    methInfo.Name.StartsWith(originalMethodName) &&
                    Enumerable.SequenceEqual(methInfo.GetParameters().Select(x => x.ParameterType), originalParamTypes))
                {
                    i.operand = replacementMethod;
                }
                return Sequence(i);
            });
        }

        private static string _Contract_RunMadLib(Contract __instance, string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }
            InterpolatedText iText = __instance.Interpolate(text);
            text = iText.ToString(false);
            __instance.messageCenter.RemoveSubscriber(MessageCenterMessageType.OnLanguageChanged,
                                                      new ReceiveMessageCenterMessage(iText.OnLanguageChanged));
            return text;
        }

        private static void _Contract_RunMadLib(Contract __instance, TagSet tagSet)
        {
            if (tagSet == null)
            {
                return;
            }
            string[] array = tagSet.ToArray();
            if (array == null)
            {
                return;
            }
            for (int i = 0; i < array.Length; i++)
            {
                string text = array[i];
                InterpolatedText iText = __instance.Interpolate(text);
                text = iText.ToString(false);
                __instance.messageCenter.RemoveSubscriber(MessageCenterMessageType.OnLanguageChanged,
                                                          new ReceiveMessageCenterMessage(iText.OnLanguageChanged));
                array[i] = text.ToLower();
            }
            tagSet.Clear();
            tagSet.AddRange(array);
        }

        private static IEnumerable<CodeInstruction> ToTMP_Transpile(IEnumerable<CodeInstruction> ins)
        {
            var originalTypes = new Type[]{typeof(GameContext), typeof(TextTooltipFormatOptions)};
            var replacementTypes = new Type[]{typeof(TextTooltipParser), typeof(GameContext), typeof(TextTooltipFormatOptions)};
            var meth = AccessTools.Method(self, "_TextTooltipParser_ToTMP", replacementTypes);
            return TranspileReplaceOverloadedCall(ins, typeof(TextTooltipParser), "ToTMP", originalTypes, meth);
        }

        private static Text
        _TextTooltipParser_ToTMP(TextTooltipParser __instance, GameContext gameContext, TextTooltipFormatOptions formatOptions)
        {
            var mc = HBS.SceneSingletonBehavior<UnityGameInstance>.Instance.Game.MessageCenter;
            return __instance.GenerateFinalString((TextTooltipData x) => {
                InterpolatedText iText = x.ToTMP(gameContext, formatOptions);
                mc.RemoveSubscriber(MessageCenterMessageType.OnLanguageChanged,
                                    new ReceiveMessageCenterMessage(iText.OnLanguageChanged));
                return iText;
            });
        }

        private static IEnumerable<CodeInstruction> Nop_Transpile(IEnumerable<CodeInstruction> ins)
        {
            return ins.SelectMany(i => {
                i.opcode = OpCodes.Nop;
                i.operand = null;
                return Sequence(i);
            });
        }

        private static void SS_Rehydrate_Pre(StarSystem __instance, SimGameState sim)
        {
            if (__instance.activeSystemContracts != null) {
                foreach (var contract in __instance.activeSystemContracts) {
                    RemoveContractSubscriptions(sim.MessageCenter, contract);
                }
            }
            if (__instance.activeSystemBreadcrumbs != null) {
                foreach (var contract in __instance.activeSystemBreadcrumbs) {
                    RemoveContractSubscriptions(sim.MessageCenter, contract);
                }
            }
        }

        private static void CO_JSON_Post(ContractOverride __instance)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            var mc = HBS.SceneSingletonBehavior<UnityGameInstance>.Instance.Game.MessageCenter;
            RemoveContractOverrideSubscriptions(mc, __instance);
        }

        private static void SGS_Rehydrate_Pre(SimGameState __instance, ref List<Contract> __state)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            __state = __instance.globalContracts;
        }

        private static void SGS_Rehydrate_Post(SimGameState __instance, ref List<Contract> __state)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            LogSpam($"{__state?.Count} contracts");
            foreach (var contract in __state) {
                RemoveContractSubscriptions(__instance.MessageCenter, contract);
            }
        }

        private static IEnumerable<CodeInstruction> SGS_Rehydrate_Transpile(IEnumerable<CodeInstruction> ins)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            var types = new Type[]{typeof(SimGameState), typeof(SimGameState.AddContractData)};
            var meth = AccessTools.Method(self, "_AddContract", types);
            return TranspileReplaceCall(ins, "AddContract", meth);
        }

        private static Contract _AddContract(SimGameState __instance, SimGameState.AddContractData contractData)
        {
            StarSystem starSystem;
            if (!string.IsNullOrEmpty(contractData.TargetSystem))
            {
                string validatedSystemString = __instance.GetValidatedSystemString(contractData.TargetSystem);
                if (!__instance.starDict.ContainsKey(validatedSystemString))
                {
                    return null;
                }
                starSystem = __instance.starDict[validatedSystemString];
            }
            else
            {
                starSystem = __instance.CurSystem;
            }
            FactionValue factionValueFromString = __instance.GetFactionValueFromString(contractData.Target);
            FactionValue factionValueFromString2 = __instance.GetFactionValueFromString(contractData.Employer);
            FactionValue factionValue = __instance.GetFactionValueFromString(contractData.TargetAlly);
            FactionValue factionValue2 = __instance.GetFactionValueFromString(contractData.EmployerAlly);
            FactionValue factionValueFromString3 = __instance.GetFactionValueFromString(contractData.NeutralToAll);
            FactionValue factionValueFromString4 = __instance.GetFactionValueFromString(contractData.HostileToAll);
            if (factionValueFromString.IsInvalidUnset || factionValueFromString2.IsInvalidUnset)
            {
                return null;
            }
            factionValue = (factionValue.IsInvalidUnset ? factionValueFromString : factionValue);
            factionValue2 = (factionValue2.IsInvalidUnset ? factionValueFromString2 : factionValue2);
            ContractOverride contractOverride = __instance.DataManager.ContractOverrides.Get(contractData.ContractName).Copy();
            RemoveContractOverrideSubscriptions(__instance.MessageCenter, contractOverride);
            ContractTypeValue contractTypeValue = contractOverride.ContractTypeValue;
            if (contractTypeValue.IsTravelOnly)
            {
                return __instance.AddTravelContract(contractOverride, starSystem, factionValueFromString2);
            }
            List<MapAndEncounters> releasedMapsAndEncountersByContractTypeAndOwnership = MetadataDatabase.Instance.GetReleasedMapsAndEncountersByContractTypeAndOwnership(contractTypeValue.ID, false);
            if (releasedMapsAndEncountersByContractTypeAndOwnership == null || releasedMapsAndEncountersByContractTypeAndOwnership.Count == 0)
            {
                UnityEngine.Debug.LogError(string.Format("There are no playable maps for __instance contract type[{0}]. Was your map published?", contractTypeValue.Name));
            }
            MapAndEncounters mapAndEncounters = releasedMapsAndEncountersByContractTypeAndOwnership[0];
            List<EncounterLayer_MDD> list = new List<EncounterLayer_MDD>();
            foreach (EncounterLayer_MDD encounterLayer_MDD in mapAndEncounters.Encounters)
            {
                if (encounterLayer_MDD.ContractTypeRow.ContractTypeID == (long)contractTypeValue.ID)
                {
                    list.Add(encounterLayer_MDD);
                }
            }
            if (list.Count <= 0)
            {
                throw new Exception("Map does not contain any encounters of type: " + contractTypeValue.Name);
            }
            string encounterLayerGUID = list[__instance.NetworkRandom.Int(0, list.Count)].EncounterLayerGUID;
            GameContext gameContext = new GameContext(__instance.Context);
            gameContext.SetObject(GameContextObjectTagEnum.TargetStarSystem, starSystem);
            if (contractData.IsGlobal)
            {
                Contract contract = __instance.CreateTravelContract(mapAndEncounters.Map.MapName, mapAndEncounters.Map.MapPath, encounterLayerGUID, contractTypeValue, contractOverride, gameContext, factionValueFromString2, factionValueFromString, factionValue, factionValue2, factionValueFromString3, factionValueFromString4, contractData.IsGlobal, contractOverride.difficulty);
                __instance.PrepContract(contract, factionValueFromString2, factionValue2, factionValueFromString, factionValue, factionValueFromString3, factionValueFromString4, mapAndEncounters.Map.BiomeSkinEntry.BiomeSkin, contract.Override.travelSeed, starSystem);
                __instance.GlobalContracts.Add(contract);
                return contract;
            }
            Contract contract2 = new Contract(mapAndEncounters.Map.MapName, mapAndEncounters.Map.MapPath, encounterLayerGUID, contractTypeValue, __instance.BattleTechGame, contractOverride, gameContext, true, contractOverride.difficulty, 0, null);
            if (!contractData.FromSave)
            {
                ContractData contractData2 = new ContractData(contractData.ContractName, contractData.Target, contractData.Employer, contractData.TargetSystem, contractData.TargetAlly, contractData.EmployerAlly);
                contractData2.SetGuid(Guid.NewGuid().ToString());
                contract2.SetGuid(contractData2.GUID);
                __instance.contractBits.Add(contractData2);
            }
            if (contractData.FromSave)
            {
                contract2.SetGuid(contractData.SaveGuid);
            }
            __instance.PrepContract(contract2, factionValueFromString2, factionValue2, factionValueFromString, factionValue, factionValueFromString3, factionValueFromString4, mapAndEncounters.Map.BiomeSkinEntry.BiomeSkin, contract2.Override.travelSeed, starSystem);
            starSystem.SystemContracts.Add(contract2);
            return contract2;
        }

        private static void ResolveCompleteContract_Pre(SimGameState __instance)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            if (__instance.CompletedContract != null) {
                RemoveContractSubscriptions(__instance.MessageCenter, __instance.CompletedContract);
            }
        }

        private static void DialogueContent_ContractInitialize_Post(DialogueContent __instance)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            __instance.combat = null;
        }

        private static void ConversationContent_ContractInitialize_Post(ConversationContent __instance)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            __instance.combat = null;
        }

        private static void DialogBucketDef_ContractInitialize_Post(DialogBucketDef __instance)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            __instance.combat = null;
        }

        private static void ResetContracts_Pre(StarSystem __instance)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            foreach (var contract in __instance.activeSystemContracts) {
                RemoveContractSubscriptions(__instance.Sim.MessageCenter, contract);
            }
            foreach (var contract in __instance.activeSystemBreadcrumbs) {
                RemoveContractSubscriptions(__instance.Sim.MessageCenter, contract);
            }
        }

        private static IEnumerable<CodeInstruction> Contract_Transpile(IEnumerable<CodeInstruction> ins)
        {
            LogInfo($"Contract_Transpile: nopping call to Copy()");
            var meth = AccessTools.Method(typeof(ContractOverride), "Copy");
            CodeInstruction toBeNopped = new CodeInstruction(OpCodes.Callvirt, meth);
            return ins.SelectMany(i => {
                if (i.opcode == toBeNopped.opcode && i.operand == toBeNopped.operand) {

                    i.opcode = OpCodes.Nop;
                    i.operand = null;
                }
                return Sequence(i);
            });
        }

        private static IEnumerable<CodeInstruction>
        GeneratePotentialContracts_Transpile(IEnumerable<CodeInstruction> ins)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            var types = new Type[]{typeof(SimGameState), typeof(bool), typeof(Action), typeof(StarSystem), typeof(bool)};
            var meth = AccessTools.Method(self, "_StartGeneratePotentialContractsRoutine", types);
            return TranspileReplaceCall(ins, "StartGeneratePotentialContractsRoutine", meth);
        }

        private static IEnumerator
        _StartGeneratePotentialContractsRoutine(SimGameState __instance, bool clearExistingContracts,
                                                Action onContractGenComplete, StarSystem systemOverride, bool useWait)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            int debugCount = 0;
            bool usingBreadcrumbs = systemOverride != null;
            if (useWait)
            {
                yield return new WaitForSeconds(0.2f);
            }
            StarSystem system;
            List<Contract> contractList;
            int maxContracts;
            if (usingBreadcrumbs)
            {
                system = systemOverride;
                contractList = __instance.CurSystem.SystemBreadcrumbs;
                maxContracts = __instance.CurSystem.CurMaxBreadcrumbs;
            }
            else
            {
                system = __instance.CurSystem;
                contractList = __instance.CurSystem.SystemContracts;
                maxContracts = Mathf.CeilToInt(system.CurMaxContracts);
            }
            if (clearExistingContracts)
            {
                // fix 3.2: when traveling to a new star system, remove the current system's contracts' OnLanguageChanged subs
                LogSpam($"clearExistingContracts is set; removing subscriptions for {contractList.Count} contractList");
                foreach(var contract in contractList) {
                    RemoveContractSubscriptions(__instance.MessageCenter, contract);
                }
                contractList.Clear();
            }
            SimGameState.ContractDifficultyRange difficultyRange = __instance.GetContractRangeDifficultyRange(system, __instance.SimGameMode, __instance.GlobalDifficulty);
            Dictionary<int, List<ContractOverride>> potentialContracts = __instance.GetSinglePlayerProceduralContractOverrides(difficultyRange);
            WeightedList<MapAndEncounters> playableMaps = SimGameState.GetSinglePlayerProceduralPlayableMaps(system);
            Dictionary<string, WeightedList<SimGameState.ContractParticipants>> validParticipants = __instance.GetValidParticipants(system);
            if (!__instance.HasValidMaps(system, playableMaps) || !__instance.HasValidContracts(difficultyRange, potentialContracts) || !__instance.HasValidParticipants(system, validParticipants))
            {
                if (onContractGenComplete != null)
                {
                    onContractGenComplete();
                }
                yield break;
            }
            __instance.ClearUsedBiomeFromDiscardPile(playableMaps);
            while (contractList.Count < maxContracts && debugCount < 1000)
            {
                int num = debugCount;
                debugCount = num + 1;
                IEnumerable<int> source = from map in playableMaps
                select map.Map.Weight;
                WeightedList<MapAndEncounters> weightedList = new WeightedList<MapAndEncounters>(WeightedListType.WeightedRandom, playableMaps.ToList(), source.ToList<int>(), 0);
                __instance.FilterActiveMaps(weightedList, contractList);
                weightedList.Reset(false);
                MapAndEncounters next = weightedList.GetNext(false);
                SimGameState.MapEncounterContractData mapEncounterContractData = __instance.FillMapEncounterContractData(system, difficultyRange, potentialContracts, validParticipants, next);
                while (!mapEncounterContractData.HasContracts && weightedList.ActiveListCount > 0)
                {
                    next = weightedList.GetNext(false);
                    mapEncounterContractData = __instance.FillMapEncounterContractData(system, difficultyRange, potentialContracts, validParticipants, next);
                }
                system.SetCurrentContractFactions(null, null);
                if (mapEncounterContractData == null || mapEncounterContractData.Contracts.Count == 0)
                {
                    if (__instance.mapDiscardPile.Count > 0)
                    {
                        __instance.mapDiscardPile.Clear();
                    }
                    else
                    {
                        debugCount = 1000;
                        SimGameState.logger.LogError(string.Format("[CONTRACT] Unable to find any valid contracts for available map pool. Alert designers."/*, Array.Empty<object>()*/));
                    }
                }
                GameContext gameContext = new GameContext(__instance.Context);
                gameContext.SetObject(GameContextObjectTagEnum.TargetStarSystem, system);
                // see fix 4.1
                Contract item = _CreateProceduralContract(__instance, system, usingBreadcrumbs, next, mapEncounterContractData, gameContext);
                contractList.Add(item);
                if (useWait)
                {
                    yield return new WaitForSeconds(0.2f);
                }
            }
            if (debugCount >= 1000)
            {
                SimGameState.logger.LogError("Unable to fill contract list. Please inform AJ Immediately");
            }
            if (onContractGenComplete != null)
            {
                onContractGenComplete();
            }
            yield break;
        }

        private static Contract
        _CreateProceduralContract(SimGameState __instance, StarSystem system, bool usingBreadcrumbs, MapAndEncounters level,
                                  SimGameState.MapEncounterContractData MapEncounterContractData, GameContext gameContext)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            WeightedList<SimGameState.PotentialContract> flatContracts = MapEncounterContractData.FlatContracts;
            __instance.FilterContracts(flatContracts);
            SimGameState.PotentialContract next = flatContracts.GetNext(true);
            int id = next.contractOverride.ContractTypeValue.ID;
            MapEncounterContractData.Encounters[id].Shuffle<EncounterLayer_MDD>();
            string encounterLayerGUID = MapEncounterContractData.Encounters[id][0].EncounterLayerGUID;
            ContractOverride contractOverride = next.contractOverride;
            FactionValue employer = next.employer;
            FactionValue target = next.target;
            FactionValue employerAlly = next.employerAlly;
            FactionValue targetAlly = next.targetAlly;
            FactionValue neutralToAll = next.NeutralToAll;
            FactionValue hostileToAll = next.HostileToAll;
            int difficulty = next.difficulty;
            Contract contract;
            if (usingBreadcrumbs) {
                contract = __instance.CreateTravelContract(level.Map.MapName, level.Map.MapPath, encounterLayerGUID,
                                                           next.contractOverride.ContractTypeValue, contractOverride,
                                                           gameContext, employer, target, targetAlly, employerAlly,
                                                           neutralToAll, hostileToAll, false, difficulty);
            } else {
                // see fix 4.1
                LogSpam("copying contractOverride");
                contractOverride = contractOverride.Copy();
                contract = new Contract(level.Map.MapName, level.Map.MapPath, encounterLayerGUID,
                                        next.contractOverride.ContractTypeValue, __instance.BattleTechGame,
                                        contractOverride, gameContext, true, difficulty, 0, null);
            }
            __instance.mapDiscardPile.Add(level.Map.MapID);
            __instance.contractDiscardPile.Add(contractOverride.ID);
            __instance.PrepContract(contract, employer, employerAlly, target, targetAlly, neutralToAll,
                                    hostileToAll, level.Map.BiomeSkinEntry.BiomeSkin, contract.Override.travelSeed, system);
            return contract;
        }

        private static void FinishCompleteBreadcrumbProcess_Pre(SimGameState __instance, ref Contract __state)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            if (__instance.activeBreadcrumb != null) {
                LogSpam($"activeBreadcrumb: {__instance.activeBreadcrumb.GetHashCode()}");
            }
            if (__instance.pendingBreadcrumb != null) {
                LogSpam($"pendingBreadcrumb: {__instance.pendingBreadcrumb.GetHashCode()}");
            }
            __state = __instance.activeBreadcrumb;
        }

        private static void FinishCompleteBreadcrumbProcess_Post(SimGameState __instance, ref Contract __state)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            if (__instance.pendingBreadcrumb == null && __state != null) {
                LogSpam($"activeBreadcrumb and pendingBreadcrumb are both null, __state isn't null; contract was removed");
                RemoveContractSubscriptions(__instance.MessageCenter, __state);
            }
        }

        private static void OnLanceConfigurationCancelled_Pre(SimGameState __instance)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            if (__instance.SelectedContract != null && !__instance.SelectedContract.IsPriorityContract &&
                __instance.pendingBreadcrumb != null && __instance.IsSelectedContractForced) {
                if (__instance.CurSystem.SystemContracts.Contains(__instance.SelectedContract) ||
                    __instance.GlobalContracts.Contains(__instance.SelectedContract)) {
                    RemoveContractSubscriptions(__instance.MessageCenter, __instance.SelectedContract);
                }
            }
        }

        private static void CancelStoryOrConsecutiveLanceConfiguration_Pre(SimGameState __instance)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            if (__instance.SelectedContract != null) {
                LogSpam($"SelectedContract: {__instance.SelectedContract.GetHashCode()}");
                RemoveContractSubscriptions(__instance.MessageCenter, __instance.SelectedContract);
            }
        }

        private static void FailBreadcrumb_Pre(SimGameState __instance)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            if (__instance.activeBreadcrumb != null) {
                RemoveContractSubscriptions(__instance.MessageCenter, __instance.activeBreadcrumb);
            }
        }

        private static void ClearMessageMemory(MessageMemory msgMemory)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            if (msgMemory.subscribedMessages != null) {
                LogSpam($"  {msgMemory.subscribedMessages.Count} subscribed ");
                foreach (var key in msgMemory.subscribedMessages.Keys) {
                    foreach (var sub in msgMemory.subscribedMessages[key]) {
                        msgMemory.messageCenter.RemoveSubscriber(key, sub);
                    }
                    msgMemory.subscribedMessages[key].Clear();
                }
                msgMemory.subscribedMessages.Clear();
            }
            if (msgMemory.trackedMessages != null) {
                LogSpam($"  {msgMemory.trackedMessages.Count} tracked ");
                foreach (var trackList in msgMemory.trackedMessages.Values) {
                    trackList.Clear();
                }
                msgMemory.trackedMessages.Clear();
            }
        }

        private static void ClearBasicMachine(BasicMachine machine)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            machine.OnChange = null;
            if (machine.stateList != null) {
                LogSpam($"  clearing state machine, {machine.stateList.Count} states");
                foreach (var state in machine.stateList) {
                    state.CanEnter = null;
                    state.OnEnter = null;
                    state.OnExit = null;
                }
            }
        }

        // TODO test this more thoroughly, i think it caused problems in the past...
        private static void PostSerialization_Post(GameInstanceSave __instance)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            //LogSpam($"{__instance.serializePassOne.ToString()}");
            //if (!__instance.serializePassOne) {
                // TODO test this on a combat save (might break in that case)
                LogSpam("setting references to an empty SerializableReferenceContainer");
                __instance.references = new SerializableReferenceContainer("the one and only");
            //}
        }
    }
}
// vim: ts=4:sw=4
