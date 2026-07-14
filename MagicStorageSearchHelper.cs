using System;
using System.Linq;
using System.Reflection;
using Terraria;
using Terraria.ModLoader;
using Terraria.UI;

namespace RecipeBrowserJPChatSearch
{
	/// <summary>
	/// Writes text into the open Magic Storage search bar (crafting station or storage heart).
	/// </summary>
	internal static class MagicStorageSearchHelper
	{
		private static bool _resolved;
		private static bool _failed;
		private static Type _magicUiType;
		private static MethodInfo _isCraftingOpen;
		private static MethodInfo _isStorageOpen;
		private static FieldInfo _craftingUiField;
		private static FieldInfo _storageUiField;
		private static MethodInfo _getDefaultPage;
		private static FieldInfo _searchBarField;
		private static PropertyInfo _stateProperty;
		private static MethodInfo _stateSet;
		private static MethodInfo _stateActivate;
		private static MethodInfo _setRefresh;

		internal static bool TrySetSearchFromItem(Item item)
		{
			if (item == null || item.IsAir)
				return false;

			string name = RecipeBrowserNameSearchHelper.GetSearchName(item);
			return !string.IsNullOrWhiteSpace(name) && TrySetSearch(name);
		}

		internal static bool TrySetSearch(string name)
		{
			if (!ModLoader.HasMod("MagicStorage") || !EnsureReflection())
				return false;

			try
			{
				object ui = null;
				if (_isCraftingOpen.Invoke(null, null) is true)
					ui = _craftingUiField.GetValue(null);
				else if (_isStorageOpen.Invoke(null, null) is true)
					ui = _storageUiField.GetValue(null);

				if (ui == null)
				{
					RbjDiag.Warn("TrySetSearch: no open crafting/storage UI");
					return false;
				}

				object page = _getDefaultPage.Invoke(ui, null);
				if (page == null)
				{
					RbjDiag.Warn("TrySetSearch: GetDefaultPage returned null");
					return false;
				}

				object searchBar = _searchBarField.GetValue(page);
				if (searchBar == null)
				{
					RbjDiag.Warn($"TrySetSearch: searchBar null on page {page.GetType().Name}");
					return false;
				}

				object state = _stateProperty.GetValue(searchBar);
				if (state == null)
				{
					RbjDiag.Warn("TrySetSearch: TextInputState null");
					return false;
				}

				_stateActivate.Invoke(state, null);
				_stateSet.Invoke(state, new object[] { name });
				_setRefresh?.Invoke(null, new object[] { true });
				RbjDiag.Info($"TrySetSearch OK name='{name}' ui={ui.GetType().Name}");
				return true;
			}
			catch (Exception ex)
			{
				RbjDiag.Error("TrySetSearch failed", ex);
				return false;
			}
		}

		internal static bool IsAnyTargetUiOpen()
		{
			if (!ModLoader.HasMod("MagicStorage") || !EnsureReflection())
				return false;

			try
			{
				return _isCraftingOpen.Invoke(null, null) is true
					|| _isStorageOpen.Invoke(null, null) is true;
			}
			catch (Exception ex)
			{
				RbjDiag.Error("IsAnyTargetUiOpen failed", ex);
				return false;
			}
		}

		/// <summary>
		/// True when the cursor is over a Magic Storage item slot / slot grid
		/// (MagicStorageItemSlot or NewUISlotZone). Name-transfer owns that click.
		/// </summary>
		internal static bool IsMouseOverItemSlot()
		{
			try
			{
				if (!TryGetUiRoot(out UIElement root))
					return false;

				Microsoft.Xna.Framework.Vector2 mouse = new(Main.mouseX, Main.mouseY);
				UIElement under = root.GetElementAt(mouse);
				while (under != null)
				{
					string name = under.GetType().Name;
					if (name is "MagicStorageItemSlot" or "NewUISlotZone")
						return true;
					under = under.Parent;
				}

				// GetElementAt can return the outer panel; hit-test slots by ContainsPoint.
				return FindElementUnderMouse(root, mouse, el =>
				{
					string n = el.GetType().Name;
					return n is "MagicStorageItemSlot" or "NewUISlotZone";
				}) != null;
			}
			catch (Exception ex)
			{
				RbjDiag.Error("IsMouseOverItemSlot failed", ex);
				return false;
			}
		}

