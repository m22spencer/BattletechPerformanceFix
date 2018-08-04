using HBS.Logging;
using Harmony;
using BattleTech;
using BattleTech.UI;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Reflection;
using System.Diagnostics;

namespace BattletechPerformanceFix
{
    [HarmonyPatch(typeof(MechLabPanel), "PopulateInventory")]

    public static class Patch_MechLabPanel_PopulateInventory {
        public static MechLabPanel inst;
        public static int index = 0;
        static int bound = 0;

        public static void Prefix(MechLabPanel __instance, ref List<MechComponentRef> ___storageInventory, MechLabInventoryWidget ___inventoryWidget, ref List<MechComponentRef> __state) {
            try {
            inst = __instance;        
            ___inventoryWidget.ClearInventory();
            __state = ___storageInventory;

            var iw = new Traverse(___inventoryWidget);
            Func<string,bool> f = (n) => iw.Field(n).GetValue<bool>();

            // Try to re-use as much as possible
            //   Note that this is a different filter than MechLabInventoryWidget uses.
            var filter = new InventoryFilter( false //this.filteringAll
                                            , f("filteringWeapons")
                                            , f("filterEnabledWeaponBallistic")
                                            , f("filterEnabledWeaponEnergy")
                                            , f("filterEnabledWeaponMissile")
                                            , f("filterEnabledWeaponSmall")
                                            , f("filteringEquipment")
                                            , f("filterEnabledHeatsink")
                                            , f("filterEnabledJumpjet")
                                            , iw.Field("mechTonnage").GetValue<float>()
                                            , f("filterEnabledUpgrade")
                                            , false );

            ListElementController_BASE tmpctl = new ListElementController_InventoryGear();
            var current = __state.Where(d => { 
                var ty = d.ComponentDefType;
                tmpctl.weaponDef = null;
                tmpctl.ammoBoxDef = null;
                tmpctl.componentDef = null;
                switch (d.ComponentDefType) {
                case ComponentType.Weapon:
                    tmpctl.weaponDef = d.Def as WeaponDef;
                    break;
                case ComponentType.AmmunitionBox:
                    tmpctl.ammoBoxDef = d.Def as AmmunitionBoxDef;
                    break;
                case ComponentType.HeatSink:
                case ComponentType.MechPart:
                case ComponentType.JumpJet:
                case ComponentType.Upgrade:
                    tmpctl.componentDef = d.Def;
                    break;
                }
                return filter.Execute(Enumerable.Repeat(tmpctl, 1)).Any();
                }).ToList();

            bound = current.Count;
            index = index < 0 ? 0 : (index > (bound-7) ? (bound-7) : index);   

            // re-use HBS sorting implementation, awful but less issues with mods that touch sorting.
            var a = new ListElementController_InventoryGear_NotListView();
            var b = new ListElementController_InventoryGear_NotListView();
            var ac = new InventoryItemElement_NotListView();
            var bc = new InventoryItemElement_NotListView();
            ac.controller = a;
            bc.controller = b;
            var cs = iw.Field("currentSort").GetValue<Comparison<InventoryItemElement_NotListView>>();
            current.Sort(new Comparison<MechComponentRef>((l,r) => {
                a.componentRef = l;
                b.componentRef = r;
                return cs.Invoke(ac, bc);
            }));
        

            ___storageInventory = current.Skip(index).Take(7).ToList();
            } catch (Exception e) {
                Control.mod.Logger.Log(string.Format("Exn: {0}", e));
            }
        }

        public static void Postfix(MechLabPanel __instance, ref List<MechComponentRef> ___storageInventory, MechLabInventoryWidget ___inventoryWidget, ref List<MechComponentRef> __state) {
            ___storageInventory = __state;
            // inventory filter may be different from filter used above, so go ahead and show all items always.
            foreach(InventoryItemElement_NotListView inventoryItemElement_NotListView in ___inventoryWidget.localInventory) {
                inventoryItemElement_NotListView.gameObject.SetActive(true);
            }
        }

        public static T[] list<T>(params T[] items) {
            return items;
        }
    }

    // sub category filters
    [HarmonyPatch(typeof(MechLabInventoryWidget), "OnFilterButtonClicked")]
    public static class HookFilterButtonClicked {
        public static void Postfix() {
            try {
            new Traverse(Patch_MechLabPanel_PopulateInventory.inst).Method("PopulateInventory").GetValue();
            } catch(Exception e) {
                Control.mod.Logger.Log(string.Format("exn {0}", e));
            }
        }
    }

    [HarmonyPatch(typeof(MechLabInventoryWidget), "SetFiltersWeapons")]
    public static class HookSetFiltersWeapons {
        public static void Postfix() {
            try {
            new Traverse(Patch_MechLabPanel_PopulateInventory.inst).Method("PopulateInventory").GetValue();
            } catch(Exception e) {
                Control.mod.Logger.Log(string.Format("exn {0}", e));
            }
        }
    }

    [HarmonyPatch(typeof(MechLabInventoryWidget), "SetFiltersEquipment")]
    public static class HookSetFiltersEquipment {
        public static void Postfix() {
            try {
            new Traverse(Patch_MechLabPanel_PopulateInventory.inst).Method("PopulateInventory").GetValue();
            } catch(Exception e) {
                Control.mod.Logger.Log(string.Format("exn {0}", e));
            }
        }
    }

    [HarmonyPatch(typeof(MechLabInventoryWidget), "SetFiltersMechParts")]
    public static class HookSetFiltersMechParts {
        public static void Postfix() {
            try {
            new Traverse(Patch_MechLabPanel_PopulateInventory.inst).Method("PopulateInventory").GetValue();
            } catch(Exception e) {
                Control.mod.Logger.Log(string.Format("exn {0}", e));
            }
        }
    }

    [HarmonyPatch(typeof(UnityEngine.UI.ScrollRect), "OnScroll")]
    public static class OnScrollHook {
        public static void Prefix(UnityEngine.EventSystems.PointerEventData data) {
            try {
            Patch_MechLabPanel_PopulateInventory.index -= Convert.ToInt32(data.scrollDelta.y);
            data.scrollDelta = new UnityEngine.Vector2(0, 0);
            new Traverse(Patch_MechLabPanel_PopulateInventory.inst).Method("PopulateInventory").GetValue();
            } catch(Exception e) {
                Control.mod.Logger.Log(string.Format("exn {0}", e));
            }
        }
    }
}