using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Harmony;

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
            Log("LL start triggered :currentScenes {currentScenes}");
        }

        public static void SimGameUXCreatorLoaded() {
            Log("SimGameUXCreatorLoaded");
        }

        public static bool OnSimGameInitializeComplete(SimGameUXCreator __instance) {
            Log("Sim game initialize complete");

            var initUX = __instance.InitializeUXRoutine(); //. .Concat(new Traverse(__instance).Method("Start").GetValue<IEnumerator>());

            CoroutineInvoker.InvokeCoroutine( __instance.InitializeUXRoutine()
                                            , () => { Log("Initialize coroutine completed");
                                                      Trap(() => new Traverse(__instance).Field("sim").GetValue<SimGameState>().SimGameUXCreatorLoaded());
                                                      Log("UXCreatorLoaded ?"); });

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
                Log("Accepting SimGameUXCreator.Awake");
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

        public static AsyncOperation Scene;
        public static bool _OnBeginAttachUX(SimGameState __instance) {
            if (Scene == null) return true;  //Not our scene
            Log("Attach UX and do *not* load scene");
            __instance.DataManager.Clear(false, false, true, true, false);
            ActiveOrDefaultSettings.CloudSettings.customUnitsAndLances.UnMountMemoryStore(__instance.DataManager);

            CoroutineInvoker.InvokeCoroutine(WaitScene(Scene), () => {
                    if (!Scene.isDone) LogError("Scene is *not ready*");
                    else { Log("Scene is loaded and done uxc? {0}", uxc != null, uxcs != null, Scene != null);
                           SceneManager.SetActiveScene(SceneManager.GetSceneByName("SimGame"));
                           SceneManager.GetAllScenes().ToList().ForEach(scn => Log($"Scene: {scn.name}"));
                           new Traverse(uxc).Method("Awake").GetValue();
                           Scene = null; }
                });
            return false;
        }

        public static void _OnBeginDefsLoad() {
            uxcs = uxc = null;
            var currentScenes = string.Join(" ", SceneManager.GetAllScenes().Select(s => s.name).ToArray());
            Log($"Load defs in parallel to scene load :currentScenes {currentScenes}");
            // It would be optimal to remove shaders and effects and lower quality options here to ensure we spend no/little time rendering
            if (SceneManager.GetAllScenes().ToList().Exists(s => s.name == "SimGame")) { Log("Sim game exists already - unloading");
                                                                                         Trap(() => SceneManager.UnloadScene("SimGame"));
                                                                                         Log("Sim game unloaded"); }
            Scene = Trap(() => SceneManager.LoadSceneAsync("SimGame", LoadSceneMode.Single));
        }

        public static IEnumerator WaitScene(AsyncOperation op) {
            Log("[WaitScene] load scene");
            while(!op.isDone) {
                Log("[WaitScene] Scene not loaded. Waiting");
                yield return null;
            }
            yield return null; // wait for single frame.
            Log("[WaitScene] Done");
        }
    }

    class CoroutineInvoker : MonoBehaviour {
        public IEnumerator Enumerator;
        public Action complete;
        public void Start() {
            Log("Invocation started");
            StartCoroutine(InvokeWithComplete());
            GameObject.DontDestroyOnLoad(this.gameObject);
        }

        public IEnumerator InvokeWithComplete() {
            yield return StartCoroutine(Enumerator);
            complete();
        }


        public static void InvokeCoroutine(IEnumerator enumerator, Action done) {
            var go = new GameObject();
            var invoker = go.AddComponent<CoroutineInvoker>();
            invoker.Enumerator = enumerator;
            invoker.complete = done;
        }

        public static void LoadSceneAsync(string scene, Action done) {

        }
    }
}
