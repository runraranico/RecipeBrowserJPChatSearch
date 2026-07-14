using System;
using System.Collections.Generic;
using System.Reflection;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.Tile_Entities;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace RecipeBrowserJPChatSearch
{
	/// <summary>
	/// World pick: exactly the tile under <see cref="RbjCursor"/> tip
	/// (vanilla SetZoom_MouseInWorld — works for all resolutions / GameZoom).
	/// Platforms return the platform item (no look-up to furniture above).
	/// Empty cells do not steal neighbors.
	/// </summary>
	internal static class WorldPlacedItemHover
	{
		/// <summary>Item-frame highlight flicker only.</summary>
		private const int StickyTtlFrames = 20;

		/// <summary>Chebyshev radius for MouseOver notes / frame sticky (not for placeables).</summary>
		private const int CursorRing = 1;

		private static int _frameIconId;
		private static bool _frameIconEnabled;
		private static string _frameHoverName = string.Empty;
		private static int _tileHoverItemType;
		private static string _tileHoverSource = string.Empty;
		private static int _tileHoverX = -1;
		private static int _tileHoverY = -1;

		private static int _stickyItemType;
		private static string _stickySource = string.Empty;
		private static int _stickyTtl;
		private static int _stickyTileX = -1;
		private static int _stickyTileY = -1;

		/// <summary>Clears per-frame tile notes only. Sticky memory is decayed separately.</summary>
		internal static void ClearFrame()
		{
			_tileHoverItemType = 0;
			_tileHoverSource = string.Empty;
			_tileHoverX = -1;
			_tileHoverY = -1;
		}

		internal static void TickSticky()
		{
			if (_stickyTtl <= 0)
			{
				_stickyItemType = 0;
				_stickySource = string.Empty;
				return;
			}

			_stickyTtl--;
			if (_stickyTtl <= 0)
			{
				_stickyItemType = 0;
				_stickySource = string.Empty;
				_stickyTileX = -1;
				_stickyTileY = -1;
			}
		}

		internal static void CaptureFrameHints()
		{
			Player player = Main.LocalPlayer;
			if (player == null)
				return;

			_frameIconEnabled = player.cursorItemIconEnabled;
			_frameIconId = player.cursorItemIconID;
			_frameHoverName = Main.hoverItemName ?? string.Empty;
		}

		/// <summary>Called from GlobalTile.MouseOver / MouseOverFar — ignored if not under the cursor.</summary>
		internal static void NoteHoveredTile(int x, int y)
		{
			if (Main.gameMenu || Main.drawingPlayerChat)
				return;

			Player player = Main.LocalPlayer;
			// UI / inventory pointer: do not SetZoom_MouseInWorld (BetterZoom conflict risk).
			if (player != null && player.mouseInterface)
				return;

			Point16 tip = RbjCursor.GetTileUnderCursorTip();
			Point16 interact = new Point16(Player.tileTargetX, Player.tileTargetY);
			// Terraria often MouseOver's the interact tileTarget far from the pointer.
			// With BetterZoom tip≠F is common — accept notes near either tip or F.
			if (!IsWithinChebyshev(x, y, tip.X, tip.Y, CursorRing)
				&& !IsWithinChebyshev(x, y, interact.X, interact.Y, CursorRing))
				return;

			_tileHoverX = x;
			_tileHoverY = y;

			if (TryGetAt(x, y, out int itemType, out string source, verbose: false) && itemType > ItemID.None)
			{
				if (IsSupportishItem(itemType))
				{
					_tileHoverItemType = 0;
					_tileHoverSource = string.Empty;
					return;
				}

				_tileHoverItemType = itemType;
				_tileHoverSource = source;
				if (IsDisplayStickySource(source))
					SetSticky(itemType, source, x, y);
			}
		}

		internal static bool TryGetItemUnderMouse(out int itemType, out string source)
		{
			itemType = 0;
			source = string.Empty;

			Player player = Main.LocalPlayer;
			if (player != null && player.mouseInterface)
			{
				LogPickMiss("mouseUi");
				return false;
			}

			// Fresh tip for the click (avoid a stale same-update mouseInterface cache).
			RbjCursor.InvalidateTipCache();
			Point16 tip = RbjCursor.GetTileUnderCursorTip();

			if (TryResolveUnderCursor(tip, out itemType, out source, out int chosenX, out int chosenY))
			{
				if (IsDisplayStickySource(source))
					SetSticky(itemType, source, chosenX, chosenY);

				LogPickHit(itemType, source, tip, chosenX, chosenY);
				return true;
			}

			// BetterZoom / UIScale often make tip world coords diverge from Player.tileTarget
			// (vanilla interact tile under the cursor). Log showed tip empty while F had the frame.
			Point16 interact = new Point16(Player.tileTargetX, Player.tileTargetY);
			if ((interact.X != tip.X || interact.Y != tip.Y)
				&& WorldGen.InWorld(interact.X, interact.Y)
				&& TryResolveUnderCursor(interact, out itemType, out source, out chosenX, out chosenY))
			{
				source = string.IsNullOrEmpty(source) ? "tileTargetF" : source + "+F";
				if (IsDisplayStickySource(source) || source.Contains("itemFrame") || source.Contains("weaponRack"))
					SetSticky(itemType, source, chosenX, chosenY);

				LogPickHit(itemType, source, tip, chosenX, chosenY);
				return true;
			}

			if (TryResolveCursorIconDisplay(interact, tip, out itemType, out source, out chosenX, out chosenY))
			{
				if (IsDisplayStickySource(source))
					SetSticky(itemType, source, chosenX, chosenY);

				LogPickHit(itemType, source, tip, chosenX, chosenY);
				return true;
			}

			if (_stickyTtl > 0
				&& _stickyItemType > ItemID.None
				&& IsDisplayStickySource(_stickySource)
				&& (IsWithinChebyshev(_stickyTileX, _stickyTileY, tip.X, tip.Y, CursorRing)
					|| IsWithinChebyshev(_stickyTileX, _stickyTileY, interact.X, interact.Y, CursorRing)))
			{
				itemType = _stickyItemType;
				source = _stickySource + "+sticky";
				LogPickHit(itemType, source, tip, _stickyTileX, _stickyTileY);
				return true;
			}

			LogPickMiss("nothing-under-cursor");
			return false;
		}

		/// <summary>
		/// When tip tile is empty but vanilla still shows a cursor item icon (item frames etc.),
		/// use the icon / frame content at the interact tile.
		/// </summary>
		private static bool TryResolveCursorIconDisplay(
			Point16 interact,
			Point16 tip,
			out int itemType,
			out string source,
			out int chosenX,
			out int chosenY)
		{
			itemType = 0;
			source = string.Empty;
			chosenX = interact.X;
			chosenY = interact.Y;

			int iconId = 0;
			if (_frameIconEnabled && _frameIconId > ItemID.None)
				iconId = _frameIconId;
			else
			{
				Player player = Main.LocalPlayer;
				if (player != null && player.cursorItemIconEnabled && player.cursorItemIconID > ItemID.None)
					iconId = player.cursorItemIconID;
			}

			if (iconId <= ItemID.None)
				return false;

			// Prefer actual frame / rack / platter content at F (or tip), then icon if it matches.
			foreach (Point16 cell in new[] { interact, tip })
			{
				if (!WorldGen.InWorld(cell.X, cell.Y))
					continue;

				if (TryGetItemFrame(cell.X, cell.Y, out int framed) && framed > ItemID.None)
				{
					itemType = framed;
					source = "itemFrame@hint";
					chosenX = cell.X;
					chosenY = cell.Y;
					return true;
				}

				if (TileEntity.TryGet(cell.X, cell.Y, out TEWeaponsRack rack)
					&& TryTake(rack.item, out int rackItem))
				{
					itemType = rackItem;
					source = "weaponRack@hint";
					chosenX = cell.X;
					chosenY = cell.Y;
					return true;
				}

				if (TileEntity.TryGet(cell.X, cell.Y, out TEFoodPlatter platter)
					&& TryTake(platter.item, out int foodItem))
				{
					itemType = foodItem;
					source = "foodPlatter@hint";
					chosenX = cell.X;
					chosenY = cell.Y;
					return true;
				}

				if (TryGetNearestItemFrame(cell.X, cell.Y, out framed) && framed > ItemID.None)
				{
					itemType = framed;
					source = "itemFrameNearby@hint";
					chosenX = cell.X;
					chosenY = cell.Y;
					return true;
				}
			}

			// Displayed gem / material icons often have no createTile — use icon as the answer to "what is this?".
			try
			{
				Item sample = ContentSamples.ItemsByType[iconId];
				if (sample == null || sample.IsAir)
					return false;

				if (sample.createTile >= 0)
				{
					// Placeable furniture icon under empty tip: trust only when F tile matches.
					if (WorldGen.InWorld(interact.X, interact.Y))
					{
						Tile t = Main.tile[interact.X, interact.Y];
						if (t != null && t.HasTile && sample.createTile == t.TileType)
						{
							itemType = iconId;
							source = "cursorIcon@F";
							return true;
						}
					}

					return false;
				}

				itemType = iconId;
				source = "cursorItemIcon";
				return true;
			}
			catch
			{
				return false;
			}
		}

		/// <summary>
		/// Exact cell under the cursor only. Platforms are valid when that cell is a platform.
		/// </summary>
		private static bool TryResolveUnderCursor(
			Point16 mouse,
			out int itemType,
			out string source,
			out int chosenX,
			out int chosenY)
		{
			itemType = 0;
			source = string.Empty;
			chosenX = mouse.X;
			chosenY = mouse.Y;

			// Exact cursor cell (furniture, MS, platform, frame, …).
			if (TryPickExactCell(mouse.X, mouse.Y, out itemType, out source))
			{
				chosenX = mouse.X;
				chosenY = mouse.Y;
				return true;
			}

			// MouseOver note only if still under cursor (display tiles / nearby note).
			if (_tileHoverItemType > ItemID.None
				&& IsWithinChebyshev(_tileHoverX, _tileHoverY, mouse.X, mouse.Y, CursorRing))
			{
				itemType = _tileHoverItemType;
				source = string.IsNullOrEmpty(_tileHoverSource) ? "tileMouseOver" : _tileHoverSource;
				chosenX = _tileHoverX;
				chosenY = _tileHoverY;
				return true;
			}

			if (TryGetNearestItemFrame(mouse.X, mouse.Y, out itemType))
			{
				source = "itemFrameNearby";
				chosenX = mouse.X;
				chosenY = mouse.Y;
				return true;
			}

			if (TryMatchCursorIconToTile(mouse.X, mouse.Y, out itemType, out source))
			{
				chosenX = mouse.X;
				chosenY = mouse.Y;
				return true;
			}

			return false;
		}

		/// <summary>Resolve only the single tile at (x,y). No neighbor / look-up steal.</summary>
		private static bool TryPickExactCell(int x, int y, out int itemType, out string source)
		{
			itemType = 0;
			source = string.Empty;

			if (!TryGetAt(x, y, out itemType, out source, verbose: true) || itemType <= ItemID.None)
				return false;

			if (RbjDiag.Enabled && IsSupportishItem(itemType))
			{
				RbjDiag.Info(
					$"WorldPick exact-platform @({x},{y}) item={itemType} '{Lang.GetItemNameValue(itemType)}' " +
					$"(platform under cursor is intentional)");
			}

			return true;
		}

		private static void LogPickHit(int itemType, string source, Point16 mouse, int chosenX, int chosenY)
		{
			if (!RbjDiag.Enabled)
				return;

			int tx = Player.tileTargetX;
			int ty = Player.tileTargetY;
			int dChosen = Chebyshev(chosenX, chosenY, mouse.X, mouse.Y);
			int dTarget = Chebyshev(tx, ty, mouse.X, mouse.Y);
			string name = itemType > 0 ? Lang.GetItemNameValue(itemType) : "";

			RbjDiag.Info(
				$"WorldPick HIT | chosen@=({chosenX},{chosenY}) dCursor={dChosen} " +
				$"item={itemType} '{name}' src={source} | " +
				$"cursorCell={DescribeTile(mouse.X, mouse.Y)} | " +
				$"interactTarget=({tx},{ty}) dTarget={dTarget} targetCell={DescribeTile(tx, ty)} | " +
				$"{RbjCursor.Snapshot()} | policy=exact-cursor-tip");
		}

		private static void LogPickMiss(string reason)
		{
			if (!RbjDiag.Enabled)
				return;

			Point16 mouse = RbjCursor.GetTileUnderCursorTip();
			int tx = Player.tileTargetX;
			int ty = Player.tileTargetY;
			int dTarget = Chebyshev(tx, ty, mouse.X, mouse.Y);

			string cursorDesc = DescribeTile(mouse.X, mouse.Y);
			string targetDesc = DescribeTile(tx, ty);
			string mismatch = dTarget > 2
				? $"WARN_tip_vs_F_d={dTarget} (F follows reach-clamp; tip can be elsewhere)"
				: "tip_near_F";

			RbjDiag.Info(
				$"WorldPick MISS | reason={reason} | " +
				$"cursorCell={cursorDesc} tileHover=({_tileHoverX},{_tileHoverY}) | " +
				$"interactTarget=({tx},{ty}) {targetDesc} | {mismatch} | " +
				$"iconEn={_frameIconEnabled} iconId={_frameIconId} | " +
				$"{RbjCursor.Snapshot()} | " +
				$"stickyType={_stickyItemType} stickyTtl={_stickyTtl} | " +
				$"policy=exact-cursor-tip");
		}

		private static bool IsWithinChebyshev(int x, int y, int ox, int oy, int max)
		{
			if (x < 0 || y < 0)
				return false;
			return Chebyshev(x, y, ox, oy) <= max;
		}

		private static int Chebyshev(int x, int y, int ox, int oy)
			=> Math.Max(Math.Abs(x - ox), Math.Abs(y - oy));

		/// <summary>
		/// Accept cursor icon only if that item places the tile at (x,y) under the cursor.
		/// </summary>
		private static bool TryMatchCursorIconToTile(int x, int y, out int itemType, out string source)
		{
			itemType = 0;
			source = string.Empty;

			if (x < 0 || y < 0 || !WorldGen.InWorld(x, y))
				return false;

			int iconId = 0;
			if (_frameIconEnabled && _frameIconId > ItemID.None)
				iconId = _frameIconId;
			else
			{
				Player player = Main.LocalPlayer;
				if (player != null && player.cursorItemIconEnabled && player.cursorItemIconID > ItemID.None)
					iconId = player.cursorItemIconID;
			}

			if (iconId <= ItemID.None || iconId >= ItemLoader.ItemCount)
				return false;

			Tile tile = Main.tile[x, y];
			if (tile == null || !tile.HasTile)
				return false;

			try
			{
				Item sample = ContentSamples.ItemsByType[iconId];
				if (sample == null || sample.createTile < 0)
					return false;

				int underType = tile.TileType;
				if (TryGetTileOrigin(x, y, out int ox, out int oy))
				{
					Tile origin = Main.tile[ox, oy];
					if (origin != null && origin.HasTile)
						underType = origin.TileType;
				}

				if (sample.createTile != underType)
					return false;

				itemType = iconId;
				source = "cursorIconMatched";
				return true;
			}
			catch
			{
				return false;
			}
		}

		private static bool IsDisplayStickySource(string source)
		{
			if (string.IsNullOrEmpty(source))
				return false;
			return source.StartsWith("itemFrame", StringComparison.Ordinal)
				|| source.StartsWith("weaponRack", StringComparison.Ordinal)
				|| source.StartsWith("foodPlatter", StringComparison.Ordinal);
		}

		private static void SetSticky(int itemType, string source, int tileX, int tileY)
		{
			_stickyItemType = itemType;
			_stickySource = source ?? string.Empty;
			_stickyTtl = StickyTtlFrames;
			_stickyTileX = tileX;
			_stickyTileY = tileY;
		}

		private static bool TryGetAt(int x, int y, out int itemType, out string source, bool verbose = false)
		{
			itemType = 0;
			source = string.Empty;

			if (!WorldGen.InWorld(x, y))
				return false;

			if (TryGetItemFrame(x, y, out itemType))
			{
				source = "itemFrame";
				return true;
			}

			if (TileEntity.TryGet(x, y, out TEWeaponsRack weaponsRack)
				&& TryTake(weaponsRack.item, out itemType))
			{
				source = "weaponRack";
				return true;
			}

			if (TileEntity.TryGet(x, y, out TEFoodPlatter foodPlatter)
				&& TryTake(foodPlatter.item, out itemType))
			{
				source = "foodPlatter";
				return true;
			}

			// Exact cell only — no look-up above platforms (platform click = platform item).
			if (TryProbeWorldTile(x, y, out itemType, out source, out _, verbose))
				return true;

			return false;
		}

		private static bool TryProbeWorldTile(
			int x,
			int y,
			out int itemType,
			out string source,
			out int score,
			bool verbose = false)
		{
			itemType = 0;
			source = string.Empty;
			score = -1;

			if (!WorldGen.InWorld(x, y))
				return false;

			Tile tile = Main.tile[x, y];
			if (tile == null || !tile.HasTile)
				return false;

			if (TryGetMagicStorageUnitItem(x, y, out itemType))
			{
				source = "msStorageUnit";
				score = 30;
				if (verbose && RbjDiag.Enabled)
				{
					RbjDiag.Info(
						$"WorldTile probe ({x},{y}) tile={tile.TileType} → item={itemType} " +
						$"'{Lang.GetItemNameValue(itemType)}' score={score} src=msStorageUnit");
				}
				return true;
			}

			if (!TryGetPlaceableTileItem(x, y, out itemType, verbose))
				return false;

			source = "placeableTile";
			score = IsSupportSurfaceTile(tile.TileType) || IsSupportishItem(itemType) ? 5 : 20;

			if (verbose && RbjDiag.Enabled)
			{
				RbjDiag.Info(
					$"WorldTile probe ({x},{y}) tile={tile.TileType} → item={itemType} " +
					$"'{Lang.GetItemNameValue(itemType)}' score={score}");
			}

			return true;
		}

		private static bool IsSupportSurfaceTile(int tileType)
		{
			if (tileType <= 0)
				return false;
			if (tileType == TileID.Platforms)
				return true;
			if (TileID.Sets.Platforms[tileType])
				return true;

			// Furniture / MS machines are often solidTop but must NOT count as platforms.
			// Treating them as support made interact fallback reject Storage Heart etc.
			if (Main.tileFrameImportant[tileType] || TileLoader.GetTile(tileType) != null)
				return false;

			return Main.tileSolidTop[tileType] && !Main.tileSolid[tileType];
		}

		private static bool IsSupportishItem(int itemType)
		{
			if (itemType <= ItemID.None || itemType >= ItemLoader.ItemCount)
				return false;

			try
			{
				Item sample = ContentSamples.ItemsByType[itemType];
				if (sample == null || sample.createTile < 0)
					return false;
				return IsSupportSurfaceTile(sample.createTile);
			}
			catch
			{
				return false;
			}
		}

		/// <summary>
		/// Generic placed furniture / machines → place-item type (mod + vanilla FrameImportant).
		/// Skips ordinary solid ground (dirt/stone) so middle-click on floors does nothing.
		/// </summary>
		private static bool TryGetPlaceableTileItem(int x, int y, out int itemType, bool verbose = false)
		{
			itemType = 0;
			if (!WorldGen.InWorld(x, y))
				return false;

			Tile tile = Main.tile[x, y];
			if (tile == null || !tile.HasTile)
				return false;

			int tileType = tile.TileType;
			// Ground / walls of dirt etc. are not FrameImportant — ignore.
			if (!Main.tileFrameImportant[tileType]
				&& TileLoader.GetTile(tileType) == null)
				return false;

			if (!TryGetTileOrigin(x, y, out int ox, out int oy))
			{
				ox = x;
				oy = y;
			}

			try
			{
				ModTile modTile = TileLoader.GetTile(tileType);
				if (modTile != null)
				{
					// Magic Storage components often NRE inside GetItemDrops for partial frames —
					// resolve via dedicated path first.
					if (TryGetMagicStorageComponentItem(x, y, modTile, out itemType))
					{
						if (verbose && RbjDiag.Enabled)
						{
							RbjDiag.Info(
								$"placeable via MagicStorage tile={tileType} class={modTile.Name} " +
								$"→ {itemType} '{Lang.GetItemNameValue(itemType)}'");
						}
						return true;
					}

					IEnumerable<Item> drops = null;
					try
					{
						drops = modTile.GetItemDrops(ox, oy);
					}
					catch (Exception dropEx)
					{
						if (verbose && RbjDiag.Enabled)
						{
							RbjDiag.Warn(
								$"GetItemDrops NRE/fail tile={tileType} class={modTile.Name} " +
								$"origin=({ox},{oy}): {dropEx.GetType().Name}: {dropEx.Message}");
						}
					}

					if (drops != null)
					{
						foreach (Item drop in drops)
						{
							if (drop != null && !drop.IsAir && drop.type > ItemID.None)
							{
								itemType = drop.type;
								if (verbose && RbjDiag.Enabled)
								{
									RbjDiag.Info(
										$"placeable via GetItemDrops tile={tileType} origin=({ox},{oy}) → {itemType} '{Lang.GetItemNameValue(itemType)}'");
								}
								return true;
							}
						}
					}
				}

				Tile origin = Main.tile[ox, oy];
				if (origin == null || !origin.HasTile)
					return false;

				int style = TileObjectData.GetTileStyle(origin);
				if (style < 0)
					style = 0;

				int dropType = TileLoader.GetItemDropFromTypeAndStyle(tileType, style);
				if (dropType > ItemID.None)
				{
					itemType = dropType;
					if (verbose && RbjDiag.Enabled)
					{
						RbjDiag.Info(
							$"placeable via StyleMap tile={tileType} style={style} origin=({ox},{oy}) → {dropType} '{Lang.GetItemNameValue(dropType)}'");
					}
					return true;
				}
			}
			catch (Exception ex)
			{
				RbjDiag.Warn(
					$"TryGetPlaceableTileItem failed tile={tileType} at=({x},{y}): " +
					$"{ex.GetType().Name}: {ex.Message}");
			}

			return false;
		}

		/// <summary>
		/// Any MagicStorage.* tile → place item (Storage Unit and other components).
		/// </summary>
		private static bool TryGetMagicStorageComponentItem(int x, int y, ModTile modTile, out int itemType)
		{
			itemType = 0;
			if (modTile == null)
				return false;

			Type tileClass = modTile.GetType();
			if (tileClass.Namespace == null
				|| !tileClass.Namespace.Contains("MagicStorage", StringComparison.Ordinal))
				return false;

			if (!TryGetTileOrigin(x, y, out int ox, out int oy))
			{
				ox = x;
				oy = y;
			}

			Tile origin = Main.tile[ox, oy];
			if (origin == null || !origin.HasTile)
				return false;

			try
			{
				MethodInfo itemTypeMethod = tileClass.GetMethod(
					"ItemType",
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
					binder: null,
					types: new[] { typeof(int), typeof(int) },
					modifiers: null);
				if (itemTypeMethod != null)
				{
					object result = itemTypeMethod.Invoke(
						modTile,
						new object[] { (int)origin.TileFrameX, (int)origin.TileFrameY });
					if (result is int typed && typed > ItemID.None)
					{
						itemType = typed;
						return true;
					}
				}
			}
			catch (Exception ex)
			{
				RbjDiag.Warn($"MagicStorage ItemType failed class={tileClass.Name}: {ex.GetType().Name}: {ex.Message}");
			}

			return false;
		}

		/// <summary>
		/// World-placed Magic Storage Storage Unit → corresponding unit item type.
		/// </summary>
		private static bool TryGetMagicStorageUnitItem(int x, int y, out int itemType)
		{
			itemType = 0;
			if (!ModLoader.HasMod("MagicStorage"))
				return false;

			if (!WorldGen.InWorld(x, y))
				return false;

			Tile tile = Main.tile[x, y];
			if (tile == null || !tile.HasTile)
				return false;

			ModTile modTile = TileLoader.GetTile(tile.TileType);
			return TryGetMagicStorageComponentItem(x, y, modTile, out itemType);
		}

		private static bool TryGetItemFrame(int x, int y, out int itemType)
		{
			itemType = 0;

			if (TileEntity.TryGet(x, y, out TEItemFrame viaTryGet)
				&& TryTake(viaTryGet.item, out itemType))
				return true;

			int id = TEItemFrame.Find(x, y);
			if (id != -1
				&& TileEntity.ByID.TryGetValue(id, out TileEntity entity)
				&& entity is TEItemFrame frame
				&& TryTake(frame.item, out itemType))
				return true;

			if (!TryGetTileOrigin(x, y, out int ox, out int oy) || (ox == x && oy == y))
				return false;

			id = TEItemFrame.Find(ox, oy);
			if (id != -1
				&& TileEntity.ByID.TryGetValue(id, out entity)
				&& entity is TEItemFrame frame2
				&& TryTake(frame2.item, out itemType))
				return true;

			return TileEntity.TryGet(ox, oy, out TEItemFrame framed)
				&& TryTake(framed.item, out itemType);
		}

		private static bool TryGetNearestItemFrame(int x, int y, out int itemType)
		{
			itemType = 0;
			TEItemFrame best = null;
			int bestDist = int.MaxValue;

			foreach (var pair in TileEntity.ByPosition)
			{
				if (pair.Value is not TEItemFrame frame || frame.item == null || frame.item.IsAir)
					continue;

				int dx = pair.Key.X - x;
				int dy = pair.Key.Y - y;
				if (dx < -1 || dx > 2 || dy < -1 || dy > 2)
					continue;

				int dist = dx * dx + dy * dy;
				if (dist >= bestDist)
					continue;

				bestDist = dist;
				best = frame;
			}

			return best != null && TryTake(best.item, out itemType);
		}

		private static bool TryGetTileOrigin(int x, int y, out int originX, out int originY)
		{
			originX = x;
			originY = y;
			Tile tile = Framing.GetTileSafely(x, y);
			if (!tile.HasTile)
				return false;

			TileObjectData data = TileObjectData.GetTileData(tile);
			if (data == null)
				return false;

			int partFrameX = tile.TileFrameX % data.CoordinateFullWidth;
			int partFrameY = tile.TileFrameY % data.CoordinateFullHeight;
			originX = x - partFrameX / data.CoordinateWidth;
			int rowH = data.CoordinateHeights is { Length: > 0 } heights ? heights[0] : 16;
			originY = y - partFrameY / (rowH + data.CoordinatePadding);
			return true;
		}

		private static string DescribeTile(int x, int y)
		{
			if (!WorldGen.InWorld(x, y))
				return "oob";

			Tile tile = Framing.GetTileSafely(x, y);
			if (!tile.HasTile)
				return "empty";

			return $"type={tile.TileType} fx={tile.TileFrameX} fy={tile.TileFrameY}";
		}

		private static bool TryTake(Item item, out int itemType)
		{
			itemType = 0;
			if (item == null || item.IsAir || item.type <= ItemID.None)
				return false;

			itemType = item.type;
			return true;
		}
	}

	internal class WorldPlacedItemHoverTile : GlobalTile
	{
		public override void MouseOver(int i, int j, int type) => WorldPlacedItemHover.NoteHoveredTile(i, j);

		public override void MouseOverFar(int i, int j, int type) => WorldPlacedItemHover.NoteHoveredTile(i, j);
	}
}
