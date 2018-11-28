using Harmony;
using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using BattleTech.UI;
using static BattletechPerformanceFix.Control;

namespace BattletechPerformanceFix
{
    public class LazyRoomInitialization : Feature
    {
        public void Activate()
        {
            Log("LazyRoomInitialization is activated");
            var specnames = new List<string> { "LeaveRoom", "InitWidgets" };
            Assembly
                .GetAssembly(typeof(SGRoomControllerBase))
                .GetTypes()
                .Where(type => type.IsSubclassOf(typeof(SGRoomControllerBase)))
                .Where(type => type != typeof(SGRoomController_Ship))
                .ToList()
                .ForEach(room =>
                {
                    var meths = AccessTools.GetDeclaredMethods(room);
                    foreach (MethodBase meth in meths)
                    {
                        try
                        {
                            var sn = specnames.Where(x => meth.Name == x).ToList();
                            var patchfun = sn.Any() ? sn[0] : "Other";
                            if (patchfun != null)
                            {
                                Control.Log("LazyRoomInitialization methname {0}, patchfun {1}", meth.Name, patchfun);
                                Control.harmony.Patch(meth, new HarmonyMethod(typeof(LazyRoomInitialization), patchfun), null);
                            }
                        }
                        catch (Exception e)
                        {
                            Log("Exception {0}", e);
                        }
                    }
                });
        }

        public static Dictionary<SGRoomControllerBase, bool> DB = new Dictionary<SGRoomControllerBase, bool>();
        public static bool allowInit = false;
        public static bool InitWidgets(SGRoomControllerBase __instance)
        {
            return Control.Trap(() =>
            {
                Control.Log("SGRoomControllerBase.InitWidgets (want initialize? {0})", allowInit);
                if (!allowInit)
                {
                    DB[__instance] = false;
                    return false;
                }
                DB[__instance] = true;
                return true;
            });
        }

        public static bool LeaveRoom(bool ___roomActive)
        {
            return Control.Trap(() =>
            {
                Control.Log("SGRoomControllerBase_LeaveRoom");
                if (___roomActive)
                    return true;
                return false;
            });
        }
        public static void Other(SGRoomControllerBase __instance, MethodBase __originalMethod)
        {
            Control.Trap(() =>
            {
                Control.Log("SGRoomControllerBase_Other {0}", __originalMethod.Name);

                if (DB[__instance] == false)
                {
                    Control.Log("Initialize Widgets");
                    allowInit = true;
                    new Traverse(__instance).Method(nameof(SGRoomControllerBase.InitWidgets)).GetValue();
                    allowInit = false;
                }
            });
        }
    }
}