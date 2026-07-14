using System;
using System.Reflection;
using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;

namespace RecipeBrowserJPChatSearch.Patches
{
	internal static class RecipeBrowserCursorSearchPatch
	{
		private delegate void orig_ProcessTriggers(object self, TriggersSet triggersSet);

		public static void Apply(Mod recipeBrowserMod)
		{
			RecipeBrowserCursorSearchBridge.Initialize(recipeBrowserMod);

			Type playerType = recipeBrowserMod.Code.GetType("RecipeBrowser.RecipeBrowserPlayer");
			if (playerType == null)
				return;

			MethodInfo processTriggers = playerType.GetMethod("ProcessTriggers", BindingFlags.Instance | BindingFlags.Public);
			if (processTriggers == null)
				return;

			MonoModHooks.Add(processTriggers, (orig_ProcessTriggers orig, object self, TriggersSet triggersSet) =>
			{
				bool queryKeyPressed = ShouldHandleCursorQueryKey();
				bool overlayHotkey = ShouldHandleOverlayCursorQuery();

				if (queryKeyPressed || overlayHotkey)
				{
					RecipeBrowserCursorSearchBridge.MarkPendingCursorQueryClear();
					RecipeBrowserCursorSearchBridge.ClearAllSearchFilters();
				}

				// Suppress native QueryHoveredItem while our search hotkey owns the action.
				Item hoverBackup = null;
				bool suppressHover = RecipeBrowserCursorSearchBridge.IsSearchHotkeyHeld() || overlayHotkey;
				if (suppressHover)
				{
					hoverBackup = Main.HoverItem;
					Main.HoverItem = new Item();
				}

				try
				{
					orig(self, triggersSet);
				}
				finally
				{
					if (suppressHover)
						Main.HoverItem = hoverBackup;
				}

				if (queryKeyPressed || overlayHotkey)
					RecipeBrowserCursorSearchBridge.ClearAllSearchFilters();

				RecipeBrowserCursorSearchBridge.ResetPendingCursorQueryClear();
			});
		}

		private static bool ShouldHandleCursorQueryKey()
		{
			if (ChatBrowseHelper.BrowseMode)
				return false;

			// Inventory / MS / RB search-hotkey transfers are handled in PostDraw.
			if (RecipeBrowserCursorSearchBridge.IsSearchHotkeyHeld())
				return false;

			return RecipeBrowserCursorSearchBridge.IsQueryHotkeyPressedThisFrame();
		}

		/// <summary>
		/// Past-log browse: clear leftover name filters; actual query runs in PostDraw on search hotkey.
		/// </summary>
		private static bool ShouldHandleOverlayCursorQuery()
		{
			if (!ChatBrowseHelper.BrowseMode)
				return false;

			return RecipeBrowserCursorSearchBridge.IsSearchHotkeyJustPressed();
		}
	}
}
