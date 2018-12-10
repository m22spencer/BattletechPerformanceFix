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
            Main.harmony.Patch(AccessTools.Method(sgs, nameof(_OnBeginDefsLoad)), new HarmonyMethod(AccessTools.Method(self, nameof(_OnBeginDefsLoad))));
            Main.harmony.Patch(AccessTools.Method(sgs, nameof(SimGameUXCreatorLoaded)), new HarmonyMethod(AccessTools.Method(self, nameof(SimGameUXCreatorLoaded))));

            Main.harmony.Patch(AccessTools.Method(typeof(SimGameUXCreator), nameof(Awake)), new HarmonyMethod(AccessTools.Method(self, nameof(Awake))));
            Main.harmony.Patch(AccessTools.Method(typeof(SimGameUXCreator), nameof(Start)), new HarmonyMethod(AccessTools.Method(self, nameof(Start))));
            Main.harmony.Patch(AccessTools.Method(typeof(SimGameUXCreator), nameof(OnSimGameInitializeComplete)), new HarmonyMethod(AccessTools.Method(self, nameof(OnSimGameInitializeComplete))));

            Main.harmony.Patch(AccessTools.Method(typeof(BattleTech.LevelLoader), nameof(LoadScene)), new HarmonyMethod(AccessTools.Method(self, nameof(LoadScene))));
            Main.harmony.Patch(AccessTools.Method(typeof(BattleTech.LevelLoader), "Start"), new HarmonyMethod(AccessTools.Method(self, nameof(LevelLoader_Start))));


            Main.harmony.Patch(AccessTools.Method(typeof(MissionResults), "Init"), new HarmonyMethod(AccessTools.Method(self, nameof(_OnBeginDefsLoad))));

        }

        public static void LoadScene(string scene) {
            var currentScenes = string.Join(" ", SceneManager.GetAllScenes().Select(s => s.name).ToArray());
            Log($"LL.LoadScene {scene} :currentScenes {currentScenes} from {new StackTrace().ToString()}");
        }

        public static void LevelLoader_Start() {
            var currentScenes = string.Join(" ", SceneManager.GetAllScenes().Select(s => s.name).ToArray());
            Log($"LL start triggered :currentScenes {currentScenes}");
        }

        public static void SimGameUXCreatorLoaded() {
            Log("SimGameUXCreatorLoaded");
        }

        public static bool OnSimGameInitializeComplete(SimGameUXCreator __instance) {
            Log($"Sim game initialize complete :frame {Time.frameCount} :time {Time.unscaledTime}");
            __instance.InitializeUXRoutine().AsPromise()
                      .Done(() => { Log($"Initialize coroutine completed :frame {Time.frameCount} :time {Time.unscaledTime}");
                                    Trap(() => new Traverse(__instance).Field("sim").GetValue<SimGameState>().SimGameUXCreatorLoaded());
                                    Log($"Load complete at :frame {Time.frameCount} :time {Time.unscaledTime}"); });

            return false;
        }

        public static SimGameUXCreator uxc;
        public static bool Awake(SimGameUXCreator __instance) {
            if (Scene == null) return true; //Not our scene
            if (uxc == null) {
                Log("Ignoring SimGameUXCreator.Awake");
                uxc = __instance;
                return false;
            } else {
                Log($"Accepting SimGameUXCreator.Awake :frame {Time.frameCount} :time {Time.unscaledTime}");
                return true;
            }
        }

        public static SimGameUXCreator uxcs;
        // This is an enumerator, this wont work.
        // lets just grab the IEnumerator from InitializeUX, force it to fully exhaust and then run the Start() body ourselves.
        public static bool Start(SimGameUXCreator __instance) {
            if (Scene == null) return true; //Not our scene
            if (uxcs == null) {
                Log("Ignoring SimGameUXCreator.Start");
                uxcs = __instance;
                return false;
            } else {
                Log("Accepting SimGameUXCreator.Start");
                return true;
            }
        }

        public static IPromise Scene;
        public static bool _OnBeginAttachUX(SimGameState __instance) {
            if (Scene == null) return true;  //Not our scene
            Log("Attach UX and do *not* load scene");
            __instance.DataManager.Clear(false, false, true, true, false);
            ActiveOrDefaultSettings.CloudSettings.customUnitsAndLances.UnMountMemoryStore(__instance.DataManager);

            Log($"_OnBeginAttachUX at :frame {Time.frameCount} :time {Time.unscaledTime}");
            Scene.Done(() => { Log("Scene is loaded and done uxc? {0}", uxc != null, uxcs != null, Scene != null);
                               SceneManager.SetActiveScene(SceneManager.GetSceneByName("SimGame"));
                               SceneManager.GetAllScenes().ToList().ForEach(scn => Log($"Scene: {scn.name}"));
                               new Traverse(uxc).Method("Awake").GetValue();
                               Scene = null; });
            return false;
        }

        public static void _OnBeginDefsLoad() {
            uxcs = uxc = null;
            var currentScenes = string.Join(" ", SceneManager.GetAllScenes().Select(s => s.name).ToArray());
            Log($"Load defs in parallel for :scene `SimGame` :frame {Time.frameCount} :time {Time.unscaledTime} :currentScenes {currentScenes}");
            // It would be optimal to remove shaders and effects and lower quality options here to ensure we spend no/little time rendering
            if (SceneManager.GetAllScenes().ToList().Exists(s => s.name == "SimGame")) { Log("Sim game exists already - unloading");
                                                                                         Trap(() => SceneManager.UnloadScene("SimGame"));
                                                                                         Log("Sim game unloaded"); }
            var sw = Stopwatch.StartNew();
            // FIXME: Can we use asyncOp.allowSceneActivation
            Scene = Trap(() => SceneManager.LoadSceneAsync("SimGame", LoadSceneMode.Single).AsPromise());
            Scene.Done(() => Log($"Scene `SimGame` loaded :frame {Time.frameCount} :time {Time.unscaledTime}"));
        }
    }
}
