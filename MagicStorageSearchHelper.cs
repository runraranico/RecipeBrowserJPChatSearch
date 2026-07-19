using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Terraria;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.UI;

namespace RecipeBrowserJPChatSearch
{
	/// <summary>
	/// Writes text into the open Magic Storage search bar (crafting station or storage heart).
	/// On crafting UI, also best-effort switches to Filter All + RecipeAll before searching.
	/// </summary>
	internal static class MagicStorageSearchHelper
	{
		private const string RecipeAllLocalizationKey = "Mods.MagicStorage.RecipeAll";

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
		private static MethodInfo _stateReset;
		private static MethodInfo _stateUnfocus;
		private static MethodInfo _setRefresh;

		// Soft filter reflection — failure must never block search.
		private static bool _filterResolved;
		private static bool _filterFailed;
		private static FieldInfo _recipeButtonsField;
		private static FieldInfo _filteringButtonsField;
		private static PropertyInfo _choiceProperty;
		private static MethodInfo _onChangedMethod;
		private static PropertyInfo _generalChoicesProperty;
		private static FieldInfo _choiceElementsField;
		private static FieldInfo _choiceElementOptionField;
		private static FieldInfo _choiceElementTextField;
		private static PropertyInfo _filteringOptionsProperty;
		private static MethodInfo _remapChoiceMethod;
		private static PropertyInfo _filteringAllTypeProperty;

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
				bool isCrafting = false;
				if (_isCraftingOpen.Invoke(null, null) is true)
				{
					ui = _craftingUiField.GetValue(null);
					isCrafting = true;
				}
				else if (_isStorageOpen.Invoke(null, null) is true)
				{
					ui = _storageUiField.GetValue(null);
				}

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

				// Crafting only: Filter All + show all known recipes. Fail soft — search continues.
				if (isCrafting)
					TryApplyCraftingSearchFilters(page);

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

		/// <summary>
		/// Clears the crafting UI search bar text (does not change recipe/filter buttons).
		/// Used when the panel 「リセット」 button fires OnMenuReset.
		/// </summary>
		internal static bool TryClearCraftingSearch()
		{
			if (!ModLoader.HasMod("MagicStorage") || !EnsureReflection())
				return false;

			try
			{
				if (_isCraftingOpen.Invoke(null, null) is not true)
					return false;

				object ui = _craftingUiField.GetValue(null);
				if (ui == null)
					return false;

				object page = _getDefaultPage.Invoke(ui, null);
				if (page == null)
					return false;

				object searchBar = _searchBarField.GetValue(page);
				if (searchBar == null)
					return false;

				object state = _stateProperty.GetValue(searchBar);
				if (state == null)
					return false;

				// Serous TextInputState.Set / Reset / Clear are no-ops when !IsActive.
				// Activate is required so the clear actually applies (do not remove).
				_stateActivate.Invoke(state, null);

				// Prefer Reset(true): clears text and drops focus (avoids leftover IME / focus).
				if (_stateReset != null)
					_stateReset.Invoke(state, new object[] { true });
				else
				{
					_stateSet.Invoke(state, new object[] { string.Empty });
					_stateUnfocus?.Invoke(state, null);
				}

				_setRefresh?.Invoke(null, new object[] { true });
				RbjDiag.Info("TryClearCraftingSearch OK");
				return true;
			}
			catch (Exception ex)
			{
				RbjDiag.Error("TryClearCraftingSearch failed", ex);
				return false;
			}
		}

		private static object _craftResetPanel;
		private static Action _craftResetClearHandler;
		private static EventInfo _onMenuResetEvent;
		private static bool _craftResetHooked;
		private static bool _craftResetGiveUp;
		private static int _craftResetRetryAttempts;
		private static int _craftResetRetryCooldownLeft;
		private static FieldInfo _craftPanelField;

		private const int CraftResetRetryIntervalFrames = 45;
		private const int CraftResetRetryMaxAttempts = 15;

