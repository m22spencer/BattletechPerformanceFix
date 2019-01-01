using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Reflection.Emit;

using Harmony;


using RT = BattleTech.BattleTechResourceType;
using ILD = BattleTech.Data.DataManager.ILoadDependencies;
using BattleTech;
using BattleTech.Data;
using HBS.Data;

using static BattletechPerformanceFix.Extensions;

namespace BattletechPerformanceFix
{
    // Whenever a RRI comes through that is ILoadDependencies
    // - Load the json -> Def
    // - RequestDependencies (!! guard MC.AddSubscriber)
    //
    // When DM tries to complete its work, Do the CDAL checks for our waiting deps
    //     announce them, and then allow DM to announce the work.
    class DMFix : Feature
    {
        public void Activate()
        {
            // Theory:
            // - Transpile RequestDependencies
            //   - Remove AddSubscriber
            //   - Call dependencies loaded immediately upon return

            // - CheckDependenciesAfter load
            //   - Should never be called by anyone but us

            // - Register a DataManagerRequestComplete listener
            //   - Right before data manager announces, check if we have all deps and report any issues.
            //     ^ This step is already done by DataManager within CheckRequestsComplete, is this good enough?

            void f<T>() where T : ILD {
                "RequestDependencies".Transpile<T>();            }

            f<MechDef>();
            f<ChassisDef>();
            f<WeaponDef>();
            f<PilotDef>();
            f<HeraldryDef>();
            f<AbilityDef>();
            f<BaseComponentRef>();
            f<MechComponentDef>();
            f<BackgroundDef>();
            f<FactionDef>();

            CollectSingletons.OnInit
                             .Done(() => { CollectSingletons.MC
                                                            .AddSubscriber( MessageCenterMessageType.DataManagerLoadCompleteMessage
                                                                          , _ => VerifyDependencies());
                                           CollectSingletons.MC
                                                            .AddSubscriber( MessageCenterMessageType.DataManagerRequestCompleteMessage
                                                                          , AddCheckDep); });
        }

        public static List<KeyValuePair<string,ILD>> ToVerify = new List<KeyValuePair<string,ILD>>();

        public static void AddCheckDep(MessageCenterMessage m) {
            if (m is DataManagerRequestCompleteMessage) {
                var res = Trap(() => new Traverse(m).Property("Resource").GetValue<object>());
                if (res != null && res is ILD) {
                    LogDebug($"AddCheckDep {(m as DataManagerRequestCompleteMessage).ResourceId}");
                    ToVerify.Add(new KeyValuePair<string,ILD>((m as DataManagerRequestCompleteMessage).ResourceId, res as ILD));
                }
            }
        }

        public static void VerifyDependencies() {
            LogDebug($"Checking {ToVerify.Count()} dependencies for validity");

            // Try and pull the weight from the ild, falling back to a default of 10000 (data + assets)
            bool Check(ILD ild) {
                var weight = new Traverse(ild).Field("loadRequest").GetValue<DataManager.DataManagerLoadRequest>()?.RequestWeight?.AllowedWeight ?? 10000;
                return ild.DependenciesLoaded(weight);
            }

            var dmrc = new DataManagerRequestCompleteMessage(default(RT), default(string));
            var failed = ToVerify.Where(dep => { dep.Value.CheckDependenciesAfterLoad(dmrc);
                                                 return !Check(dep.Value); })
                                 .ToList();
            if (failed.Any()) {
                LogError($"The following[{failed.Count} items did not resolve dependencies {failed.Select(x => x.Key).ToArray().Dump(false)}");

                AlertUser("DMFix: Failed Dependencies", $"{failed.Count} dependencies did not resolve\nCheck your Mods/BattletechPerformanceFix/BattletechPerformanceFix.log file");
            }
            ToVerify.Clear();
        }


        // It's preferable to patch out all the "DependenciesLoaded"/"RequestDependencies" methods called within
        //    but this adds complexity, which we want to avoid for now.
        public static IEnumerable<CodeInstruction> RequestDependencies_Transpile(IEnumerable<CodeInstruction> ins) {
            var found = false;
            var nins = ins.SelectMany(i => {
                    if ((i.operand is MethodInfo) && (i.operand as MethodInfo).Name == "AddSubscriber") {
                        i.opcode = OpCodes.Pop;
                        i.operand = null;
                        // pop instance, pop message center guid, pop CDAL call
                        found = true;
                        return Sequence(i, new CodeInstruction(OpCodes.Pop), new CodeInstruction(OpCodes.Pop));
                    } else {
                        return Sequence(i);
                    }
                }).ToList();  //force, so we can determine if the method was found or not.
            if (!found) LogError("Unable to patch out CDAL subscriber");
            return nins;
        }
    }
}
