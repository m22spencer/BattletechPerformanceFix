using Harmony;
using BattleTech;
using BattleTech.UI;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Diagnostics;
using static BattletechPerformanceFix.Extensions;

namespace BattletechPerformanceFix
{
    public class MechlabFix : Feature {
        public void Activate() {
            "BeginSalvageScreen".Pre<AAR_SalvageScreen>();
            "PopulateInventory".Pre<MechLabPanel>();
            "ConfirmRevertMech".Pre<MechLabPanel>();
            "ExitMechLab".Pre<MechLabPanel>();
            "LateUpdate".Pre<UnityEngine.UI.ScrollRect>();
            "OnAddItem".Pre<MechLabInventoryWidget>();
            "OnRemoveItem".Pre<MechLabInventoryWidget>();
            "OnItemGrab".Pre<MechLabInventoryWidget>();
            "ApplyFiltering".Pre<MechLabInventoryWidget>();
            "MechCanEquipItem".Pre<MechLabPanel>();
            "ApplySorting".Pre<MechLabInventoryWidget>();

            // Fix some annoying seemingly vanilla log spam
            "OnDestroy".Pre<InventoryItemElement_NotListView>(iel => { if(iel.iconMech != null) iel.iconMech.sprite = null;
                                                                       return false; });
        }
        public static PatchMechlabLimitItems state;

        public static bool PopulateInventory_Pre(MechLabPanel __instance)
        {
            if (state != null) LogError("[LimitItems] PopulateInventory was not properly cleaned");
            LogDebug("[LimitItems] PopulateInventory patching (Mechlab fix)");
            state = new PatchMechlabLimitItems(__instance);
            return false;
        }

        public static void BeginSalvageScreen_Pre()
        {
            // Only for logging purposes.
            LogDebug("[LimitItems] Open Salvage screen");
        }

        public static void ConfirmRevertMech_Pre()
        {
            LogDebug("[LimitItems] RevertMech");
        }

        public static void ExitMechLab_Pre(MechLabPanel __instance)
        {
            if (state == null) { LogError("[LimitItems] Unhandled ExitMechLab"); return; }
            LogDebug("[LimitItems] Exiting mechlab");
            state.Dispose();
            state = null;
        }

        public static void LateUpdate_Pre(UnityEngine.UI.ScrollRect __instance)
        {
            if (state != null && new Traverse(state.inventoryWidget).Field("scrollbarArea").GetValue<UnityEngine.UI.ScrollRect>() == __instance) {
                var newIndex = (int)((state.endIndex) * (1.0f - __instance.verticalNormalizedPosition));
                if (state.filteredInventory.Count < PatchMechlabLimitItems.itemsOnScreen) {
                    newIndex = 0;
                }
                if (state.index != newIndex) {
                    state.index = newIndex;
                    LogDebug(string.Format("[LimitItems] Refresh with: {0} {1}", newIndex, __instance.verticalNormalizedPosition));
                    state.Refresh(false);
                }
            }        
        }

        public static bool OnAddItem_Pre(MechLabInventoryWidget __instance, IMechLabDraggableItem item)
        {
            if (state != null && state.inventoryWidget == __instance) {
                try {
                    var nlv = item as InventoryItemElement_NotListView;
                    var quantity = nlv == null ? 1 : nlv.controller.quantity;
                    var existing = state.FetchItem(item.ComponentRef);
                    if (existing == null) {
                        LogDebug(string.Format("OnAddItem new {0}", quantity));
                        var controller = nlv == null ? null : nlv.controller;
                        if (controller == null) {
                            if (item.ComponentRef.ComponentDefType == ComponentType.Weapon) {
                                var ncontroller = new ListElementController_InventoryWeapon_NotListView();
                                ncontroller.InitAndCreate(item.ComponentRef, state.instance.dataManager, state.inventoryWidget, quantity, false);
                                controller = ncontroller;
                            } else {
                                var ncontroller = new ListElementController_InventoryGear_NotListView();
                                ncontroller.InitAndCreate(item.ComponentRef, state.instance.dataManager, state.inventoryWidget, quantity, false);
                                controller = ncontroller;
                            }
                        }
                        state.rawInventory.Add(controller);
                        state.rawInventory = state.Sort(state.rawInventory);
                        state.FilterChanged(false);
                    } else {
                        LogDebug(string.Format("OnAddItem existing {0}", quantity));
                        if (existing.quantity != Int32.MinValue) {
                            existing.ModifyQuantity(quantity);
                        }
                        state.Refresh(false);
                    }            
                } catch(Exception e) {
                    LogException(e);
                }
                return false;
            } else {
                return true;
            }
        }

