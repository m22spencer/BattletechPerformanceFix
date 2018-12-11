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
using BattleTech.Data;
using BattleTech.UI;
using BattleTech.Save;
using System.Reflection;
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

            // For some reason, triggering a scene load before _OnBeginDefsLoad is invoked causes the UI to never initiate. Postfix to avoid this.
            Main.harmony.Patch(AccessTools.Method(sgs, nameof(_OnBeginDefsLoad)), null, new HarmonyMethod(AccessTools.Method(self, nameof(_OnBeginDefsLoad))));
          
            Main.harmony.Patch(AccessTools.Method(typeof(BattleTech.LevelLoader), nameof(LoadScene)), new HarmonyMethod(AccessTools.Method(self, nameof(LoadScene))));
            Main.harmony.Patch(AccessTools.Method(typeof(BattleTech.LevelLoader), "Start"), new HarmonyMethod(AccessTools.Method(self, nameof(LevelLoader_Start))));

            Main.harmony.Patch(AccessTools.Method(typeof(MissionResults), "Init"), new HarmonyMethod(AccessTools.Method(self, nameof(_OnBeginDefsLoad))));


            var watchMeths = List("Awake", "PooledInstantiate"); //, "Start");

            var allmeths = List(typeof(SimGameUXCreator))
                .Select(Assembly.GetAssembly)
                .SelectMany(asm => asm.GetTypes())
                .Where(type => !type.IsGenericType && !type.IsGenericTypeDefinition && !type.FullName.Contains("HBS.Stopwatch"))
                .SelectMany(types => types.GetMethods(AccessTools.all))
                .Where(meth => watchMeths.Contains(meth.Name) && !meth.IsGenericMethodDefinition && !meth.IsGenericMethod)
                .ToList();
            allmeths.ForEach(meth => meth.Instrument());

            var resl = typeof(Resources).GetMethods(AccessTools.all)
                             .Where(meth => meth.Name == "Load" && !meth.IsGenericMethod && !meth.IsGenericMethodDefinition && meth.GetMethodBody() != null)
                             .SingleOrDefault();

            if (resl == null)
                LogError("RESU null");
            resl.Instrument();
            
            var resu = typeof(BattleTech.Assetbundles.AssetBundleManager).GetMethods(AccessTools.all)
                                                                         .Where(meth => meth.Name == "GetAssetFromBundle")
                                                                         .SingleOrDefault();

            if (resu == null)
                LogError("RESU null");
            resu.MakeGenericMethod(typeof(GameObject)).Instrument();

            //Main.harmony.Patch(AccessTools.Method(typeof(PrefabCache), "Clear"), new HarmonyMethod(AccessTools.Method(self, "Clear")));

            Main.harmony.Patch(AccessTools.Method(typeof(BattleTech.UI.SimGameOptionsMenu), "OnAddedToHierarchy"), new HarmonyMethod(AccessTools.Method(self, "Summary")));
            Main.harmony.Patch(AccessTools.Method(typeof(SGLoadSavedGameScreen), "LoadSelectedSlot"), new HarmonyMethod(AccessTools.Method(self, "Summary")));
        }

        public static void HandleScene() {
            Log($"Handling intercepted scene {SceneName}");
            SceneOp.allowSceneActivation = true;
            Scene.Done(() => { Log($"Activating scene {SceneName}");
                               SceneManager.SetActiveScene(SceneManager.GetSceneByName(SceneName));
                               Scene = null; // This may need to apply next frame to prevent LL.Start from handling our custom load
                             });

        }

        public static bool LoadScene(string scene) {
            if (Scene != null) {
                Log($"LL.LoadScene intercepted for :scene {scene}");
                HandleScene();
                return false;
            }
            var currentScenes = string.Join(" ", SceneManager.GetAllScenes().Select(s => s.name).ToArray());
            Log($"LL.LoadScene {scene} :currentScenes {currentScenes} from {new StackTrace().ToString()}");
            return true;
        }

        public static bool LevelLoader_Start() {
            if (Scene != null) {
                Log($"LL.Start intercepted");
                Scene = null;
                return false;
            }

            var currentScenes = string.Join(" ", SceneManager.GetAllScenes().Select(s => s.name).ToArray());
            Log($"LL.Start triggered :currentScenes {currentScenes}");
            return true;
        }

        public static string SceneName;
        public static IPromise Scene;
        public static AsyncOperation SceneOp;
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
                    SceneName = "SimGame";
                    Scene = op.AsPromise();
                    Scene.Done(() => Log($"Scene `SimGame` loaded :frame {Time.frameCount} :time {Time.unscaledTime}"));
                });
        }
    }
}
