using HBS.Logging;
using Harmony;
using BattleTech;
using BattleTech.UI;
using BattleTech.Data;
using System.Collections.Generic;
using System.Linq;
using System;

namespace BattletechPerformanceFix {
	public static class Deps {
	}

	[HarmonyPatch(typeof(DataManager), "NotifyFileLoadFailed")]
	public static class Patch_AlertWhenFailedFile {
		public static void Prefix(DataManager.DataManagerLoadRequest request) {
			Control.mod.Logger.LogError(string.Format("PANIC: NotifyLoadFileFailed(this breaks CDAL hack): {0} of {1}", request.ResourceId, request.ResourceType));
		}
	}

	public static class Patch_DependencySpam {
		public static void Initialize() {
			var prefix  = AccessTools.Method(typeof(Patch_MechDef_CheckLessOften_Please), "Prefix");
			var postfix = AccessTools.Method(typeof(Patch_MechDef_CheckLessOften_Please), "Postfix");

			var topatch = new Type[] { typeof(HeraldryDef)
			                         , typeof(BaseComponentRef)
									 , typeof(PilotDef)

									 , typeof(TurretDef)
									 , typeof(VehicleDef)
									 , typeof(DialogBucketDef)
									 , typeof(FactionDef)
									 , typeof(MapMetaData)
									 , typeof(BackgroundDef)

									 , typeof(WeaponDef)

									 , typeof(VehicleChassisDef)
									 , typeof(TurretChassisDef)

			                         , typeof(MechDef)
									 //, typeof(MechComponentDef)   // causes infinite load
									 
									 , typeof(DesignMaskDef)
									 , typeof(ChassisDef)
									 , typeof(AbilityDef)
			                         };

			foreach (Type t in topatch) {
				Control.harmony.Patch( AccessTools.Method(t, "CheckDependenciesAfterLoad")
									, new HarmonyMethod(prefix)
									, new HarmonyMethod(postfix));
			}						
		}
	}

	// Try and prevent battletech spamming dependency checks every single time anything loads.
	//[HarmonyPatch(typeof(MechDef), "CheckDependenciesAfterLoad")]
	public static class Patch_MechDef_CheckLessOften_Please {
		static Dictionary<DataManager, Dictionary<DataManager.ILoadDependencies,string>> D = new Dictionary<DataManager, Dictionary<DataManager.ILoadDependencies,string>>();
		static bool WantUnfreeze = false;
		public static bool Prefix(DataManager.ILoadDependencies __instance, DataManager ___dataManager, MessageCenterMessage message) {
			string mdwait;

			if (message is DataManagerLoadCompleteMessage)
			{
				// If a resource is somehow skipped, try to load the mech anyways
				return true;
			}

			// Very quick hack just in case there are multiple dataManagers
		    if (!D.ContainsKey(___dataManager)) {
				D[___dataManager] = new Dictionary<DataManager.ILoadDependencies,string>();
			}

			var Dc = D[___dataManager];
			if (Dc.TryGetValue(__instance, out mdwait)) {
				if (message is DataManagerRequestCompleteMessage) {
					var rcm = message as DataManagerRequestCompleteMessage;
					if (rcm.ResourceId == mdwait) {
						// Resources have loaded past what mech asked for.
						Control.mod.Logger.Log(string.Format("Loaded {0}", "?"));
						WantUnfreeze = true;
						return true;
					}
				} 
				return false;
			} else {
				// This mech hasn't been seen yet, let it request resources.
				Control.mod.Logger.Log(string.Format("ResolveDeps {0}", "?"));
				return true;
			}
		}
		
		public static void Postfix(DataManager.ILoadDependencies __instance, DataManager ___dataManager) {
			string tmp;

			if (!D.ContainsKey(___dataManager)) {
				D[___dataManager] = new Dictionary<DataManager.ILoadDependencies,string>();
			}

			var Dc = D[___dataManager];

			if (!Dc.TryGetValue(__instance, out tmp)) {
				//
				var l =	(List<DataManager.DataManagerLoadRequest>)Traverse.Create(___dataManager).Field("foregroundRequests").GetValue(); 
				var waitUntil = l.Last().ResourceId;
				Control.mod.Logger.Log(string.Format("Freeze {0} until {1}", "?", waitUntil));
				Dc[__instance] = waitUntil;
			}

			if (WantUnfreeze) {
				WantUnfreeze = false;
				Dc.Remove(__instance);
				Control.mod.Logger.Log(string.Format("Unfreeze {0}", "?"));
			}
		}
	}
}