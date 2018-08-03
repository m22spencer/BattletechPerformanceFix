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
            index = index < 0 ? 0 : (index > (bound-10) ? (bound-10) : index);           
            ___inventoryWidget.ClearInventory();
            bound = ___storageInventory.Count;
            __state = ___storageInventory;

            // re-use HBS sorting implementation, awful but less issues with mods that touch sorting.
            var a = new ListElementController_InventoryGear_NotListView();
            var b = new ListElementController_InventoryGear_NotListView();
            var ac = new InventoryItemElement_NotListView();
            var bc = new InventoryItemElement_NotListView();
            ac.controller = a;
            bc.controller = b;
            var cs = iw.Field("currentSort").GetValue<Comparison<InventoryItemElement_NotListView>>();
            __state.Sort(new Comparison<MechComponentRef>((l,r) => {
                a.componentRef = l;
                b.componentRef = r;
                return cs.Invoke(ac, bc);
            }));
        

            ___storageInventory = x.Skip(index).Take(10).ToList();
            } catch (Exception e) {
                Control.mod.Logger.Log(string.Format("Exn: {0}", e));
            }
        }

        public static void Postfix(ref List<MechComponentRef> ___storageInventory, ref List<MechComponentRef> __state) {
            ___storageInventory = __state;
        }
    }

    [HarmonyPatch(typeof(UnityEngine.UI.ScrollRect), "OnScroll")]
    public static class OnScrollHook {
        public static void Prefix(UnityEngine.EventSystems.PointerEventData data) {
            Patch_MechLabPanel_PopulateInventory.index -= Convert.ToInt32(data.scrollDelta.y);
            data.scrollDelta = new UnityEngine.Vector2(0, 0);
            new Traverse(Patch_MechLabPanel_PopulateInventory.inst).Method("PopulateInventory").GetValue();
        }
    }
}