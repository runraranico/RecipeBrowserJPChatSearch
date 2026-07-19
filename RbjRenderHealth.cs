using System;
using System.Text;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;

namespace RecipeBrowserJPChatSearch
{
	/// <summary>
	/// Black-screen / render-health diagnostics:
	/// event trail, zoom remap nesting, mouse leak checks, periodic pulses.
	/// Search RBJ_Debug_latest.txt for: RbjRenderHealth / ZOOM_MISMATCH / ZOOM/MOUSE LEAK / BlackTrail
	/// </summary>
	internal static class RbjRenderHealth
	{
		private const int TrailCapacity = 32;

		private static long _nextPeriodicTick;
		private static long _nextMismatchTick;
		private static bool _wasComposing;
		private static bool _wasBrowsing;
		private static bool _wasPlayerInv;
		private static int _postDrawCount;
		private static int _prevHoverType;
		private static readonly string[] _trail = new string[TrailCapacity];
		private static int _trailWrite;
		private static int _trailCount;

		// Filled by RbjCursor each WithWorldMouseZoom call.
		internal static int ZoomRemapsThisFrame;
		internal static int ZoomDepth;
		internal static int ZoomRemapsTotalSession;

		internal static void NotifyPostDrawBegin(string path)
		{
			_postDrawCount++;
			ZoomRemapsThisFrame = 0;
			// Depth should be 0 at PostDraw entry; leftover means a prior remap escaped finally.
			if (ZoomDepth != 0)
			{
				RbjDiag.Warn(
					$"RbjRenderHealth ZOOM_DEPTH_LEAK at PostDraw enter depth={ZoomDepth} path={path}");
				ZoomDepth = 0;
			}
		}

		internal static void NoteZoomRemapEnter()
		{
			ZoomRemapsThisFrame++;
			ZoomRemapsTotalSession++;
			ZoomDepth++;
		}

		internal static void NoteZoomRemapExit()
		{
			if (ZoomDepth > 0)
				ZoomDepth--;
		}

		/// <summary>Record a short breadcrumb (kept in a ring; dumped with pulses / suspects).</summary>
		internal static void Mark(string evt)
		{
			if (!RbjDiag.Enabled || string.IsNullOrEmpty(evt))
				return;

			string line = $"{DateTime.Now:HH:mm:ss.fff} {evt}";
			_trail[_trailWrite] = line;
			_trailWrite = (_trailWrite + 1) % TrailCapacity;
			if (_trailCount < TrailCapacity)
				_trailCount++;
		}

		internal static void MarkWithSnap(string evt, string path = "event")
		{
			Mark(evt);
			if (RbjDiag.Enabled)
				RbjDiag.Info($"RbjRenderHealth EVENT {evt} | {Snapshot(path)}");
		}

		internal static void DumpTrail(string reason)
		{
			if (!RbjDiag.Enabled || _trailCount == 0)
				return;

			var sb = new StringBuilder(512);
			sb.Append("BlackTrail reason=").Append(reason).Append(" last=").Append(_trailCount).Append(':');
			int start = (_trailWrite - _trailCount + TrailCapacity) % TrailCapacity;
			for (int i = 0; i < _trailCount; i++)
			{
				string e = _trail[(start + i) % TrailCapacity];
				if (string.IsNullOrEmpty(e))
					continue;
				sb.Append(" | ").Append(e);
			}

			RbjDiag.Info(sb.ToString());
		}

		internal static void OnChatClosed()
		{
			Patches.ChatParseMessageCache.Invalidate();
			Mark("chat-closed");
			RbjDiag.Info("RbjRenderHealth chat-closed → parse-cache invalidate");
		}

