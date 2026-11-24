using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.UI;
using Terraria.UI.Chat;

namespace RecipeBrowserJPChatSearch
{
	internal static class MagicStorageRequiredObjectSearch
	{
		private static FieldInfo _textLines;
		private static FieldInfo _selectedRecipe;

		internal static void Initialize(Mod mod)
		{
			_textLines = null;
			_selectedRecipe = null;
			if (mod == null)
				return;

			Assembly assembly = mod.Code;
			_textLines = assembly.GetType("MagicStorage.UI.States.CraftingUIState")?.GetField(
				"reqObjTextLines", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			_selectedRecipe = assembly.GetType("MagicStorage.CraftingGUI")?.GetField(
				"selectedRecipe", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
		}

		internal static bool TryHandleCraftingRequiredObject()
		{
			if (_textLines == null || !MagicStorageSearchHelper.IsCraftingUiOpen()
				|| !MagicStorageSearchHelper.TryGetUiRoot(out UIElement root)
				|| _textLines.DeclaringType?.IsInstanceOfType(root) != true)
				return false;

			try
			{
				Vector2 mouse = new(Main.mouseX, Main.mouseY);
				if (_textLines.GetValue(root) is not IEnumerable<UIText> lines)
					return false;

				foreach (UIText line in lines)
				{
					if (line?.Parent == null || !line.Parent.ContainsPoint(mouse))
						continue;

					string name = PickLabelName(line.Text, line.GetDimensions(), mouse,
						Language.GetTextValue("LegacyInterface.23"), Measure);
					if (name == null)
						continue;
					if (_selectedRecipe?.GetValue(null) is Recipe recipe &&
						recipe.Conditions.Any(condition => condition.Description.Value == name))
						return false;

					if (RecipeBrowserCursorSearchBridge.TryConsumeSearchActionThisFrame("MS crafting required object")
						&& MagicStorageSearchHelper.TrySetSearch(ToSearchName(name)))
						SoundEngine.PlaySound(SoundID.MenuTick);
					return true;
				}
			}
			catch (Exception ex)
			{
				RbjDiag.Error("Magic Storage required object search failed", ex);
			}
			return false;
		}

		private static float Measure(string value)
			=> ChatManager.GetStringSize(FontAssets.MouseText.Value, value, Vector2.One).X;

		internal static string PickLabelName(string text, CalculatedStyle bounds, Vector2 mouse, string none, Func<string, float> measure)
		{
			// Use the live label rectangle in the same UI coordinates as Magic Storage's item slots.
			if (bounds.Width <= 0 || bounds.Height <= 0 || string.IsNullOrWhiteSpace(text)
				|| text.Trim() == none || mouse.X < bounds.X || mouse.X >= bounds.X + bounds.Width
				|| mouse.Y < bounds.Y - 3 || mouse.Y >= bounds.Y + bounds.Height + 3)
				return null;

			int start = 0;
			int parentheses = 0;
			for (int i = 0; i < text.Length - 1; i++)
			{
				if (text[i] == '(')
					parentheses++;
				else if (text[i] == ')' && parentheses > 0)
					parentheses--;
				else if (parentheses == 0 && text[i] == ',' && text[i + 1] == ' ')
				{
					// Multiple object names may share a native label. Measure only when splitting that label.
					float full = measure(text);
					if (full <= 0 || (mouse.X - bounds.X) / bounds.Width < measure(text[..(i + 2)]) / full)
						return text[start..i].Trim();
					start = i + 2;
				}
			}
			return text[start..].Trim();
		}

		internal static string ToSearchName(string name)
		{
			int suffix = name.LastIndexOf(" (", StringComparison.Ordinal);
			if (suffix > 0 && name.EndsWith(')') &&
				name[..suffix].Any(c => c is >= '\u3040' and <= '\u30ff' or >= '\u3400' and <= '\u9fff') &&
				name[(suffix + 2)..^1].All(c => c <= 127))
				return name[..suffix];
			return name;
		}
	}
}
