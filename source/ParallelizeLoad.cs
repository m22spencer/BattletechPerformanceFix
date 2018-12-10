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
            Log("Attach UX and do *not* load scene");
            __instance.DataManager.Clear(false, false, true, true, false);
            ActiveOrDefaultSettings.CloudSettings.customUnitsAndLances.UnMountMemoryStore(__instance.DataManager);
            if (!Scene.isDone) LogError("Scene is *not ready*");
            else { Log("Scene is loaded and done uxc? {0}", uxc != null);
                   SceneManager.SetActiveScene(SceneManager.GetSceneByName("SimGame"));
                   SceneManager.GetAllScenes().ToList().ForEach(scn => Log($"Scene: {scn.name}"));
                   new Traverse(uxc).Method("Awake").GetValue(); }
                   //new Traverse(uxc).Method("Start").GetValue(); }
            return false;
        }

        public static void _OnBeginDefsLoad() {
            Log("Load defs in parallel to scene load");
            // It would be optimal to remove shaders and effects and lower quality options here to ensure we spend no/little time rendering
            Scene = SceneManager.LoadSceneAsync("SimGame", LoadSceneMode.Single);
        }
    }

    class CoroutineInvoker : MonoBehaviour {
        public IEnumerator Enumerator;
        public Action complete;
        public void Start() {
            Log("Invocation started");
            StartCoroutine(InvokeWithComplete());
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
    }
}
