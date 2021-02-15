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
            // fix 1.3: remove OnLanguageChanged subscriptions for these objects, which never unsub and therefore leak.
            //          b/c the user must drop back to main menu to change the language, there's no reason
            //          to use these in the first place (objects are created in-game and never on the main menu)
            // Contract
            var contractCtorTypes = new Type[]{typeof(string), typeof(string), typeof(string), typeof(ContractTypeValue),
                                               typeof(GameInstance), typeof(ContractOverride), typeof(GameContext),
                                               typeof(bool), typeof(int), typeof(int), typeof(int)};
            Main.harmony.Patch(AccessTools.Constructor(typeof(Contract), contractCtorTypes),
                               null, null, new HarmonyMethod(self, "Contract_ctor_Transpile"));
            "PostDeserialize".Transpile<Contract>();
            // ContractObjectiveOverride
            Main.harmony.Patch(AccessTools.Constructor(typeof(ContractObjectiveOverride), new Type[]{}),
                               null, null, new HarmonyMethod(self, "ContractObjectiveOverride_ctor_Transpile"));
            var cooCtorTypes = new Type[]{typeof(ContractObjectiveGameLogic)};
            Main.harmony.Patch(AccessTools.Constructor(typeof(ContractObjectiveOverride), cooCtorTypes),
                               null, null, new HarmonyMethod(self, "ContractObjectiveOverride_ctor_cogl_Transpile"));
            // ObjectiveOverride
            Main.harmony.Patch(AccessTools.Constructor(typeof(ObjectiveOverride), new Type[]{}),
                               null, null, new HarmonyMethod(self, "ObjectiveOverride_ctor_Transpile"));
            var ooCtorTypes = new Type[]{typeof(ObjectiveGameLogic)};
            Main.harmony.Patch(AccessTools.Constructor(typeof(ObjectiveOverride), ooCtorTypes),
                               null, null, new HarmonyMethod(self, "ObjectiveOverride_ctor_ogl_Transpile"));
            // DialogueContentOverride
            Main.harmony.Patch(AccessTools.Constructor(typeof(DialogueContentOverride), new Type[]{}),
                               null, null, new HarmonyMethod(self, "DialogueContentOverride_ctor_Transpile"));
            var dcoCtorTypes = new Type[]{typeof(DialogueContent)};
            Main.harmony.Patch(AccessTools.Constructor(typeof(DialogueContentOverride), dcoCtorTypes),
                               null, null, new HarmonyMethod(self, "DialogueContentOverride_ctor_dc_Transpile"));
            // InterpolatedText
            "Init".Transpile<InterpolatedText>();
            // these finalizers could never run to begin with, and they only did RemoveSubscriber; nop them
            // FIXME? may need to nop out specifically the call to RemoveSubscriber (test this)
            "Finalize".Transpile<Contract>("TranspileNopAll");
            "Finalize".Transpile<ContractObjectiveOverride>("TranspileNopAll");
            "Finalize".Transpile<ObjectiveOverride>("TranspileNopAll");
            "Finalize".Transpile<DialogueContentOverride>("TranspileNopAll");
            "Finalize".Transpile<InterpolatedText>("TranspileNopAll");

            // fixes group 2: occurs on entering/exiting a contract
            // fix 2.1: none of these classes need to store a CombatGameState
            "ContractInitialize".Post<DialogueContent>("DialogueContent_ContractInitialize_Post");
            "ContractInitialize".Post<ConversationContent>("ConversationContent_ContractInitialize_Post");
            "ContractInitialize".Post<DialogBucketDef>("DialogBucketDef_ContractInitialize_Post");

            // fixes group 3: occurs on creating a new savefile
            // fix 3.1: clean up the GameInstanceSave.references after serialization is complete
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

        private static IEnumerable<CodeInstruction> Contract_ctor_Transpile(IEnumerable<CodeInstruction> ins)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            return TranspileNopIndicesRange(ins, 125, 134);
        }

        private static IEnumerable<CodeInstruction> PostDeserialize_Transpile(IEnumerable<CodeInstruction> ins)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            return TranspileNopIndicesRange(ins, 21, 27);
        }

        private static IEnumerable<CodeInstruction>
        ContractObjectiveOverride_ctor_Transpile(IEnumerable<CodeInstruction> ins)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            return TranspileNopIndicesRange(ins, 5, 14);
        }

        private static IEnumerable<CodeInstruction>
        ContractObjectiveOverride_ctor_cogl_Transpile(IEnumerable<CodeInstruction> ins)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            return TranspileNopIndicesRange(ins, 9, 18);
        }

        private static IEnumerable<CodeInstruction>
        ObjectiveOverride_ctor_Transpile(IEnumerable<CodeInstruction> ins)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            return TranspileNopIndicesRange(ins, 8, 17);
        }

        private static IEnumerable<CodeInstruction>
        ObjectiveOverride_ctor_ogl_Transpile(IEnumerable<CodeInstruction> ins)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            return TranspileNopIndicesRange(ins, 12, 21);
        }

        private static IEnumerable<CodeInstruction>
        DialogueContentOverride_ctor_Transpile(IEnumerable<CodeInstruction> ins)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            return TranspileNopIndicesRange(ins, 23, 32);
        }

        private static IEnumerable<CodeInstruction>
        DialogueContentOverride_ctor_dc_Transpile(IEnumerable<CodeInstruction> ins)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            return TranspileNopIndicesRange(ins, 60, 69);
        }

        private static IEnumerable<CodeInstruction> Init_Transpile(IEnumerable<CodeInstruction> ins)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            return TranspileNopIndicesRange(ins, 3, 10);
        }

        private static IEnumerable<CodeInstruction>
        TranspileNopIndicesRange(IEnumerable<CodeInstruction> ins, int startIndex, int endIndex)
        {
            LogDebug($"TranspileNopIndicesRange: nopping indices {startIndex}-{endIndex}");
            if (endIndex < startIndex || startIndex < 0) {
                LogError($"TranspileNopIndicesRange: invalid use with startIndex = {startIndex}," +
                         $" endIndex = {endIndex} (transpiled method remains unmodified)");
                return ins;
            }

            var code = ins.ToList();
            try {
                for (int i = startIndex; i <= endIndex; i++) {
                    code[i].opcode = OpCodes.Nop;
                    code[i].operand = null;
                }
                return code.AsEnumerable();
            } catch (ArgumentOutOfRangeException ex) {
                LogError($"TranspileNopIndicesRange: {ex.Message} (transpiled method remains unmodified)");
                return ins;
            }
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

        private static IEnumerable<CodeInstruction> TranspileNopAll(IEnumerable<CodeInstruction> ins)
        {
            return ins.SelectMany(i => {
                i.opcode = OpCodes.Nop;
                i.operand = null;
                return Sequence(i);
            });
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

        private static void PostSerialization_Post(GameInstanceSave __instance)
        {
            LogSpam($"{new StackTrace().GetFrame(0).GetMethod()} called");
            __instance.references = new SerializableReferenceContainer("the one and only");
        }
    }
}
// vim: ts=4:sw=4
