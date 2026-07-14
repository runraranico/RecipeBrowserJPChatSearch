using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameInput;

namespace RecipeBrowserJPChatSearch
{
	/// <summary>
	/// Cursor → world tile for world-pick (publication-safe for all resolutions / zoom).
	/// <para>
	/// Hotkey / interface code often runs under UI zoom input context
	/// (<c>PostDrawInterface</c>, UIScale). Raw <see cref="Main.MouseWorld"/> then
	/// points at the wrong cell when GameZoom or ForcedMinimumZoom ≠ 1
	/// (common on 1440p / 4K). Vanilla remaps the tip with
	/// <see cref="PlayerInput.SetZoom_MouseInWorld"/> — we do the same, then restore
	/// via <see cref="PlayerInput.SetZoom_Context"/>.
	/// </para>
	/// <para>
	/// Important: <see cref="PlayerInput.SetZoom_MouseInWorld"/> rewrites
	/// <see cref="Main.mouseX"/> / <see cref="Main.mouseY"/>. Hover tooltips use those
	/// coordinates, so if they are left remapped after PostDraw world-pick / logging,
	/// the same item tip briefly appears at the wrong screen position. We always
	/// snapshot and restore integer mouse coords around the zoom remap.
	/// </para>
	/// <para>
	/// Perf / black-screen mitigation: tip world/tile is cached once per
	/// <see cref="Main.GameUpdateCount"/> + mouse pixel so GlobalTile.MouseOver
	/// spam cannot call SetZoom hundreds of times per frame (BetterZoom conflict).
	/// </para>
	/// </summary>
	internal static class RbjCursor
	{
		private static uint _cacheUpdateCount = uint.MaxValue;
		private static int _cacheUiMouseX = int.MinValue;
		private static int _cacheUiMouseY = int.MinValue;
		private static Vector2 _cacheWorld;
		private static Point16 _cacheTile;
		private static int _cacheWorldMouseX;
		private static int _cacheWorldMouseY;
		private static bool _cacheValid;

		/// <summary>World pixel under the cursor tip (drawn game world).</summary>
		internal static Vector2 GetWorldUnderCursorTip()
		{
			EnsureTipCache();
			return _cacheWorld;
		}

		/// <summary>Tile under the cursor tip.</summary>
		internal static Point16 GetTileUnderCursorTip()
		{
			EnsureTipCache();
			return _cacheTile;
		}

		/// <summary>Force a fresh SetZoom remap (rare; prefer cached getters).</summary>
		internal static void InvalidateTipCache()
		{
			_cacheValid = false;
			_cacheUpdateCount = uint.MaxValue;
		}

		private static void EnsureTipCache()
		{
			uint update = Main.GameUpdateCount;
			int mx = Main.mouseX;
			int my = Main.mouseY;
			if (_cacheValid
				&& _cacheUpdateCount == update
				&& _cacheUiMouseX == mx
				&& _cacheUiMouseY == my)
				return;

			_cacheWorld = WithWorldMouseZoom(() =>
			{
				_cacheWorldMouseX = Main.mouseX;
				_cacheWorldMouseY = Main.mouseY;
				return Main.MouseWorld;
			});
			_cacheTile = _cacheWorld.ToTileCoordinates16();
			_cacheUpdateCount = update;
			_cacheUiMouseX = mx;
			_cacheUiMouseY = my;
			_cacheValid = true;
		}

		/// <summary>
		/// Run <paramref name="action"/> while mouse coords are mapped for the world view.
		/// Always restores zoom context AND the integer mouse position that tip drawing uses.
		/// </summary>
		internal static T WithWorldMouseZoom<T>(Func<T> action)
		{
			int mouseX = Main.mouseX;
			int mouseY = Main.mouseY;
			int inputX = PlayerInput.MouseX;
			int inputY = PlayerInput.MouseY;

			RbjRenderHealth.NoteZoomRemapEnter();
			PlayerInput.SetZoom_MouseInWorld();
			try
			{
				return action();
			}
			finally
			{
				PlayerInput.SetZoom_Context();
				// Intentionally remapped above — restore quietly (no delta spam).
				RestoreMouse(mouseX, mouseY, inputX, inputY, logIfChanged: false, reason: null);
				RbjRenderHealth.NoteZoomRemapExit();
			}
		}

		internal static void WithWorldMouseZoom(Action action)
		{
			WithWorldMouseZoom(() =>
			{
				action();
				return 0;
			});
		}

