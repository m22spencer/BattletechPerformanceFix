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

            void f<T>() {
                "RequestDependencies".Transpile<MechDef>();
                "RequestDependencies".Pre<MechDef>();
            }

            f<MechDef>();
        }

        // We fill this queue during the DM load process, and then do a CDAL at the very end.
        public static List<ILD> RequiresResolution = new List<ILD>();

        public static void RequestDependencies_Pre(MechDef __instance) {
            RequiresResolution.Add(__instance);
            Spam(() => $"RequestDepenencies of {__instance.ChassisID} in queue {RequiresResolution.Count}");
        }

        public static IEnumerable<CodeInstruction> RequestDependencies_Transpile(IEnumerable<CodeInstruction> ins) {
            var found = false;
            var nins = ins.SelectMany(i => {
                    if ((i.operand is MethodInfo) && (i.operand as MethodInfo).Name == "AddSubscriber") {
                        i.opcode = OpCodes.Pop;
                        i.operand = null;
                        // pop this, pop message center guid, pop CDAL call
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
