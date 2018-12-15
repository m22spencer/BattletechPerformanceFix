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

using BattleTech.Rendering.MechCustomization;

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

            Log("ReceiveButtonPress");
            AccessTools.Method(typeof(SGNegotiationWidget), "ReceiveButtonPress").Track();

            // We want to request the map here, but this makes DataManager just stop loading because "reasons"
            Main.harmony.Patch( AccessTools.Method(typeof(LanceConfiguratorPanel), "ContinueConfirmClicked")
                              , new HarmonyMethod(AccessTools.Method(self, nameof(LanceConfiguratorPanel_SetData))));

            /*
            Main.harmony.Patch( AccessTools.Method(typeof(Contract)
                                                  , "BeginRequestResources")
                                                  //, "RequestResourcesComplete")
                              , new HarmonyMethod(AccessTools.Method(self, nameof(LanceConfiguratorPanel_SetData))));
                              */


            /*
            AccessTools.Method(typeof(SimGameState), "StartContract").Track();
            AccessTools.Method(typeof(Contract), "Begin").Track();
            AccessTools.Method(typeof(Contract), "BeginRequestResources").Track();
            AccessTools.Method(typeof(Contract), "RequestResourcesComplete").Track();
            AccessTools.Method(typeof(LevelLoadRequestListener), "Start").Track();
            AccessTools.Method(typeof(LevelLoadRequestListener), "OnRequestLevelLoad").Track();
            AccessTools.Method(typeof(DataManager), "Clear").Track();
            */

            "Load".Pre<LoadTransitioning>();
        }

        public static bool FIRST = true;
        public static void Load_Pre(BattleTech.Save.SaveGameStructure.SlotModel save) {
            LogDebug($"Load_Pre");
            if (FIRST) { FIRST = false; return; }
            if (!save.HasCombatData) {
                Log("SimGame IMMEDIATE");
                if (Scene != null) LogError("An early scene load was triggered, but a scene is already buffered");
                Log($"Early scene load trigger for SimGame");
                if (SceneManager.GetActiveScene().name == "SimGame") SceneManager.UnloadScene("SimGame");   //FIXME: Won't work as there is no existing scene to fallback to. Probably keep Empty around all the time and just switch to it.
                Scene = "SimGame".LoadSceneAsync();
            }
        }

        // I'd like to do this at SetData, but we need to be able to cancel the map load request to do that.
        public static void LanceConfiguratorPanel_SetData(Contract ___activeContract) {
            Log($"LanceConfiguratorPanel: checking if we can early load map");
            var contract = ___activeContract;
            if (contract != null) {
                if (Scene != null) LogError("A scene load was in progress, not good");
                //FIXME: This needs handling for bundle based maps
                Log($"Background loading {contract.mapName}");
                var entry = OverridePrefabCache.lookupId(contract.mapName);
                Log($"Map needs bundle? {(entry == null ? "no" : "yes")}");
                Scene = Trap(() => contract.mapName.LoadSceneAsync());
            }
        }

        public static void HandleScene() {
            return;

            Scene().Done(scn => { Log($"Activating scene {scn.name}");
                                  SceneManager.SetActiveScene(scn);
                                  Scene = null; // This may need to apply next frame to prevent LL.Start from handling our custom load
                                });

        }

        public static void Virtual_Briefing() {
            Log($"Virtual_Briefing override");
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
            }

            game.MessageCenter.AddSubscriber(MessageCenterMessageType.OnInitializeContractComplete, InitContractComplete);
            game.MessageCenter.PublishMessage(new InitializeContractMessage(game.Combat));
        }

        // General flow:  Loads load scene, which triggers load from LevelLoader object, which then(optionally) loads an interstitial, which contains an Awake which continues game process.
        public static bool LoadScene(LevelLoader __instance, string scene, string loadingInterstitialScene, ref string ___loadTarget, Action ___interstitialComplete, LevelLoader.LoadState ___loaderState) {
            Log($"LL.LoadScene intercepted for :scene {scene} :interstitialScene {loadingInterstitialScene} :active {SceneManager.GetActiveScene().name} from {new StackTrace().ToString()}");

            ___loadTarget = scene;

            ___loaderState = LevelLoader.LoadState.InterstitialActive;

            Log($"LL.LoadScene: Load new scene async :active {SceneManager.GetActiveScene().name}");

            if (Scene != null) Log($"LL.LoadScene: A scene is currently being background loaded and will now be allowed to initialize");
            Scene = Scene ?? scene.LoadSceneAsync();  //Either use an existing in progress scene, or start the load now;

            Scene().Done(scn => { Scene = null;
                                  SceneManager.SetActiveScene(scn);
                                  Log($"Activate scene {scn.name}");
                                  UnityGameInstance.BattleTechGame.MessageCenter.PublishMessage(new LevelLoadCompleteMessage(scene, loadingInterstitialScene));
                                  ___loaderState = LevelLoader.LoadState.Loaded;
                                  if (___interstitialComplete != null) { Log($"Informing of load completion");
                                                                         ___interstitialComplete();
                                                                         //___interstitialComplete = null; // FIXME
                                  }
                                  if (loadingInterstitialScene == "Interstitial_Briefing") Virtual_Briefing();

                                  // Wait 4 frames because meh.
                                  WaitAFrame().Done(() => { Log($"Hide loading curtain.. hopefully?");
                                                            LoadingCurtain.Hide(); });
                                });



            return false;        }

        public static bool LevelLoader_Start() {
            Log($"LL.Start discarded");
            return false;
        }

        public static Func<IPromise<Scene>> Scene;

        //FIXME: Look for an earlier trigger
        public static void _OnBeginDefsLoad() {
            if (Scene != null) LogError("An early scene load was triggered, but a scene is already buffered");
            Log($"Early scene load trigger for SimGame");
            if (SceneManager.GetActiveScene().name == "SimGame") SceneManager.UnloadScene("SimGame");   //FIXME: Won't work as there is no existing scene to fallback to. Probably keep Empty around all the time and just switch to it.
            Scene = "SimGame".LoadSceneAsync();
        }
    }
}
