using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Harmony;
using BattleTech;
using BattleTech.Data;
using HBS.Data;
using System.Diagnostics;
using System.IO;
using System.Reflection.Emit;
using BattleTech.Rendering;
using UnityEngine;

namespace BattletechPerformanceFix
{
    public class BTLightControllerThrottle : Feature
    {
        public void Activate()
        {
            Control.Log("Activate BTLightControllerThrottle");
            Control.harmony.Patch(AccessTools.Method(typeof(BTCustomRenderer), "OnPreCull")
                                 , null
                                 , null
                                 , new HarmonyMethod(typeof(BTLightControllerThrottle), "OnPreCullPatch")
                                 );
        }

        public static BTLight.LightStruct[] last = null;
        public static Vector4 lastNumLights = default(Vector4);
        public static float lastRender = -10;
        public static float renderEveryXS = .2f;
        public static BTLight.LightStruct[] GetLightArray(Camera camera, bool simGame, ComputeBuffer cullBuffer, ComputeBuffer lightMatricies, out Vector4 numLights, bool isPortrait)
        {
            Control.Trap(() => { 
                if (last == null || Time.unscaledTime > lastRender + renderEveryXS)
                {
                    lastRender = Time.unscaledTime;
                    var sw = new Stopwatch();
                    sw.Start();
                    last = BTLightController.GetLightArray(camera, simGame, cullBuffer, lightMatricies, out lastNumLights, isPortrait);
                    sw.Stop();

                    Control.LogDebug("GetLightArray :frame {0} :ms {1}", Time.unscaledTime, sw.Elapsed.TotalMilliseconds);
                }
            });
            numLights = lastNumLights;
            return last;
        }

        public static IEnumerable<CodeInstruction> OnPreCullPatch(IEnumerable<CodeInstruction> ins)
        {
            return ins.MethodReplacer( AccessTools.Method(typeof(BTLightController), "GetLightArray")
                                     , AccessTools.Method(typeof(BTLightControllerThrottle), "GetLightArray"));
        }
    }
}
           