		internal static void TickAfterPostDraw(string path, RbjCursor.MouseSnap? mouseAtEntry = null)
		{
			bool composing = Main.drawingPlayerChat;
			bool browsing = ChatBrowseHelper.IsDisplayingOverlay;
			bool playerInv = Main.playerInventory;
			int hover = Main.HoverItem != null && !Main.HoverItem.IsAir ? Main.HoverItem.type : 0;

			if (!_wasComposing && composing)
			{
				Mark("chat-open");
				RbjDiag.Info($"RbjRenderHealth chat-open {Snapshot(path)}");
			}

			if (_wasComposing && !composing)
				OnChatClosed();

			if (!_wasBrowsing && browsing)
			{
				Mark("browse-start");
				RbjDiag.Info($"RbjRenderHealth browse-start {Snapshot(path)}");
			}

			if (_wasBrowsing && !browsing)
			{
				Mark("browse-end");
				RbjDiag.Info($"RbjRenderHealth browse-end {Snapshot(path)}");
			}

			if (!_wasPlayerInv && playerInv)
				Mark("inv-open");
			if (_wasPlayerInv && !playerInv)
				Mark("inv-close");

			if (hover != _prevHoverType)
			{
				if (hover != 0)
					Mark($"hover→{hover}");
				else if (_prevHoverType != 0)
					Mark($"hover-clear←{_prevHoverType}");
				_prevHoverType = hover;
			}

			_wasComposing = composing;
			_wasBrowsing = browsing;
			_wasPlayerInv = playerInv;

			if (ZoomDepth != 0)
			{
				RbjDiag.Warn(
					$"RbjRenderHealth ZOOM_DEPTH_LEAK after {path} depth={ZoomDepth} remapsFrame={ZoomRemapsThisFrame}");
				DumpTrail("zoom-depth-leak");
				ZoomDepth = 0;
			}

			if (ZoomRemapsThisFrame >= 8)
			{
				RbjDiag.Warn(
					$"RbjRenderHealth ZOOM_REMAP_SPAM remapsFrame={ZoomRemapsThisFrame} path={path} | {Snapshot(path)}");
				DumpTrail("zoom-remap-spam");
			}

			if (mouseAtEntry.HasValue)
			{
				var snap = mouseAtEntry.Value;
				if (Main.mouseX != snap.MainX || Main.mouseY != snap.MainY
					|| PlayerInput.MouseX != snap.InputX || PlayerInput.MouseY != snap.InputY)
				{
					RbjDiag.Warn(
						$"RbjRenderHealth ZOOM/MOUSE LEAK after {path} " +
						$"now=({Main.mouseX},{Main.mouseY})/{PlayerInput.MouseX},{PlayerInput.MouseY} " +
						$"want=({snap.MainX},{snap.MainY})/{snap.InputX},{snap.InputY} | {Snapshot(path)}");
					DumpTrail("mouse-leak");
					RbjCursor.RestoreMouse(snap);
				}
			}

			// After CaptureFrameHints: icon + tip remap same frame ⇒ flicker suspect.
			RbjDiagPolicy.ObserveIconVsTipRemap(
				WorldPlacedItemHover.FrameIconEnabled,
				WorldPlacedItemHover.FrameIconId,
				ZoomRemapsThisFrame);

			string snapText = Snapshot(path);
			bool badZoom = snapText.Contains("BAD_ZOOM");
			// Steady ZOOM_MISMATCH with BetterZoom is normal at extreme zoom — not a black-screen signal alone.
			bool remapAnomaly = ZoomRemapsThisFrame >= 3 || ZoomDepth != 0;
			bool suspect = badZoom || (snapText.Contains("ZOOM_MISMATCH") && remapAnomaly);
			long now = Environment.TickCount64;

			if (suspect && now >= _nextMismatchTick)
			{
				_nextMismatchTick = now + 5000;
				RbjDiag.Warn($"RbjRenderHealth suspect black-world culprit: {snapText}");
				DumpTrail("zoom-mismatch");
			}

			if (now < _nextPeriodicTick)
				return;

			_nextPeriodicTick = now + 5000;
			if (composing || browsing || playerInv || HoverTooltipSuppress.Active || RbjDiag.Enabled)
			{
				RbjDiag.Info(
					$"RbjRenderHealth pulse postDraws={_postDrawCount} remapsFrame={ZoomRemapsThisFrame} " +
					$"remapsSession={ZoomRemapsTotalSession} {snapText}");
				DumpTrail("pulse");
				_postDrawCount = 0;
			}
		}

