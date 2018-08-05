using HBS.Logging;
using Harmony;
using BattleTech;
using BattleTech.UI;
using System.Collections.Generic;
using System.Collections;
using System;
using System.Linq;
using System.Reflection;
using System.Diagnostics;

namespace BattletechPerformanceFix
{
      public class DefAndCount {
        public MechComponentRef ComponentRef;
        public int Count;
        public DefAndCount(MechComponentRef componentRef, int count) {
            this.ComponentRef = componentRef;
            this.Count = count;
        }

        public void Decr() {
            if (Count != int.MinValue) Count--;
        }
        public void Incr() {
            if (Count != int.MinValue) Count++;
        }
    }

    public class PatchMechlabLimitItems {
        MechLabPanel instance;
        MechLabInventoryWidget inventoryWidget;

        List<DefAndCount> inventory;

        List<InventoryItemElement_NotListView> ielCache;

        List<ListElementController_BASE_NotListView> rawInventory;
        List<ListElementController_BASE_NotListView> filteredInventory;

        int index = 0;
        int endIndex = 0;

        PatchMechlabLimitItems(MechLabPanel instance) {
            try {
            this.instance = instance;
            this.inventoryWidget = new Traverse(instance).Field("inventoryWidget").GetValue<MechLabInventoryWidget>();

            if (instance.IsSimGame) {
                new Traverse(instance).Field("originalStorageInventory").SetValue(instance.storageInventory);
            }


            inventory = instance.storageInventory.Select(mcr => {
                mcr.DataManager = instance.dataManager;
                mcr.RefreshComponentDef();
                var num = !instance.IsSimGame ? int.MinValue : instance.sim.GetItemCount(mcr.Def.Description, mcr.Def.GetType(), SimGameState.ItemCountType.UNDAMAGED_ONLY); // Undamaged only is wrong, just for testing.
                return new DefAndCount(mcr, num);
            }).ToList();

            rawInventory = inventory.Select<DefAndCount, ListElementController_BASE_NotListView>(dac => {
                if (dac.ComponentRef.ComponentDefType == ComponentType.Weapon) {
                    ListElementController_InventoryWeapon_NotListView controller = new ListElementController_InventoryWeapon_NotListView();
                    controller.InitAndFillInSpecificWidget(dac.ComponentRef, null, instance.dataManager, null, dac.Count, false);
                    return controller;
                } else {
                    ListElementController_InventoryGear_NotListView controller = new ListElementController_InventoryGear_NotListView();
                    controller.InitAndFillInSpecificWidget(dac.ComponentRef, null, instance.dataManager, null, dac.Count, false);
                    return controller;
                }
            }).ToList();
            rawInventory = Sort(rawInventory);

            ielCache = Enumerable.Repeat<Func<InventoryItemElement_NotListView>>( () => {
                var nlv = instance.dataManager.PooledInstantiate( ListElementController_BASE_NotListView.INVENTORY_ELEMENT_PREFAB_NotListView
                                                                                                                              , BattleTechResourceType.UIModulePrefabs, null, null, null)
                                                                                                            .GetComponent<InventoryItemElement_NotListView>();
				nlv.SetRadioParent(new Traverse(inventoryWidget).Field("inventoryRadioSet").GetValue<HBSRadioSet>());
				nlv.gameObject.transform.SetParent(new Traverse(inventoryWidget).Field("listParent").GetValue<UnityEngine.Transform>(), false);
				nlv.gameObject.transform.localScale = UnityEngine.Vector3.one;
                return nlv; }
                                                                                , itemLimit)
                                 .Select(thunk => thunk())
                                 .ToList();
            var li = new Traverse(inventoryWidget).Field("localInventory").GetValue<List<InventoryItemElement_NotListView>>();
            ielCache.ForEach(iw => li.Add(iw));
            
            var lp = new Traverse(inventoryWidget).Field("listParent").GetValue<UnityEngine.Transform>();

            if (DummyStart == null) DummyStart = new UnityEngine.GameObject().AddComponent<UnityEngine.RectTransform>();
            if (DummyEnd   == null) DummyEnd   = new UnityEngine.GameObject().AddComponent<UnityEngine.RectTransform>();

            DummyStart.SetParent(lp, false);
            DummyEnd.SetParent(lp, false);

            FilterChanged();
            } catch(Exception e) {
                Control.mod.Logger.Log(string.Format("[LimitItems] exn: {0}", e));
            }
        }

