using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameInput;

namespace RecipeBrowserJPChatSearch
{
	/// <summary>
	/// Mouse snap / restore and diagnostic Snapshot.
	/// <para>
	/// IMPORTANT:
	/// Do not use GetWorldUnderCursorTip, GetTileUnderCursorTip,
	/// or WithWorldMouseZoom for gameplay item/tile/NPC picking.
	///
	/// PlayerInput.SetZoom_MouseInWorld temporarily remaps mouse coordinates.
	/// This has produced incorrect picks and cursor flicker under:
	/// - ForcedMinimumZoom
	/// - BetterZoom or non-vanilla zoom
	/// - multi-monitor / DUAL_SCREEN
	/// - RealScreenWidth != Main.screenWidth
	///
	/// These tip/SetZoom methods are retained for diagnostics or legacy compatibility only.
	/// They are unused by current gameplay paths (2026-07 audit).
	/// New gameplay logic must use an already-resolved Terraria gameplay source
	/// such as cursorItemIcon, HoverItem, PreHover, tileTarget, or another
	/// context-appropriate vanilla result.
	/// Do not reintroduce SetZoom remapping without explicit approval and testing.
	/// </para>
	/// Active safe API: <see cref="CaptureMouse"/>, <see cref="RestoreMouse"/>,
	/// <see cref="Snapshot"/> (no SetZoom).
	/// </summary>
	internal static class RbjCursor
	{
		private static uint _cacheUpdateCount = uint.MaxValue;
		private static int _cacheUiMouseX = int.MinValue;
		private static int _cacheUiMouseY = int.MinValue;
		private static int _cacheScreenW = int.MinValue;
		private static int _cacheScreenH = int.MinValue;
		private static Vector2 _cacheWorld;
		private static Point16 _cacheTile;
		private static int _cacheWorldMouseX;
		private static int _cacheWorldMouseY;
		private static bool _cacheValid;

		/// <summary>
		/// Legacy tip world pixel under cursor. Unused by gameplay — calls SetZoom remap.
		/// </summary>
		/// <param name="reason">Diag tag when tip cache misses and SetZoom runs (null = silent).</param>
		[Obsolete("Do not use for gameplay picks. SetZoom_MouseInWorld remaps mouse coords and can mis-pick / flicker under ForcedMinimumZoom, BetterZoom, DUAL_SCREEN. Use cursorItemIcon / HoverItem / PreHover / tileTarget instead.")]
		internal static Vector2 GetWorldUnderCursorTip(string reason = null)
		{
			EnsureTipCache(reason);
			return _cacheWorld;
		}

		/// <summary>
		/// Legacy tip tile under cursor. Unused by gameplay — calls SetZoom remap.
		/// </summary>
		[Obsolete("Do not use for gameplay picks. SetZoom_MouseInWorld remaps mouse coords and can mis-pick / flicker under ForcedMinimumZoom, BetterZoom, DUAL_SCREEN. Use cursorItemIcon / HoverItem / PreHover / tileTarget instead.")]
		internal static Point16 GetTileUnderCursorTip(string reason = null)
		{
			EnsureTipCache(reason);
			return _cacheTile;
		}

		/// <summary>Force a fresh SetZoom remap. Unused — do not call from gameplay.</summary>
		[Obsolete("Do not use for gameplay. Invalidating tip cache only matters for the unused SetZoom tip path.")]
		internal static void InvalidateTipCache()
		{
			_cacheValid = false;
			_cacheUpdateCount = uint.MaxValue;
		}

		private static void EnsureTipCache(string reason = null)
		{
			uint update = Main.GameUpdateCount;
			int mx = Main.mouseX;
			int my = Main.mouseY;
			int sw = Main.screenWidth;
			int sh = Main.screenHeight;
			if (_cacheValid
				&& _cacheUpdateCount == update
				&& _cacheUiMouseX == mx
				&& _cacheUiMouseY == my
				&& _cacheScreenW == sw
				&& _cacheScreenH == sh)
				return;

			string tag = string.IsNullOrEmpty(reason) ? "unspecified" : reason;
			RbjDiagPolicy.NoteTipRemap(tag);

#pragma warning disable CS0618 // Legacy tip path only; WithWorldMouseZoom is Obsolete for gameplay reuse.
			_cacheWorld = WithWorldMouseZoom(() =>
			{
				_cacheWorldMouseX = Main.mouseX;
				_cacheWorldMouseY = Main.mouseY;
				return Main.MouseWorld;
			}, tag);
#pragma warning restore CS0618

			_cacheTile = _cacheWorld.ToTileCoordinates16();
			_cacheUpdateCount = update;
			_cacheUiMouseX = mx;
			_cacheUiMouseY = my;
			_cacheScreenW = sw;
			_cacheScreenH = sh;
			_cacheValid = true;
		}

