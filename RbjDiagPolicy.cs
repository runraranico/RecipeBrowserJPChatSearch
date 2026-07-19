using System;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;

namespace RecipeBrowserJPChatSearch
{
	/// <summary>
	/// Diagnostics policy / counters / manual marks for log-driven fixes.
	/// Does not mutate game state except writing logs and chat feedback on mark.
	/// </summary>
	internal static class RbjDiagPolicy
	{
		/// <summary>Bump when diagnostic behavior changes (search logs for this id).</summary>
		internal const string DiagBuild = "20260719a";

		internal static int HoverHoldSkipCount;
		internal static int SetZoomSizeDeltaCount;
		internal static int TipRemapCount;
		internal static int IconWithRemapFrames;

		private const int MarkCapacity = 24;
		private static readonly string[] _marks = new string[MarkCapacity];
		private static int _markWrite;
		private static int _markCount;

		private static long _nextSizeLogTick;
		private static long _nextTipRemapLogTick;
		private static long _nextIconRemapLogTick;
		private static string _lastTipRemapReason = string.Empty;

		internal static void ResetSessionCounters()
		{
			HoverHoldSkipCount = 0;
			SetZoomSizeDeltaCount = 0;
			TipRemapCount = 0;
			IconWithRemapFrames = 0;
			WorldPlacedItemHover.WorldPickOkCount = 0;
			WorldPlacedItemHover.WorldPickMissCount = 0;
			_markWrite = 0;
			_markCount = 0;
			Array.Clear(_marks, 0, _marks.Length);
			_lastTipRemapReason = string.Empty;
		}

		internal static void LogPolicyFingerprint()
		{
			RbjDiag.Release(
				$"DiagPolicy build={DiagBuild} verbose={RbjDiag.Enabled} " +
				$"npc=A-closed/preHover>smart>talk-when-RBopen(noTip) invMs=syncRecipeCraftItemName " +
				$"worldPick=cursorIcon+MouseOverMS(noTip/noF) holdRepeat=OFF " +
				$"screenSizeForceRestore=OFF setZoomSizeObserve=ON " +
				$"NativeCursor={ModLoader.HasMod("NativeCursor")} " +
				$"BetterZoom={ModLoader.HasMod("BetterZoom")}");
		}

		/// <summary>Main / PlayerInput / OS raw mouse — no SetZoom side effects.</summary>
		internal static string MouseTriple()
		{
			MouseState raw = Mouse.GetState();
			bool outside = Main.mouseX < 0 || Main.mouseY < 0
				|| Main.mouseX >= Main.screenWidth || Main.mouseY >= Main.screenHeight;
			return
				$"mouseMain=({Main.mouseX},{Main.mouseY}) " +
				$"mouseInput=({PlayerInput.MouseX},{PlayerInput.MouseY}) " +
				$"mouseRaw=({raw.X},{raw.Y}) " +
				$"win={Main.screenWidth}x{Main.screenHeight} " +
				$"real={PlayerInput.RealScreenWidth}x{PlayerInput.RealScreenHeight} " +
				$"outsideWin={outside} NativeCursor={ModLoader.HasMod("NativeCursor")}";
		}

		internal static void NoteHoverHoldSkip() => HoverHoldSkipCount++;

		/// <summary>
		/// Tip cache miss → SetZoom. Idle MouseOver must not call this (cursorItemIcon flicker).
		/// </summary>
		internal static void NoteTipRemap(string reason)
		{
			TipRemapCount++;
			_lastTipRemapReason = reason ?? string.Empty;
			if (!RbjDiag.Enabled)
				return;

			long now = Environment.TickCount64;
			if (now < _nextTipRemapLogTick)
				return;

			_nextTipRemapLogTick = now + 1000;
			RbjDiag.Info(
				$"TipRemap reason='{_lastTipRemapReason}' count={TipRemapCount} " +
				$"remapsFrame={RbjRenderHealth.ZoomRemapsThisFrame} | {MouseTriple()}");
		}