        public static bool OnRemoveItem_Pre(MechLabInventoryWidget __instance, IMechLabDraggableItem item)
        {
            if (state != null && state.inventoryWidget == __instance) {
                try {
                    var nlv = item as InventoryItemElement_NotListView;

                    var existing = state.FetchItem(item.ComponentRef);
                    if (existing == null) {
                        LogError(string.Format("OnRemoveItem new (should be impossible?) {0}", nlv.controller.quantity));
                    } else {
                        LogDebug(string.Format("OnRemoveItem existing {0}", nlv.controller.quantity));
                        if (existing.quantity != Int32.MinValue) {
                            existing.ModifyQuantity(-1);
                            if (existing.quantity < 1)
                                state.rawInventory.Remove(existing);
                        }
                        state.FilterChanged(false);
                        state.Refresh(false);
                    }            
                } catch(Exception e) {
                    LogException(e);
                }
                return false;
            } else {
                return true;
            }
        }

        public static bool ApplyFiltering_Pre(MechLabInventoryWidget __instance, bool refreshPositioning)
        {
            if (state != null && state.inventoryWidget == __instance && !PatchMechlabLimitItems.filterGuard) {
                LogDebug(string.Format("OnApplyFiltering (refresh-pos? {0})", refreshPositioning));
                state.FilterChanged(refreshPositioning);
                return false;
            } else {
                return true;
            }
        }

        public static bool ApplySorting_Pre(MechLabInventoryWidget __instance)
        {
            if (state != null && state.inventoryWidget == __instance) {
                // it's a mechlab screen, we do our own sort.
                var _cs = new Traverse(__instance).Field("currentSort").GetValue<Comparison<InventoryItemElement_NotListView>>();
                var cst = _cs.Method;
                LogDebug(string.Format("OnApplySorting using {0}::{1}", cst.DeclaringType.FullName, cst.ToString()));
                state.FilterChanged(false);
                return false;
            } else {
                return true;
            }
        }

        public static bool MechCanEquipItem_Pre(InventoryItemElement_NotListView item)
        {
            return item.ComponentRef == null ? false : true;
        }
        
        public static void OnItemGrab_Pre(MechLabInventoryWidget __instance, ref IMechLabDraggableItem item) {
            if (state != null && state.inventoryWidget == __instance) {
                try {
                    LogDebug(string.Format("OnItemGrab"));
                    var nlv = item as InventoryItemElement_NotListView;
                    var nlvtmp = state.instance.dataManager.PooledInstantiate( ListElementController_BASE_NotListView.INVENTORY_ELEMENT_PREFAB_NotListView
                                                                             , BattleTechResourceType.UIModulePrefabs, null, null, null)
                                      .GetComponent<InventoryItemElement_NotListView>();
                    var lec = nlv.controller;
                    var iw = nlvtmp;
                    var cref = state.GetRef(lec);
                    iw.ClearEverything();
                    iw.ComponentRef = cref;
                    lec.ItemWidget = iw;
                    iw.SetData(lec, state.inventoryWidget, lec.quantity, false, null);
                    lec.SetupLook(iw);
                    iw.gameObject.SetActive(true);
                    item = iw;
                } catch(Exception e) {
                    LogException(e);
                }
            }
        }

    }

