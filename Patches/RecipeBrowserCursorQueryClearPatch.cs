using System;
using System.Collections.Generic;
using System.Reflection;
using Terraria.ModLoader;

namespace RecipeBrowserJPChatSearch.Patches
{
	/// <summary>
	/// Clears text filters only after a cursor-query hotkey actually updates the query slot.
	/// </summary>
	internal static class RecipeBrowserCursorQueryClearPatch
	{
		private delegate void orig_ReplaceWithFake(object self, int type);

		private static readonly Dictionary<Type, FieldInfo> _skipHistoryCache = new();

		public static void Apply(Mod recipeBrowserMod)
		{
			HookReplaceWithFake(recipeBrowserMod.Code.GetType("RecipeBrowser.UIElements.UIQueryItemSlot"));
			HookReplaceWithFake(recipeBrowserMod.Code.GetType("RecipeBrowser.UIQueryItemSlot"));
			HookReplaceWithFake(recipeBrowserMod.Code.GetType("RecipeBrowser.UIElements.UIRecipeCatalogueQueryItemSlot"));
			HookReplaceWithFake(recipeBrowserMod.Code.GetType("RecipeBrowser.UIRecipeCatalogueQueryItemSlot"));
			HookReplaceWithFake(recipeBrowserMod.Code.GetType("RecipeBrowser.UIElements.UIBestiaryQueryItemSlot"));
			HookReplaceWithFake(recipeBrowserMod.Code.GetType("RecipeBrowser.UIBestiaryQueryItemSlot"));
		}

		private static void HookReplaceWithFake(Type querySlotType)
		{
			if (querySlotType == null)
				return;

			MethodInfo replaceWithFake = querySlotType.GetMethod(
				"ReplaceWithFake",
				BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (replaceWithFake == null || replaceWithFake.DeclaringType != querySlotType)
				return;

			MonoModHooks.Add(replaceWithFake, (orig_ReplaceWithFake orig, object self, int type) =>
			{
				orig(self, type);

				if (type <= 0 || IsHistoryNavigation(self))
					return;

				if (RecipeBrowserCursorSearchBridge.ConsumePendingCursorQueryClear())
					RecipeBrowserCursorSearchBridge.ClearAllSearchFilters();
			});
		}

		private static bool IsHistoryNavigation(object querySlot)
		{
			if (querySlot == null)
				return false;

			Type type = querySlot.GetType();
			if (!_skipHistoryCache.TryGetValue(type, out FieldInfo skipHistoryField))
			{
				skipHistoryField = type.GetField("skipHistory", BindingFlags.Instance | BindingFlags.NonPublic);
				_skipHistoryCache[type] = skipHistoryField;
			}

			return skipHistoryField != null && skipHistoryField.GetValue(querySlot) is true;
		}
	}
}