		internal static string Snapshot(string path)
		{
			int hover = Main.HoverItem != null && !Main.HoverItem.IsAir ? Main.HoverItem.type : 0;
			Vector2 zoom = Main.GameViewMatrix.Zoom;
			float zt = Main.GameZoomTarget;
			bool zoomMismatch = zt > 0.01f
				&& (Math.Abs(zoom.X - zt) > 0.2f || Math.Abs(zoom.Y - zt) > 0.2f);
			bool badZoom = float.IsNaN(zoom.X) || float.IsInfinity(zoom.X)
				|| float.IsNaN(zoom.Y) || float.IsInfinity(zoom.Y);
			bool betterZoom = ModLoader.HasMod("BetterZoom");
			string zoomFlag = badZoom ? " BAD_ZOOM" : (zoomMismatch ? " ZOOM_MISMATCH" : "");
			Vector2 screen = Main.screenPosition;
			float uiScale = Main.UIScale;
			int chatLen = Main.chatText?.Length ?? 0;

			return
				$"path={path} compose={Main.drawingPlayerChat} chatLen={chatLen} " +
				$"browse={ChatBrowseHelper.BrowseMode} " +
				$"linger={ChatBrowseHelper.IsDisplayingOverlay && !ChatBrowseHelper.BrowseMode} " +
				$"hoverType={hover} hoverHold={HoverTooltipSuppress.Active}/{HoverTooltipSuppress.HeldType} " +
				$"mouse=({Main.mouseX},{Main.mouseY}) mouseUi={(Main.LocalPlayer != null && Main.LocalPlayer.mouseInterface)} " +
				$"win=({Main.screenWidth}x{Main.screenHeight}) uiScale={uiScale:0.###} " +
				$"screenPos=({screen.X:0},{screen.Y:0}) " +
				$"zoom=({zoom.X:0.###},{zoom.Y:0.###}) zoomTarget={zt:0.###}{zoomFlag} " +
				$"zoomDepth={ZoomDepth} remapsFrame={ZoomRemapsThisFrame} " +
				$"BetterZoom={betterZoom} " +
				$"playerInv={Main.playerInventory} gameMenu={Main.gameMenu}";
		}

		internal static void LogStartupConflicts()
		{
			bool betterZoom = ModLoader.HasMod("BetterZoom");
			bool localizer = ModLoader.HasMod("ExternalLocalizer") || ModLoader.HasMod("MachineTranslate");
			bool chatImprover = ModLoader.HasMod("ChatImprover");
			bool magicStorage = ModLoader.HasMod("MagicStorage");
			bool recipeBrowser = ModLoader.HasMod("RecipeBrowser");
			int realW = Terraria.GameInput.PlayerInput.RealScreenWidth;
			int realH = Terraria.GameInput.PlayerInput.RealScreenHeight;
			string dual = (realW != Main.screenWidth || realH != Main.screenHeight) ? " DUAL_SCREEN" : "";
			RbjDiag.Release(
				$"Session fingerprint BetterZoom={betterZoom} MagicStorage={magicStorage} " +
				$"RecipeBrowser={recipeBrowser} ExternalLocalizer/MachineTranslate={localizer} " +
				$"ChatImprover={chatImprover} NativeCursor={ModLoader.HasMod("NativeCursor")} " +
				$"verbose={RbjDiag.Enabled} diagBuild={RbjDiagPolicy.DiagBuild} " +
				$"win={Main.screenWidth}x{Main.screenHeight} real={realW}x{realH}{dual} " +
				$"uiScale={Main.UIScale:0.###}");
			RbjDiagPolicy.LogPolicyFingerprint();
			RbjDiag.Info(
				"RbjRenderHealth breadcrumb: BlackTrail / ZOOM_MISMATCH / SIZE_DELTA / DUAL_SCREEN / " +
				"NpcHover REJECT / F-fallback REJECT / RBJ_MARK");
			RbjDiag.Info(
				"Mark broken moments: chat /rbjmark broken-ui. " +
				"World pick = cursorItemIcon only (no tip/SetZoom/F). " +
				"NPC = PreHover/smart/talk while RB open. Inv/MS sync Recipe+Craft+ItemName.");
			if (betterZoom)
			{
				RbjDiag.Warn(
					"BetterZoom detected. Extreme zoom can mis-pick tiles vs UI tip. " +
					"Prefer vanilla-range zoom for consistent Workshop behavior.");
			}
		}
	}
}