		/// <summary>
		/// Subscribes to Magic Storage crafting panel OnMenuReset so 「リセット」 clears the search bar.
		/// Safe to call repeatedly; never double-registers. Pair with <see cref="TickCraftResetHookRetry"/>.
		/// </summary>
		internal static void TryHookCraftPanelResetClearSearch()
		{
			if (_craftResetHooked || _craftResetGiveUp || !ModLoader.HasMod("MagicStorage"))
				return;

			try
			{
				if (!EnsureReflection())
					return;

				object craftingUi = _craftingUiField?.GetValue(null);
				if (craftingUi == null)
					return;

				if (_craftPanelField == null)
				{
					const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
					_craftPanelField = craftingUi.GetType().GetField("panel", instance)
						?? craftingUi.GetType().BaseType?.GetField("panel", instance);
				}

				object panel = _craftPanelField?.GetValue(craftingUi);
				if (panel == null)
					return;

				EventInfo onMenuReset = panel.GetType().GetEvent("OnMenuReset", BindingFlags.Instance | BindingFlags.Public);
				if (onMenuReset == null)
					return;

				_craftResetClearHandler = OnCraftPanelMenuReset;
				onMenuReset.AddEventHandler(panel, _craftResetClearHandler);
				_onMenuResetEvent = onMenuReset;
				_craftResetPanel = panel;
				_craftResetHooked = true;
				_craftResetRetryCooldownLeft = 0;
				RbjDiag.Info(
					$"CraftResetClear: hooked OnMenuReset (attempt={_craftResetRetryAttempts})");
			}
			catch (Exception ex)
			{
				RbjDiag.Error("TryHookCraftPanelResetClearSearch failed", ex);
			}
		}

		/// <summary>
		/// After PostSetupContent's first hook attempt: if still not hooked, wait one interval before retrying.
		/// </summary>
		internal static void ArmCraftResetHookRetryIfNeeded()
		{
			if (_craftResetHooked || _craftResetGiveUp || !ModLoader.HasMod("MagicStorage"))
				return;

			if (_craftResetRetryCooldownLeft <= 0)
				_craftResetRetryCooldownLeft = CraftResetRetryIntervalFrames;
		}

		/// <summary>
		/// Sparse retry when PostSetupContent ran before craftingUI/panel existed.
		/// Not every frame — ~45 frames, max 15 attempts, then stop.
		/// </summary>
		internal static void TickCraftResetHookRetry()
		{
			if (_craftResetHooked || _craftResetGiveUp)
				return;

			if (!ModLoader.HasMod("MagicStorage"))
			{
				_craftResetGiveUp = true;
				return;
			}

			if (_craftResetRetryCooldownLeft > 0)
			{
				_craftResetRetryCooldownLeft--;
				return;
			}

			if (_craftResetRetryAttempts >= CraftResetRetryMaxAttempts)
			{
				_craftResetGiveUp = true;
				RbjDiag.Warn(
					$"CraftResetClear: give up after {CraftResetRetryMaxAttempts} attempts");
				return;
			}

			_craftResetRetryAttempts++;
			TryHookCraftPanelResetClearSearch();

			if (_craftResetHooked)
				return;

			_craftResetRetryCooldownLeft = CraftResetRetryIntervalFrames;
		}

		private static void OnCraftPanelMenuReset()
		{
			try
			{
				// Only clear when crafting UI is actually open (panel reset can fire while closed in edge cases).
				if (!IsCraftingUiOpen())
					return;

				TryClearCraftingSearch();
			}
			catch (Exception ex)
			{
				RbjDiag.Error("OnCraftPanelMenuReset failed", ex);
			}
		}

