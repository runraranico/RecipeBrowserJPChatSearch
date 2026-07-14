using System.Reflection;
using Terraria;
using Terraria.ModLoader;

namespace RecipeBrowserJPChatSearch
{
	/// <summary>
	/// Magic Storage → Recipe Browser transfer helpers.
	/// </summary>
	internal static class RecipeBrowserNameSearchHelper
	{
		private static FieldInfo _itemUpdateNeededField;
		private static bool _fieldsCached;

		/// <summary>
		/// MS middle-click:
		/// panel 0/1 → query slot; panel 2 (Item) → name-search only;
		/// panel 3 (Bestiary) → switch to Recipe catalogue query (same as inventory middle-click).
		/// </summary>
		internal static bool TryTransferFromMagicStorage(Item item)
		{
			if (item == null || item.IsAir)
				return false;

			RecipeBrowserCursorSearchBridge.TryInitialize();
			if (!RecipeBrowserCursorSearchBridge.IsReady)
			{
				RbjDiag.Warn("TryTransferFromMagicStorage: bridge not ready");
				return false;
			}

			if (!RecipeBrowserCursorSearchBridge.TryGetUiState(out _, out int currentPanel))
			{
				RbjDiag.Warn("TryTransferFromMagicStorage: Recipe Browser UI not ready");
				return false;
			}

			RecipeBrowserCursorSearchBridge.ShowRecipeBrowser();

			if (currentPanel == 2)
			{
				string name = GetSearchName(item);
				if (string.IsNullOrWhiteSpace(name))
					return false;

				bool ok = TrySetNameSearchOnItemTab(name);
				RbjDiag.Info($"MS→RB Item-tab name search ok={ok} name='{name}'");
				return ok;
			}

			RecipeBrowserCursorSearchBridge.PerformHoveredItemQuery(item.type, allowToggleClose: false);
			RbjDiag.Info($"MS→RB query slot panel={currentPanel} type={item.type}");
			return true;
		}

		/// <summary>
		/// Item catalogue tab has no query slot — write the name filter only.
		/// Also used by inventory middle-click when Item tab is open.
		/// </summary>
		internal static bool TrySetNameSearchOnItemTab(string name)
		{
			EnsureUpdateNeededFields();

			object textBox = RecipeBrowserCursorSearchBridge.GetItemNameFilterTextBox();
			if (textBox == null)
			{
				RbjDiag.Warn("TrySetNameSearchOnItemTab: item name filter missing");
				return false;
			}

			RecipeBrowserCursorSearchBridge.WriteNameFilter(textBox, name);
			object catalogue = RecipeBrowserCursorSearchBridge.GetItemCatalogueInstance();
			if (_itemUpdateNeededField != null && catalogue != null)
				_itemUpdateNeededField.SetValue(catalogue, true);

			return true;
		}

		internal static bool TrySetNameSearchOnItemTabFromItem(Item item)
		{
			if (item == null || item.IsAir)
				return false;

			string name = GetSearchName(item);
			return !string.IsNullOrWhiteSpace(name) && TrySetNameSearchOnItemTab(name);
		}

		internal static string GetSearchName(Item item)
		{
			if (!string.IsNullOrWhiteSpace(item.Name))
				return item.Name.Trim();

			string localized = Lang.GetItemNameValue(item.type);
			return string.IsNullOrWhiteSpace(localized) ? string.Empty : localized.Trim();
		}

		private static void EnsureUpdateNeededFields()
		{
			if (_fieldsCached)
				return;

			_fieldsCached = true;
			if (!ModLoader.TryGetMod("RecipeBrowser", out Mod recipeBrowser))
				return;

			const BindingFlags anyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
			_itemUpdateNeededField = recipeBrowser.Code.GetType("RecipeBrowser.ItemCatalogueUI")
				?.GetField("updateNeeded", anyInstance);
		}
	}
}
