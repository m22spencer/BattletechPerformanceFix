using BattleTech;
using BattleTech.UI;
using Harmony;

namespace BattletechPerformanceFix
{
    class DisableSimAnimations : Feature
    {
        public void Activate()
        {
            "TransitionCameraRoom".Pre<SimGameCameraController>();
            "TransitionCameraSimple".Pre<SimGameCameraController>();
            "TransitionCameraRoomLeopard".Pre<SimGameCameraController>();
            "TransitionMech".Pre<SGRoomController_MechBay>();
        }

        // disables scene transition animation
        public static void TransitionCameraRoom_Pre(ref float transitionTime) => transitionTime = 0f;
        public static void TransitionCameraSimple_Pre(ref float transitionTime) => transitionTime = 0f;
        public static void TransitionCameraRoomLeopard_Pre(ref float transitionTime) => transitionTime = 0f;

        // disables mech transition animation
        public static void TransitionMech_Pre(ref float fadeDuration)
        {
            UnityGameInstance.BattleTechGame.Simulation.CameraController.mechLabSpin = null;
            fadeDuration = 0f;
        }
    }
}