		private static void UnhookCraftPanelResetClearSearch()
		{
			try
			{
				if (_craftResetHooked
					&& _onMenuResetEvent != null
					&& _craftResetPanel != null
					&& _craftResetClearHandler != null)
				{
					_onMenuResetEvent.RemoveEventHandler(_craftResetPanel, _craftResetClearHandler);
				}
			}
			catch (Exception ex)
			{
				RbjDiag.Error("UnhookCraftPanelResetClearSearch failed", ex);
			}
			finally
			{
				_craftResetHooked = false;
				_craftResetGiveUp = false;
				_craftResetRetryAttempts = 0;
				_craftResetRetryCooldownLeft = 0;
				_craftResetPanel = null;
				_craftResetClearHandler = null;
				_onMenuResetEvent = null;
				_craftPanelField = null;
			}
		}

		/// <summary>
		/// Best-effort: filteringButtons → All, recipeButtons → RecipeAll (by localization / Definitions).
		/// Never throws to caller; never blocks search. No fixed Choice index fallbacks.
		/// </summary>
		private static void TryApplyCraftingSearchFilters(object page)
		{
			if (page == null || !EnsureFilterReflection())
			{
				RbjDiag.Info("CraftFilters skipped reason=reflect-fail");
				return;
			}

			try
			{
				bool recipeOk = TrySetRecipeAll(page);
				bool filterOk = TrySetFilteringAll(page);
				RbjDiag.Info($"CraftFilters recipeAll={recipeOk} filterAll={filterOk}");
			}
			catch (Exception ex)
			{
				RbjDiag.Warn($"CraftFilters failed (search continues): {ex.GetType().Name}: {ex.Message}");
			}
		}

		private static bool TrySetRecipeAll(object page)
		{
			if (_recipeButtonsField == null || _choiceProperty == null || _onChangedMethod == null)
				return false;

			object recipeButtons = _recipeButtonsField.GetValue(page);
			if (recipeButtons == null)
				return false;

			int target = TryFindChoiceByLocalizationKey(recipeButtons, RecipeAllLocalizationKey);
			if (target < 0)
			{
				RbjDiag.Info("CraftFilters RecipeAll not found (skip choice; search continues)");
				return false;
			}

			int before = _choiceProperty.GetValue(recipeButtons) is int b ? b : -1;
			if (before == target)
				return true; // already RecipeAll — do not call OnChanged

			_choiceProperty.SetValue(recipeButtons, target);
			_onChangedMethod.Invoke(recipeButtons, null);
			return true;
		}

		private static bool TrySetFilteringAll(object page)
		{
			if (_filteringButtonsField == null || _choiceProperty == null || _onChangedMethod == null)
				return false;

			object filteringButtons = _filteringButtonsField.GetValue(page);
			if (filteringButtons == null)
				return false;

			int target = TryFindFilteringAllChoiceIndex(filteringButtons);
			if (target < 0)
			{
				RbjDiag.Info("CraftFilters FilterAll not found (skip choice; search continues)");
				return false;
			}

			bool clearedGeneral = false;
			object generalObj = _generalChoicesProperty?.GetValue(filteringButtons);
			if (generalObj != null)
			{
				int count = 0;
				PropertyInfo countProp = generalObj.GetType().GetProperty("Count");
				if (countProp?.GetValue(generalObj) is int c)
					count = c;
				else if (generalObj is ICollection coll)
					count = coll.Count;

				if (count > 0)
				{
					MethodInfo clear = generalObj.GetType().GetMethod("Clear", Type.EmptyTypes);
					clear?.Invoke(generalObj, null);
					clearedGeneral = true;
				}
			}

			int before = _choiceProperty.GetValue(filteringButtons) is int b ? b : -1;
			bool choiceChanged = before != target;
			if (choiceChanged)
				_choiceProperty.SetValue(filteringButtons, target);

			// Already All with no general filters — skip refresh side effects.
			if (!choiceChanged && !clearedGeneral)
				return true;

			_onChangedMethod.Invoke(filteringButtons, null);
			return true;
		}