		/// <summary>
		/// Resolve the MS item under the cursor even when GetElementAt misses child slots
		/// (common with UI scale / overlapping panels).
		/// </summary>
		internal static bool TryGetItemUnderMouseRobust(out Item item, out string how)
		{
			item = null;
			how = "no-root";
			if (!TryGetUiRoot(out UIElement root))
				return false;

			Microsoft.Xna.Framework.Vector2 mouse = new(Main.mouseX, Main.mouseY);
			UIElement under = root.GetElementAt(mouse);
			if (Patches.SearchHotkeyProbe.TryResolveMsItem(under, out item, out how))
				return true;

			if (TryGetItemFromHoveredSlotZone(out item, out how))
				return true;

			Type slotType = Patches.MiddleClickTransferPatch.MsSlotType;
			PropertyInfo stored = Patches.MiddleClickTransferPatch.MsStoredItemProperty;
			if (slotType != null && stored != null)
			{
				UIElement slot = FindElementUnderMouse(root, mouse, el => slotType.IsInstanceOfType(el));
				if (slot != null)
				{
					item = stored.GetValue(slot) as Item;
					if (item != null && !item.IsAir)
					{
						how = "ContainsPoint-MagicStorageItemSlot";
						return true;
					}

					how = "ContainsPoint-MagicStorageItemSlot-empty";
				}
			}

			how = how ?? "no-ms-item";
			return false;
		}

		private static UIElement FindElementUnderMouse(
			UIElement root,
			Microsoft.Xna.Framework.Vector2 mouse,
			Func<UIElement, bool> predicate)
		{
			if (root == null || predicate == null)
				return null;

			// Depth-first: prefer deepest matching child.
			foreach (UIElement child in root.Children)
			{
				UIElement found = FindElementUnderMouse(child, mouse, predicate);
				if (found != null)
					return found;
			}

			if (predicate(root) && root.ContainsPoint(mouse))
				return root;

			return null;
		}

		/// <summary>
		/// Scan MS UI for a NewUISlotZone that currently has HoverSlot &gt;= 0 with an item.
		/// Covers cases where GetElementAt returns a parent panel instead of the zone.
		/// </summary>
		internal static bool TryGetItemFromHoveredSlotZone(out Item item, out string how)
		{
			item = null;
			how = "no-zone";
			if (!TryGetUiRoot(out UIElement root))
			{
				how = "no-root";
				return false;
			}

			return TryFindHoveredNewUiSlotZone(root, out item, out how);
		}

		private static bool TryFindHoveredNewUiSlotZone(UIElement el, out Item item, out string how)
		{
			item = null;
			how = "no-zone";
			if (el == null)
				return false;

			if (el.GetType().Name == "NewUISlotZone"
				&& Patches.SearchHotkeyProbe.TryResolveMsItem(el, out item, out how)
				&& item != null
				&& !item.IsAir)
				return true;

			foreach (UIElement child in el.Children)
			{
				if (TryFindHoveredNewUiSlotZone(child, out item, out how))
					return true;
			}

			how = "no-zone";
			return false;
		}

		internal static bool TryGetUiRoot(out UIElement root)
		{
			root = null;
			if (!ModLoader.HasMod("MagicStorage") || !EnsureReflection())
				return false;

			try
			{
				object ui = null;
				if (_isCraftingOpen.Invoke(null, null) is true)
					ui = _craftingUiField.GetValue(null);
				else if (_isStorageOpen.Invoke(null, null) is true)
					ui = _storageUiField.GetValue(null);

				if (ui is not UIElement element)
					return false;

				root = element;
				return true;
			}
			catch (Exception ex)
			{
				RbjDiag.Error("TryGetUiRoot failed", ex);
				return false;
			}
		}

