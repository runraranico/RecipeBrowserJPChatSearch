using System;
using System.Reflection;
using Terraria;
using Terraria.ModLoader;

namespace RecipeBrowserJPChatSearch.Patches
{
	/// <summary>
	/// Observes <see cref="Main.MouseText"/> draws for HoverLoc diagnostics.
	/// Does not clear <see cref="Main.HoverItem"/> and does not mute tooltips.
	/// </summary>
	internal static class HoverTooltipDrawPatch
	{
		private delegate void orig_MouseTextSimple(
			Main self,
			string cursorText,
			int rare,
			byte diff,
			int hackedMouseX,
			int hackedMouseY,
			int hackedScreenWidth,
			int hackedScreenHeight,
			int pushWidthX);

		private delegate void orig_MouseTextBuff(
			Main self,
			string cursorText,
			string buffTooltip,
			int rare,
			byte diff,
			int hackedMouseX,
			int hackedMouseY,
			int hackedScreenWidth,
			int hackedScreenHeight,
			int pushWidthX,
			bool noOverride);

		private delegate void orig_MouseTextNoOverride(
			Main self,
			string cursorText,
			int rare,
			byte diff,
			int hackedMouseX,
			int hackedMouseY,
			int hackedScreenWidth,
			int hackedScreenHeight,
			int pushWidthX);

		private delegate void orig_MouseTextHackZoom(
			Main self,
			string text,
			int itemRarity,
			byte diff,
			string buffTooltip);

		private delegate void orig_MouseTextHackZoomSimple(Main self, string text, string buffTooltip);

		private static bool _applied;

		internal static void Apply()
		{
			if (_applied)
				return;

			BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
			Type main = typeof(Main);

			MethodInfo simple = main.GetMethod(
				nameof(Main.MouseText),
				flags,
				null,
				new[]
				{
					typeof(string), typeof(int), typeof(byte),
					typeof(int), typeof(int), typeof(int), typeof(int), typeof(int)
				},
				null);
			if (simple != null)
			{
				MonoModHooks.Add(simple, (orig_MouseTextSimple orig, Main self,
					string cursorText, int rare, byte diff,
					int hackedMouseX, int hackedMouseY, int hackedScreenWidth, int hackedScreenHeight, int pushWidthX) =>
				{
					HoverTooltipLocationProbe.NoteMouseText(
						"MouseText", cursorText, rare,
						hackedMouseX, hackedMouseY, hackedScreenWidth, hackedScreenHeight);
					orig(self, cursorText, rare, diff, hackedMouseX, hackedMouseY, hackedScreenWidth, hackedScreenHeight, pushWidthX);
				});
			}

			MethodInfo withBuff = main.GetMethod(
				nameof(Main.MouseText),
				flags,
				null,
				new[]
				{
					typeof(string), typeof(string), typeof(int), typeof(byte),
					typeof(int), typeof(int), typeof(int), typeof(int), typeof(int), typeof(bool)
				},
				null);
			if (withBuff != null)
			{
				MonoModHooks.Add(withBuff, (orig_MouseTextBuff orig, Main self,
					string cursorText, string buffTooltip, int rare, byte diff,
					int hackedMouseX, int hackedMouseY, int hackedScreenWidth, int hackedScreenHeight, int pushWidthX, bool noOverride) =>
				{
					string combined = string.IsNullOrEmpty(buffTooltip)
						? cursorText
						: $"{cursorText} | buff={Truncate(buffTooltip, 24)}";
					HoverTooltipLocationProbe.NoteMouseText(
						"MouseText+buff", combined, rare,
						hackedMouseX, hackedMouseY, hackedScreenWidth, hackedScreenHeight);
					orig(self, cursorText, buffTooltip, rare, diff, hackedMouseX, hackedMouseY, hackedScreenWidth, hackedScreenHeight, pushWidthX, noOverride);
				});
			}

			MethodInfo noOverride = main.GetMethod(nameof(Main.MouseTextNoOverride), flags);
			if (noOverride != null)
			{
				MonoModHooks.Add(noOverride, (orig_MouseTextNoOverride orig, Main self,
					string cursorText, int rare, byte diff,
					int hackedMouseX, int hackedMouseY, int hackedScreenWidth, int hackedScreenHeight, int pushWidthX) =>
				{
					HoverTooltipLocationProbe.NoteMouseText(
						"MouseTextNoOverride", cursorText, rare,
						hackedMouseX, hackedMouseY, hackedScreenWidth, hackedScreenHeight);
					orig(self, cursorText, rare, diff, hackedMouseX, hackedMouseY, hackedScreenWidth, hackedScreenHeight, pushWidthX);
				});
			}

			MethodInfo hackZoom = main.GetMethod(
				nameof(Main.MouseTextHackZoom),
				flags,
				null,
				new[] { typeof(string), typeof(int), typeof(byte), typeof(string) },
				null);
			if (hackZoom != null)
			{
				MonoModHooks.Add(hackZoom, (orig_MouseTextHackZoom orig, Main self,
					string text, int itemRarity, byte diff, string buffTooltip) =>
				{
					string combined = string.IsNullOrEmpty(buffTooltip)
						? text
						: $"{text} | buff={Truncate(buffTooltip, 24)}";
					HoverTooltipLocationProbe.NoteMouseTextHackZoom("MouseTextHackZoom", combined, itemRarity);
					orig(self, text, itemRarity, diff, buffTooltip);
				});
			}

			MethodInfo hackZoomSimple = main.GetMethod(
				nameof(Main.MouseTextHackZoom),
				flags,
				null,
				new[] { typeof(string), typeof(string) },
				null);
			if (hackZoomSimple != null)
			{
				MonoModHooks.Add(hackZoomSimple, (orig_MouseTextHackZoomSimple orig, Main self,
					string text, string buffTooltip) =>
				{
					string combined = string.IsNullOrEmpty(buffTooltip)
						? text
						: $"{text} | buff={Truncate(buffTooltip, 24)}";
					HoverTooltipLocationProbe.NoteMouseTextHackZoom("MouseTextHackZoomSimple", combined, 0);
					orig(self, text, buffTooltip);
				});
			}

			_applied = true;
			RbjDiag.Info(
				$"HoverTooltipDrawPatch applied " +
				$"MouseText={simple != null} MouseTextBuff={withBuff != null} " +
				$"NoOverride={noOverride != null} HackZoom={hackZoom != null}/{hackZoomSimple != null}");
		}

		private static string Truncate(string s, int max)
		{
			if (string.IsNullOrEmpty(s))
				return string.Empty;
			return s.Length <= max ? s : s.Substring(0, max) + "…";
		}
	}
}
