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

    /*
    [HarmonyPatch(typeof(MechLabInventoryWidget), "ApplySorting")]
    public static class Patch_NoSorting {
        public static bool Prefix() {
            return false;
        }
    }
    */

    [HarmonyPatch(typeof(MechLabPanel), "PopulateInventory")]

    public static class Patch_MechLabPanel_PopulateInventory {
        public static MechLabPanel inst;
        public static int lastIndex = 0;
        public static int index = 0;
        public static int bound = 0;

        public static UnityEngine.GameObject DummyStart;
        public static UnityEngine.GameObject DummyEnd;

        public static void Prefix(MechLabPanel __instance, ref List<MechComponentRef> ___storageInventory, MechLabInventoryWidget ___inventoryWidget, ref List<MechComponentRef> __state) {
            try {
            inst = __instance;        
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

            if (delta != 0 && ___inventoryWidget.localInventory.Count() == 7) {
                // Positive delta removes from front
                // Negative delta removes from back
                // Re-use
                var inv = ___inventoryWidget.localInventory;
                var remove = delta > 0 ? inv.Take(delta) : inv.Skip(7 - delta);
                var keep   = inv.Where(i => !remove.Contains(i));
                if (remove.Count() + keep.Count() != 7)
                    throw new System.Exception("Remove+Keep mismatch of " + (remove.Count() + keep.Count()).ToString());
                
                ___inventoryWidget.localInventory = keep.ToList();
                ___storageInventory = (delta > 0 ? ___storageInventory.Skip(7 - delta) : ___storageInventory.Take(delta)).ToList();
            } else {
                ___inventoryWidget.ClearInventory();
            }

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

            try {
            if (DummyStart == null) {
                DummyStart = new UnityEngine.GameObject();
                DummyStart.AddComponent<UnityEngine.RectTransform>()
                    .SetParent(new Traverse(___inventoryWidget).Field("listParent").GetValue<UnityEngine.Transform>(), false);
            }
            var itemsize = 60.0f;
            var st = DummyStart.GetComponent<UnityEngine.RectTransform>();
            st.sizeDelta = new UnityEngine.Vector2(100, itemsize * index);
            st.SetAsFirstSibling();

            if (DummyEnd == null) {
                DummyEnd = new UnityEngine.GameObject();
                DummyEnd.AddComponent<UnityEngine.RectTransform>()
                    .SetParent(new Traverse(___inventoryWidget).Field("listParent").GetValue<UnityEngine.Transform>(), false);
            }
            var ed = DummyEnd.GetComponent<UnityEngine.RectTransform>();
            ed.sizeDelta = new UnityEngine.Vector2(100, itemsize * (bound - index - 7));
            ed.SetAsLastSibling();

            var sr = new Traverse(___inventoryWidget).Field("scrollbarArea").GetValue<UnityEngine.UI.ScrollRect>();
            if (sr == null)
                throw new System.Exception("sr is null");
            
            var pos = 1.0f - ((float)index / (float)(bound-7));

            __instance.StartCoroutine(Go(sr, pos));
            } catch(Exception e) {
                Control.mod.Logger.Log(string.Format("Exn: {0}", e));
            }
        }

        public static IEnumerator Go(UnityEngine.UI.ScrollRect sr, float pos) {
            yield return new UnityEngine.WaitForEndOfFrame();
            UnityEngine.Canvas.ForceUpdateCanvases();
            sr.verticalNormalizedPosition = pos;
            yield break;
        }

        public static void Refresh() {
            try {
            var mlp = new Traverse(Patch_MechLabPanel_PopulateInventory.inst);
            mlp.Field("inventoryWidget").Method("ClearInventory").GetValue();
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
                Patch_MechLabPanel_PopulateInventory.Refresh();
            } catch(Exception e) {
                Control.mod.Logger.Log(string.Format("exn {0}", e));
            }
        }
    }

    [HarmonyPatch(typeof(MechLabInventoryWidget), "SetFiltersWeapons")]
    public static class HookSetFiltersWeapons {
        public static void Postfix() {
            try {
                Patch_MechLabPanel_PopulateInventory.Refresh();
            } catch(Exception e) {
                Control.mod.Logger.Log(string.Format("exn {0}", e));
            }
        }
    }

    [HarmonyPatch(typeof(MechLabInventoryWidget), "SetFiltersEquipment")]
    public static class HookSetFiltersEquipment {
        public static void Postfix() {
            try {
                Patch_MechLabPanel_PopulateInventory.Refresh();
            } catch(Exception e) {
                Control.mod.Logger.Log(string.Format("exn {0}", e));
            }
        }
    }

    [HarmonyPatch(typeof(MechLabInventoryWidget), "SetFiltersMechParts")]
    public static class HookSetFiltersMechParts {
        public static void Postfix() {
            try {
                Patch_MechLabPanel_PopulateInventory.Refresh();
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
            Patch_MechLabPanel_PopulateInventory.Refresh();
            } catch(Exception e) {
                Control.mod.Logger.Log(string.Format("exn {0}", e));
            }
        }
    }
}