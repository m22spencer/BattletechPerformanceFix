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
    [HarmonyPatch(typeof(MechLabInventoryWidget), "ApplyFiltering")]
    public static class Patch_NoFiltering {
        public static bool Prefix() {
            return false;
        }
    }

    [HarmonyPatch(typeof(MechLabInventoryWidget), "ApplySorting")]
    public static class Patch_NoSorting {
        public static bool Prefix() {
            return false;
        }
    }

    [HarmonyPatch(typeof(MechLabInventoryWidget), "OnRemoveItem")]
    public static class Patch_OnRemoveItem {
        public static void Postfix(MechLabInventoryWidget __instance, IMechLabDraggableItem item) {
            if (Patch_MechLabPanel_PopulateInventory.Data == null) return;
            Control.mod.Logger.Log("Remove");
            // Need to track this removal via storageItems
            //   *additionally* ensure that this item is not cleared from widget inventory
            //      while dragging, technically inventory should not be modified at all.
            Patch_MechLabPanel_PopulateInventory
                .Data
                .Find(d => d.ComponentRef.ComponentDefID == item.ComponentRef.ComponentDefID).Decr();

            //Patch_MechLabPanel_PopulateInventory.Refresh(true);
        }
    }

    [HarmonyPatch(typeof(MechLabInventoryWidget), "OnAddItem")]
    public static class Patch_OnAddItem {
        public static bool guard = false;
        public static void Postfix(IMechLabDraggableItem item) {
            if (guard || Patch_MechLabPanel_PopulateInventory.Data == null) return;
            Control.mod.Logger.Log("add & refresh");
            Patch_MechLabPanel_PopulateInventory
                .Data
                .Find(d => d.ComponentRef.ComponentDefID == item.ComponentRef.ComponentDefID).Incr();
            Patch_MechLabPanel_PopulateInventory.Refresh(false);    
        }
    }


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

    [HarmonyPatch(typeof(MechLabPanel), "OnRequestResourcesComplete")]
    public static class Patch_ResetItemListHack {
        public static void Prefix() {
            Patch_MechLabPanel_PopulateInventory.Reset();
        }
    }
    
    [HarmonyPatch(typeof(MechLabPanel), "ConfirmRevertMech")]
    
    public static class Patch_ResetItemListHack2 {
        public static void Prefix() {
            Patch_MechLabPanel_PopulateInventory.Reset();
        }
    }

    [HarmonyPatch(typeof(MechLabPanel), "ExitMechLab")]
    
    public static class Patch_ResetItemListHack3 {
        public static void Prefix() {
            Patch_MechLabPanel_PopulateInventory.Reset();
        }
    }
    

    [HarmonyPatch(typeof(MechLabPanel), "PopulateInventory")]
    public static class Patch_MechLabPanel_PopulateInventory {
        public static MechLabPanel inst;
        public static int lastIndex = 0;
        public static int index = 0;
        public static int bound = 0;

        public static UnityEngine.GameObject DummyStart;
        public static UnityEngine.GameObject DummyEnd;

        public static List<DefAndCount> Data;

        public static void Reset() {
            Control.mod.Logger.Log("Reset");
            Data = null;
            if (DummyStart != null) UnityEngine.GameObject.Destroy(DummyStart);
            DummyStart = null;
            if (DummyEnd != null) UnityEngine.GameObject.Destroy(DummyEnd);
            DummyEnd = null;
            index = 0;
            bound = 0;
            lastIndex = 0;
            inst = null;
            Patch_OnAddItem.guard = false; 
        }

        public static void Prefix(MechLabPanel __instance, ref List<MechComponentRef> ___storageInventory, MechLabInventoryWidget ___inventoryWidget, ref List<MechComponentRef> __state) {
            // TODO:  On first run, cache a sorted (___storageInventory,some quantity)
            //     OnItemAdded/OnItemRemoved/ClearInventory modifies quantity
            //     Refresh simply filters and takes first 7 elements.
            //     Need to write our own PopulateInventory
            try {
                inst = __instance;        
                var iw = new Traverse(___inventoryWidget);
                Func<string,bool> f = (n) => iw.Field(n).GetValue<bool>();
            if (Data == null) {
                var sw = new Stopwatch();
                var tmp = ___storageInventory.Select(def => {
                    def.DataManager = __instance.dataManager;
                    def.RefreshComponentDef();
                    var num = !__instance.IsSimGame ? int.MinValue : __instance.sim.GetItemCount(def.Def.Description, def.Def.GetType(), SimGameState.ItemCountType.UNDAMAGED_ONLY); // Undamaged only is wrong, just for testing.
                    return new DefAndCount(def, num);
                }).ToList();

                sw.Start();
                
                // re-use HBS sorting implementation, awful but less issues with mods that touch sorting.
                var _a = new ListElementController_InventoryGear_NotListView();
                var _b = new ListElementController_InventoryGear_NotListView();
                var _ac = new InventoryItemElement_NotListView();
                var _bc = new InventoryItemElement_NotListView();
                _ac.controller = _a;
                _bc.controller = _b;
                var _cs = iw.Field("currentSort").GetValue<Comparison<InventoryItemElement_NotListView>>();
                tmp.Sort(new Comparison<DefAndCount>((l,r) => {
                    _a.componentRef = l.ComponentRef;
                    _b.componentRef = r.ComponentRef;
                    return _cs.Invoke(_ac, _bc);
                }));
                Data = tmp;
                Control.mod.Logger.Log(string.Format("Preprocess {0} ms", sw.Elapsed.TotalMilliseconds));
            }





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

            var current = Data.Where(dac => dac.Count == Int32.MinValue || dac.Count > 0)
                              .Select(dac => dac.ComponentRef)
                              .Where(d => { 
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
            var delta = index - lastIndex;
            lastIndex = index;
       
            ___storageInventory = current.Skip(index).Take(7).ToList();

            ___inventoryWidget.ClearInventory();

            } catch (Exception e) {
                Control.mod.Logger.Log(string.Format("Exn: {0}", e));
            }
            Patch_OnAddItem.guard = true;
        }

        public static void Postfix(MechLabPanel __instance, ref List<MechComponentRef> ___storageInventory, ref List<MechComponentRef> ___originalStorageInventory, MechLabInventoryWidget ___inventoryWidget, ref List<MechComponentRef> __state) {
            try {
            ___storageInventory = Data.Select(dac => dac.ComponentRef).ToList();
            ___originalStorageInventory = Data.Select(dac => dac.ComponentRef).ToList();
            // inventory filter may be different from filter used above, so go ahead and show all items always.
            foreach(InventoryItemElement_NotListView inventoryItemElement_NotListView in ___inventoryWidget.localInventory) {
                inventoryItemElement_NotListView.gameObject.SetActive(true);
                new Traverse(inventoryItemElement_NotListView).Field("quantity").SetValue(Data.Find(d => d.ComponentRef.ComponentDefID == inventoryItemElement_NotListView.ComponentRef.ComponentDefID).Count);
                inventoryItemElement_NotListView.RefreshQuantity();
            }
            
            if (DummyStart == null) {
                DummyStart = new UnityEngine.GameObject();
                DummyStart.AddComponent<UnityEngine.RectTransform>()
                    .SetParent(new Traverse(___inventoryWidget).Field("listParent").GetValue<UnityEngine.Transform>(), false);
            }
            var itemsize = 60.0f;
            var st = DummyStart.GetComponent<UnityEngine.RectTransform>();
            st.sizeDelta = new UnityEngine.Vector2(100, bound <= 7 ? 0.0f : itemsize * index);
            st.SetAsFirstSibling();

            var itemsCt = ___inventoryWidget.localInventory.Count;

            if (DummyEnd == null) {
                DummyEnd = new UnityEngine.GameObject();
                DummyEnd.AddComponent<UnityEngine.RectTransform>()
                    .SetParent(new Traverse(___inventoryWidget).Field("listParent").GetValue<UnityEngine.Transform>(), false);
            }
            var ed = DummyEnd.GetComponent<UnityEngine.RectTransform>();
            ed.sizeDelta = new UnityEngine.Vector2(100, bound <= 7 ? 0.0f : itemsize * (bound - index - itemsCt));
            ed.SetAsLastSibling();

            var sr = new Traverse(___inventoryWidget).Field("scrollbarArea").GetValue<UnityEngine.UI.ScrollRect>();
            if (sr == null) {
                Control.mod.Logger.Log("Warning: sr is null");
                return;
            }

            // Something else keeps setting the normalizedPosition, so ensure we set it last.
            __instance.StartCoroutine(Go(sr, bound <= 7 ? 1.0f : sr.verticalNormalizedPosition));
            } catch(Exception e) {
                Control.mod.Logger.Log(string.Format("Exn: {0}", e));
            }
            
            Patch_OnAddItem.guard = false;
        }

        public static IEnumerator Go(UnityEngine.UI.ScrollRect sr, float pos) {
            yield return new UnityEngine.WaitForEndOfFrame();
            UnityEngine.Canvas.ForceUpdateCanvases();
            Control.mod.Logger.Log("Pos: " + pos.ToString());
            sr.verticalNormalizedPosition = pos;
            yield break;
        }

        public static void Refresh(bool wantClear) {
            try {
            var mlp = new Traverse(Patch_MechLabPanel_PopulateInventory.inst);
            //if (wantClear) mlp.Field("inventoryWidget").Method("ClearInventory").GetValue();
            mlp.Method("PopulateInventory").GetValue();
            } catch(Exception e) {
                Control.mod.Logger.Log(string.Format("Exn: {0}", e));
            }
        }
    }

    // sub category filters
    [HarmonyPatch(typeof(MechLabInventoryWidget), "OnFilterButtonClicked")]
    public static class HookFilterButtonClicked {
        public static void Postfix() {
            try {
                if (Patch_MechLabPanel_PopulateInventory.Data == null) return;
                Patch_MechLabPanel_PopulateInventory.Refresh(true);
            } catch(Exception e) {
                Control.mod.Logger.Log(string.Format("exn {0}", e));
            }
        }
    }

    [HarmonyPatch(typeof(MechLabInventoryWidget), "SetFiltersWeapons")]
    public static class HookSetFiltersWeapons {
        public static void Postfix() {
            try {
                if (Patch_MechLabPanel_PopulateInventory.Data == null) return;
                Patch_MechLabPanel_PopulateInventory.Refresh(true);
            } catch(Exception e) {
                Control.mod.Logger.Log(string.Format("exn {0}", e));
            }
        }
    }

    [HarmonyPatch(typeof(MechLabInventoryWidget), "SetFiltersEquipment")]
    public static class HookSetFiltersEquipment {
        public static void Postfix() {
            try {
                if (Patch_MechLabPanel_PopulateInventory.Data == null) return;
                Patch_MechLabPanel_PopulateInventory.Refresh(true);
            } catch(Exception e) {
                Control.mod.Logger.Log(string.Format("exn {0}", e));
            }
        }
    }

    [HarmonyPatch(typeof(MechLabInventoryWidget), "SetFiltersMechParts")]
    public static class HookSetFiltersMechParts {
        public static void Postfix() {
            try {
                if (Patch_MechLabPanel_PopulateInventory.Data == null) return;
                Patch_MechLabPanel_PopulateInventory.Refresh(true);
            } catch(Exception e) {
                Control.mod.Logger.Log(string.Format("exn {0}", e));
            }
        }
    }
    
    [HarmonyPatch(typeof(UnityEngine.UI.ScrollRect), "LateUpdate")]
    public static class OnDragHook {
        public static void Postfix(UnityEngine.UI.ScrollRect __instance) {
            try {
                if (Patch_MechLabPanel_PopulateInventory.Data == null) return;
                if (new Traverse(Patch_MechLabPanel_PopulateInventory.inst).Field("inventoryWidget").Field("scrollbarArea").GetValue<UnityEngine.UI.ScrollRect>() != __instance)
                    return;
                var newIndex = (int)((Patch_MechLabPanel_PopulateInventory.bound-7.0f) * (1.0f - __instance.verticalNormalizedPosition));
                if (Patch_MechLabPanel_PopulateInventory.bound <= 7) {
                    newIndex = 0;
                }
                if (Patch_MechLabPanel_PopulateInventory.index != newIndex) {
                    Patch_MechLabPanel_PopulateInventory.index = newIndex;
                    Control.mod.Logger.Log("Refresh with: " + newIndex.ToString());
                    Patch_MechLabPanel_PopulateInventory.Refresh(false);
                }
            } catch(Exception e) {
                Control.mod.Logger.Log(string.Format("exn {0}", e));
            }
        }
    }
}