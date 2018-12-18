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
                "RequestDependencies".Transpile<T>();
            }

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
