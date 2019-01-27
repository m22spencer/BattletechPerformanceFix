using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BattleTech.UI;
using UnityEngine;
using static BattletechPerformanceFix.Extensions;

namespace BattletechPerformanceFix
{
    class FilterByTyping : Feature
    {
        public void Activate()
        {
            "Update".Post<BattleTech.UnityGameInstance>();

            "ApplyFiltering".Post<MechLabInventoryWidget>();
            "PopulateInventory".Post<MechLabPanel>();
            "ExitMechLab".Post<MechLabPanel>();
        }

        public static void ExitMechLab_Post()
        {
            widget = null;
        }

        public static string lastFilter = "";
        public static string filter = "";
        public static double lastTime = -100000;
        public static void Update_Post()
        {
            if (widget == null) return;
            var curTime = Time.unscaledTime;
            if (curTime - lastTime > 2.0) {
                filter = "";
            }
            if (!string.IsNullOrEmpty(Input.inputString))
            {
                lastTime = curTime;
                filter += Input.inputString;

                LogInfo("Updated filter to: " + filter);

                if (lastFilter != filter && widget != null)
                {
                    LogInfo("Re-Filter");

                    WantFiltering = true;
                    widget?.ApplyFiltering();
                    WantFiltering = false;
                }
            }
        }

        public static bool WantFiltering = false;
        public static void ApplyFiltering_Post(List<InventoryItemElement_NotListView> ___localInventory)
        {

            if (widget == null) return;
            if (!WantFiltering) { filter = lastFilter = "";  }
            lastFilter = filter;
            Trap(() =>
            {
                var inv = ___localInventory;
                inv.ForEach(item =>
                {
                    if (filter == null) return;
                    var name = item.ComponentRef?.Def?.Description?.UIName;
                    if (item.gameObject.activeSelf && !name.ToLowerInvariant().Contains(filter))
                    {
                        LogSpam($"Deactivating {name}");
                        item.gameObject.SetActive(false);
                    }
                });
            });
        }

        public static MechLabInventoryWidget widget;
        public static void PopulateInventory_Post(MechLabInventoryWidget ___inventoryWidget)
        {
            widget = ___inventoryWidget;
        }
    }
}
