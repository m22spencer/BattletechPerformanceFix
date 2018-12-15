using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Harmony;
using BattleTech;
using BattleTech.Data;
using BattleTech.Assetbundles;
using static BattletechPerformanceFix.Extensions;

namespace BattletechPerformanceFix
{
    class CollectSingletons : Feature
    {
        public static DataManager DM;
        public static TextureManager TM;
        public static PrefabCache PC;
        public static SpriteCache SC;
        public static SVGCache SVC;
        public static BattleTechResourceLocator RL;
        public static AssetBundleManager BM;

        public static void SetUnityDataManagers_Post(DataManager __instance) {
            DM = __instance;
            var t = new Traverse(__instance);
            
            T f<T>(string name) {
                return Trap(() => t.Property(name).GetValue<T>());
            }

            TM = f<TextureManager>("TextureManager");
            TM = f<TextureManager>("TextureManager");
            PC = f<PrefabCache>("GameObjectPool");
            SC = f<SpriteCache>("SpriteCache");
            SVC = f<SVGCache>("SVGCache");
            RL = DM.ResourceLocator;
            BM = f<AssetBundleManager>("AssetBundleManager");
        }

        public void Activate() {
            "SetUnityDataManagers".Post<DataManager>();
        }
    }
}