		/// <summary>
		/// Save/restore helper for PostDraw paths that may call world-zoom utilities
		/// and must not leave tip anchors at the remapped world position.
		/// </summary>
		internal static MouseSnap CaptureMouse()
			=> new MouseSnap(Main.mouseX, Main.mouseY, PlayerInput.MouseX, PlayerInput.MouseY);

		internal static void RestoreMouse(MouseSnap snap)
			=> RestoreMouse(snap, logIfChanged: false, reason: null);

		internal static void RestoreMouse(MouseSnap snap, bool logIfChanged, string reason)
			=> RestoreMouse(snap.MainX, snap.MainY, snap.InputX, snap.InputY, logIfChanged, reason);

		internal static void RestoreMouse(int mainX, int mainY, int inputX, int inputY)
			=> RestoreMouse(mainX, mainY, inputX, inputY, logIfChanged: false, reason: null);

		internal static void RestoreMouse(
			int mainX,
			int mainY,
			int inputX,
			int inputY,
			bool logIfChanged,
			string reason)
		{
			if (logIfChanged
				&& (Main.mouseX != mainX || Main.mouseY != mainY
					|| PlayerInput.MouseX != inputX || PlayerInput.MouseY != inputY))
			{
				RbjDiag.Warn(
					$"MouseRestore delta reason='{reason ?? ""}' " +
					$"was=({Main.mouseX},{Main.mouseY})/{PlayerInput.MouseX},{PlayerInput.MouseY} " +
					$"restore=({mainX},{mainY})/{inputX},{inputY}");
			}

			Main.mouseX = mainX;
			Main.mouseY = mainY;
			PlayerInput.MouseX = inputX;
			PlayerInput.MouseY = inputY;
		}

		/// <summary>Diagnostic one-liner (only meaningful when <see cref="RbjDiag"/> is on).</summary>
		internal static string Snapshot()
		{
			MouseSnap before = CaptureMouse();
			try
			{
				Vector2 uiCtx = Main.MouseWorld;
				Point16 uiTile = uiCtx.ToTileCoordinates16();

				// Reuse per-update tip cache — avoids a second SetZoom after world-pick.
				EnsureTipCache();
				Vector2 worldCtx = _cacheWorld;
				Point16 worldTile = _cacheTile;
				int worldMouseX = _cacheWorldMouseX;
				int worldMouseY = _cacheWorldMouseY;

				int tx = Player.tileTargetX;
				int ty = Player.tileTargetY;

				Vector2 zoom = Main.GameViewMatrix.Zoom;
				float zoomTarget = Main.GameZoomTarget;

				int dWorldTarget = Math.Max(Math.Abs(worldTile.X - tx), Math.Abs(worldTile.Y - ty));
				int dUiTarget = Math.Max(Math.Abs(uiTile.X - tx), Math.Abs(uiTile.Y - ty));
				bool outside = before.MainX < 0 || before.MainY < 0
					|| before.MainX >= Main.screenWidth || before.MainY >= Main.screenHeight;

				string agree = (uiTile.X == worldTile.X && uiTile.Y == worldTile.Y)
					? "uiCtx==worldCtx"
					: "uiCtx!=worldCtx";

				return
					$"cursorMethod=SetZoom_MouseInWorld {agree} " +
					$"zoom=({zoom.X:0.###},{zoom.Y:0.###}) zoomTarget={zoomTarget:0.###} " +
					$"scrUi=({before.MainX},{before.MainY}) scrWorld=({worldMouseX},{worldMouseY}) " +
					$"win=({Main.screenWidth},{Main.screenHeight}) outsideWin={outside} " +
					$"worldChosen=({worldCtx.X:0},{worldCtx.Y:0}) " +
					$"tileUiCtx=({uiTile.X},{uiTile.Y}) tileWorldCtx=({worldTile.X},{worldTile.Y}) " +
					$"tileTarget=({tx},{ty}) dWorldTarget={dWorldTarget} dUiTarget={dUiTarget} " +
					$"(tileTarget=reach-clamped F-target; tip≠F when out of reach)";
			}
			finally
			{
				RestoreMouse(before);
			}
		}

		internal readonly struct MouseSnap
		{
			internal readonly int MainX;
			internal readonly int MainY;
			internal readonly int InputX;
			internal readonly int InputY;

			internal MouseSnap(int mainX, int mainY, int inputX, int inputY)
			{
				MainX = mainX;
				MainY = mainY;
				InputX = inputX;
				InputY = inputY;
			}
		}
	}
}
