using HBS.Logging;
using Harmony;
using BattleTech;
using BattleTech.UI;


namespace BattletechPerformanceFix
{

	// Block any sorts until the listview is completely built, and then sort once.
	[HarmonyPatch(typeof(SG_Shop_Screen), "AddShopInventory")]
    public static class Patch_SG_Shop_Screen_AddShopInventory_OnlySortAtEnd {
    	public static void Prefix() {
    		Patch_HBSLoopScrollRect_OptionalSort.WantSort = false;
    	}

    	public static void Postfix(SG_Shop_Screen __instance) {
    		Patch_HBSLoopScrollRect_OptionalSort.WantSort = true;
    		Traverse.Create(__instance).Field("ListView")
    		                           .Method("Sort")
    		                           .GetValue();
    	}
    }

    [HarmonyPatch(typeof(HBSLoopScrollRect<InventoryItemElement, ListElementController_BASE>), "Sort")]
    public static class Patch_HBSLoopScrollRect_OptionalSort {
    	public static bool WantSort = false;
    	public static bool Prefix() {
    		return WantSort;
    	}
    }
}