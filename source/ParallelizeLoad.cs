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
using BattleTech.Rendering.UI;
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
            return;
            Log($"Handling intercepted scene {SceneName}");

            Scene().Done(scn => { Log($"Activating scene {scn.name}");
                                  SceneManager.SetActiveScene(scn);
                                  Scene = null; // This may need to apply next frame to prevent LL.Start from handling our custom load
                                });

        }

        public static void Virtual_Briefing() {
            var game = HBS.LazySingletonBehavior<UnityGameInstance>.Instance.Game;

            void InitContractComplete(MessageCenterMessage message) {
                Log($"Virtual_Briefing: Contract init complete");
                game.MessageCenter.RemoveSubscriber(MessageCenterMessageType.OnInitializeContractComplete, InitContractComplete);
                Log($"Virtual_Briefing: Begin playing");
                game.MessageCenter.PublishMessage(new EncounterBeginMessage());

                Log($"Doing a few other things from the interstitial");
                CombatGameState combat = game.Combat;
                if (combat != null && combat.ActiveContract != null)
                {
                    combat.ActiveContract.StartProgress();
                }
                if (UICameraRenderer.HasInstance)
                {
                    UICameraRenderer.Instance.EnableRenderTexture();
                }
                if (CameraFadeManager.HasInstance)
                {
                    CameraFadeManager.Instance.RefreshCamera();
                }
                if (combat != null && combat.WasFromSave)
                {
                    combat.SetAudioFromSaveState();
                }
                Log($"Done");
                LoadingCurtain.Hide();
                Log($"Hid loading curtain");
            }

            game.MessageCenter.AddSubscriber(MessageCenterMessageType.OnInitializeContractComplete, InitContractComplete);
            game.MessageCenter.PublishMessage(new InitializeContractMessage(game.Combat));
        }

        public static bool LoadScene(LevelLoader __instance, string scene, string loadingInterstitialScene, ref string ___loadTarget, Action ___interstitialComplete, LevelLoader.LoadState ___loaderState) {
            Log($"LL.LoadScene intercepted for :scene {scene} :interstitialScene {loadingInterstitialScene} :active {SceneManager.GetActiveScene().name} from {new StackTrace().ToString()}");

            ___loadTarget = scene;


            /*
            var active = SceneManager.GetActiveScene();
            Log($"LL.LoadScene: Load Empty over active {active.name} :active {SceneManager.GetActiveScene().name}");
            SceneManager.LoadScene("Empty");
            Log($"LL.LoadScene: Set Empty over active {active.name} :active {SceneManager.GetActiveScene().name}");
            Log($"GSE? {SceneManager.GetSceneByName("Empty")}");
            Trap(() => SceneManager.SetActiveScene(SceneManager.GetSceneByName("Empty")));
            Log($"LL.LoadScene: Unloading {active.name} :active {SceneManager.GetActiveScene().name}");
            SceneManager.UnloadScene(active);
            */

            ___loaderState = LevelLoader.LoadState.InterstitialActive;
            // if (loadingInterstitialScene != null)
            //     loadingInterstitialScene.LoadSceneAsync(LoadSceneMode.Additive)();

            Log($"LL.LoadScene: Load new scene async :active {SceneManager.GetActiveScene().name}");
            scene.LoadSceneAsync()().Done(scn => { SceneManager.SetActiveScene(scn);
                                                   Log($"Activate scene {scn.name}");
                                                   UnityGameInstance.BattleTechGame.MessageCenter.PublishMessage(new LevelLoadCompleteMessage(scene, loadingInterstitialScene));
                                                   ___loaderState = LevelLoader.LoadState.Loaded;
                                                   if (___interstitialComplete != null) { Log($"Informing of load completion");
                                                                                          ___interstitialComplete();
                                                                                          //___interstitialComplete = null; // FIXME
                                                   }
                                                   Virtual_Briefing();
                                                 });



            return false;
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
            Log($"LL.Start discarded");
            return false;
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
        public static Func<IPromise<Scene>> Scene;
        public static void _OnBeginDefsLoad() {
            return;
            var currentScenes = string.Join(" ", SceneManager.GetAllScenes().Select(s => s.name).ToArray());
            Log($"Load defs in parallel for :scene `SimGame` :frame {Time.frameCount} :time {Time.unscaledTime} :currentScenes {currentScenes} :from \r\n{new StackTrace().ToString()}");

            if (SceneManager.GetAllScenes().ToList().Exists(s => s.name == "SimGame")) { Log("Sim game exists already - unloading");
                                                                                         Trap(() => SceneManager.UnloadScene("SimGame"));
                                                                                         Log("Sim game unloaded"); }
            var sw = Stopwatch.StartNew();
            // FIXME: Can we use asyncOp.allowSceneActivation

            Trap(() => {
                    // It would be optimal to remove shaders and effects and lower quality options here to ensure we spend no/little time rendering
                    Scene = "SimGame".LoadSceneAsync();
                    SceneName = "SimGame";
                });
        }
    }
}
