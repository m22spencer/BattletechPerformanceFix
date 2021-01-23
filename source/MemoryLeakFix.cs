using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;
using System.Linq;
using Harmony;
using static BattletechPerformanceFix.Extensions;
using BattleTech;
using BattleTech.Analytics.Sim;
using BattleTech.Framework;
using BattleTech.UI;
using Localize;
using HBS.Collections;

namespace BattletechPerformanceFix
{
    class MemoryLeakFix: Feature
    {
        private static Type self = typeof(MemoryLeakFix);

        public void Activate() {
            // fixes group 1: occurs on save file load
            // fix 1.1: allow the BattleTechSimAnalytics class to properly remove its message subscriptions
            "BeginSession".Transpile<BattleTechSimAnalytics>("_SessionTranspiler");
            "EndSession".Transpile<BattleTechSimAnalytics>("_SessionTranspiler");
            // fix 1.2: add a RemoveSubscriber() for a message type that never had one to begin with
            "OnSimGameInitializeComplete".Post<SimGameUXCreator>("_OnSimGameInitializeComplete_Post");
            // fix 1.3.1: clear InterpolatedText objects that aren't supposed to live forever
            "ClearSimulation".Pre<GameInstance>("_ClearSimulation_Pre");
            "ClearSimulation".Post<GameInstance>("_ClearSimulation_Post");
            // fix 1.3.2: patch methods making an InterpolatedText object and doesn't store it anywhere
            // FIXME may also need to patch calls to LocalizableText.UpdateTMPText() [this uses LocalizableText objects as well] [untested]
            // TODO/FIXME also patch over the class' finalizers with a nop so we don't have doubled calls to RemoveSubscriber()
            "RunMadLibs".Transpile<LanceOverride>("_MadLib_TagSet_Transpiler");
            "RunMadLibsOnLanceDef".Transpile<LanceOverride>("_MadLib_TagSet_Transpiler");
            // this method uses both overloads of RunMadLib, so it needs two transpiler passes
            "RunMadLib".Transpile<UnitSpawnPointOverride>("_MadLib_TagSet_Transpiler");
            "RunMadLib".Transpile<UnitSpawnPointOverride>("_MadLib_string_Transpiler");
        }

        private static IEnumerable<CodeInstruction> _SessionTranspiler(IEnumerable<CodeInstruction> ins)
        {
            var meth = AccessTools.Method(self, "_UpdateMessageSubscriptions");
            return TranspileReplaceCall(ins, "UpdateMessageSubscriptions", meth);
        }

        private static void _UpdateMessageSubscriptions(BattleTechSimAnalytics __instance, bool subscribe)
        {
            LogSpam("_UpdateMessageSubscriptions called");
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

        private static void _OnSimGameInitializeComplete_Post(SimGameUXCreator __instance)
        {
            LogSpam("_OnSimGameInitializeComplete_Post called");
            __instance.sim.MessageCenter.RemoveSubscriber(
                    MessageCenterMessageType.OnSimGameInitialized,
                    new ReceiveMessageCenterMessage(__instance.OnSimGameInitializeComplete));
        }

        private static void _ClearSimulation_Pre(GameInstance __instance, ref SimGameState __state)
        {
            LogSpam("_ClearSimulation_Pre called");
            __state = __instance.Simulation;
        }

        private static void _ClearSimulation_Post(GameInstance __instance, ref SimGameState __state)
        {
            LogSpam("_ClearSimulation_Post called");
            if (__state == null) {
                LogSpam("SimGameState was null (ok if first load)");
                return;
            }

            var mc = __instance.MessageCenter;

            foreach (var contract in __state.GetAllCurrentlySelectableContracts()) {
                mc.RemoveSubscriber(MessageCenterMessageType.OnLanguageChanged,
                                    new ReceiveMessageCenterMessage(contract.OnLanguageChanged));

                if (contract.Override == null) {
                    LogDebug("contract.Override is null!");
                    continue;
                }

                foreach (var dialogue in contract.Override.dialogueList) {
                    foreach (var content in dialogue.dialogueContent) {
                        RemoveTextSubscriber(mc, (InterpolatedText) content.GetWords());
                        mc.RemoveSubscriber(MessageCenterMessageType.OnLanguageChanged,
                                            new ReceiveMessageCenterMessage(content.OnLanguageChanged));
                    }
                }

                foreach (var objective in contract.Override.contractObjectiveList) {
                    RemoveTextSubscriber(mc, (InterpolatedText) objective.GetTitle());
                    RemoveTextSubscriber(mc, (InterpolatedText) objective.GetDescription());
                    mc.RemoveSubscriber(MessageCenterMessageType.OnLanguageChanged,
                                        new ReceiveMessageCenterMessage(objective.OnLanguageChanged));
                }

                foreach (var objective in contract.Override.objectiveList) {
                    RemoveTextSubscriber(mc, (InterpolatedText) objective._Title);
                    RemoveTextSubscriber(mc, (InterpolatedText) objective._Description);
                    mc.RemoveSubscriber(MessageCenterMessageType.OnLanguageChanged,
                                        new ReceiveMessageCenterMessage(objective.OnLanguageChanged));
                }
            }
        }

        private static void RemoveTextSubscriber(MessageCenter mc, InterpolatedText text) {
            if (text != null) {
                mc.RemoveSubscriber(MessageCenterMessageType.OnLanguageChanged,
                                    new ReceiveMessageCenterMessage(text.OnLanguageChanged));
            }
        }

        private static IEnumerable<CodeInstruction> _MadLib_string_Transpiler(IEnumerable<CodeInstruction> ins)
        {
            var meth = AccessTools.Method(self, "_Contract_RunMadLib",
                                          new Type[]{typeof(Contract), typeof(string)});
            return TranspileReplaceOverloadedCall(ins, typeof(Contract), "RunMadLib",
                                                  new Type[]{typeof(string)}, meth);
        }

        private static IEnumerable<CodeInstruction> _MadLib_TagSet_Transpiler(IEnumerable<CodeInstruction> ins)
        {
            var meth = AccessTools.Method(self, "_Contract_RunMadLib",
                                          new Type[]{typeof(Contract), typeof(TagSet)});
            return TranspileReplaceOverloadedCall(ins, typeof(Contract), "RunMadLib",
                                                  new Type[]{typeof(TagSet)}, meth);
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
            LogSpam("_Contract_RunMadLib(string) called");
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
            LogSpam("_Contract_RunMadLib(tagSet) called");
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
    }
}