    /* This patch fixes the slow inventory list creation within the mechlab. Without the fix, it manifests as a very long loadscreen where the indicator is frozen.
       
       The core of the problem is a lack of separation between Data & Visuals.
       Most of the logic requires operating on visual elements, which come from the asset pool (or a prefab if not in pool)
       additionally, the creation or modification of data causes preperation for re-render of the assets. (UpdateTooltips, UpdateDescription, Update....)
    
       Solution:
       Separate the data & visual elements entirely.
       Always process the data first, and then only create or re-use a couple of visual elements to display it.
       The user only sees 8 items at once, and they're expensive to create, so only make 8 of them.
    */
    public class PatchMechlabLimitItems {
        public MechLabPanel instance;
        public MechLabInventoryWidget inventoryWidget;

        public List<InventoryItemElement_NotListView> ielCache;

        public List<ListElementController_BASE_NotListView> rawInventory;
        public List<ListElementController_BASE_NotListView> filteredInventory;

        // Index of current item element at the top of scrollrect
        public int index = 0;

        public int endIndex = 0;

        // Temporary visual element used in the filter process.
        public InventoryItemElement_NotListView iieTmp;

        public PatchMechlabLimitItems(MechLabPanel instance) {
            try {
                var sw = new Stopwatch();
                sw.Start();
                this.instance = instance;
                this.inventoryWidget = new Traverse(instance).Field("inventoryWidget")
                                                             .GetValue<MechLabInventoryWidget>()
                                                             .LogIfNull("inventoryWidget is null");

                if (instance.IsSimGame) {
                    new Traverse(instance).Field("originalStorageInventory").SetValue(instance.storageInventory.LogIfNull("storageInventory is null"));
                }

                LogDebug($"Mechbay Patch initialized :simGame? {instance.IsSimGame}");

                List<ListElementController_BASE_NotListView> BuildRawInventory()
                    => instance.storageInventory.Select<MechComponentRef, ListElementController_BASE_NotListView>(componentRef => {
                            componentRef.LogIfNull("componentRef is null");
                            componentRef.DataManager = instance.dataManager.LogIfNull("(MechLabPanel instance).dataManager is null");
                            componentRef.RefreshComponentDef();
                            componentRef.Def.LogIfNull("componentRef.Def is null");
                            var count = (!instance.IsSimGame
                                          ? int.MinValue
                                          : instance.sim.GetItemCount(componentRef.Def.Description, componentRef.Def.GetType(), instance.sim.GetItemCountDamageType(componentRef)));

                            if (componentRef.ComponentDefType == ComponentType.Weapon) {
                                ListElementController_InventoryWeapon_NotListView controller = new ListElementController_InventoryWeapon_NotListView();
                                controller.InitAndFillInSpecificWidget(componentRef, null, instance.dataManager, null, count, false);
                                return controller;
                            } else {
                                ListElementController_InventoryGear_NotListView controller = new ListElementController_InventoryGear_NotListView();
                                controller.InitAndFillInSpecificWidget(componentRef, null, instance.dataManager, null, count, false);
                                return controller;
                            }
                        }).ToList();
                /* Build a list of data only for all components. */
                rawInventory = Sort(BuildRawInventory());

                InventoryItemElement_NotListView mkiie(bool nonexistant) {
                    var nlv = instance.dataManager.PooledInstantiate( ListElementController_BASE_NotListView.INVENTORY_ELEMENT_PREFAB_NotListView
                                                                    , BattleTechResourceType.UIModulePrefabs, null, null, null)
                                      .GetComponent<InventoryItemElement_NotListView>()
                                      .LogIfNull("Inventory_Element_prefab does not contain a NLV");
                    if (!nonexistant) {
                        nlv.SetRadioParent(new Traverse(inventoryWidget).Field("inventoryRadioSet").GetValue<HBSRadioSet>());
                        nlv.gameObject.transform.SetParent(new Traverse(inventoryWidget).Field("listParent").GetValue<UnityEngine.Transform>(), false);
                        nlv.gameObject.transform.localScale = UnityEngine.Vector3.one;
                    }
                    return nlv;
                };

                iieTmp = mkiie(true);

                /* Allocate very few visual elements, as this is extremely slow for both allocation and deallocation.
                   It's the difference between a couple of milliseconds and several seconds for many unique items in inventory 
                   This is the core of the fix, the rest is just to make it work within HBS's existing code.
                */
                List<InventoryItemElement_NotListView> make_ielCache()
                    => Enumerable.Repeat<Func<InventoryItemElement_NotListView>>( () => mkiie(false), itemLimit)
                                 .Select(thunk => thunk())
                                 .ToList();
                ielCache = make_ielCache();
                    
                var li = new Traverse(inventoryWidget).Field("localInventory").GetValue<List<InventoryItemElement_NotListView>>();
                ielCache.ForEach(iw => li.Add(iw));
                // End

                var lp = new Traverse(inventoryWidget).Field("listParent").GetValue<UnityEngine.Transform>();

                // DummyStart&End are blank rects stored at the beginning and end of the list so that unity knows how big the scrollrect should be
                // "placeholders"
                if (DummyStart == null) DummyStart = new UnityEngine.GameObject().AddComponent<UnityEngine.RectTransform>();
                if (DummyEnd   == null) DummyEnd   = new UnityEngine.GameObject().AddComponent<UnityEngine.RectTransform>();

                DummyStart.SetParent(lp, false);
                DummyEnd.SetParent(lp, false);
                LogDebug(string.Format("[LimitItems] inventory cached in {0} ms", sw.Elapsed.TotalMilliseconds));

                FilterChanged();
            } catch(Exception e) {
                LogException(e);
            }
        }

