using Harmony;
using BattleTech.Framework;
using BattleTech;
using HBS.Collections;
using static BattletechPerformanceFix.Extensions;

namespace BattletechPerformanceFix
{
    // Fix provided by Eck, until an official hotfix is released.
    public class DynamicTagsFix : Feature
    {
        public void Activate()
        {
            Trap(() =>
            {
                var harmony = Control.harmony;
                harmony.Patch(AccessTools.Method(typeof(LanceOverride), "RunMadLibs")
                                     , new HarmonyMethod(typeof(DynamicTagsFix), nameof(LanceOverride_RunMadLibs)));

                harmony.Patch(AccessTools.Method(typeof(LanceOverride), "RunMadLibsOnLanceDef")
                                     , new HarmonyMethod(typeof(DynamicTagsFix), nameof(LanceOverride_RunMadLibsOnLanceDef)));

                harmony.Patch(AccessTools.Method(typeof(UnitSpawnPointOverride), "RunMadLib")
                                     , new HarmonyMethod(typeof(DynamicTagsFix), nameof(UnitSpawnPointOverride_RunMadLib)));
            });
        }

        public static void Contract_RunMadLib(Contract __instance, TagSet tagSet)
        {
            Trap(() =>
            {
                if (tagSet == null)
                    return;

                string[] items = tagSet.ToArray();
                if (items == null)
                    return;

                for (int i = 0; i < items.Length; ++i)
                {
                    string tag = items[i];
                    // We should never localize internal dev names.
                    tag = __instance.Interpolate(tag).ToString(localize: false);
                    items[i] = tag.ToLower();
                }

                tagSet.Clear();
                tagSet.AddRange(items);
            });
            return;
        }

        public static bool LanceOverride_RunMadLibs(LanceOverride __instance, Contract contract)
        {
            Trap(() =>
            {
                Contract_RunMadLib(contract, __instance.lanceTagSet);
                Contract_RunMadLib(contract, __instance.lanceExcludedTagSet);
                //lanceTagSet.SetContractContext(contract);
                //lanceExcludedTagSet.SetContractContext(contract);

                for (int i = 0; i < __instance.unitSpawnPointOverrideList.Count; ++i)
                {
                    __instance.unitSpawnPointOverrideList[i].RunMadLib(contract);
                }
            });
            return false;
        }

        public static bool LanceOverride_RunMadLibsOnLanceDef(LanceOverride __instance, Contract contract, LanceDef lanceDef)
        {
            Trap(() =>
            {
                if (contract != null)
                {
                    Contract_RunMadLib(contract, lanceDef.LanceTags);
                    //lanceDef.LanceTags.SetContractContext(contract);

                    foreach (LanceDef.Unit unit in lanceDef.LanceUnits)
                    {
                        new Traverse(unit).Method("EnsureTagSets").GetValue();

                        Contract_RunMadLib(contract, unit.unitTagSet);
                        Contract_RunMadLib(contract, unit.excludedUnitTagSet);
                        Contract_RunMadLib(contract, unit.pilotTagSet);
                        Contract_RunMadLib(contract, unit.excludedPilotTagSet);
                        //unit.unitTagSet.SetContractContext(contract);
                        //unit.excludedUnitTagSet.SetContractContext(contract);
                        //unit.pilotTagSet.SetContractContext(contract);
                        //unit.excludedPilotTagSet.SetContractContext(contract);
                    }
                }
            });
            return false;
        }

        public static bool UnitSpawnPointOverride_RunMadLib(UnitSpawnPointOverride __instance, Contract contract)
        {
            Trap(() =>
            {
                Contract_RunMadLib(contract, __instance.unitTagSet);
                Contract_RunMadLib(contract, __instance.unitExcludedTagSet);
                Contract_RunMadLib(contract, __instance.pilotTagSet);
                Contract_RunMadLib(contract, __instance.pilotExcludedTagSet);
            });
            return false;
        }
    }
}