        List<ListElementController_BASE_NotListView> Sort(List<ListElementController_BASE_NotListView> items) {
            var _a = new ListElementController_InventoryGear_NotListView();
            var _b = new ListElementController_InventoryGear_NotListView();
            var _ac = new InventoryItemElement_NotListView();
            var _bc = new InventoryItemElement_NotListView();
            _ac.controller = _a;
            _bc.controller = _b;
            var _cs = new Traverse(inventoryWidget).Field("currentSort").GetValue<Comparison<InventoryItemElement_NotListView>>();
            var tmp = items.ToList();
            tmp.Sort(new Comparison<ListElementController_BASE_NotListView>((l,r) => {
                _a.componentRef = GetRef(l);
                _b.componentRef = GetRef(r);
                return _cs.Invoke(_ac, _bc);
            }));
            return tmp;
        }

        List<ListElementController_BASE_NotListView> Filter(List<ListElementController_BASE_NotListView> items) {
            var iw = new Traverse(inventoryWidget);
            Func<string,bool> f = (n) => iw.Field(n).GetValue<bool>();
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

            var current = items.Where(item => { 
                tmpctl.weaponDef = null;
                tmpctl.ammoBoxDef = null;
                tmpctl.componentDef = null;
                var def = item.componentDef;
                switch (def.ComponentType) {
                case ComponentType.Weapon:
                    tmpctl.weaponDef = def as WeaponDef;
                    break;
                case ComponentType.AmmunitionBox:
                    tmpctl.ammoBoxDef = def as AmmunitionBoxDef;
                    break;
                case ComponentType.HeatSink:
                case ComponentType.MechPart:
                case ComponentType.JumpJet:
                case ComponentType.Upgrade:
                    tmpctl.componentDef = def;
                    break;
                }
                return filter.Execute(Enumerable.Repeat(tmpctl, 1)).Any();
                }).ToList();
            return current;
        }

        MechComponentRef GetRef(ListElementController_BASE_NotListView lec) {
            if (lec is ListElementController_InventoryWeapon_NotListView) return (lec as ListElementController_InventoryWeapon_NotListView).componentRef;
            if (lec is ListElementController_InventoryGear_NotListView) return (lec as ListElementController_InventoryGear_NotListView).componentRef;
            Control.mod.Logger.LogError("[LimitItems] lec is not gear or weapon: " + lec.GetId());
            return null;
        }

        public void FilterChanged() {
            Control.mod.Logger.Log("[LimitItems] Filter changed");
            index = 0;
            filteredInventory = Filter(rawInventory);
            endIndex = filteredInventory.Count - itemLimit;
            Refresh();
        }

        void Refresh(bool wantClobber = true) {
            Control.mod.Logger.Log(string.Format("[LimitItems] Refresh: {0} {1} {2} {3}", index, filteredInventory.Count, itemLimit, new Traverse(inventoryWidget).Field("scrollbarArea").GetValue<UnityEngine.UI.ScrollRect>().verticalNormalizedPosition));
            if (index > filteredInventory.Count - itemsOnScreen)
                index = filteredInventory.Count - itemsOnScreen;
            if (filteredInventory.Count < itemsOnScreen)
                index = 0;
            if (index < 0)
                index = 0;


            var toShow = filteredInventory.Skip(index).Take(itemLimit).ToList();

            var icc = ielCache.ToList();

            Control.mod.Logger.Log("[LimitItems] Showing: " + string.Join(",", toShow.Select(lec => lec.componentDef.Description.Name).ToArray()));

            toShow.ForEach(lec => {
                var iw = icc[0]; icc.RemoveAt(0);
                var cref = GetRef(lec);
                iw.ClearEverything();
                iw.ComponentRef = cref;
                lec.ItemWidget = iw;
                iw.SetData(lec, inventoryWidget, lec.quantity, false, null);
                lec.SetupLook(iw);
                iw.gameObject.SetActive(true);
            });
            icc.ForEach(unused => unused.gameObject.SetActive(false));


            var tsize = 60.0f;
            
            Control.mod.Logger.Log("[LimitItems] Items prefixing: " + index);
            DummyStart.sizeDelta = new UnityEngine.Vector2(100, tsize * index);
            DummyStart.SetAsFirstSibling();

            var itemsHanging = filteredInventory.Count - (index + itemsOnScreen);
            Control.mod.Logger.Log("[LimitItems] Items hanging: " + itemsHanging);



            DummyEnd.sizeDelta = new UnityEngine.Vector2(100, tsize * itemsHanging);
            DummyEnd.SetAsLastSibling();
            
            
			new Traverse(instance).Method("RefreshInventorySelectability").GetValue();
            Control.mod.Logger.Log(string.Format("[LimitItems] RefreshDone {0} {1}", DummyStart.anchoredPosition.y, new Traverse(inventoryWidget).Field("scrollbarArea").GetValue<UnityEngine.UI.ScrollRect>().verticalNormalizedPosition));
        }