        public ListElementController_BASE_NotListView FetchItem(MechComponentRef mcr)
        {
            return rawInventory.Where(ri => ri.componentDef == mcr.Def && mcr.DamageLevel == GetRef(ri).DamageLevel).FirstOrDefault();
        }

        public MechLabDraggableItemType ToDraggableType(MechComponentDef def) {
            switch(def.ComponentType) {
            case ComponentType.NotSet: return MechLabDraggableItemType.NOT_SET;
            case ComponentType.Weapon: return MechLabDraggableItemType.InventoryWeapon;
            case ComponentType.AmmunitionBox: return MechLabDraggableItemType.InventoryItem;
            case ComponentType.HeatSink: return MechLabDraggableItemType.InventoryGear;
            case ComponentType.JumpJet: return MechLabDraggableItemType.InventoryGear;
            case ComponentType.Upgrade: return MechLabDraggableItemType.InventoryGear;
            case ComponentType.Special: return MechLabDraggableItemType.InventoryGear;
            case ComponentType.MechPart: return MechLabDraggableItemType.InventoryGear;
            }
            return MechLabDraggableItemType.NOT_SET;
        }

        /* Fast sort, which works off data, rather than visual elements. 
           Since only 7 visual elements are allocated, this is required.
        */
        public List<ListElementController_BASE_NotListView> Sort(List<ListElementController_BASE_NotListView> items) {
            LogSpam($"Sorting: {items.Select(item => GetRef(item).ComponentDefID).ToArray().Dump(false)}");

            var sw = Stopwatch.StartNew();
            var _a = new ListElementController_InventoryGear_NotListView();
            var _b = new ListElementController_InventoryGear_NotListView();
            var go = new UnityEngine.GameObject();
            var _ac = go.AddComponent<InventoryItemElement_NotListView>(); //new InventoryItemElement_NotListView();
            var go2 = new UnityEngine.GameObject();
            var _bc = go2.AddComponent<InventoryItemElement_NotListView>();
            _ac.controller = _a;
            _bc.controller = _b;
            var _cs = new Traverse(inventoryWidget).Field("currentSort").GetValue<Comparison<InventoryItemElement_NotListView>>();
            var cst = _cs.Method;
            LogDebug(string.Format("Sort using {0}::{1}", cst.DeclaringType.FullName, cst.ToString()));

            var tmp = items.ToList();
            tmp.Sort(new Comparison<ListElementController_BASE_NotListView>((l,r) => {
                        _ac.ComponentRef = _a.componentRef = GetRef(l);
                        _bc.ComponentRef = _b.componentRef = GetRef(r);
                        _ac.controller = l;
                        _bc.controller = r;
                        _ac.controller.ItemWidget = _ac;
                        _bc.controller.ItemWidget = _bc;
                        _ac.ItemType = ToDraggableType(l.componentDef);
                        _bc.ItemType = ToDraggableType(r.componentDef);
                        var res = _cs.Invoke(_ac, _bc);
                        LogSpam($"Compare {_a.componentRef.ComponentDefID}({_ac != null},{_ac.controller.ItemWidget != null}) & {_b.componentRef.ComponentDefID}({_bc != null},{_bc.controller.ItemWidget != null}) -> {res}");
                        return res;
                    }));

            UnityEngine.GameObject.Destroy(go);
            UnityEngine.GameObject.Destroy(go2);

            var delta = sw.Elapsed.TotalMilliseconds;
            LogInfo(string.Format("Sorted in {0} ms", delta));

            LogSpam($"Sorted: {tmp.Select(item => GetRef(item).ComponentDefID).ToArray().Dump(false)}");

            return tmp;
        }