		/// <summary>
		/// Legacy: run <paramref name="action"/> while mouse coords are mapped for the world view.
		/// Restores zoom context + mouse integers only — never mutates screenWidth/Height.
		/// Unused by current gameplay. Do not call for item/tile/NPC picking.
		/// </summary>
		[Obsolete("Do not use for gameplay picks. SetZoom_MouseInWorld remaps mouse coords and can mis-pick / flicker under ForcedMinimumZoom, BetterZoom, DUAL_SCREEN.")]
		internal static T WithWorldMouseZoom<T>(Func<T> action, string reason = null)
		{
			int mouseX = Main.mouseX;
			int mouseY = Main.mouseY;
			int inputX = PlayerInput.MouseX;
			int inputY = PlayerInput.MouseY;
			int swBefore = Main.screenWidth;
			int shBefore = Main.screenHeight;

			RbjRenderHealth.NoteZoomRemapEnter();
			PlayerInput.SetZoom_MouseInWorld();
			try
			{
				return action();
			}
			finally
			{
				PlayerInput.SetZoom_Context();
				RestoreMouse(mouseX, mouseY, inputX, inputY, logIfChanged: false, reason: null);
				RbjDiagPolicy.ObserveSetZoomSizes(
					swBefore, shBefore, Main.screenWidth, Main.screenHeight,
					string.IsNullOrEmpty(reason) ? "WithWorldMouseZoom" : "WithWorldMouseZoom:" + reason);
				RbjRenderHealth.NoteZoomRemapExit();
			}
		}

		/// <summary>Legacy SetZoom wrapper. Unused by gameplay.</summary>
		[Obsolete("Do not use for gameplay picks. SetZoom_MouseInWorld remaps mouse coords and can mis-pick / flicker under ForcedMinimumZoom, BetterZoom, DUAL_SCREEN.")]
		internal static void WithWorldMouseZoom(Action action, string reason = null)
		{
#pragma warning disable CS0618
			WithWorldMouseZoom(() =>
			{
				action();
				return 0;
			}, reason);
#pragma warning restore CS0618
		}

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

		/// <summary>
		/// Diagnostic one-liner. Does NOT call SetZoom / tip remap (that flickered cursorItemIcon).
		/// Compares UI MouseWorld vs tileTarget only.
		/// </summary>
		internal static string Snapshot()
		{
			int sw0 = Main.screenWidth;
			int sh0 = Main.screenHeight;
			int realW = PlayerInput.RealScreenWidth;
			int realH = PlayerInput.RealScreenHeight;
			Vector2 orig = PlayerInput.OriginalScreenSize;
			MouseSnap before = CaptureMouse();
			MouseState raw = Mouse.GetState();

			Vector2 uiCtx = Main.MouseWorld;
			Point16 uiTile = uiCtx.ToTileCoordinates16();

			int tx = Player.tileTargetX;
			int ty = Player.tileTargetY;

			Vector2 zoom = Main.GameViewMatrix.Zoom;
			float zoomTarget = Main.GameZoomTarget;

			int dUiTarget = Math.Max(Math.Abs(uiTile.X - tx), Math.Abs(uiTile.Y - ty));
			bool outside = before.MainX < 0 || before.MainY < 0
				|| before.MainX >= sw0 || before.MainY >= sh0;

			string dual = (realW != sw0 || realH != sh0) ? " DUAL_SCREEN" : "";
			string tipNote = _cacheValid
				? $"tipCache=({_cacheTile.X},{_cacheTile.Y})"
				: "tipCache=none";

			return
				$"cursorMethod=no-SetZoom-in-Snapshot{dual} " +
				$"zoom=({zoom.X:0.###},{zoom.Y:0.###}) zoomTarget={zoomTarget:0.###} " +
				$"scrUi=({before.MainX},{before.MainY}) " +
				$"rawMouse=({raw.X},{raw.Y}) " +
				$"win=({sw0},{sh0}) real=({realW},{realH}) orig=({orig.X:0},{orig.Y:0}) " +
				$"outsideWin={outside} " +
				$"worldUi=({uiCtx.X:0},{uiCtx.Y:0}) " +
				$"tileUiCtx=({uiTile.X},{uiTile.Y}) " +
				$"tileTarget=({tx},{ty}) dUiTarget={dUiTarget} {tipNote} " +
				$"(tileTarget=reach-clamped F-target; tip remap only on click pick)";
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
