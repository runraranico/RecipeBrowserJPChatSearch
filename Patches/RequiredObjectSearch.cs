using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;
using Terraria.UI.Chat;

namespace RecipeBrowserJPChatSearch.Patches
{
	/// <summary>Hotkey-only support for the names drawn by Recipe Browser's recipe info.</summary>
	internal static class RequiredObjectSearch
	{
		private static FieldInfo _catalogueInstance;
		private static FieldInfo _recipeInfo;
		private static FieldInfo _selectedIndex;
		private static Func<int, string> _getTileName;

		internal static void Initialize(Mod recipeBrowser)
		{
			Unload();
			if (recipeBrowser == null)
				return;
			try
			{
				const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
				Assembly code = recipeBrowser.Code;
				Type catalogue = code.GetType("RecipeBrowser.RecipeCatalogueUI");
				_catalogueInstance = catalogue?.GetField("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
				_recipeInfo = catalogue?.GetField("recipeInfo", instance);
				_selectedIndex = catalogue?.GetField("selectedIndex", instance);
				MethodInfo tileName = code.GetType("RecipeBrowser.Utilities")?.GetMethod(
					"GetTileName", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
					null, new[] { typeof(int) }, null);
				if (tileName != null)
					_getTileName = (Func<int, string>)tileName.CreateDelegate(typeof(Func<int, string>));
			}
			catch
			{
				Unload();
			}
		}

		internal static void Unload()
		{
			_catalogueInstance = null;
			_recipeInfo = null;
			_selectedIndex = null;
			_getTileName = null;
		}

		internal static bool TryHandleHoveredRequiredObject()
		{
			if (!TryGetHoveredTarget(out Item item, out string searchName))
				return false;
			if (!RecipeBrowserCursorSearchBridge.TryConsumeSearchActionThisFrame("RB-required-object"))
				return true;

			// Use the existing destination routes; an unsuccessful transfer still consumes this input.
			try
			{
				MagicStorageUiKind kind = MagicStorageCraftingAccessHelper.GetCurrentUiKind();
				if (kind != MagicStorageUiKind.None && kind != MagicStorageUiKind.Storage && kind != MagicStorageUiKind.Crafting)
					MagicStorageSearchHelper.CancelPendingCraftSearches();
				bool success;
				if (kind == MagicStorageUiKind.None)
					success = item != null
						? MagicStorageCraftingAccessHelper.TryOpenNearestCraftingAccessAndSetSearch(item, null, out _)
						: MagicStorageCraftingAccessHelper.TryOpenNearestCraftingAccessAndSetSearchText(searchName, out _);
				else if (item == null)
					success = MagicStorageSearchHelper.TrySetSearch(searchName);
				else if (kind == MagicStorageUiKind.Crafting)
					success = MagicStorageSearchHelper.TrySetSearchFromItemAndSelectCraftRecipe(item, null);
				else
					success = MagicStorageSearchHelper.TrySetSearchFromItem(item);

				if (success)
				{
					SoundEngine.PlaySound(SoundID.MenuTick);
					if (item != null)
						HoverTooltipSuppress.Hold(item, reason: "RB-required-object");
				}
			}
			catch
			{
				// Optional integration: do not pass a recognized text target to world/item search.
			}
			return true;
		}

		private static bool TryGetHoveredTarget(out Item item, out string searchName)
		{
			item = null;
			searchName = null;
			if (_catalogueInstance == null || _recipeInfo == null || _selectedIndex == null || _getTileName == null)
				return false;
			try
			{
				if (!RecipeBrowserCursorSearchBridge.TryGetUiState(out _, out int panel) || panel != 0
					|| !RecipeBrowserCursorSearchBridge.TryGetRecipeBrowserPanelUnderMouse(out UIElement under))
					return false;
				object catalogue = _catalogueInstance.GetValue(null);
				if (catalogue == null || !(_recipeInfo.GetValue(catalogue) is UIElement info))
					return false;
				UIElement ancestor = under;
				while (ancestor != null && ancestor != info)
					ancestor = ancestor.Parent;
				if (ancestor == null)
					return false;
				int index = (int)_selectedIndex.GetValue(catalogue);
				if (index < 0 || index >= Main.recipe.Length || Main.recipe[index] == null)
					return false;

				List<string> names = GetRequiredNames(Main.recipe[index], _getTileName, out List<int> tiles);
				int hit = FindHoveredName(names,
					text => ChatManager.GetStringSize(FontAssets.MouseText.Value, text, Vector2.One, -1f),
					info.GetInnerDimensions().Position(), new Vector2(Main.mouseX, Main.mouseY));
				if (hit < 0)
					return false;
				searchName = names[hit];
				int type = tiles[hit] < 0 ? 0 : TileLoader.GetItemDropFromTypeAndStyle(tiles[hit], 0);
				if (type > ItemID.None && type < ItemLoader.ItemCount)
				{
					item = new Item();
					item.SetDefaults(type);
				}
				return true;
			}
			catch
			{
				return false;
			}
		}

		internal static List<string> GetRequiredNames(Recipe recipe, Func<int, string> tileName, out List<int> tiles)
		{
			var names = new List<string>();
			tiles = new List<int>();
			// Follow UIRecipeInfo's data order. No requirements produces no target, regardless of language.
			for (int i = 0; i < recipe.requiredTile.Count && recipe.requiredTile[i] != -1; i++)
			{
				int tile = recipe.requiredTile[i];
				names.Add(tileName(tile) ?? string.Empty);
				tiles.Add(tile);
			}
			foreach (var condition in recipe.Conditions)
			{
				names.Add(condition.Description.Value ?? string.Empty);
				tiles.Add(-1);
			}
			return names;
		}

		internal static int FindHoveredName(IReadOnlyList<string> names, Func<string, Vector2> measure, Vector2 origin, Vector2 mouse)
		{
			if (names.Count == 0 || mouse.Y < origin.Y + 24f || mouse.Y >= origin.Y + 50f)
				return -1;
			var text = new StringBuilder();
			foreach (string name in names)
			{
				if (name == null || name.IndexOfAny(new[] { '\r', '\n', '[', ']' }) >= 0)
					return -1; // Do not guess positions for multiline text or nested snippets.
				if (text.Length > 0)
					text.Append(", ");
				text.Append("[c/FFFFFF:").Append(name).Append(']');
			}
			float width = measure(text.ToString()).X;
			if (!float.IsFinite(width) || width <= 0)
				return -1;
			float scale = Math.Min(1f, 170f / width);
			text.Clear();
			int hit = -1;
			float distance = float.MaxValue;
			for (int i = 0; i < names.Count; i++)
			{
				if (i > 0)
					text.Append(", ");
				float left = measure(text.ToString()).X * scale;
				text.Append("[c/FFFFFF:").Append(names[i]).Append(']');
				float right = measure(text.ToString()).X * scale;
				float x = mouse.X - origin.X;
				if (!string.IsNullOrWhiteSpace(names[i]) && x >= Math.Max(0f, left - 2f)
					&& x < Math.Min(170f, right + 2f))
				{
					float d = Math.Abs(x - (left + right) / 2f);
					if (d < distance) { hit = i; distance = d; }
				}
			}
			return hit;
		}
	}
}
