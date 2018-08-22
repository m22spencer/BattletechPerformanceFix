using HBS.Logging;
using Harmony;
using BattleTech;
using BattleTech.UI;
using System;
using System.Diagnostics;
using System.Collections.Generic;

namespace BattletechPerformanceFix
{
	[HarmonyPatch(typeof(SG_Shop_Screen), "AddShopInventory")]
    public static class Patch_SG_Shop_Screen_AddShopInventory_OnlySortAtEnd {
    	public static void Postfix(SG_Shop_Screen __instance) {
            var _this = Traverse.Create(__instance);
            var lv = _this.Field("inventoryWidget").Field("ListView");

            //These don't actually seem to be needed, but keeping them just in case.
            lv.Method("Sort").GetValue();
            lv.Method("Refresh").GetValue();
        }
    }

    // Perform only the necessary data changes, ignore all UI fixup.
    [HarmonyPatch(typeof(MechLabInventoryWidget_ListView), "AddItemToInventory")]
    public static class Patch_AddItemToInventory {
        public static bool Prefix(MechLabInventoryWidget_ListView __instance, InventoryDataObject_BASE ItemData) {
            var _this = __instance;
            var _items = (List<InventoryDataObject_BASE>)Traverse.Create(__instance).Field("ListView").Property("Items").GetValue();
            InventoryDataObject_BASE listElementController_BASE = null;
            foreach (InventoryDataObject_BASE listElementController_BASE2 in _this.inventoryData)
            {
                if (listElementController_BASE2.GetItemType() == ItemData.GetItemType() && listElementController_BASE2.IsDuplicateContent(ItemData) && _this.ParentDropTarget != null && _this.StackQuantities)
                {
                    listElementController_BASE = listElementController_BASE2;
                    break;
                }
            }

            if (listElementController_BASE != null)
            {
                listElementController_BASE.ModifyQuantity(1);
            }
            else
            {
                // OnItemAdded logic does not seem to be needed?
                // HBSLoopScroll 219-228
                _items.Add(ItemData);
            }
            return false;
        }
    }
}