        static int itemsOnScreen = 7;
        static int itemLimit = 7;
        public static UnityEngine.RectTransform DummyStart; 
        public static UnityEngine.RectTransform DummyEnd;
        public static PatchMechlabLimitItems limitItems = null;
        static MethodInfo PopulateInventory = AccessTools.Method(typeof(MechLabPanel), "PopulateInventory");
        static MethodInfo ConfirmRevertMech = AccessTools.Method(typeof(MechLabPanel), "ConfirmRevertMech");
        static MethodInfo ExitMechLab       = AccessTools.Method(typeof(MechLabPanel), "ExitMechLab");
        public static void Initialize() {
            var onSalvageScreen = AccessTools.Method(typeof(AAR_SalvageScreen), "BeginSalvageScreen");
            Hook.Prefix(onSalvageScreen, Fun.fun(() => {
                // Only for logging purposes.
                Control.mod.Logger.Log("[LimitItems] Open Salvage screen");
            }).Method);
            Hook.Prefix(PopulateInventory, Fun.fun((MechLabPanel __instance) => { 
                if (limitItems != null) Control.mod.Logger.LogError("[LimitItems] PopulateInventory was not properly cleaned");
                Control.mod.Logger.Log("[LimitItems] PopulateInventory patching (Mechlab fix)");
                limitItems = new PatchMechlabLimitItems(__instance);
                return false;
            }).Method);

            Hook.Prefix(ConfirmRevertMech, Fun.fun((MechLabPanel __instance) => { 
                if (limitItems == null) Control.mod.Logger.LogError("[LimitItems] Unhandled ConfirmRevertMech");
                Control.mod.Logger.Log("[LimitItems] Reverting mech");
                limitItems = null;
            }).Method);

            Hook.Prefix(ExitMechLab, Fun.fun((MechLabPanel __instance) => { 
                if (limitItems == null) Control.mod.Logger.LogError("[LimitItems] Unhandled ExitMechLab");
                Control.mod.Logger.Log("[LimitItems] Exiting mechlab");
                limitItems = null;
            }).Method);

            var onLateUpdate = AccessTools.Method(typeof(UnityEngine.UI.ScrollRect), "LateUpdate");
            Hook.Prefix(onLateUpdate, Fun.fun((UnityEngine.UI.ScrollRect __instance) => {
                if (limitItems != null && new Traverse(limitItems.inventoryWidget).Field("scrollbarArea").GetValue<UnityEngine.UI.ScrollRect>() == __instance) {
                    var newIndex = (int)((limitItems.endIndex) * (1.0f - __instance.verticalNormalizedPosition));
                    if (limitItems.filteredInventory.Count < itemsOnScreen) {
                        newIndex = 0;
                    }
                    if (limitItems.index != newIndex) {
                        limitItems.index = newIndex;
                        Control.mod.Logger.Log(string.Format("[LimitItems] Refresh with: {0} {1}", newIndex, __instance.verticalNormalizedPosition));
                        limitItems.Refresh(false);
                    }
                }        
            }).Method); 

            var onApplyFiltering = AccessTools.Method(typeof(MechLabInventoryWidget), "ApplyFiltering");
            Hook.Prefix(onApplyFiltering, Fun.fun((MechLabInventoryWidget __instance) => {
                if (limitItems != null && limitItems.inventoryWidget == __instance) {
                    limitItems.FilterChanged();
                    return false;
                } else {
                    return true;
                }
            }).Method);

            var onApplySorting = AccessTools.Method(typeof(MechLabInventoryWidget), "ApplySorting");
            Hook.Prefix(onApplyFiltering, Fun.fun((MechLabInventoryWidget __instance) => {
                if (limitItems != null && limitItems.inventoryWidget == __instance) {
                    // it's a mechlab screen, we do our own sort.
                     return false;
                } else {
                    return true;
                }
            }).Method);            
        }
    }
}