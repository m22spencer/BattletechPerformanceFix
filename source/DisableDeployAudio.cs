using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BattleTech.UI;
using Harmony;
using System.Reflection;
using System.Reflection.Emit;
using BattleTech;
using static BattletechPerformanceFix.Extensions;

namespace BattletechPerformanceFix
{
    /* 4, maybe 5 second faster load times.
     * The audio clip delays the load, and can't (easily) be played in parallel
     *   due to how aggressive Btech is about unloading assets.
     */
    class DisableDeployAudio : Feature
    {
        public void Activate()
        {
            var ccc = nameof(LanceConfiguratorPanel.ContinueConfirmClicked);

            ccc.Pre<LanceConfiguratorPanel>();
            ccc.Post<LanceConfiguratorPanel>();
        }
        
        public static void ContinueConfirmClicked_Pre(ref float __state)
        {
            __state = AudioEventManager.VoiceVolume;
            AudioEventManager.VoiceVolume = 0;
        }

        public static void ContinueConfirmClicked_Post(ref float __state)
        {
            AudioEventManager.VoiceVolume = __state;
        }
    }
}
