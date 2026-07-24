using System;
using System.Collections.Generic;
using System.Reflection;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace RecipeBrowserJPChatSearch
{
	/// <summary>
	/// World middle-click → Recipe Browser without tip / SetZoom / tileTarget.
	/// <list type="number">
	/// <item><see cref="Player.cursorItemIconID"/> (vanilla icon)</item>
	/// <item>Sticky item from <see cref="GlobalTile.MouseOver"/> — vanilla already
	/// decided the tile; we only map that tile → item (Magic Storage first). No tip remap.</item>
	/// </list>
	/// Platforms / planter boxes are never sent.
	/// </summary>
	internal static class WorldPlacedItemHover
	{
		private const int StickyFrames = 6;

		private static int _frameIconId;
		private static bool _frameIconEnabled;

		private static int _stickyType;
		private static string _stickySource = string.Empty;
		private static int _stickyTtl;
		private static bool _stickyIsMagicStorage;

		private static int _prevLiveIconId = int.MinValue;
		private static bool _prevLiveIconEn;
		private static long _nextIconDeltaLogTick;

		private enum MsItemTypeKind : byte
		{
			Unresolved = 0,
			Missing = 1,
			InstanceProperty = 2,
			InstanceField = 3,
			StaticField = 4
		}

		private struct MsItemTypeCacheEntry
		{
			internal MsItemTypeKind Kind;
			internal PropertyInfo Prop;
			internal FieldInfo Field;
		}

		private static readonly Dictionary<Type, MsItemTypeCacheEntry> _msItemTypeCache = new();

		internal static int WorldPickOkCount;
		internal static int WorldPickMissCount;

		internal static void ClearFrame()
		{
			if (_stickyTtl > 0)
				_stickyTtl--;
			if (_stickyTtl <= 0)
			{
				_stickyType = 0;
				_stickySource = string.Empty;
				_stickyIsMagicStorage = false;
			}
		}

		internal static void ClearReflectionCache()
		{
			_msItemTypeCache.Clear();
		}

		internal static void CaptureFrameHints()
		{
			Player player = Main.LocalPlayer;
			if (player == null)
				return;

			_frameIconEnabled = player.cursorItemIconEnabled;
			_frameIconId = player.cursorItemIconID;
			ObserveIconDelta(player);
		}

		private static void ObserveIconDelta(Player player)
		{
			if (!RbjDiag.Enabled || player == null)
				return;

			bool en = player.cursorItemIconEnabled;
			int id = player.cursorItemIconID;
			if (en == _prevLiveIconEn && id == _prevLiveIconId)
				return;

			int prevId = _prevLiveIconId;
			bool prevEn = _prevLiveIconEn;
			_prevLiveIconEn = en;
			_prevLiveIconId = id;

			long now = Environment.TickCount64;
			if (now < _nextIconDeltaLogTick)
				return;

			_nextIconDeltaLogTick = now + 200;
			RbjDiag.Info(
				$"CursorIcon DELTA en {prevEn}->{en} id {prevId}->{id} | " +
				$"tipRemaps={RbjDiagPolicy.TipRemapCount} sticky={_stickyType}/{_stickySource}");
		}

		/// <summary>
		/// Vanilla MouseOver: tile (x,y) already chosen by the game — no SetZoom.
		/// Prefer Magic Storage; also keep non-platform placeables as sticky.
		/// </summary>
		internal static void NoteHoveredTile(int x, int y)
		{
			if (Main.gameMenu || Main.drawingPlayerChat)
				return;

			Player player = Main.LocalPlayer;
			if (player != null && player.mouseInterface)
				return;

			if (!TryResolveTileItem(x, y, out int itemType, out string source, out bool isMs))
				return;

			if (itemType <= ItemID.None || IsSupportishItem(itemType))
				return;

			// Prefer MS notes over generic when both fire in one frame.
			if (_stickyTtl > 0 && _stickyIsMagicStorage && !isMs)
				return;

			_stickyType = itemType;
			_stickySource = source;
			_stickyTtl = StickyFrames;
			_stickyIsMagicStorage = isMs;
		}

		internal static bool FrameIconEnabled => _frameIconEnabled;

		internal static int FrameIconId => _frameIconId;

		internal static bool TryGetItemUnderMouse(out int itemType, out string source)
		{
			itemType = 0;
			source = string.Empty;

			Player player = Main.LocalPlayer;
			if (player != null && player.mouseInterface)
			{
				LogMiss("mouseUi");
				return false;
			}

			// 1) Vanilla cursor icon (non-platform).
			int iconId = 0;
			string how = null;
			if (player != null && player.cursorItemIconEnabled && player.cursorItemIconID > ItemID.None)
			{
				iconId = player.cursorItemIconID;
				how = "cursorItemIconLive";
			}
			else if (_frameIconEnabled && _frameIconId > ItemID.None)
			{
				iconId = _frameIconId;
				how = "cursorItemIconCached";
			}

			if (iconId > ItemID.None && !IsSupportishItem(iconId))
			{
				itemType = iconId;
				source = how;
				WorldPickOkCount++;
				LogOk(itemType, source);
				return true;
			}

			if (iconId > ItemID.None && IsSupportishItem(iconId))
				RbjDiag.Info($"WorldPick skip icon platform type={iconId}");

			// 2) MouseOver sticky — Magic Storage first, then other non-platform.
			if (_stickyTtl > 0 && _stickyType > ItemID.None && !IsSupportishItem(_stickyType))
			{
				itemType = _stickyType;
				source = _stickySource + "+mouseOverSticky";
				WorldPickOkCount++;
				LogOk(itemType, source);
				return true;
			}

			LogMiss(_stickyTtl > 0 ? $"sticky-blocked type={_stickyType}" : "no-icon-no-mouseOver");
			return false;
		}

		private static void LogOk(int itemType, string source)
		{
			RbjDiag.Info(
				$"WorldPick OK source={source} type={itemType} '{Lang.GetItemNameValue(itemType)}' " +
				$"msSticky={_stickyIsMagicStorage} stickyTtl={_stickyTtl} " +
				$"okN={WorldPickOkCount} missN={WorldPickMissCount} tipRemaps={RbjDiagPolicy.TipRemapCount} | " +
				$"policy=icon+mouseOver(noTip/noF)");
		}

		private static void LogMiss(string reason)
		{
			WorldPickMissCount++;
			if (!RbjDiag.Enabled)
				return;

			RbjDiag.Info(
				$"WorldPick MISS | reason={reason} | " +
				$"iconEn={_frameIconEnabled} iconId={_frameIconId} " +
				$"liveId={(Main.LocalPlayer?.cursorItemIconID ?? 0)} " +
				$"sticky={_stickyType}/{_stickySource} ttl={_stickyTtl} ms={_stickyIsMagicStorage} | " +
				$"tipRemaps={RbjDiagPolicy.TipRemapCount} | policy=icon+mouseOver(noTip/noF)");
		}

		private static bool TryResolveTileItem(
			int x,
			int y,
			out int itemType,
			out string source,
			out bool isMagicStorage)
		{
			itemType = 0;
			source = string.Empty;
			isMagicStorage = false;

			if (!WorldGen.InWorld(x, y))
				return false;

			Tile tile = Main.tile[x, y];
			if (tile == null || !tile.HasTile)
				return false;

			ModTile modTile = TileLoader.GetTile(tile.TileType);
			if (modTile != null)
			{
				string ns = modTile.GetType().Namespace ?? string.Empty;
				isMagicStorage = ns.IndexOf("MagicStorage", StringComparison.OrdinalIgnoreCase) >= 0;

				if (TryGetMagicStorageItem(modTile, x, y, out itemType))
				{
					source = "msMouseOver";
					isMagicStorage = true;
					return true;
				}

				if (TryGetModTileDrop(modTile, x, y, out itemType))
				{
					source = isMagicStorage ? "msMouseOverDrop" : "modTileMouseOver";
					return true;
				}
			}

			if (TryGetVanillaOrLoaderDrop(tile, out itemType))
			{
				source = "tileDropMouseOver";
				return true;
			}

			return false;
		}

		private static bool TryGetMagicStorageItem(ModTile modTile, int x, int y, out int itemType)
		{
			itemType = 0;
			if (modTile == null)
				return false;

			Type tileClass = modTile.GetType();
			string ns = tileClass.Namespace ?? string.Empty;
			if (ns.IndexOf("MagicStorage", StringComparison.OrdinalIgnoreCase) < 0)
				return false;

			try
			{
				if (!_msItemTypeCache.TryGetValue(tileClass, out MsItemTypeCacheEntry entry)
					|| entry.Kind == MsItemTypeKind.Unresolved)
				{
					entry = ResolveMsItemTypeAccessor(tileClass);
					_msItemTypeCache[tileClass] = entry;
				}

				switch (entry.Kind)
				{
					case MsItemTypeKind.InstanceProperty:
					{
						object v = entry.Prop.GetValue(modTile);
						if (v is int id && id > 0)
						{
							itemType = id;
							return true;
						}

						break;
					}
					case MsItemTypeKind.InstanceField:
					{
						object v = entry.Field.GetValue(modTile);
						if (v is int id && id > 0)
						{
							itemType = id;
							return true;
						}

						break;
					}
					case MsItemTypeKind.StaticField:
					{
						object v = entry.Field.GetValue(null);
						if (v is int id && id > 0)
						{
							itemType = id;
							return true;
						}

						break;
					}
				}
			}
			catch (Exception ex)
			{
				if (RbjDiag.Enabled)
					RbjDiag.Info($"MS ItemType reflect fail {tileClass.Name}: {ex.GetType().Name}");
			}

			return TryGetModTileDrop(modTile, x, y, out itemType);
		}

		private static MsItemTypeCacheEntry ResolveMsItemTypeAccessor(Type tileClass)
		{
			var entry = new MsItemTypeCacheEntry { Kind = MsItemTypeKind.Missing };
			try
			{
				PropertyInfo prop = tileClass.GetProperty(
					"ItemType",
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
				if (prop != null && prop.PropertyType == typeof(int))
				{
					entry.Kind = MsItemTypeKind.InstanceProperty;
					entry.Prop = prop;
					return entry;
				}

				FieldInfo field = tileClass.GetField(
					"ItemType",
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
				if (field != null && field.FieldType == typeof(int))
				{
					entry.Kind = field.IsStatic ? MsItemTypeKind.StaticField : MsItemTypeKind.InstanceField;
					entry.Field = field;
					return entry;
				}
			}
			catch (Exception ex)
			{
				if (RbjDiag.Enabled)
					RbjDiag.Info($"MS ItemType resolve fail {tileClass.Name}: {ex.GetType().Name}");
			}

			return entry;
		}

		private static bool TryGetModTileDrop(ModTile modTile, int x, int y, out int itemType)
		{
			itemType = 0;
			try
			{
				IEnumerable<Item> drops = modTile.GetItemDrops(x, y);
				if (drops == null)
					return false;

				foreach (Item drop in drops)
				{
					if (drop == null || drop.IsAir || drop.type <= ItemID.None)
						continue;
					itemType = drop.type;
					return true;
				}
			}
			catch (Exception ex)
			{
				// MS partial frames often NRE inside GetItemDrops — ignore.
				if (RbjDiag.Enabled)
					RbjDiag.Info($"GetItemDrops skip {modTile.Name}: {ex.GetType().Name}");
			}

			return false;
		}

		private static bool TryGetVanillaOrLoaderDrop(Tile tile, out int itemType)
		{
			itemType = 0;
			try
			{
				int style = TileObjectData.GetTileStyle(tile);
				if (style < 0)
					style = 0;

				int drop = TileLoader.GetItemDropFromTypeAndStyle(tile.TileType, style);
				if (drop > ItemID.None)
				{
					itemType = drop;
					return true;
				}
			}
			catch
			{
				// ignore
			}

			return false;
		}

		private static bool IsSupportishItem(int itemType)
		{
			if (itemType <= ItemID.None)
				return false;

			try
			{
				Item sample = ContentSamples.ItemsByType[itemType];
				if (sample == null || sample.IsAir)
					return false;

				int create = sample.createTile;
				if (create < 0)
					return false;

				return create == TileID.Platforms
					|| create == TileID.PlanterBox;
			}
			catch
			{
				return false;
			}
		}
	}

	internal class WorldPlacedItemHoverTile : GlobalTile
	{
		public override void MouseOver(int i, int j, int type)
			=> WorldPlacedItemHover.NoteHoveredTile(i, j);

		// MouseOverFar intentionally unused — far tiles caused wrong sticky notes.
	}
}
