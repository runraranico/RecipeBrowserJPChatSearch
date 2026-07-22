using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;

namespace RecipeBrowserJPChatSearch
{
	internal enum MagicStorageUiKind
	{
		None,
		Storage,
		Crafting
	}

	/// <summary>
	/// On-demand Crafting Access discovery / open for RB→MS search when no MS UI is open.
	/// Reflection is cached; tile/TE scan runs only when the search hotkey fires.
	/// </summary>
	internal static class MagicStorageCraftingAccessHelper
	{
		private const int PendingSearchMaxFrames = 5;

		private static bool _resolved;
		private static bool _failed;
		private static Type _teCraftingAccessType;
		private static MethodInfo _openStorageMethod;

		private static Item _pendingItem;
		private static Recipe _pendingPreferredRecipe;
		private static int _pendingFramesLeft;
		private static Point16 _pendingTile = Point16.NegativeOne;

		internal static MagicStorageUiKind GetCurrentUiKind()
		{
			if (!ModLoader.HasMod("MagicStorage") || !MagicStorageSearchHelper.EnsureReflectionPublic())
				return MagicStorageUiKind.None;

			try
			{
				if (MagicStorageSearchHelper.IsCraftingUiOpen())
					return MagicStorageUiKind.Crafting;
				if (MagicStorageSearchHelper.IsStorageUiOpen())
					return MagicStorageUiKind.Storage;
			}
			catch (Exception ex)
			{
				RbjDiag.Error("GetCurrentUiKind failed", ex);
			}

			return MagicStorageUiKind.None;
		}

		internal static string UiKindLabel(MagicStorageUiKind kind) => kind switch
		{
			MagicStorageUiKind.Storage => "StorageAccess",
			MagicStorageUiKind.Crafting => "CraftingAccess",
			_ => "None"
		};

		internal static bool TryOpenNearestCraftingAccessAndSetSearch(
			Item item,
			out string failReason)
			=> TryOpenNearestCraftingAccessAndSetSearch(item, preferredRecipe: null, out failReason);

		/// <summary>
		/// Finds reachable Crafting Access TEs, opens the nearest via Magic Storage's OpenStorage path,
		/// then sets search (or arms a short pending apply) and preferred-recipe select.
		/// </summary>
		internal static bool TryOpenNearestCraftingAccessAndSetSearch(
			Item item,
			Recipe preferredRecipe,
			out string failReason)
		{
			failReason = string.Empty;
			ClearPending();

			if (item == null || item.IsAir)
			{
				failReason = "item-air";
				return false;
			}

			if (!ModLoader.HasMod("MagicStorage") || !EnsureReflection())
			{
				failReason = "reflect-fail";
				return false;
			}

			Player player = Main.LocalPlayer;
			if (player == null || !player.active)
			{
				failReason = "no-player";
				return false;
			}

			if (!TryFindNearestReachableCraftingAccess(player, out Point16 selected, out int candidateCount))
			{
				failReason = candidateCount == 0 ? "no-candidates" : "none-reachable";
				RbjDiag.Info(
					$"RB→MS CraftingAccess open miss | CandidateCount={candidateCount} " +
					$"Selected=none reason={failReason}");
				return false;
			}

			RbjDiag.Info(
				$"RB→MS CraftingAccess Selected=({selected.X},{selected.Y}) CandidateCount={candidateCount}");

			if (!TryOpenCraftingAccessAt(player, selected))
			{
				failReason = "open-failed";
				RbjDiag.Info($"OpenCraftingAccess=false tile=({selected.X},{selected.Y})");
				return false;
			}

			RbjDiag.Info($"OpenCraftingAccess=true tile=({selected.X},{selected.Y})");

			if (MagicStorageSearchHelper.IsCraftingUiOpen()
				&& MagicStorageSearchHelper.TrySetSearchFromItemAndSelectCraftRecipe(item, preferredRecipe))
			{
				RbjDiag.Info("SetSearchSuccess=true (immediate after open + craft select armed)");
				return true;
			}

			_pendingItem = item.Clone();
			_pendingPreferredRecipe = preferredRecipe;
			_pendingFramesLeft = PendingSearchMaxFrames;
			_pendingTile = selected;
			RbjDiag.Info(
				$"SetSearch deferred pendingFrames={PendingSearchMaxFrames} " +
				$"tile=({selected.X},{selected.Y}) type={item.type} preferred={(preferredRecipe != null)}");
			return true;
		}

		/// <summary>Cheap: only runs while a pending post-open search is armed.</summary>
		internal static void TickPendingSearch()
		{
			if (_pendingItem == null || _pendingFramesLeft <= 0)
			{
				if (_pendingItem != null)
				{
					RbjDiag.Info("SetSearchSuccess=false reason=pending-timeout");
					ClearPending();
				}

				return;
			}

			_pendingFramesLeft--;

			if (!MagicStorageSearchHelper.IsCraftingUiOpen())
			{
				if (_pendingFramesLeft <= 0)
				{
					RbjDiag.Info("SetSearchSuccess=false reason=pending-timeout-ui-closed");
					ClearPending();
				}

				return;
			}

			bool ok = MagicStorageSearchHelper.TrySetSearchFromItemAndSelectCraftRecipe(
				_pendingItem,
				_pendingPreferredRecipe);
			RbjDiag.Info(
				$"SetSearchSuccess={ok} (pending+select) type={_pendingItem.type} " +
				$"preferred={(_pendingPreferredRecipe != null)} " +
				$"tile=({_pendingTile.X},{_pendingTile.Y})");
			ClearPending();
		}

