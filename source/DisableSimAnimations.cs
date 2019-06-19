using BattleTech;
using BattleTech.UI;
using Harmony;

namespace BattletechPerformanceFix
{
    class DisableSimAnimations : Feature
    {
        public void Activate()
        {
            var init = "Init";
            var transitionMech = "TransitionMech";
            
            init.Pre<SimGameCameraController>();
            transitionMech.Pre<SGRoomController_MechBay>();
        }

        // disables scene transition animation
        [HarmonyPatch(typeof(SimGameCameraController), "Init")]
        public static class Init_Pre
        {
            public static void Prefix(
                ref float ___betweenRoomTransitionTime, ref float ___inRoomTransitionTime)
            {
                ___betweenRoomTransitionTime = ___inRoomTransitionTime = 0f;
            }
        }

        // disables mech transition animation
        [HarmonyPatch(typeof(SGRoomController_MechBay), "TransitionMech")]
        public static class TransitionMech_Pre
        {
            // ReSharper disable once RedundantAssignment
            public static void Prefix(ref float fadeDuration)
            {
                UnityGameInstance.BattleTechGame.Simulation.CameraController.mechLabSpin = null;
                fadeDuration = 0f;
            }
        }
    }
}
