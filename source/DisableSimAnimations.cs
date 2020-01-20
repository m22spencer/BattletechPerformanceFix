using BattleTech;
using BattleTech.UI;
using Harmony;

namespace BattletechPerformanceFix
{
    class DisableSimAnimations : Feature
    {
        public void Activate()
        {            
            "Init".Pre<SimGameCameraController>();
            "TransitionMech".Pre<SGRoomController_MechBay>();
        }

        // disables scene transition animation
        public static void Init_Pre(ref float ___betweenRoomTransitionTime, ref float ___inRoomTransitionTime)
        {
            ___betweenRoomTransitionTime = ___inRoomTransitionTime = 0f;
        }

        // disables mech transition animation
        public static void TransitionMech_Pre(ref float fadeDuration)
        {
            UnityGameInstance.BattleTechGame.Simulation.CameraController.mechLabSpin = null;
            fadeDuration = 0f;
        }
    }
}
