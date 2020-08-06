using System.Collections.Generic;
using System.Linq;
using BattleTech;
using BattleTech.Framework;
using Harmony;
using System.Reflection;
using System.Diagnostics;
using static System.Reflection.Emit.OpCodes;
using static BattletechPerformanceFix.Extensions;
using System.Reflection.Emit;
using BestHTTP.ServerSentEvents;
using BattleTech.Data;
using System;
using System.Security.Permissions;

namespace BattletechPerformanceFix
{
    class VersionManifestPatches : Feature
    {
        public void Activate()
        {
            typeof(VersionManifestBase)
                .GetMethods(AccessTools.all)
                .First(m => m.Name == "Contains" && m.GetParameters().Length == 2)
                .Patch("Contains_Pre");

            FieldInfo version_fld = typeof(List<VersionManifestEntry>).GetField("_version", AccessTools.all);
            var d = new DynamicMethod("GetVersion", typeof(int), Array(typeof(List<VersionManifestEntry>)), typeof(List<VersionManifestEntry>));
            var il = d.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, version_fld);
            il.Emit(OpCodes.Ret);

            get_version = d.CreateDelegate(typeof(GetVersion)) as GetVersion;
        }

        public delegate int GetVersion(List<VersionManifestEntry> list);

        public static GetVersion get_version;

        static Dictionary<VersionManifestBase, (int ver, HashSet<(string, string)> cache)> ManifestCache = new Dictionary<VersionManifestBase, (int ver, HashSet<(string, string)> cache)>();

        public static bool Contains_Pre(VersionManifestBase __instance, string id, string type, List<VersionManifestEntry> ___entries, ref bool __result)
        {
            var version = get_version(___entries);
            if (ManifestCache.TryGetValue(__instance, out var state) && state.ver == version)
            {
                __result = state.cache.Contains((id, type));
            } else
            {
                var newcache = ___entries.Select(e => (e.Id, e.Type)).ToHashSet();
                ManifestCache[__instance] = (version, newcache);
                __result = newcache.Contains((id, type));
            }

            return false;
        }
    }
}