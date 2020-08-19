using Harmony;
using BattleTech.UI;
using System.Collections.Generic;
using static BattletechPerformanceFix.Extensions;

namespace BattletechPerformanceFix
{
    public class ShopTabLagFix : Feature
    {
        public void Activate()
        {
            var asi = Main.CheckPatch(AccessTools.Method(typeof(SG_Shop_Screen), nameof(SG_Shop_Screen.AddShopInventory))
                                        , "f07ff50a8bd1b049d0ad576720c91c2b473d240e203f046ba4bd6ed4ca03f653");
            var aiti = Main.CheckPatch(AccessTools.Method(typeof(MechLabInventoryWidget_ListView), nameof(MechLabInventoryWidget_ListView.AddItemToInventory))
                                         , "204c93ea7a8f7474dd1e185f3b99a3da63c0d0bbd44eeb94a1378a9f1ae9938e");

            Main.harmony.Patch(asi, null, new HarmonyMethod(AccessTools.Method(typeof(ShopTabLagFix), nameof(OnlySortAtEnd))));
            Main.harmony.Patch(aiti, new HarmonyMethod(AccessTools.Method(typeof(ShopTabLagFix), nameof(AddItemToInventory))));

        }

        public static void OnlySortAtEnd(SG_Shop_Screen __instance)
        {
            LogDebug("ShopTabLagFix: OnlySortAtEnd");
            var lv = __instance.inventoryWidget.ListView;

            //These don't actually seem to be needed, but keeping them just in case.
            lv.Sort();
            lv.Refresh();
        }
        public static bool AddItemToInventory(MechLabInventoryWidget_ListView __instance, InventoryDataObject_BASE ItemData)
        {
            LogDebug("ShopTabLagFix: AddItemToInventory");
            var _this = __instance;
            var _items = _this.ListView.Items;
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
