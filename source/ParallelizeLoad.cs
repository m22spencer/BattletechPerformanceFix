using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Harmony;
using RSG;

using UnityEngine;
using UnityEngine.SceneManagement;
using BattleTech;
using BattleTech.UI;
using BattleTech.Save;
using System.Diagnostics;
using static BattletechPerformanceFix.Extensions;

namespace BattletechPerformanceFix
{
    // Unity scene load occurs during SimGameState._OnBeginAttachUX
    // move it to SimGameState._OnBeginDefsLoad
    class ParallelizeLoad : Feature
    {
        public void Activate() {
            var sgs = typeof(SimGameState);
            var self = typeof(ParallelizeLoad);
            Main.harmony.Patch(AccessTools.Method(sgs, nameof(_OnBeginAttachUX)), new HarmonyMethod(AccessTools.Method(self, nameof(_OnBeginAttachUX))));

            // For some reason, triggering a scene load before _OnBeginDefsLoad is invoked causes the UI to never initiate. Postfix to avoid this.
            Main.harmony.Patch(AccessTools.Method(sgs, nameof(_OnBeginDefsLoad)), null, new HarmonyMethod(AccessTools.Method(self, nameof(_OnBeginDefsLoad))));
          
            Main.harmony.Patch(AccessTools.Method(typeof(BattleTech.LevelLoader), nameof(LoadScene)), new HarmonyMethod(AccessTools.Method(self, nameof(LoadScene))));
            Main.harmony.Patch(AccessTools.Method(typeof(BattleTech.LevelLoader), "Start"), new HarmonyMethod(AccessTools.Method(self, nameof(LevelLoader_Start))));


            Main.harmony.Patch(AccessTools.Method(typeof(MissionResults), "Init"), new HarmonyMethod(AccessTools.Method(self, nameof(_OnBeginDefsLoad))));

        }

        public static bool LoadScene(string scene) {
            if (Scene != null) {
                Log($"LL.LoadScene intercepted for :scene {scene}");
                return false;
            }
            var currentScenes = string.Join(" ", SceneManager.GetAllScenes().Select(s => s.name).ToArray());
            Log($"LL.LoadScene {scene} :currentScenes {currentScenes} from {new StackTrace().ToString()}");
            return true;
        }

        public static bool LevelLoader_Start() {
            if (Scene != null) {
                Log($"LL.Start intercepted");
                return false;
            }

            var currentScenes = string.Join(" ", SceneManager.GetAllScenes().Select(s => s.name).ToArray());
            Log($"LL start triggered :currentScenes {currentScenes}");
            return true;
        }

        public static IPromise Scene;
        public static AsyncOperation SceneOp;
        public static bool _OnBeginAttachUX(SimGameState __instance) {
            if (Scene == null) return true; //Not our scene
            Log($"Attach UX and do *not* load scene from {new StackTrace().ToString()}");
            __instance.DataManager.Clear(false, false, true, true, false);
            ActiveOrDefaultSettings.CloudSettings.customUnitsAndLances.UnMountMemoryStore(__instance.DataManager);

            SceneOp.allowSceneActivation = true;

            Log($"_OnBeginAttachUX at :frame {Time.frameCount} :time {Time.unscaledTime}");
            Scene.Done(() => { Log("Scene is loaded and done? {0}", Scene != null);
                               SceneManager.SetActiveScene(SceneManager.GetSceneByName("SimGame"));
                               SceneManager.GetAllScenes().ToList().ForEach(scn => Log($"Scene: {scn.name}"));
                               Scene = null; });
            return false;
        }

        public static void _OnBeginDefsLoad() {
            var currentScenes = string.Join(" ", SceneManager.GetAllScenes().Select(s => s.name).ToArray());
            Log($"Load defs in parallel for :scene `SimGame` :frame {Time.frameCount} :time {Time.unscaledTime} :currentScenes {currentScenes} :from \r\n{new StackTrace().ToString()}");

            if (SceneManager.GetAllScenes().ToList().Exists(s => s.name == "SimGame")) { Log("Sim game exists already - unloading");
                                                                                         Trap(() => SceneManager.UnloadScene("SimGame"));
                                                                                         Log("Sim game unloaded"); }
            var sw = Stopwatch.StartNew();
            // FIXME: Can we use asyncOp.allowSceneActivation

            Trap(() => {
                    // It would be optimal to remove shaders and effects and lower quality options here to ensure we spend no/little time rendering
                    var op = SceneManager.LoadSceneAsync("SimGame", LoadSceneMode.Single);
                    SceneOp = op;
                    op.allowSceneActivation = false;
                    Scene = op.AsPromise();
                    Scene.Done(() => Log($"Scene `SimGame` loaded :frame {Time.frameCount} :time {Time.unscaledTime}"));
                });
        }
    }
}