		private static int TryFindChoiceByLocalizationKey(object buttonChoice, string localizationKey)
		{
			if (_choiceElementsField == null || _choiceElementOptionField == null || _choiceElementTextField == null)
				return -1;

			if (_choiceElementsField.GetValue(buttonChoice) is not IEnumerable elements)
				return -1;

			foreach (object el in elements)
			{
				if (el == null)
					continue;

				if (_choiceElementTextField.GetValue(el) is not LocalizedText text)
					continue;

				if (!string.Equals(text.Key, localizationKey, StringComparison.Ordinal))
					continue;

				if (_choiceElementOptionField.GetValue(el) is int option && option >= 0)
					return option;
			}

			return -1;
		}

		private static int TryFindFilteringAllChoiceIndex(object filteringButtons)
		{
			int allType = TryGetFilteringAllType();
			if (allType < 0)
				return -1;

			// Prefer Options list + Type match (stable across button layout).
			if (_filteringOptionsProperty?.GetValue(filteringButtons) is IEnumerable options)
			{
				int index = 0;
				foreach (object opt in options)
				{
					if (opt == null)
					{
						index++;
						continue;
					}

					PropertyInfo typeProp = opt.GetType().GetProperty("Type", BindingFlags.Instance | BindingFlags.Public);
					if (typeProp?.GetValue(opt) is int t && t == allType)
						return index;

					index++;
				}
			}

			// Fallback: RemapChoice(i) == All.Type
			if (_remapChoiceMethod != null && _filteringOptionsProperty?.GetValue(filteringButtons) is ICollection countSource)
			{
				int count = countSource.Count;
				for (int i = 0; i < count; i++)
				{
					object remapped = _remapChoiceMethod.Invoke(filteringButtons, new object[] { i });
					if (remapped is int t && t == allType)
						return i;
				}
			}

			return -1;
		}

		private static int TryGetFilteringAllType()
		{
			if (_filteringAllTypeProperty == null)
				return -1;

			try
			{
				object allOpt = _filteringAllTypeProperty.GetValue(null);
				if (allOpt == null)
					return -1;

				PropertyInfo typeProp = allOpt.GetType().GetProperty("Type", BindingFlags.Instance | BindingFlags.Public);
				return typeProp?.GetValue(allOpt) is int t ? t : -1;
			}
			catch
			{
				return -1;
			}
		}