		/// <summary>
		/// Vanilla cursorItemIcon visible in the same frame as a tip SetZoom — flicker suspect.
		/// </summary>
		internal static void ObserveIconVsTipRemap(bool iconEnabled, int iconId, int remapsThisFrame)
		{
			if (!iconEnabled || remapsThisFrame <= 0)
				return;

			IconWithRemapFrames++;
			if (!RbjDiag.Enabled)
				return;

			long now = Environment.TickCount64;
			if (now < _nextIconRemapLogTick)
				return;

			_nextIconRemapLogTick = now + 500;
			RbjDiag.Warn(
				$"IconFlickerSuspect iconId={iconId} remapsFrame={remapsThisFrame} " +
				$"lastTip='{_lastTipRemapReason}' tipRemaps={TipRemapCount} " +
				$"iconRemapFrames={IconWithRemapFrames} | {MouseTriple()}");
		}

		/// <summary>
		/// Read-only: log when screen size changes across SetZoom_MouseInWorld → Context.
		/// Never writes Main.screenWidth/Height.
		/// </summary>
		internal static void ObserveSetZoomSizes(int beforeW, int beforeH, int afterW, int afterH, string where)
		{
			if (beforeW == afterW && beforeH == afterH)
				return;

			SetZoomSizeDeltaCount++;
			long now = Environment.TickCount64;
			if (!RbjDiag.Enabled || now < _nextSizeLogTick)
				return;

			_nextSizeLogTick = now + 2000;
			RbjDiag.Info(
				$"SetZoom SIZE_DELTA where={where} before={beforeW}x{beforeH} after={afterW}x{afterH} " +
				$"real={PlayerInput.RealScreenWidth}x{PlayerInput.RealScreenHeight} " +
				$"(observe-only; no force restore) count={SetZoomSizeDeltaCount}");
		}

		internal static void Mark(string tag)
		{
			if (string.IsNullOrWhiteSpace(tag))
				tag = "mark";

			tag = tag.Trim();
			if (tag.Length > 80)
				tag = tag.Substring(0, 80);

			string line =
				$"{DateTime.Now:HH:mm:ss.fff} MARK '{tag}' | {MouseTriple()} | " +
				$"holdSkip={HoverHoldSkipCount} " +
				$"sizeDelta={SetZoomSizeDeltaCount} tipRemap={TipRemapCount} iconRemapFr={IconWithRemapFrames}";

			_marks[_markWrite] = line;
			_markWrite = (_markWrite + 1) % MarkCapacity;
			if (_markCount < MarkCapacity)
				_markCount++;

			RbjDiag.Release($"RBJ_MARK {line}");
			RbjRenderHealth.Mark($"MARK:{tag}");

			try
			{
				Main.NewText($"[RBJ] marked '{tag}' → Logs/RBJ_Debug_latest.txt", 120, 220, 255);
			}
			catch
			{
				// ignore
			}
		}

		internal static void LogSessionSummary(string reason)
		{
			RbjDiag.Release(
				$"DiagSummary reason={reason} build={DiagBuild} " +
				$"hoverHoldSkip={HoverHoldSkipCount} setZoomSizeDelta={SetZoomSizeDeltaCount} " +
				$"worldPickOk={WorldPlacedItemHover.WorldPickOkCount} " +
				$"worldPickMiss={WorldPlacedItemHover.WorldPickMissCount} " +
				$"tipRemap={TipRemapCount} iconRemapFrames={IconWithRemapFrames} marks={_markCount}");
		}
	}

	/// <summary>Chat: /rbjmark [tag] — stamps the log at this moment for screenshot correlation.</summary>
	public class RbjMarkCommand : ModCommand
	{
		public override CommandType Type => CommandType.Chat;

		public override string Command => "rbjmark";

		public override string Usage => "/rbjmark [tag]";

		public override string Description => "Stamp RBJ debug log (e.g. /rbjmark broken-ui)";

		public override void Action(CommandCaller caller, string input, string[] args)
		{
			string tag = args != null && args.Length > 0
				? string.Join(" ", args)
				: "mark";
			RbjDiagPolicy.Mark(tag);
		}
	}
}
