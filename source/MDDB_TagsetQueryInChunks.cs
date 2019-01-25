using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Harmony;
using System.Reflection;
using System.Reflection.Emit;
using BattleTech.Data;
using System.Data;
using static BattletechPerformanceFix.Extensions;

namespace BattletechPerformanceFix
{
    /* Yet another duct tape "fix" to MDDB to keep it running one day longer...
     * MDDB is building massive queries that SQLite can't process.
     * Rather than raising the limits, this makes the queries happen in chunks.
     */
    class MDDB_TagsetQueryInChunks : Feature
    {
        public void Activate()
        {
            var name = nameof(GetMatchingDataByTagSet);
            typeof(TagSetQueryExtensions)
                .GetMethods(AccessTools.all)
                .Where(m => m.Name == name && m.GetParameters().Count() == 8)
                .Single()
                .MakeGenericMethod(typeof(object)).Patch(null, null, name);
        }

        public static IEnumerable<object> InterceptQuery(MetadataDatabase mdd, string sql, object param, IDbTransaction transaction, bool buffered, int? commandTimeout, CommandType? commandType, TagSetType type)
        {
            LogDebug($"Intercepted query wanting {type.ToString()}");

            // This is required to find the real type to send to Query, since Harmony for some reason intercepts all the concrete methods but substitutes their generic type to the patch type.
            var typemap = new Dictionary<TagSetType, Type> { { TagSetType.LanceDef, typeof(LanceDef_MDD)}
                                                           , { TagSetType.PilotDef, typeof(PilotDef_MDD) }
                                                           , { TagSetType.UnitDef, typeof(UnitDef_MDD) } };

            var realType = typemap.GetWithDefault(type, () => throw new Exception("Unknown query type {type.ToString()}"));

            var tagSetsInChunks = new Traverse(param).Property("TagSetID")
                                                     .NullCheckError("Unable to find TagSetID on param")
                                                     .GetValue<string[]>()
                                                     .NullCheckError("TagSetID was not the expected string[] type")
                                                     .GroupsOf(100);
            if (Debug) LogDebug($"Query-of :number-of-tags {tagSetsInChunks.Sum(i => i.Count())} :number-of-chunks {tagSetsInChunks.Count()}");
            if (Spam) LogSpam($"SQL {sql}");

            var results = tagSetsInChunks
                .SelectMany(ts => mdd.Query(realType, sql, new { TagSetID = ts.ToArray() }, null, true, null, null))
                .ToList();

            if (Spam) LogSpam($"Retrieved({realType.Name}) {results.Dump(false)}");
            return results;
        }

        public static IEnumerable<CodeInstruction> GetMatchingDataByTagSet(MethodBase orig, IEnumerable<CodeInstruction> ins)
        {
            return ins.SelectMany(i => {
                    if ((i.operand as MethodInfo)?.Name == "Query") {
                        var ops = Sequence( new CodeInstruction(OpCodes.Ldarg_1)  // put tagSetType on stack
                                          , new CodeInstruction(OpCodes.Call, typeof(MDDB_TagsetQueryInChunks).GetMethod("InterceptQuery", AccessTools.all)));
                        return i.Replace(ops);                  
                    } else {
                        return Sequence(i);
                    }
                });
        }
    }
}
