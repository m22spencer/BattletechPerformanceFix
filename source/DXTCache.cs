using System.Collections.Generic;
using System.Linq;
using BattleTech;
using BattleTech.Framework;
using Harmony;
using System;
using System.IO;
using UnityEngine;
using System.Reflection;
using System.Diagnostics;
using BattleTech.Data;
using static System.Reflection.Emit.OpCodes;
using static BattletechPerformanceFix.Extensions;
using Localize;
using BattleTech.Serialization.Handlers;
using BestHTTP.Decompression.Zlib;
using System.Runtime.Remoting.Messaging;

namespace BattletechPerformanceFix
{
	/* Intercept png/jpg image loads and cache as DXT
	 * Relevant methods:
	 * - ImageUtils.LoadSprite
	 * - SpriteLoadRequest.SpriteFromDisk
	 * - SocketIOWePlaySample.OnFrame
	 * - Gaia.Utils.DecompressFromSingleChannelFileImage
	 * - BattleTech.Data.TextureManager.TextureFromBytes (id from BattleTech.Data.TextureManager.ProcessCompletedRequest)
	 * - HoudiniEngineUnity.HEU_MaterialFactory.ExtractHoudiniImageToTexturePNGJPEG
	 * - HoudiniEngineUnity.HEU_GeneralUtility.LoadTextureFromFile
	 */
	class DXTCache : Feature
    {
        public void Activate()
        {
            "SpriteFromDisk".Pre<DataManager.SpriteLoadRequest>();

			Directory.CreateDirectory(Path.Combine("Mods", ".ddscache"));
        }

        public static bool SpriteFromDisk_Pre(DataManager.SpriteLoadRequest __instance, string assetPath, ref Sprite __result)
        {
			var cacheFile = Path.Combine("Mods", ".ddscache", ((uint)assetPath.GetHashCode()).ToString());


			if (!File.Exists(assetPath))
            {
				__result = null;
				return false;
            }
			try
			{
				Texture2D texture2D;
				var sw = Stopwatch.StartNew();
				if (!File.Exists(cacheFile))
				{
					
					byte[] array = File.ReadAllBytes(assetPath);
					var disk_time = sw.Elapsed.TotalMilliseconds;
					if (TextureManager.IsDDS(array))
					{
						sw = Stopwatch.StartNew();
						texture2D = TextureManager.LoadTextureDXT(array);
						var dxt_load = sw.Elapsed.TotalMilliseconds;

						LogDebug($"LoadImage[{texture2D.format.ToString()}] {assetPath} {texture2D.width}x{texture2D.height} :disk {disk_time}ms :load/convert {dxt_load}ms");
					}
					else
					{
						if (!TextureManager.IsPNG(array) && !TextureManager.IsJPG(array))
						{
							DataManager.FileLoadRequest.logger.LogWarning($"Unable to load unknown file type from disk (not DDS, PNG, or JPG) at: {assetPath}");
							__result = null;
							return false;
						}
						texture2D = new Texture2D(2, 2, TextureFormat.DXT5, mipChain: false);
						sw = Stopwatch.StartNew();
						if (!texture2D.LoadImage(array))
						{
							__result = null;
							return false;
						}

						Write(cacheFile, texture2D);
						var ctxf = Read(cacheFile);

						if (!texture2D.GetRawTextureData().SequenceEqual(ctxf.GetRawTextureData()))
                        {
							LogError("Cache file corrupted");
                        }

						var load_image_time = sw.Elapsed.TotalMilliseconds;
						LogDebug($"LoadImage[PNG/JPG] {assetPath} {texture2D.width}x{texture2D.height} :disk {disk_time}ms :load/convert {load_image_time}ms");
					}
				}
				else
				{
					sw = Stopwatch.StartNew();
					texture2D = Read(cacheFile);
					var rcs_time = sw.Elapsed.TotalMilliseconds;
					LogDebug($"LoadImage[CACHE {texture2D.format.ToString()}] {assetPath} {texture2D.width}x{texture2D.height} :load/convert {rcs_time}ms");
				}
				__result = Sprite.Create(texture2D, new Rect(0f, 0f, texture2D.width, texture2D.height), new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect, Vector4.zero);
				return false;
			}
			catch (Exception ex)
			{
				DataManager.FileLoadRequest.logger.LogError($"Unable to load image at: {assetPath}\nExceptionMessage:\n{ex.Message}");
				__result = null;
				return false;
			}
		}


		public static int HEADER_SIZE = 16;

		// TODO: RLE encoding may be worth it for larger textures.
		public static void Write(string file, Texture2D tex)
        {
			using (var ms = new MemoryStream())
			using (var bw = new BinaryWriter(ms))
			{
				bw.Write(tex.width);
				bw.Write(tex.height);
				bw.Write((int)tex.format);

				var texdata = tex.GetRawTextureData();
				bw.Write(texdata.Length);
				Assert.Equals(bw.BaseStream.Position, HEADER_SIZE);
				bw.Write(texdata);

				File.WriteAllBytes(file, ms.ToArray());
			}
        }

		public unsafe static Texture2D Read(string file)
        {
			var buffer = File.ReadAllBytes(file);


			using (var ms = new MemoryStream(buffer))
			using (var br = new BinaryReader(ms))
			{
				var t = new Texture2D(br.ReadInt32(), br.ReadInt32(), (TextureFormat)br.ReadInt32(), false);
				var data_size = br.ReadInt32();

				fixed (byte* ptr = buffer) {
					var tex_data_ptr = ptr + HEADER_SIZE;
					t.LoadRawTextureData((IntPtr)tex_data_ptr, data_size);
                }

				return t;
			}
		}
    }
}
