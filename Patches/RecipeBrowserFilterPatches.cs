using System;
using System.Collections.Generic;
using System.Reflection;
using Terraria;
using Terraria.ModLoader;

namespace RecipeBrowserJPChatSearch.Patches
{
	/// <summary>
	/// Supplement Recipe Browser filters with localized name matching.
	/// Localized fallback applies only when vanilla would pass except for the English name check.
	/// </summary>
	internal static class RecipeBrowserFilterPatches
	{
		private delegate bool orig_PassItemFilters(object self, object slot);
		private delegate bool orig_PassRecipeFilters(object self, object recipeSlot, Recipe recipe, List<int> groups);
		private delegate bool orig_PassNpcFilters(object self, object slot);

		private static FieldInfo _textBoxCurrentString;
		private static FieldInfo _catalogueSlotItem;
		private static FieldInfo _npcSlotNpc;

		public static void Apply(Mod recipeBrowserMod)
		{
			const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

			_textBoxCurrentString = recipeBrowserMod.Code.GetType("RecipeBrowser.NewUITextBox")
				?.GetField("currentString", instance);

			Type itemCatalogueType = recipeBrowserMod.Code.GetType("RecipeBrowser.ItemCatalogueUI");
			if (itemCatalogueType != null)
			{
				FieldInfo nameFilter = itemCatalogueType.GetField("itemNameFilter", instance);
				MethodInfo passItemFilters = itemCatalogueType.GetMethod("PassItemFilters", instance);
				_catalogueSlotItem = recipeBrowserMod.Code.GetType("RecipeBrowser.UIItemCatalogueItemSlot")
					?.GetField("item", instance);

				if (passItemFilters != null && nameFilter != null && _textBoxCurrentString != null)
				{
					MonoModHooks.Add(passItemFilters, (orig_PassItemFilters orig, object self, object slot) =>
						PassWithLocalizedItemName(orig, self, slot, nameFilter));
				}
			}

			Type recipeCatalogueType = recipeBrowserMod.Code.GetType("RecipeBrowser.RecipeCatalogueUI");
			if (recipeCatalogueType != null)
			{
				FieldInfo nameFilter = recipeCatalogueType.GetField("itemNameFilter", instance);
				MethodInfo passRecipeFilters = recipeCatalogueType.GetMethod("PassRecipeFilters", instance);

				if (passRecipeFilters != null && nameFilter != null && _textBoxCurrentString != null)
				{
					MonoModHooks.Add(passRecipeFilters, (orig_PassRecipeFilters orig, object self, object recipeSlot, Recipe recipe, List<int> groups) =>
					{
						object nameFilterBox = nameFilter.GetValue(self);
						string query = GetFilterText(nameFilterBox);

						return PassWithLocalizedNameFilter(
							nameFilterBox,
							query,
							() => orig(self, recipeSlot, recipe, groups),
							() => LocalizedSearchHelper.ItemMatches(recipe.createItem, query));
					});
				}
			}

			Type bestiaryType = recipeBrowserMod.Code.GetType("RecipeBrowser.BestiaryUI");
			if (bestiaryType != null)
			{
				FieldInfo nameFilter = bestiaryType.GetField("npcNameFilter", instance);
				MethodInfo passNpcFilters = bestiaryType.GetMethod("PassNPCFilters", instance);
				_npcSlotNpc = recipeBrowserMod.Code.GetType("RecipeBrowser.UINPCSlot")
					?.GetField("npc", instance);

				if (passNpcFilters != null && nameFilter != null && _textBoxCurrentString != null)
				{
					MonoModHooks.Add(passNpcFilters, (orig_PassNpcFilters orig, object self, object slot) =>
						PassWithLocalizedNpcName(orig, self, slot, nameFilter));
				}
			}
		}

		private static bool PassWithLocalizedItemName(orig_PassItemFilters orig, object self, object slot, FieldInfo nameFilterField)
		{
			object nameFilterBox = nameFilterField.GetValue(self);
			string query = GetFilterText(nameFilterBox);

			return PassWithLocalizedNameFilter(
				nameFilterBox,
				query,
				() => orig(self, slot),
				() => _catalogueSlotItem?.GetValue(slot) is Item item && LocalizedSearchHelper.ItemMatches(item, query));
		}

		private static bool PassWithLocalizedNpcName(orig_PassNpcFilters orig, object self, object slot, FieldInfo nameFilterField)
		{
			object nameFilterBox = nameFilterField.GetValue(self);
			string query = GetFilterText(nameFilterBox);

			return PassWithLocalizedNameFilter(
				nameFilterBox,
				query,
				() => orig(self, slot),
				() => _npcSlotNpc?.GetValue(slot) is NPC npc && LocalizedSearchHelper.NpcNameMatches(npc.netID, query));
		}

		private static bool PassWithLocalizedNameFilter(
			object nameFilterBox,
			string query,
			Func<bool> runOrig,
			Func<bool> runLocalizedMatch)
		{
			if (string.IsNullOrEmpty(query))
				return runOrig();

			if (runOrig())
				return true;

			string saved = GetFilterText(nameFilterBox);
			SetFilterTextSilently(nameFilterBox, string.Empty);
			try
			{
				if (!runOrig())
					return false;

				return runLocalizedMatch();
			}
			finally
			{
				SetFilterTextSilently(nameFilterBox, saved);
			}
		}

		private static string GetFilterText(object textBox)
		{
			return _textBoxCurrentString?.GetValue(textBox) as string ?? string.Empty;
		}

		private static void SetFilterTextSilently(object textBox, string text)
		{
			_textBoxCurrentString?.SetValue(textBox, text ?? string.Empty);
		}
	}
}
