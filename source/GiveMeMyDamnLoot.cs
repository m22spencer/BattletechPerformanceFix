using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Harmony;
using BattleTech;
using BattleTech.UI;
using static BattletechPerformanceFix.Extensions;

namespace BattletechPerformanceFix
{
    // This is garbage, but it works.
    class GiveMeMyDamnLoot : Feature
    {
        public void Activate()
        {
            "Init".Post<AAR_SalvageChosen>();
        }

        public static void Init_Post(AAR_SalvageChosen __instance, Contract ___contract, List<GameObject> ___tempHoldingGridSpaces)
        {
            LogInfo("Adding more temp holding grid spaces");

            Trap(() =>
            {
                new Traverse(___contract).Property("FinalPrioritySalvageCount").SetValue(howManyLoots);

                var fst = ___tempHoldingGridSpaces.First();
                var parent = fst.transform.parent;

                var content = parent;
                var viewport = content.transform.parent;
                var container = viewport.transform.parent;

                var sr = container.gameObject.AddComponent<ScrollRect>();
                sr.content = content.GetComponent<RectTransform>();
                sr.vertical = true;
                sr.horizontal = false;
                sr.scrollSensitivity = 30;
                sr.viewport = viewport.GetComponent<RectTransform>();

                LogDebug("Here and create graphic");
                var graphic = sr.viewport.gameObject.GetComponent<Image>();
                LogDebug("graphic created");
                graphic.NullCheckError("graphic is null, wtf");
                graphic.color = Color.white;
                LogDebug("Here and create rect transform");
                var rt = sr.viewport.GetComponent<RectTransform>();
                rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 470);
                rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 600);
                LogDebug("Here and enable image");
                graphic.enabled = true;

                LogDebug("Here and create mask");
                var mask = sr.viewport.gameObject.AddComponent<Mask>();
                mask.showMaskGraphic = false;
                LogDebug("OK");

                var needExtra = howManyLoots - ___tempHoldingGridSpaces.Count;
                for (var i = 0; i < needExtra; i++)
                {
                    var n = GameObject.Instantiate(fst, parent);
                    LogInfo($"Created {n.name}");
                    ___tempHoldingGridSpaces.Add(n);
                }
            });
        }
    }
}