		private static bool EnsureFilterReflection()
		{
			if (_filterResolved)
				return !_filterFailed;

			_filterResolved = true;
			try
			{
				if (!ModLoader.TryGetMod("MagicStorage", out Mod magicStorage))
				{
					_filterFailed = true;
					return false;
				}

				Assembly asm = magicStorage.Code;
				const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
				const BindingFlags staticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

				// RecipesPage is nested: MagicStorage.UI.States.CraftingUIState+RecipesPage
				Type recipesPage = asm.GetType("MagicStorage.UI.States.CraftingUIState+RecipesPage")
					?? asm.GetTypes().FirstOrDefault(t => t.Name == "RecipesPage"
						&& t.DeclaringType?.Name == "CraftingUIState");
				Type accessPage = asm.GetType("MagicStorage.UI.States.BaseStorageUIAccessPage");
				Type buttonChoice = asm.GetType("MagicStorage.UI.NewUIButtonChoice");
				Type choiceElement = buttonChoice?.GetNestedType("ChoiceElement", instance)
					?? asm.GetType("MagicStorage.UI.NewUIButtonChoice+ChoiceElement");
				Type filteringButtonChoice = asm.GetType("MagicStorage.UI.FilteringOptionButtonChoice");
				Type filteringLoader = asm.GetType("MagicStorage.CrossMod.FilteringOptionLoader");
				Type definitions = filteringLoader?.GetNestedType("Definitions", staticFlags);

				_recipeButtonsField = recipesPage?.GetField("recipeButtons", instance);
				_filteringButtonsField = accessPage?.GetField("filteringButtons", instance);
				_choiceProperty = buttonChoice?.GetProperty("Choice", BindingFlags.Instance | BindingFlags.Public);
				_onChangedMethod = buttonChoice?.GetMethod("OnChanged", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
				_generalChoicesProperty = buttonChoice?.GetProperty("GeneralChoices", BindingFlags.Instance | BindingFlags.Public);
				_choiceElementsField = buttonChoice?.GetField("choiceElements", instance);
				_choiceElementOptionField = choiceElement?.GetField("option", instance);
				_choiceElementTextField = choiceElement?.GetField("text", instance);
				_filteringOptionsProperty = filteringButtonChoice?.GetProperty("Options", BindingFlags.Instance | BindingFlags.Public);
				_remapChoiceMethod = filteringButtonChoice?.GetMethod("RemapChoice", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(int) }, null)
					?? buttonChoice?.GetMethod("RemapChoice", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(int) }, null);
				_filteringAllTypeProperty = definitions?.GetProperty("All", staticFlags);

				_filterFailed = _recipeButtonsField == null
					|| _filteringButtonsField == null
					|| _choiceProperty == null
					|| _onChangedMethod == null;

				if (_filterFailed)
				{
					RbjDiag.Warn(
						$"CraftFilter reflection incomplete: recipeBtn={_recipeButtonsField != null} " +
						$"filterBtn={_filteringButtonsField != null} choice={_choiceProperty != null} " +
						$"onChanged={_onChangedMethod != null}");
				}
				else
				{
					RbjDiag.Info(
						$"CraftFilter reflection OK recipeLoc={_choiceElementsField != null} " +
						$"filterAllType={_filteringAllTypeProperty != null}");
				}

				return !_filterFailed;
			}
			catch (Exception ex)
			{
				_filterFailed = true;
				RbjDiag.Warn($"CraftFilter EnsureFilterReflection failed: {ex.GetType().Name}: {ex.Message}");
				return false;
			}
		}

		internal static bool IsAnyTargetUiOpen()
		{
			if (!ModLoader.HasMod("MagicStorage") || !EnsureReflection())
				return false;

			try
			{
				return IsCraftingUiOpen() || IsStorageUiOpen();
			}
			catch (Exception ex)
			{
				RbjDiag.Error("IsAnyTargetUiOpen failed", ex);
				return false;
			}
		}

		internal static bool IsCraftingUiOpen()
		{
			if (!ModLoader.HasMod("MagicStorage") || !EnsureReflection())
				return false;

			try
			{
				return _isCraftingOpen.Invoke(null, null) is true;
			}
			catch (Exception ex)
			{
				RbjDiag.Error("IsCraftingUiOpen failed", ex);
				return false;
			}
		}

		internal static bool IsStorageUiOpen()
		{
			if (!ModLoader.HasMod("MagicStorage") || !EnsureReflection())
				return false;

			try
			{
				return _isStorageOpen.Invoke(null, null) is true;
			}
			catch (Exception ex)
			{
				RbjDiag.Error("IsStorageUiOpen failed", ex);
				return false;
			}
		}

		/// <summary>Shared by Crafting Access opener (cached reflection).</summary>
		internal static bool EnsureReflectionPublic() => EnsureReflection();

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
				// Optional: used by craft-reset clear for focus-safe wipe (Set/Reset need IsActive).
				_stateReset = stateType?.GetMethod("Reset", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(bool) }, null);
				_stateUnfocus = stateType?.GetMethod("Unfocus", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);

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
			UnhookCraftPanelResetClearSearch();

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
			_stateReset = null;
			_stateUnfocus = null;
			_setRefresh = null;

			_filterResolved = false;
			_filterFailed = false;
			_recipeButtonsField = null;
			_filteringButtonsField = null;
			_choiceProperty = null;
			_onChangedMethod = null;
			_generalChoicesProperty = null;
			_choiceElementsField = null;
			_choiceElementOptionField = null;
			_choiceElementTextField = null;
			_filteringOptionsProperty = null;
			_remapChoiceMethod = null;
			_filteringAllTypeProperty = null;
		}
	}
}