		internal static void ClearPending()
		{
			_pendingItem = null;
			_pendingPreferredRecipe = null;
			_pendingFramesLeft = 0;
			_pendingTile = Point16.NegativeOne;
		}

		private static bool TryFindNearestReachableCraftingAccess(
			Player player,
			out Point16 selected,
			out int candidateCount)
		{
			selected = Point16.NegativeOne;
			candidateCount = 0;
			if (!EnsureReflection() || _teCraftingAccessType == null)
				return false;

			Point16 best = Point16.NegativeOne;
			float bestDistSq = float.MaxValue;
			Vector2 playerCenter = player.Center;
			int listed = 0;

			foreach (KeyValuePair<int, TileEntity> pair in TileEntity.ByID)
			{
				TileEntity te = pair.Value;
				if (te == null || !_teCraftingAccessType.IsInstanceOfType(te))
					continue;

				Point16 pos = te.Position;
				if (!WorldGen.InWorld(pos.X, pos.Y))
					continue;

				candidateCount++;
				bool reachable = IsReachableLikeRightClick(player, pos.X, pos.Y);
				float distSq = Vector2.DistanceSquared(playerCenter, pos.ToWorldCoordinates(8f, 8f));

				if (RbjDiag.Enabled && listed < 8)
				{
					listed++;
					int chebyshev = Math.Max(
						Math.Abs((int)(playerCenter.X / 16f) - pos.X),
						Math.Abs((int)(playerCenter.Y / 16f) - pos.Y));
					RbjDiag.Info(
						$"Candidate tile=({pos.X},{pos.Y}) Reachable={reachable} " +
						$"tileDistChebyshev={chebyshev} distSq={distSq:0}");
				}

				if (!reachable)
					continue;

				if (distSq < bestDistSq)
				{
					bestDistSq = distSq;
					best = pos;
				}
			}

			if (best.X < 0)
				return false;

			selected = best;
			return true;
		}

		/// <summary>
		/// Same idea as vanilla tile interaction + Magic Storage's keep-open range
		/// (<see cref="Player.lastTileRangeX"/> / Y includes Journey / reach accessories).
		/// </summary>
		private static bool IsReachableLikeRightClick(Player player, int tileX, int tileY)
		{
			try
			{
				if (!player.IsInTileInteractionRange(tileX, tileY, Terraria.DataStructures.TileReachCheckSettings.Simple))
					return false;
			}
			catch
			{
				// Older/newer tML signature drift — fall through to lastTileRange only.
			}

			// Magic Storage closes when outside lastTileRange; mirror that so we never open
			// something that would immediately close (image 2 / out of reach).
			int playerX = (int)(player.Center.X / 16f);
			int playerY = (int)(player.Center.Y / 16f);
			if (playerX < tileX - player.lastTileRangeX
				|| playerX > tileX + player.lastTileRangeX + 1
				|| playerY < tileY - player.lastTileRangeY
				|| playerY > tileY + player.lastTileRangeY + 1)
				return false;

			return true;
		}

		private static bool TryOpenCraftingAccessAt(Player player, Point16 tile)
		{
			if (_openStorageMethod == null || player == null)
				return false;

			try
			{
				// MagicStorage.Components.StorageAccess.OpenStorage(Player, int, int, bool remoteCrafting = false)
				_openStorageMethod.Invoke(null, new object[] { player, (int)tile.X, (int)tile.Y, false });
				return MagicStorageSearchHelper.IsCraftingUiOpen()
					|| MagicStorageSearchHelper.IsAnyTargetUiOpen();
			}
			catch (Exception ex)
			{
				RbjDiag.Error("TryOpenCraftingAccessAt failed", ex);
				return false;
			}
		}

		private static bool EnsureReflection()
		{
			if (_resolved)
				return !_failed;

			_resolved = true;
			try
			{
				if (!ModLoader.TryGetMod("MagicStorage", out Mod magicStorage))
				{
					_failed = true;
					return false;
				}

				Assembly asm = magicStorage.Code;
				_teCraftingAccessType = asm.GetType("MagicStorage.Components.TECraftingAccess");
				Type storageAccessType = asm.GetType("MagicStorage.Components.StorageAccess");
				_openStorageMethod = storageAccessType?.GetMethod(
					"OpenStorage",
					BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
					null,
					new[] { typeof(Player), typeof(int), typeof(int), typeof(bool) },
					null);

				_failed = _teCraftingAccessType == null || _openStorageMethod == null;
				if (_failed)
				{
					RbjDiag.Warn(
						$"CraftingAccess reflection incomplete: te={_teCraftingAccessType != null} " +
						$"open={_openStorageMethod != null}");
				}
				else
				{
					RbjDiag.Info("CraftingAccess reflection OK (TECraftingAccess + StorageAccess.OpenStorage)");
				}

				return !_failed;
			}
			catch (Exception ex)
			{
				_failed = true;
				RbjDiag.Error("CraftingAccess EnsureReflection failed", ex);
				return false;
			}
		}

		internal static void Unload()
		{
			ClearPending();
			_resolved = false;
			_failed = false;
			_teCraftingAccessType = null;
			_openStorageMethod = null;
		}
	}
}
