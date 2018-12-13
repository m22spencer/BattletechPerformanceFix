using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RSG;
using BattleTech;
using UnityEngine;
using static BattletechPerformanceFix.Extensions;

namespace BattletechPerformanceFix.AlternativeLoading
{
    delegate void AcceptReject<T>(Action<T> accept, Action<Exception> reject);
    class Load
    {
        public static IPromise<T> MapSync<T,R>( VersionManifestEntry entry
                                              , Func<byte[],T> byFile
                                              , Func<R,T> byResource 
                                              , Func<R,T> byBundle
                                              , AcceptReject<T> recover = null) where R : UnityEngine.Object {
            try { if (entry == null) throw new Exception("MapSync: null entry");
                  if (entry.IsAssetBundled && byBundle != null) return LoadAssetFromBundle<R>(entry.Id, entry.AssetBundleName).Then(byBundle);
                  if (entry.IsResourcesAsset && byResource != null) return Promise<T>.Resolved(byResource(Resources.Load<R>(entry.ResourcesLoadPath)));
                  if (entry.IsFileAsset && byFile != null) return Promise<T>.Resolved(byFile(File.ReadAllBytes(entry.FilePath))); }
            catch(Exception e) { return Promise<T>.Rejected(e); }
            return Promise<T>.Rejected(new Exception($"Missing method to load {entry.Id}"));
        }


        public static IPromise<T> LoadAssetFromBundle<T>(string id, string bundleName) {
            return Promise<T>.Rejected(new Exception("NYI: LoadAssetFromBundle"));
        }
    }
}
