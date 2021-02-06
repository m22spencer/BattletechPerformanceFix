using System;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;
using System.Linq;
using Harmony;
using static BattletechPerformanceFix.Extensions;
using BattleTech;
using BattleTech.Analytics.Sim;
using BattleTech.Data;
using BattleTech.Framework;
using BattleTech.UI;
using BattleTech.UI.Tooltips;
using BattleTech.UI.TMProWrapper;
using Localize;
using HBS.Collections;
using HBS.Util;

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
            "Rehydrate".Pre<StarSystem>();
            // fix 1.5: TODO explain this
            // FIXME unable to attach to a generic method with generic parameter (how???)
            //var paramTypes = new Type[]{typeof(ContractOverride), typeof(string)};
            //var genericsTypes = new Type[]{typeof(ContractOverride)};
            //var meth = AccessTools.Method(typeof(JSONSerializationUtility), "FromJSON", paramTypes, genericsTypes);
            //var patch = new HarmonyMethod(AccessTools.Method(self, "FromJSON_Post"));
            // NOTE ideally we would patch FromJSON<T>() for T : ContractOverride,
            //      but Harmony has trouble dealing with generic methods, so we patch this instead.
            var paramTypes = new Type[]{typeof(object), typeof(Dictionary<string, object>), typeof(string), typeof(HBS.Stopwatch),
                                        typeof(HBS.Stopwatch), typeof(JSONSerializationUtility.RehydrationFilteringMode),
                                        typeof(Func<string, bool>[])};
            var meth = AccessTools.Method(typeof(JSONSerializationUtility), "RehydrateObjectFromDictionary", paramTypes);
            var patch = new HarmonyMethod(AccessTools.Method(self, "RehydrateObjectFromDictionary_Post"));
            Main.harmony.Patch(meth, null, patch);

            // fixes group 2: occurs on entering/exiting a contract
            // fix 2.2: when a contract completes, remove its OnLanguageChanged subs
            "ResolveCompleteContract".Pre<SimGameState>();

            // fixes group 3: occurs on transiting between star systems
            // fix 3.1: when a star system removes its contracts, remove those contracts' OnLanguageChanged subs
            "ResetContracts".Pre<StarSystem>();
            // fix 3.2: when traveling to a new star system, remove the current system's contracts' OnLanguageChanged subs
            "StartGeneratePotentialContractsRoutine".Pre<SimGameState>();

            // fixes group 4: occurs on accepting & completing a travel contract
            // fix 4.1: don't let the Contract constructor make a copy of its given ContractOverride, let the caller handle it
            paramTypes = new Type[]{ typeof(string), typeof(string), typeof(string), typeof(ContractTypeValue),
                                     typeof(GameInstance), typeof(ContractOverride), typeof(GameContext),
                                     typeof(bool), typeof(int), typeof(int), typeof(int?)};
            var ctor = AccessTools.Constructor(typeof(Contract), paramTypes);
            patch = new HarmonyMethod(AccessTools.Method(self, "Contract_Transpile"));
            Main.harmony.Patch(ctor, null, null, patch);
            "StartGeneratePotentialContractsRoutine".Transpile<SimGameState>();

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
                LogSpam("  contract.Override is set, removing its subs");
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

        private static void Rehydrate_Pre(StarSystem __instance, SimGameState sim)
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

        private static void RehydrateObjectFromDictionary_Post(object target)
        {
            if (target.GetType() != typeof(ContractOverride)) return;
            //LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called with target type ContractOverride");
            var mc = HBS.SceneSingletonBehavior<UnityGameInstance>.Instance.Game.MessageCenter;
            RemoveContractOverrideSubscriptions(mc, (ContractOverride) target);
        }

        private static void ResolveCompleteContract_Pre(SimGameState __instance)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            if (__instance.CompletedContract != null) {
                RemoveContractSubscriptions(__instance.MessageCenter, __instance.CompletedContract);
            }
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

        private static void
        StartGeneratePotentialContractsRoutine_Pre(SimGameState __instance, bool clearExistingContracts,
                                                   StarSystem systemOverride)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            if (clearExistingContracts) {
                List<Contract> contracts = (systemOverride != null) ? __instance.CurSystem.SystemBreadcrumbs :
                                                                      __instance.CurSystem.SystemContracts;
                LogSpam($"clearExistingContracts is set; removing subscriptions for {contracts.Count} contracts");
                foreach(var contract in contracts) {
                    RemoveContractSubscriptions(__instance.MessageCenter, contract);
                }
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
        StartGeneratePotentialContractsRoutine_Transpile(IEnumerable<CodeInstruction> ins)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            var types = new Type[]{typeof(SimGameState), typeof(StarSystem), typeof(bool), typeof(MapAndEncounters),
                                   typeof(SimGameState.MapEncounterContractData), typeof(GameContext)};
            var meth = AccessTools.Method(self, "_CreateProceduralContract", types);
            return TranspileReplaceCall(ins, "CreateProceduralContract", meth);
        }

        private Contract
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
    }
}
// vim: ts=4:sw=4