        /* Fast filtering code which works off the data, rather than the visual elements.
           Suboptimal due to potential desyncs with normal filter proceedure, but simply required for performance */
        public List<ListElementController_BASE_NotListView> Filter(List<ListElementController_BASE_NotListView> _items) {
            var items = Sort(_items);

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

            InventoryDataObject_BASE tmpctl = new InventoryDataObject_InventoryWeapon();

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
                    var yes = filter.Execute(Enumerable.Repeat(tmpctl, 1)).Any();
                    if (!yes) LogDebug(string.Format("[Filter] Removing :id {0} :componentType {1} :quantity {2}", def.Description.Id, def.ComponentType, item.quantity));
                    return yes;
                }).ToList();
            return current;
        }

        /* Most mods hook the visual element code to filter. This function will do that as quickly as possible
           by re-using a single visual element.
        */
        public List<ListElementController_BASE_NotListView> FilterUsingHBSCode(List<ListElementController_BASE_NotListView> items) {
            try {
                var sw = new Stopwatch();
                sw.Start();
                var tmp = inventoryWidget.localInventory;
                var iw = iieTmp;
                inventoryWidget.localInventory = Enumerable.Repeat(iw, 1).ToList();

                // Filter items once using the faster code, then again to handle mods.
                var okItems = Filter(items).Where(lec => {
                        var cref = GetRef(lec);
                        lec.ItemWidget = iw;
                        iw.ComponentRef = cref;
                        // Not using SetData here still works, but is much slower
                        // TODO: Figure out why.
                        iw.SetData(lec, inventoryWidget, lec.quantity, false, null);
                        if (!iw.gameObject.activeSelf) { 
                            // Set active is very very slow, only call if absolutely needed
                            // It would be preferable to hook SetActive, but it's an external function.
                            iw.gameObject.SetActive(true); 
                        }
                        filterGuard = true;
                        // Let the main game or any mods filter if needed
                        // filter guard is to prevent us from infinitely recursing here, as this is also our triggering patch.
                        inventoryWidget.ApplyFiltering(false);
                        filterGuard = false;
                        lec.ItemWidget = null;
                        var yes = iw.gameObject.activeSelf == true;
                        if (!yes) LogDebug(string.Format( "[FilterUsingHBSCode] Removing :id {0} :componentType {1} :quantity {2} :tonnage {3}"
                                                        , lec.componentDef.Description.Id
                                                        , lec.componentDef.ComponentType
                                                        , lec.quantity
                                                        , (inventoryWidget.ParentDropTarget as MechLabPanel)?.activeMechDef?.Chassis?.Tonnage));
                        return yes;
                    }).ToList();
                inventoryWidget.localInventory = tmp;
                LogInfo(string.Format("Filter took {0} ms and resulted in {1} items", sw.Elapsed.TotalMilliseconds, okItems.Count));

                return okItems;
            } catch (Exception e) {
                LogException(e);
                return null;
            }
        }

        public MechComponentRef GetRef(ListElementController_BASE_NotListView lec) {
            if (lec is ListElementController_InventoryWeapon_NotListView) return (lec as ListElementController_InventoryWeapon_NotListView).componentRef;
            if (lec is ListElementController_InventoryGear_NotListView) return (lec as ListElementController_InventoryGear_NotListView).componentRef;
            LogError("[LimitItems] lec is not gear or weapon: " + lec.GetId());
            return null;
        }

        /* The user has changed a filter, and we rebuild the item cache. */
        public void FilterChanged(bool resetIndex = true) {
            try {
                var iw = new Traverse(inventoryWidget);
                Func<string,bool> f = (n) => iw.Field(n).GetValue<bool>();

                LogDebug(string.Format("[LimitItems] Filter changed (reset? {9}):\n  :weapons {0}\n  :equip {1}\n  :ballistic {2}\n  :energy {3}\n  :missile {4}\n  :small {5}\n  :heatsink {6}\n  :jumpjet {7}\n  :upgrade {8}"
                                      , f("filteringWeapons")
                                      , f("filteringEquipment")
                                      , f("filterEnabledWeaponBallistic")
                                      , f("filterEnabledWeaponEnergy")
                                      , f("filterEnabledWeaponMissile")
                                      , f("filterEnabledWeaponSmall")
                                      , f("filterEnabledHeatsink")
                                      , f("filterEnabledJumpjet")
                                      , f("filterEnabledUpgrade")
                                      , resetIndex));
                if (resetIndex) {
                    new Traverse(inventoryWidget).Field("scrollbarArea").GetValue<UnityEngine.UI.ScrollRect>().verticalNormalizedPosition = 1.0f;
                    index = 0;
                }

                filteredInventory = FilterUsingHBSCode(rawInventory);
                endIndex = filteredInventory.Count - itemsOnScreen;
                Refresh();
            } catch (Exception e) {
                LogException(e);
            }
        }

        public void Refresh(bool wantClobber = true) {
            LogDebug(string.Format("[LimitItems] Refresh: {0} {1} {2} {3}", index, filteredInventory.Count, itemLimit, new Traverse(inventoryWidget).Field("scrollbarArea").GetValue<UnityEngine.UI.ScrollRect>().verticalNormalizedPosition));
            if (index > filteredInventory.Count - itemsOnScreen) {
                index = filteredInventory.Count - itemsOnScreen;
            }
            if (filteredInventory.Count < itemsOnScreen) {
                index = 0;
            }
            if (index < 0) {
                index = 0;
            }
            if (Spam) LogSpam(string.Format("[LimitItems] Refresh(F): {0} {1} {2} {3}", index, filteredInventory.Count, itemLimit, new Traverse(inventoryWidget).Field("scrollbarArea").GetValue<UnityEngine.UI.ScrollRect>().verticalNormalizedPosition));

            var toShow = filteredInventory.Skip(index).Take(itemLimit).ToList();

            var icc = ielCache.ToList();

            Func<ListElementController_BASE_NotListView,string> pp = lec => {
                return string.Format( "[id:{0},damage:{1},quantity:{2},id:{3}]"
                                    , GetRef(lec).ComponentDefID
                                    , GetRef(lec).DamageLevel
                                    , lec.quantity
                                    , lec.GetId());
            };

            if (Spam) LogSpam("[LimitItems] Showing: " + string.Join(", ", toShow.Select(pp).ToArray()));

            var details = new List<string>();

            toShow.ForEach(lec => {
                    var iw = icc[0]; icc.RemoveAt(0);
                    var cref = GetRef(lec);
                    iw.ClearEverything();
                    iw.ComponentRef = cref;
                    lec.ItemWidget = iw;
                    iw.SetData(lec, inventoryWidget, lec.quantity, false, null);
                    lec.SetupLook(iw);
                    iw.gameObject.SetActive(true);
                    details.Insert(0, string.Format("enabled {0} {1}", iw.ComponentRef.ComponentDefID, iw.GetComponent<UnityEngine.RectTransform>().anchoredPosition));
                });
            icc.ForEach(unused => unused.gameObject.SetActive(false));

            var iw_corrupted_add = inventoryWidget.localInventory.Where(x => !ielCache.Contains(x)).ToList();
            if (iw_corrupted_add.Count > 0) {
                LogError("inventoryWidget has been corrupted, items were added: " + string.Join(", ", iw_corrupted_add.Select(c => c.controller).Select(pp).ToArray()));
                instance.ExitMechLab();
            }
            var iw_corrupted_remove = ielCache.Where(x => !inventoryWidget.localInventory.Contains(x)).ToList();
            if (iw_corrupted_remove.Count > 0) {
                LogError("inventoryWidget has been corrupted, items were removed");
                instance.ExitMechLab();
            }

            var listElemSize = 64.0f;
            var spacerTotal  = 16.0f; // IEL elements are 64 tall, but have a total of 80 pixels between each when considering spacing.
            var spacerHalf   = spacerTotal * .5f;
            var tsize        = listElemSize + spacerTotal;
            
            var virtualStartSize = tsize * index - spacerHalf;
            DummyStart.gameObject.SetActive(index > 0); //If nothing prefixing, must disable to prevent halfspacer offset.
            DummyStart.sizeDelta = new UnityEngine.Vector2(100, virtualStartSize);
            DummyStart.SetAsFirstSibling();

            var itemsHanging = filteredInventory.Count - (index + ielCache.Count(ii => ii.gameObject.activeSelf));

            var ap1 = ielCache[0].GetComponent<UnityEngine.RectTransform>().anchoredPosition;
            var ap2 = ielCache[1].GetComponent<UnityEngine.RectTransform>().anchoredPosition;

            LogDebug(string.Format("[LimitItems] Items prefixing {0} hanging {1} total {2} {3}/{4}", index, itemsHanging, filteredInventory.Count, ap1, ap2));



            var virtualEndSize = tsize * itemsHanging - spacerHalf;
            DummyEnd.gameObject.SetActive(itemsHanging > 0); //If nothing postfixing, must disable to prevent halfspacer offset.
            DummyEnd.sizeDelta = new UnityEngine.Vector2(100, virtualEndSize);
            DummyEnd.SetAsLastSibling();
            
            new Traverse(instance).Method("RefreshInventorySelectability").GetValue();
            if (Spam) { var sr = new Traverse(inventoryWidget).Field("scrollbarArea").GetValue<UnityEngine.UI.ScrollRect>();
                        LogSpam(string.Format( "[LimitItems] RefreshDone dummystart {0} dummyend {1} vnp {2} lli {3}"
                                             , DummyStart.anchoredPosition.y
                                             , DummyEnd.anchoredPosition.y
                                             , sr.verticalNormalizedPosition
                                             , "(" + string.Join(", ", details.ToArray()) + ")"
                                             ));
            }
        }

        public void Dispose() {
            inventoryWidget.localInventory.ForEach(ii => ii.controller = null);
        }

        public readonly static int itemsOnScreen = 7;

        // Maximum # of visual elements to allocate (will be used for slightly off screen elements.)
        public readonly static int itemLimit = 8;
        public static UnityEngine.RectTransform DummyStart; 
        public static UnityEngine.RectTransform DummyEnd;
        public static PatchMechlabLimitItems limitItems = null;

        public static bool filterGuard = false;
    }
}
