using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Harmony;
using BattleTech;
using BattleTech.UI;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using BattleTech.Assetbundles;
using UnityEngine;
using static BattletechPerformanceFix.Extensions;

namespace BattletechPerformanceFix
{
    /* Mechs built off 1.5 bundles do not have the new UW ghost blip assets
     *  to ensure they can be loaded, we re-use the existing blips.
     *  This is almost certainly wrong, but it does not crash.
     */
    class GhostBlipOverride : Feature
    {
        public void Activate()
        {
            "Init".Pre<PilotableActorRepresentation>();
        }

        public static void Init_Pre(PilotableActorRepresentation __instance)
        {
            __instance.BlipObjectGhostWeak = __instance.BlipObjectUnknown;
            __instance.BlipObjectGhostStrong = __instance.BlipObjectIdentified;
        }
    }
}