		private static bool EnsureReflection()
		{
			if (_resolved)
				return !_failed;

			_resolved = true;
			try
			{
				if (!ModLoader.TryGetMod("MagicStorage", out Mod magicStorage))
				{
					_failed = true;
					return false;
				}

				Assembly asm = magicStorage.Code;
				_magicUiType = asm.GetType("MagicStorage.Common.Systems.MagicUI");
				if (_magicUiType == null)
				{
					_failed = true;
					RbjDiag.Warn("MagicUI type not found");
					return false;
				}

				const BindingFlags staticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
				const BindingFlags instanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

				_isCraftingOpen = _magicUiType.GetMethod("IsCraftingUIOpen", staticFlags);
				_isStorageOpen = _magicUiType.GetMethod("IsStorageUIOpen", staticFlags);
				_craftingUiField = _magicUiType.GetField("craftingUI", staticFlags);
				_storageUiField = _magicUiType.GetField("storageUI", staticFlags);
				_setRefresh = _magicUiType.GetMethod("SetRefresh", staticFlags, null, new[] { typeof(bool) }, null);

				Type baseStorageUi = asm.GetType("MagicStorage.UI.States.BaseStorageUI");
				_getDefaultPage = baseStorageUi?
					.GetMethods(instanceFlags)
					.FirstOrDefault(m => m.Name == "GetDefaultPage"
						&& !m.IsGenericMethodDefinition
						&& m.GetParameters().Length == 0);

				Type accessPage = asm.GetType("MagicStorage.UI.States.BaseStorageUIAccessPage");
				_searchBarField = accessPage?.GetField("searchBar", instanceFlags);

				Type searchBarType = asm.GetType("MagicStorage.UI.Input.NewUISearchBar");
				_stateProperty = searchBarType?.GetProperty("State", BindingFlags.Instance | BindingFlags.Public);
				if (_stateProperty == null
					&& ModLoader.TryGetMod("SerousCommonLib", out Mod serousForBar))
				{
					_stateProperty = serousForBar.Code.GetType("SerousCommonLib.UI.TextInputBar")
						?.GetProperty("State", BindingFlags.Instance | BindingFlags.Public);
				}

				Type stateType = null;
				if (ModLoader.TryGetMod("SerousCommonLib", out Mod serous))
					stateType = serous.Code.GetType("SerousCommonLib.API.Input.TextInputState");
				_stateSet = stateType?.GetMethod("Set", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(string) }, null);
				_stateActivate = stateType?.GetMethod("Activate", BindingFlags.Instance | BindingFlags.Public);

				_failed = _isCraftingOpen == null
					|| _isStorageOpen == null
					|| _craftingUiField == null
					|| _storageUiField == null
					|| _getDefaultPage == null
					|| _searchBarField == null
					|| _stateProperty == null
					|| _stateSet == null
					|| _stateActivate == null;

				if (_failed)
				{
					RbjDiag.Warn(
						$"MagicStorage reflection incomplete: " +
						$"craftOpen={_isCraftingOpen != null}, storageOpen={_isStorageOpen != null}, " +
						$"getDefaultPage={_getDefaultPage != null}, searchBar={_searchBarField != null}, " +
						$"state={_stateProperty != null}, set={_stateSet != null}, activate={_stateActivate != null}");
				}
				else
				{
					RbjDiag.Info("MagicStorage reflection OK");
				}

				return !_failed;
			}
			catch (Exception ex)
			{
				_failed = true;
				RbjDiag.Error("EnsureReflection failed", ex);
				return false;
			}
		}

		internal static void Unload()
		{
			_resolved = false;
			_failed = false;
			_magicUiType = null;
			_isCraftingOpen = null;
			_isStorageOpen = null;
			_craftingUiField = null;
			_storageUiField = null;
			_getDefaultPage = null;
			_searchBarField = null;
			_stateProperty = null;
			_stateSet = null;
			_stateActivate = null;
			_setRefresh = null;
		}
	}
}
