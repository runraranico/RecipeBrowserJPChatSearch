using System;
using Terraria;

namespace RecipeBrowserJPChatSearch
{
	/// <summary>
	/// Short capture window after search-hotkey / transfer: logs where MouseText
	/// is asked to draw (hacked vs real cursor) and HoverItem type changes.
	/// Write path: RBJ_Debug_latest.txt via <see cref="RbjDiag"/>.
	/// </summary>
	internal static class HoverTooltipLocationProbe
	{
		private const int CaptureMs = 1500;
		private const int MaxLinesPerCapture = 48;

		private static long _untilTick;
		private static int _lines;
		private static string _trigger = string.Empty;
		private static int _lastHoverType = int.MinValue;
		private static string _lastTextKey = string.Empty;

		internal static bool Capturing =>
			Environment.TickCount64 < _untilTick && _lines < MaxLinesPerCapture;

		internal static void Arm(string trigger)
		{
			_untilTick = Environment.TickCount64 + CaptureMs;
			_lines = 0;
			_trigger = trigger ?? string.Empty;
			_lastHoverType = int.MinValue;
			_lastTextKey = string.Empty;
			RbjDiag.Info(
				$"HoverLoc CAPTURE begin ms={CaptureMs} trigger='{_trigger}' " +
				$"cursor=({Main.mouseX},{Main.mouseY}) " +
				$"hoverType={(Main.HoverItem != null && !Main.HoverItem.IsAir ? Main.HoverItem.type : 0)} " +
				$"holdActive={HoverTooltipSuppress.Active} holdType={HoverTooltipSuppress.HeldType}");
		}

		internal static void NoteHoverItemDelta(string where)
		{
			if (!Capturing)
				return;

			int type = Main.HoverItem != null && !Main.HoverItem.IsAir ? Main.HoverItem.type : 0;
			if (type == _lastHoverType)
				return;

			int prev = _lastHoverType;
			_lastHoverType = type;
			string name = type > 0 && Main.HoverItem != null ? Truncate(Main.HoverItem.Name, 40) : "";
			Emit(
				$"DELTA where={where} hover {prev}->{type} name='{name}' " +
				$"cursor=({Main.mouseX},{Main.mouseY}) " +
				$"hold={HoverTooltipSuppress.Active}/{HoverTooltipSuppress.HeldType}");
		}

		internal static void NoteMouseText(
			string api,
			string text,
			int rare,
			int hackedMouseX,
			int hackedMouseY,
			int hackedScreenWidth = -1,
			int hackedScreenHeight = -1)
		{
			if (!Capturing)
				return;

			NoteHoverItemDelta($"pre-{api}");

			int hoverType = Main.HoverItem != null && !Main.HoverItem.IsAir ? Main.HoverItem.type : 0;
			string tip = Truncate(text, 48);
			string key = $"{api}|{hoverType}|{hackedMouseX},{hackedMouseY}|{tip}";
			// Dedupe identical spam within the same capture (e.g. every frame same tip).
			bool sameAsLast = key == _lastTextKey;
			_lastTextKey = key;
			if (sameAsLast)
				return;

			bool hacked = hackedMouseX != -1 || hackedMouseY != -1;
			int drawX = hackedMouseX != -1 ? hackedMouseX : Main.mouseX;
			int drawY = hackedMouseY != -1 ? hackedMouseY : Main.mouseY;
			int dx = drawX - Main.mouseX;
			int dy = drawY - Main.mouseY;
			double dist = Math.Sqrt((double)dx * dx + (double)dy * dy);

			string region = ClassifyScreenRegion(drawX, drawY);

			Emit(
				$"DRAW api={api} rare={rare} hoverType={hoverType} " +
				$"text='{tip}' " +
				$"cursor=({Main.mouseX},{Main.mouseY}) " +
				$"hacked=({hackedMouseX},{hackedMouseY}) useHacked={hacked} " +
				$"drawAt=({drawX},{drawY}) dCursor=({dx},{dy}) dist={dist:0} " +
				$"region={region} " +
				$"scr=({Main.screenWidth}x{Main.screenHeight}) " +
				$"hackScr=({hackedScreenWidth}x{hackedScreenHeight}) " +
				$"hold={HoverTooltipSuppress.Active}/{HoverTooltipSuppress.HeldType} " +
				$"msLeft={Math.Max(0, _untilTick - Environment.TickCount64)}");
		}

		internal static void NoteMouseTextHackZoom(string api, string text, int rare)
		{
			// HackZoom uses current mouse; still log so we see which API drew.
			NoteMouseText(api, text, rare, -1, -1);
		}

		private static void Emit(string message)
		{
			if (!Capturing)
				return;

			_lines++;
			RbjDiag.Info($"HoverLoc [{_lines}/{MaxLinesPerCapture}] {message}");
			if (_lines >= MaxLinesPerCapture)
			{
				RbjDiag.Info($"HoverLoc CAPTURE end (line cap) trigger='{_trigger}'");
				_untilTick = 0;
			}
		}

		/// <summary>
		/// Rough on-screen bucket so logs map to user's screenshots
		/// (equip column / inventory / RB-ish / center).
		/// </summary>
		internal static string ClassifyScreenRegion(int x, int y)
		{
			int w = Math.Max(1, Main.screenWidth);
			int h = Math.Max(1, Main.screenHeight);
			string hBand = x < w * 0.28 ? "L" : x > w * 0.72 ? "R" : "C";
			string vBand = y < h * 0.28 ? "T" : y > h * 0.72 ? "B" : "M";

			// Named hints that match player screenshots.
			if (hBand == "R" && vBand != "B")
				return $"{hBand}{vBand}/equip-ish";
			if (hBand == "L" && vBand == "B")
				return $"{hBand}{vBand}/inv-ish";
			if (hBand == "C" && vBand == "M")
				return $"{hBand}{vBand}/center-world-ish";
			if (hBand != "L" && vBand != "T" && x > w * 0.35 && x < w * 0.95)
				return $"{hBand}{vBand}/rb-panel-ish";
			return $"{hBand}{vBand}";
		}

		private static string Truncate(string s, int max)
		{
			if (string.IsNullOrEmpty(s))
				return string.Empty;
			s = s.Replace('\n', ' ').Replace('\r', ' ');
			return s.Length <= max ? s : s.Substring(0, max) + "…";
		}
	}
}
