using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using Microsoft.Xna.Framework.Input;
using RecipeBrowserJPChatSearch.Patches;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;

namespace RecipeBrowserJPChatSearch
{
	internal static class RecipeBrowserCursorSearchBridge
	{
		private static Mod _recipeBrowserMod;
		private static ModKeybind _queryHoveredItemHotKey;
		private static MethodInfo _setTextMethod;
		private static MethodInfo _unfocusMethod;
		private static FieldInfo _focusedField;
		private static FieldInfo _currentStringField;
		private static FieldInfo _recipeBrowserUiInstanceField;
		private static FieldInfo _recipeBrowserShowField;
		private static PropertyInfo _recipeBrowserShowProperty;
		private static FieldInfo _recipeBrowserCurrentPanelField;
		private static PropertyInfo _recipeBrowserCurrentPanelProperty;
		private static FieldInfo _mainPanelField;
		private static FieldInfo _favoritePanelField;
		private static bool _initFailLogged;
		private static FieldInfo _recipeCatalogueInstanceField;
		private static FieldInfo _itemCatalogueInstanceField;
		private static FieldInfo _bestiaryInstanceField;
		private static FieldInfo _craftInstanceField;
		private static FieldInfo _recipeNameFilterField;
		private static FieldInfo _recipeDescriptionFilterField;
		private static FieldInfo _itemNameFilterField;
		private static FieldInfo _itemDescriptionFilterField;
		private static FieldInfo _bestiaryNameFilterField;
		private static FieldInfo _recipeUpdateNeededField;
		private static FieldInfo _recipeQueryItemField;
		private static FieldInfo _bestiaryQueryItemField;
		private static FieldInfo _craftRecipeResultSlotField;
		private static MethodInfo _recipeQueryReplaceMethod;
		private static MethodInfo _craftSetItemMethod;
		private static MethodInfo _bestiaryQueryReplaceMethod;
		private static FieldInfo _tabControllerField;
		private static MethodInfo _tabControllerSetPanelMethod;
		private static FieldInfo _bestiaryUpdateNeededField;
		private static FieldInfo _bestiaryNpcSlotsField;
		private static FieldInfo _bestiaryQueryLootNpcField;
		private static FieldInfo _bestiaryNpcGridField;
		private static MethodInfo _bestiaryUpdateMethod;
		private static MethodInfo _bestiarySetNpcMethod;
		private static MethodInfo _npcGridGotoMethod;
		private static Type _rbNpcSlotType;
		private static FieldInfo _rbNpcSlotNpcTypeField;
		private static FieldInfo _rbNpcSlotNpcField;
		private static Type _rbItemSlotType;
		private static FieldInfo _rbItemSlotItemField;
		private static PropertyInfo _keybindBindingsProperty;
		private static bool _initialized;
		private static int _lastHoveredItemType;
		private static int _lastHoveredItemStickyTtl;
		private const int HoverItemStickyFrames = 30;
		private static bool _pendingCursorQueryClear;

		internal static void MarkPendingCursorQueryClear() => _pendingCursorQueryClear = true;

		internal static bool ConsumePendingCursorQueryClear()
		{
			if (!_pendingCursorQueryClear)
				return false;

			_pendingCursorQueryClear = false;
			return true;
		}

		internal static void ResetPendingCursorQueryClear() => _pendingCursorQueryClear = false;

		internal static bool IsQueryHotkeyPressedThisFrame()
		{
			TryInitialize();

			if (_queryHoveredItemHotKey == null || IsModifierBlockingCursorSearch())
				return false;

			if (_queryHoveredItemHotKey.JustPressed)
				return true;

			return WasBindingJustPressed(
				_queryHoveredItemHotKey,
				PreviousKeyboard,
				PreviousMouseLeft,
				PreviousMouseRight,
				PreviousMouseMiddle);
		}

		internal static KeyboardState PreviousKeyboard { get; private set; }
		internal static bool PreviousMouseLeft { get; private set; }
		internal static bool PreviousMouseRight { get; private set; }
		internal static bool PreviousMouseMiddle { get; private set; }
		/// <summary>Hardware middle button (ignores Terraria / AdvancedKeybinds remaps of Main.mouseMiddle).</summary>
		internal static bool PreviousPhysicalMiddle { get; private set; }

		private static uint _rbUnderMouseCacheFrame = uint.MaxValue;
		private static int _rbUnderMouseCacheX = int.MinValue;
		private static int _rbUnderMouseCacheY = int.MinValue;
		private static bool _rbUnderMouseCacheHit;
		private static UIElement _rbUnderMouseCacheElement;

		/// <summary>
		/// Search hotkey just pressed (default Mouse3 → hardware middle, ignores MOUSE3 remaps).
		/// </summary>
		internal static bool IsSearchHotkeyJustPressed()
			=> ModKeybinds.IsSearchHotkeyJustPressed();

		/// <summary>Search hotkey currently held.</summary>
		internal static bool IsSearchHotkeyHeld()
			=> ModKeybinds.IsSearchHotkeyHeld();

		internal static void Initialize(Mod recipeBrowserMod) => TryInitialize(recipeBrowserMod);

		internal static void Unload()
		{
			_initialized = false;
			_initFailLogged = false;
			_recipeBrowserMod = null;
			_queryHoveredItemHotKey = null;
			_recipeBrowserShowProperty = null;
			_recipeBrowserShowField = null;
			_mainPanelField = null;
			_favoritePanelField = null;
			_lastHoveredItemType = 0;
			_itemFieldCache.Clear();
			_rbUnderMouseCacheFrame = uint.MaxValue;
			_rbUnderMouseCacheHit = false;
			_rbUnderMouseCacheElement = null;
		}

		internal static bool IsPhysicalMiddleDown()
			=> Mouse.GetState().MiddleButton == ButtonState.Pressed;

		internal static void RememberInputState(
			KeyboardState keyboard,
			bool mouseLeft,
			bool mouseRight,
			bool mouseMiddle)
		{
			PreviousKeyboard = keyboard;
			PreviousMouseLeft = mouseLeft;
			PreviousMouseRight = mouseRight;
			PreviousMouseMiddle = mouseMiddle;
			PreviousPhysicalMiddle = IsPhysicalMiddleDown();
		}

		internal static bool TryInitialize(Mod recipeBrowserMod = null)
		{
			if (_initialized)
				return true;

			if (recipeBrowserMod == null)
			{
				if (_recipeBrowserMod != null)
					recipeBrowserMod = _recipeBrowserMod;
				else if (!ModLoader.TryGetMod("RecipeBrowser", out recipeBrowserMod))
					return false;
			}

			_recipeBrowserMod = recipeBrowserMod;

			Assembly asm = recipeBrowserMod.Code;
			Type recipeBrowserType = asm.GetType("RecipeBrowser.RecipeBrowser");
			Type recipeBrowserUiType = asm.GetType("RecipeBrowser.RecipeBrowserUI");
			Type recipeCatalogueType = asm.GetType("RecipeBrowser.RecipeCatalogueUI");
			Type itemCatalogueType = asm.GetType("RecipeBrowser.ItemCatalogueUI");
			Type bestiaryType = asm.GetType("RecipeBrowser.BestiaryUI");
			Type craftUiType = asm.GetType("RecipeBrowser.CraftUI");
			Type textBoxType = asm.GetType("RecipeBrowser.NewUITextBox");
			Type recipeQueryItemType = asm.GetType("RecipeBrowser.UIElements.UIRecipeCatalogueQueryItemSlot")
				?? asm.GetType("RecipeBrowser.UIRecipeCatalogueQueryItemSlot");
			Type bestiaryQueryItemType = asm.GetType("RecipeBrowser.UIElements.UIBestiaryQueryItemSlot")
				?? asm.GetType("RecipeBrowser.UIBestiaryQueryItemSlot");

			if (recipeBrowserType == null || recipeBrowserUiType == null || textBoxType == null)
				return false;

			const BindingFlags anyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
			const BindingFlags anyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

			object recipeBrowserInstance = recipeBrowserType.GetField("instance", anyStatic)?.GetValue(null);
			if (recipeBrowserInstance == null)
				return false;

			_queryHoveredItemHotKey = recipeBrowserType.GetField("QueryHoveredItemHotKey", anyInstance)?.GetValue(recipeBrowserInstance) as ModKeybind;
			_keybindBindingsProperty = typeof(ModKeybind).GetProperty("Bindings", anyInstance)
				?? typeof(ModKeybind).GetProperty("KeyBindings", anyInstance);

			_setTextMethod = textBoxType.GetMethod("SetText", anyInstance, null, new[] { typeof(string) }, null);
			_unfocusMethod = textBoxType.GetMethod("Unfocus", anyInstance);
			_focusedField = textBoxType.GetField("focused", anyInstance);
			_currentStringField = textBoxType.GetField("currentString", anyInstance);
			_recipeBrowserUiInstanceField = recipeBrowserUiType.GetField("instance", anyStatic);
			// ShowRecipeBrowser may be property and/or backing field depending on RB version.
			_recipeBrowserShowProperty = recipeBrowserUiType.GetProperty(
					"ShowRecipeBrowser",
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
				?? recipeBrowserUiType.GetProperty(
					"ShowRecipeBrowser",
					BindingFlags.Instance | BindingFlags.Public);
			_recipeBrowserShowField = recipeBrowserUiType.GetField("ShowRecipeBrowser", anyInstance)
				?? recipeBrowserUiType.GetField("showRecipeBrowser", anyInstance);
			_recipeBrowserCurrentPanelField = recipeBrowserUiType.GetField("CurrentPanel", anyInstance);
			_recipeBrowserCurrentPanelProperty = recipeBrowserUiType.GetProperty("CurrentPanel", anyInstance);
			_mainPanelField = recipeBrowserUiType.GetField("mainPanel", anyInstance);
			_favoritePanelField = recipeBrowserUiType.GetField("favoritePanel", anyInstance);
			_tabControllerField = recipeBrowserUiType.GetField("tabController", anyInstance);
			if (_tabControllerField != null)
			{
				_tabControllerSetPanelMethod = _tabControllerField.FieldType.GetMethod(
					"SetPanel",
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
					null,
					new[] { typeof(int) },
					null);
			}

			if (recipeCatalogueType != null)
			{
				_recipeCatalogueInstanceField = recipeCatalogueType.GetField("instance", anyStatic);
				_recipeNameFilterField = recipeCatalogueType.GetField("itemNameFilter", anyInstance);
				_recipeDescriptionFilterField = recipeCatalogueType.GetField("itemDescriptionFilter", anyInstance);
				_recipeQueryItemField = recipeCatalogueType.GetField("queryItem", anyInstance);
				_recipeUpdateNeededField = recipeCatalogueType.GetField("updateNeeded", anyInstance);
				_recipeQueryReplaceMethod = recipeQueryItemType?.GetMethod("ReplaceWithFake", anyInstance, null, new[] { typeof(int) }, null);
			}

			if (itemCatalogueType != null)
			{
				_itemCatalogueInstanceField = itemCatalogueType.GetField("instance", anyStatic);
				_itemNameFilterField = itemCatalogueType.GetField("itemNameFilter", anyInstance);
				_itemDescriptionFilterField = itemCatalogueType.GetField("itemDescriptionFilter", anyInstance);
			}

			if (bestiaryType != null)
			{
				_bestiaryInstanceField = bestiaryType.GetField("instance", anyStatic);
				_bestiaryNameFilterField = bestiaryType.GetField("npcNameFilter", anyInstance);
				_bestiaryQueryItemField = bestiaryType.GetField("queryItem", anyInstance);
				_bestiaryUpdateNeededField = bestiaryType.GetField("updateNeeded", anyInstance);
				_bestiaryNpcSlotsField = bestiaryType.GetField("npcSlots", anyInstance);
				_bestiaryQueryLootNpcField = bestiaryType.GetField("queryLootNPC", anyInstance);
				_bestiaryNpcGridField = bestiaryType.GetField("npcGrid", anyInstance);
				_bestiaryUpdateMethod = bestiaryType.GetMethod("Update", anyInstance, null, Type.EmptyTypes, null);
				_bestiarySetNpcMethod = bestiaryType.GetMethod("SetNPC", anyInstance);
				_bestiaryQueryReplaceMethod = bestiaryQueryItemType?.GetMethod("ReplaceWithFake", anyInstance, null, new[] { typeof(int) }, null);
			}

			if (craftUiType != null)
			{
				_craftInstanceField = craftUiType.GetField("instance", anyStatic);
				_craftSetItemMethod = craftUiType.GetMethod("SetItem", anyInstance, null, new[] { typeof(int) }, null);
				_craftRecipeResultSlotField = craftUiType.GetField("recipeResultItemSlot", anyInstance);
			}

			_rbNpcSlotType = asm.GetType("RecipeBrowser.UIElements.UINPCSlot")
				?? asm.GetType("RecipeBrowser.UINPCSlot");
			if (_rbNpcSlotType != null)
			{
				_rbNpcSlotNpcTypeField = _rbNpcSlotType.GetField("npcType", anyInstance);
				_rbNpcSlotNpcField = _rbNpcSlotType.GetField("npc", anyInstance);
			}

			_rbItemSlotType = asm.GetType("RecipeBrowser.UIElements.UIItemSlot")
				?? asm.GetType("RecipeBrowser.UIItemSlot");
			_rbItemSlotItemField = _rbItemSlotType?.GetField("item", anyInstance);

			bool hasShow = _recipeBrowserShowProperty != null || _recipeBrowserShowField != null;
			// Show is optional: query slot ReplaceWithFake works even if we cannot toggle the window.
			_initialized = _setTextMethod != null
				&& _currentStringField != null
				&& _recipeBrowserUiInstanceField != null
				&& (_recipeBrowserCurrentPanelProperty != null || _recipeBrowserCurrentPanelField != null);

			if (_initialized)
			{
				_initFailLogged = false;
				RbjDiag.Info(
					$"RB bridge init OK | showProp={_recipeBrowserShowProperty != null}, " +
					$"showField={_recipeBrowserShowField != null}, hotkey={_queryHoveredItemHotKey != null}, " +
					$"recipeQuery={_recipeQueryItemField != null}, recipeReplace={_recipeQueryReplaceMethod != null}, " +
					$"craftSet={_craftSetItemMethod != null}, bestiaryQuery={_bestiaryQueryItemField != null}");
				if (!hasShow)
					RbjDiag.Warn("ShowRecipeBrowser missing — will still set query if RB is already open");
			}
			else if (!_initFailLogged)
			{
				_initFailLogged = true;
				RbjDiag.Warn(
					$"RB bridge init failed: setText={_setTextMethod != null}, currentString={_currentStringField != null}, " +
					$"uiInstance={_recipeBrowserUiInstanceField != null}, showProp={_recipeBrowserShowProperty != null}, " +
					$"showField={_recipeBrowserShowField != null}, " +
					$"panel={_recipeBrowserCurrentPanelProperty != null || _recipeBrowserCurrentPanelField != null}, " +
					$"hotkey={_queryHoveredItemHotKey != null}");
				try
				{
					string props = string.Join(", ",
						Array.ConvertAll(
							recipeBrowserUiType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
							p => p.Name));
					string fields = string.Join(", ",
						Array.ConvertAll(
							recipeBrowserUiType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
							f => f.Name));
					RbjDiag.Warn($"RecipeBrowserUI props: {props}");
					RbjDiag.Warn($"RecipeBrowserUI fields: {fields}");
				}
				catch (Exception ex)
				{
					RbjDiag.Error("Failed dumping RecipeBrowserUI members", ex);
				}
			}

			return _initialized;
		}

		/// <summary>UI reflection ready. QueryHoveredItem hotkey is optional (not required for inventory middle-click).</summary>
		internal static bool IsReady => _initialized;

		internal static bool TryGetUiState(out object recipeBrowserUi, out int currentPanel)
		{
			recipeBrowserUi = null;
			currentPanel = 0;
			TryInitialize();
			if (_recipeBrowserUiInstanceField == null)
				return false;

			recipeBrowserUi = _recipeBrowserUiInstanceField.GetValue(null);
			if (recipeBrowserUi == null)
				return false;

			if (_recipeBrowserCurrentPanelProperty != null)
				currentPanel = (int)_recipeBrowserCurrentPanelProperty.GetValue(recipeBrowserUi);
			else if (_recipeBrowserCurrentPanelField != null)
				currentPanel = (int)_recipeBrowserCurrentPanelField.GetValue(recipeBrowserUi);
			else
				return false;

			return true;
		}

		internal static void ShowRecipeBrowser()
		{
			TryInitialize();
			object recipeBrowserUi = _recipeBrowserUiInstanceField?.GetValue(null);
			if (recipeBrowserUi == null)
				return;

			SetShowRecipeBrowser(recipeBrowserUi, true);
		}

		internal static object GetItemNameFilterTextBox()
		{
			TryInitialize();
			object catalogue = _itemCatalogueInstanceField?.GetValue(null);
			return catalogue == null ? null : _itemNameFilterField?.GetValue(catalogue);
		}

		internal static object GetItemCatalogueInstance()
		{
			TryInitialize();
			return _itemCatalogueInstanceField?.GetValue(null);
		}

		internal static void WriteNameFilter(object textBox, string name)
		{
			if (textBox == null)
				return;

			TryInitialize();
			SetFilterText(textBox, name ?? string.Empty);
			UnfocusTextBox(textBox);
		}

		/// <param name="ignoreAlt">Past-log browse may still hold Alt; must not block follow-up search.</param>
		/// <param name="ignoreShift">Do not treat Shift as a blocker.</param>
		internal static bool IsModifierBlockingCursorSearch(
			bool ignoreAlt = false,
			bool ignoreShift = false)
		{
			KeyboardState keyboard = Keyboard.GetState();
			if (!ignoreAlt
				&& (keyboard.IsKeyDown(Keys.LeftAlt) || keyboard.IsKeyDown(Keys.RightAlt)))
				return true;

			if (keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl))
				return true;

			if (!ignoreShift
				&& (keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift)))
				return true;

			return false;
		}

		internal static void RememberHoveredItem(int itemType)
		{
			if (itemType <= 0)
				return;

			_lastHoveredItemType = itemType;
			_lastHoveredItemStickyTtl = HoverItemStickyFrames;
		}

		internal static void TickRememberedHoveredItem()
		{
			if (_lastHoveredItemStickyTtl <= 0)
			{
				_lastHoveredItemType = 0;
				return;
			}

			_lastHoveredItemStickyTtl--;
			if (_lastHoveredItemStickyTtl <= 0)
				_lastHoveredItemType = 0;
		}

		internal static void ClearRememberedHoveredItem()
		{
			// Immediate clear only when explicitly needed — prefer Tick + sticky for flicker.
			if (Main.HoverItem.IsAir && _lastHoveredItemStickyTtl <= 0)
				_lastHoveredItemType = 0;
		}

		internal static int GetEffectiveHoveredItemType()
		{
			if (!Main.HoverItem.IsAir)
			{
				RememberHoveredItem(Main.HoverItem.type);
				return Main.HoverItem.type;
			}

			return _lastHoveredItemType;
		}

		internal static bool TryHandleChatCursorSearch()
		{
			if (!ChatBrowseHelper.BrowseMode)
				return false;

			// Alt may still be held from opening browse mode.
			if (IsModifierBlockingCursorSearch(ignoreAlt: true))
				return false;

			if (RecipeBrowserInputHelper.ActiveRecipeBrowserTextBox != null)
				return false;

			if (!IsSearchHotkeyJustPressed())
				return false;

			int hoveredType = GetEffectiveHoveredItemType();
			if (hoveredType <= 0)
				return false;

			RbjDiag.Info($"Browse search-hotkey type={hoveredType}");
			MarkPendingCursorQueryClear();
			PerformHoveredItemQuery(hoveredType, allowToggleClose: false);
			ChatBrowseHelper.BeginLingerAfterCursorSearch();
			return true;
		}

		/// <summary>
		/// Search hotkey (default: physical middle-click):
		/// 1) inventory / shop / chest / bank slots
		/// 2) any Main.HoverItem icon (chat tags, tooltips, etc.)
		/// 3) NPC: only while Recipe Browser is open —
		///    C = bestiary UINPCSlot; B = SmartInteract / talkNPC;
		///    enemy = tip + name-hover (ShowNameOnHover). Closed RB → no world NPC (A).
		/// 4) placed Item Frame / Weapon Rack / Food Platter / placeables
		/// Magic Storage item slots are owned by MS↔RB transfer when that path consumes the hotkey.
		/// </summary>
		internal static bool TryHandleHoverItemMiddleClickQuery()
		{
			TryInitialize();

			if (!IsSearchHotkeyJustPressed())
				return false;

			// Snapshot HoverItem first — later UI paths must not depend on a cleared field.
			Item hoverSnap = Main.HoverItem != null && !Main.HoverItem.IsAir
				? Main.HoverItem.Clone()
				: null;

			bool slotHover = Patches.InventoryHoverTrackPatch.HoveringTrackedSlot;
			int slotType = Patches.InventoryHoverTrackPatch.HoveredItemType;
			string source = Patches.InventoryHoverTrackPatch.HoveredSource;
			int context = Patches.InventoryHoverTrackPatch.HoveredContext;
			int slotIndex = Patches.InventoryHoverTrackPatch.HoveredSlot;
			bool overRbPanel = IsMouseOverRecipeBrowserPanel();
			bool overRbItem = IsMouseOverRecipeBrowserItemSlot();
			bool overMs = MagicStorageSearchHelper.IsMouseOverItemSlot();
			int hoverType = hoverSnap?.type
				?? (Main.HoverItem.IsAir ? 0 : Main.HoverItem.type);
			bool rbOpen = GetShowRecipeBrowser();
			// NPC tip SetZoom is deferred until after UI/HoverItem paths fail (see below).
			int npcType = 0;
			int npcNetId = 0;
			bool hasNpc = false;

			KeyboardState kb = Keyboard.GetState();
			RbjDiag.Info(
				$"Hover search-hotkey detected | ready={IsReady} gameMenu={Main.gameMenu} " +
				$"playerInv={Main.playerInventory} npcShop={Main.npcShop} chest={Main.LocalPlayer?.chest} " +
				$"source={source} context={context} slot={slotIndex} " +
				$"slotHover={slotHover} slotType={slotType} hoverType={hoverType} " +
				$"rbOpen={rbOpen} " +
				$"overRbPanel={overRbPanel} overRbItem={overRbItem} overMs={overMs} hoverAir={hoverSnap == null} " +
				$"chatOpen={Main.drawingPlayerChat} " +
				$"{RbjDiagPolicy.MouseTriple()} " +
				$"mods LCtrl={kb.IsKeyDown(Keys.LeftControl)} RCtrl={kb.IsKeyDown(Keys.RightControl)} " +
				$"LAlt={kb.IsKeyDown(Keys.LeftAlt)} RAlt={kb.IsKeyDown(Keys.RightAlt)} " +
				$"LShift={kb.IsKeyDown(Keys.LeftShift)} RShift={kb.IsKeyDown(Keys.RightShift)} " +
				$"physMid={IsPhysicalMiddleDown()} gameMid={Main.mouseMiddle}");

			if (slotHover && hoverSnap != null && hoverSnap.type != slotType)
			{
				RbjDiag.Warn(
					$"Hover type mismatch: tracked slotType={slotType} source={source} " +
					$"but HoverItem.type={hoverSnap.type} — using tracked slotType");
			}

			if (!IsReady || Main.gameMenu)
			{
				RbjDiag.Warn($"Hover search-hotkey aborted: ready={IsReady} gameMenu={Main.gameMenu}");
				return false;
			}

			if (ChatBrowseHelper.BrowseMode)
			{
				RbjDiag.Info("Hover search-hotkey skipped: browse mode");
				SearchHotkeyProbe.LogBlock("hover-skip", "browseMode");
				return false;
			}

			if (RecipeBrowserInputHelper.ActiveRecipeBrowserTextBox != null)
			{
				RbjDiag.Info("Hover search-hotkey skipped: RB textbox focused");
				SearchHotkeyProbe.LogBlock("hover-skip", "rbTextBoxFocused");
				return false;
			}

			if (IsModifierBlockingCursorSearch())
			{
				RbjDiag.Info("Hover search-hotkey skipped: Ctrl/Alt held");
				SearchHotkeyProbe.LogBlock("hover-skip", "modifierBlocking");
				return false;
			}

			// Inventory / equip / chest / bank / shop tracked by ItemSlot.MouseHover.
			// Must run BEFORE overRb: when RB (or its tooltip region) overlaps inventory,
			// ContainsPoint reports overRb=true and used to skip valid inv→RB clicks.
			if (slotHover && slotType > 0)
			{
				if (!Main.playerInventory)
				{
					RbjDiag.Info("Hover search-hotkey skipped: player inventory closed (slot path)");
					return false;
				}

				if (source == "shop" && Main.npcShop <= 0)
				{
					RbjDiag.Warn("Hover search-hotkey skipped: shop source but npcShop<=0 (stale hover?)");
					return false;
				}

				if (overRbPanel)
				{
					RbjDiag.Info(
						$"Hover search-hotkey inv-over-rb-priority source={source} type={slotType} " +
						$"slot={slotIndex} sticky={Patches.InventoryHoverTrackPatch.UsingStickyTrack} " +
						$"(RB panel overlaps inventory; preferring ItemSlot track)");
				}

				RbjDiag.Info(
					$"Hover search-hotkey accepted source={source} type={slotType} " +
					$"sticky={Patches.InventoryHoverTrackPatch.UsingStickyTrack} → Recipe Browser (sync tabs)");
				SearchHotkeyProbe.LogBlock(
					"hover-ok",
					overRbPanel
						? $"inv-over-rb-priority source={source} type={slotType} sticky={Patches.InventoryHoverTrackPatch.UsingStickyTrack}"
						: $"inv source={source} type={slotType} sticky={Patches.InventoryHoverTrackPatch.UsingStickyTrack}");
				PerformInvMsSyncedQuery(slotType);
				return true;
			}

			// RB item icons first (bestiary loot drops = UIBestiaryItemSlot, recipe grid icons, etc.).
			// Must beat UINPCSlot — naive npcSlots ContainsPoint scans falsely hit off-grid critters
			// (e.g. Combat Wrench click → Sluggy) when list slots still report ContainsPoint.
			if (TryHandleRecipeBrowserItemIconMiddleClick())
				return true;

			// Bestiary grid NPC icon only when GetElementAt says we are ON that icon.
			if (TryHandleBestiaryNpcIconMiddleClick())
				return true;

			// Remaining RB item cases already handled above.
			if (overRbItem)
			{
				RbjDiag.Info("Hover search-hotkey skipped: over Recipe Browser item slot (unresolved)");
				SearchHotkeyProbe.LogBlock("hover-skip", "overRbItem-unresolved");
				return false;
			}

			if (overRbPanel && hoverSnap != null && Main.playerInventory)
			{
				// Panel overlaps inventory without a tracked slot / without RB item icon:
				// treat HoverItem as inv→RB (was wrongly RB→MS via HoverItem-snap-on-rb).
				int type = hoverSnap.type;
				RbjDiag.Info(
					$"Hover search-hotkey accepted source=inv-under-rb-panel type={type} → Recipe Browser (sync tabs)");
				SearchHotkeyProbe.LogBlock("hover-ok", $"inv-under-rb-panel type={type}");
				PerformInvMsSyncedQuery(type);
				return true;
			}

			if (overMs)
			{
				// Transfer path owns MS slots; if we still get here, slot resolve failed.
				if (hoverSnap != null)
				{
					int type = hoverSnap.type;
					RbjDiag.Info($"Hover search-hotkey accepted source=msHoverItemFallback type={type} → Recipe Browser (sync tabs)");
					SearchHotkeyProbe.LogBlock("hover-ok", $"msHoverItemFallback type={type}");
					PerformInvMsSyncedQuery(type);
					return true;
				}

				if (MagicStorageSearchHelper.TryGetItemFromHoveredSlotZone(out Item zoneItem, out string zoneHow)
					&& zoneItem != null
					&& !zoneItem.IsAir)
				{
					RbjDiag.Info(
						$"Hover search-hotkey accepted source=msZoneFallback how={zoneHow} " +
						$"type={zoneItem.type} → Recipe Browser (sync tabs)");
					SearchHotkeyProbe.LogBlock("hover-ok", $"msZoneFallback type={zoneItem.type}");
					PerformInvMsSyncedQuery(zoneItem.type);
					return true;
				}

				RbjDiag.Info("Hover search-hotkey skipped: over Magic Storage slot but no item resolved");
				SearchHotkeyProbe.LogBlock("hover-miss", "overMs-no-item");
				return false;
			}

			// Live HoverItem snapshot (no sticky memory).
			Player local = Main.LocalPlayer;
			bool mouseOnUi = local != null && local.mouseInterface;
			if (hoverSnap != null)
			{
				int liveHover = hoverSnap.type;
				RbjDiag.Info(
					$"Hover search-hotkey accepted source=hoverItemLive type={liveHover} " +
					$"chatOpen={Main.drawingPlayerChat} mouseUi={mouseOnUi} → Recipe Browser");
				SearchHotkeyProbe.LogBlock("hover-ok", $"hoverItemLive type={liveHover}");
				PerformHoveredItemQuery(liveHover, allowToggleClose: false);
				return true;
			}

			// Policy A+B+enemy: world NPC only while RB is open.
			// Tip SetZoom only here — after UI / HoverItem paths already failed.
			if (rbOpen)
			{
				hasNpc = NpcHoverTrack.TryGetNpcWhileRecipeBrowserOpen(out npcType, out npcNetId);
			}

			if (rbOpen && hasNpc && npcType > 0)
			{
				RbjDiag.Info(
					$"Hover search-hotkey accepted source=npc-rb-open type={npcType} netId={npcNetId} " +
					$"→ Bestiary");
				SearchHotkeyProbe.LogBlock("hover-ok", $"npc-rb-open type={npcType}");
				HoverTooltipSuppress.Cancel("npc-bestiary");
				return PerformNpcBestiarySearch(npcType, npcNetId);
			}

			if (!rbOpen && (Main.SmartInteractNPC >= 0 || (Main.LocalPlayer?.talkNPC ?? -1) >= 0))
			{
				RbjDiag.Info(
					"Hover search-hotkey skipped: world NPC focus ignored while RB closed (policy A)");
			}

			// Placed tiles — never while a UI owns the mouse (RB query slot etc.)
			// No sticky HoverHold: wrong world picks must not glue tooltips for 700ms.
			if (!mouseOnUi
				&& WorldPlacedItemHover.TryGetItemUnderMouse(out int placedType, out string placedSource)
				&& placedType > 0)
			{
				RbjDiag.Info(
					$"Hover search-hotkey accepted source={placedSource} type={placedType} " +
					$"(under-cursor policy, no HoverHold) → Recipe Browser");
				SearchHotkeyProbe.LogBlock("hover-ok", $"{placedSource} type={placedType}");
				PerformHoveredItemQuery(placedType, allowToggleClose: false, stickyTooltip: false);
				return true;
			}

			RbjDiag.Info(
				$"Hover search-hotkey skipped: nothing under cursor " +
				$"(npcShop={Main.npcShop} chest={Main.LocalPlayer?.chest})");
			SearchHotkeyProbe.LogBlock("hover-miss", "nothing-under-cursor");
			return false;
		}

		/// <summary>
		/// Opens Recipe Browser Bestiary, clears item-query slot, selects the NPC
		/// (same idea as RB's double-click on an NPC slot).
		/// </summary>
		internal static bool PerformNpcBestiarySearch(int npcType, int npcNetId = 0)
		{
			TryInitialize();
			if (!IsReady)
			{
				RbjDiag.Warn("PerformNpcBestiarySearch aborted: bridge not ready");
				return false;
			}

			if (npcNetId <= 0)
				npcNetId = npcType;

			try
			{
				object recipeBrowserUi = _recipeBrowserUiInstanceField?.GetValue(null);
				if (recipeBrowserUi == null)
				{
					RbjDiag.Warn("PerformNpcBestiarySearch: RecipeBrowserUI.instance null");
					return false;
				}

				ShowRecipeBrowser();
				if (!TrySetCurrentPanel(3))
					RbjDiag.Warn("PerformNpcBestiarySearch: SetPanel(Bestiary) failed — continuing");

				object bestiary = _bestiaryInstanceField?.GetValue(null);
				if (bestiary == null)
				{
					RbjDiag.Warn("PerformNpcBestiarySearch: BestiaryUI null");
					return false;
				}

				MarkPendingCursorQueryClear();
				ClearAllSearchFilters();

				object queryItem = _bestiaryQueryItemField?.GetValue(bestiary);
				try
				{
					if (_bestiaryQueryReplaceMethod != null)
						_bestiaryQueryReplaceMethod.Invoke(queryItem, new object[] { 0 });
					else
						InvokeReplaceWithFake(queryItem, 0);
				}
				catch (Exception ex)
				{
					RbjDiag.Error("PerformNpcBestiarySearch: clear query item failed", ex);
				}

				string npcName = Lang.GetNPCNameValue(npcNetId);
				object nameFilter = _bestiaryNameFilterField?.GetValue(bestiary);
				if (nameFilter != null && !string.IsNullOrWhiteSpace(npcName))
					WriteNameFilter(nameFilter, npcName);

				_bestiaryUpdateNeededField?.SetValue(bestiary, true);
				_bestiaryUpdateMethod?.Invoke(bestiary, null);

				bool selected = TrySelectBestiaryNpc(bestiary, npcType);
				SetShowRecipeBrowser(recipeBrowserUi, true);

				RbjDiag.Info(
					$"PerformNpcBestiarySearch end type={npcType} netId={npcNetId} " +
					$"name='{npcName}' selected={selected}");
				return true;
			}
			catch (Exception ex)
			{
				RbjDiag.Error("PerformNpcBestiarySearch crashed", ex);
				return false;
			}
			finally
			{
				ResetPendingCursorQueryClear();
			}
		}

		private static bool TrySetCurrentPanel(int panel)
		{
			object recipeBrowserUi = _recipeBrowserUiInstanceField?.GetValue(null);
			if (recipeBrowserUi == null || _tabControllerField == null || _tabControllerSetPanelMethod == null)
			{
				RbjDiag.Warn($"TrySetCurrentPanel({panel}) aborted: reflection incomplete");
				return false;
			}

			int before = -1;
			try
			{
				if (TryGetUiState(out _, out int cur))
					before = cur;
			}
			catch
			{
				// ignore panel read failure — still try set
			}

			object tabController = _tabControllerField.GetValue(recipeBrowserUi);
			if (tabController == null)
			{
				RbjDiag.Warn($"TrySetCurrentPanel({panel}) aborted: tabController null (from={before})");
				return false;
			}

			try
			{
				_tabControllerSetPanelMethod.Invoke(tabController, new object[] { panel });
			}
			catch (Exception ex)
			{
				RbjDiag.Error($"TrySetCurrentPanel({panel}) Invoke failed (from={before})", ex);
				return false;
			}

			int after = before;
			try
			{
				if (TryGetUiState(out _, out int cur))
					after = cur;
			}
			catch
			{
				// ignore
			}

			RbjDiag.Info($"TrySetCurrentPanel {before}→{panel} readBack={after} show={GetShowRecipeBrowser()}");
			return true;
		}

		/// <summary>
		/// Middle-click on Recipe Browser <c>UIItemSlot</c> (incl. bestiary loot drops).
		/// Uses GetElementAt deepest hit — not HoverItem alone.
		/// </summary>
		private static bool TryHandleRecipeBrowserItemIconMiddleClick()
		{
			if (!TryGetRecipeBrowserItemSlotUnderMouse(out Item item, out string how))
				return false;

			RbjDiag.Info(
				$"Hover search-hotkey accepted source=rbItemSlot type={item.type} " +
				$"name='{item.Name}' how={how} → Recipe Browser");
			SearchHotkeyProbe.LogBlock("hover-ok", $"rbItemSlot type={item.type} how={how}");
			PerformHoveredItemQuery(item.type, allowToggleClose: false);
			return true;
		}

		private static bool TryGetRecipeBrowserItemSlotUnderMouse(out Item item, out string how)
		{
			item = null;
			how = null;
			TryInitialize();
			if (_rbItemSlotType == null || _rbItemSlotItemField == null)
				return false;

			if (!TryGetRecipeBrowserPanelUnderMouse(out UIElement under) || under == null)
				return false;

			UIElement cur = under;
			while (cur != null)
			{
				// Deepest first: item slot wins over parent panels.
				if (_rbItemSlotType.IsInstanceOfType(cur))
				{
					item = _rbItemSlotItemField.GetValue(cur) as Item;
					if (item != null && !item.IsAir)
					{
						how = "GetElementAt-UIItemSlot";
						return true;
					}

					return false;
				}

				// Standing on an NPC icon — not an item slot.
				if (_rbNpcSlotType != null && _rbNpcSlotType.IsInstanceOfType(cur))
					return false;

				cur = cur.Parent;
			}

			return false;
		}

		/// <summary>
		/// Middle-click on a Recipe Browser Bestiary <c>UINPCSlot</c>.
		/// Maps NPC → catchable item via <see cref="Item.makeNPC"/> (never passes npcType into
		/// <c>ReplaceWithFake</c>). If that item appears in any recipe, switch to Recipe tab and query.
		/// Otherwise consume the hotkey and do nothing (no crash, no wrong-item search).
		/// </summary>
		private static bool TryHandleBestiaryNpcIconMiddleClick()
		{
			try
			{
				if (!TryGetBestiaryNpcSlotUnderMouse(out int npcType, out int npcNetId, out int catchItem))
					return false;

				string npcName = Lang.GetNPCNameValue(npcNetId != 0 ? npcNetId : npcType);

				if (!TryFindCatchableItemForNpc(npcType, npcNetId, catchItem, out int itemType, out string how))
				{
					RbjDiag.Info(
						$"Hover search-hotkey rbNpc skip: no catchable item " +
						$"npcType={npcType} netId={npcNetId} catchItem={catchItem} " +
						$"npcName='{npcName}' (hotkey consumed, query untouched)");
					SearchHotkeyProbe.LogBlock(
						"hover-skip",
						$"rbNpc-no-catch-item npc={npcType} netId={npcNetId} catchItem={catchItem}");
					return true;
				}

				string itemName = Lang.GetItemNameValue(itemType);
				if (!ItemAppearsInAnyRecipe(itemType))
				{
					RbjDiag.Info(
						$"Hover search-hotkey rbNpc skip: no recipes for catchable " +
						$"item={itemType} name='{itemName}' how={how} " +
						$"npc={npcType}/{npcNetId} npcName='{npcName}' " +
						$"(not in createItem/requiredItem/RecipeGroup)");
					SearchHotkeyProbe.LogBlock(
						"hover-skip",
						$"rbNpc-no-recipe item={itemType} name='{itemName}' npc={npcType}");
					return true;
				}

				RbjDiag.Info(
					$"Hover search-hotkey accepted source=rbNpcSlot→recipe " +
					$"npc={npcType} netId={npcNetId} npcName='{npcName}' " +
					$"item={itemType} itemName='{itemName}' how={how}");
				SearchHotkeyProbe.LogBlock(
					"hover-ok",
					$"rbNpc→recipe item={itemType} name='{itemName}' npc={npcType} how={how}");

				ShowRecipeBrowser();
				if (!TrySetCurrentPanel(0))
					RbjDiag.Warn("rbNpc→recipe: SetPanel(Recipe) failed — continuing");

				PerformHoveredItemQuery(itemType, allowToggleClose: false);
				return true;
			}
			catch (Exception ex)
			{
				RbjDiag.Error("TryHandleBestiaryNpcIconMiddleClick crashed", ex);
				return true; // consume hotkey — avoid cascading handlers after partial UI mutation
			}
		}

		/// <summary>
		/// Strict: only the element under the cursor (GetElementAt parent chain).
		/// Do NOT scan whole npcSlots with ContainsPoint — scrolled/off-hitboxes falsely match
		/// (loot bar clicks were mapped to Sluggy etc.).
		/// </summary>
		private static bool TryGetBestiaryNpcSlotUnderMouse(out int npcType, out int npcNetId, out int catchItem)
		{
			npcType = 0;
			npcNetId = 0;
			catchItem = 0;
			TryInitialize();

			if (_rbNpcSlotType == null)
				return false;

			if (!TryGetUiState(out _, out int panel) || panel != 3)
				return false;

			if (!TryGetRecipeBrowserPanelUnderMouse(out UIElement under) || under == null)
				return false;

			UIElement cur = under;
			while (cur != null)
			{
				if (_rbItemSlotType != null && _rbItemSlotType.IsInstanceOfType(cur))
					return false;

				if (_rbNpcSlotType.IsInstanceOfType(cur)
					&& TryReadNpcSlotIds(cur, out npcType, out npcNetId, out catchItem))
					return npcType > 0 || npcNetId != 0;

				cur = cur.Parent;
			}

			return false;
		}

		private static bool TryReadNpcSlotIds(object slot, out int npcType, out int npcNetId, out int catchItem)
		{
			npcType = 0;
			npcNetId = 0;
			catchItem = 0;
			if (slot == null || _rbNpcSlotType == null || !_rbNpcSlotType.IsInstanceOfType(slot))
				return false;

			if (_rbNpcSlotNpcTypeField?.GetValue(slot) is int t)
				npcType = t;

			if (_rbNpcSlotNpcField?.GetValue(slot) is NPC npc && npc != null)
			{
				if (npcType <= 0)
					npcType = npc.type;
				npcNetId = npc.netID;
				catchItem = npc.catchItem;
			}

			if (npcNetId == 0)
				npcNetId = npcType;

			return npcType > 0 || npcNetId != 0;
		}

		/// <summary>
		/// Prefer <see cref="NPC.catchItem"/> (what a bug net would drop), then reverse
		/// <see cref="Item.makeNPC"/> lookup. Never returns the NPC type as an item id.
		/// </summary>
		private static bool TryFindCatchableItemForNpc(
			int npcType,
			int npcNetId,
			int catchItemFromNpc,
			out int itemType,
			out string how)
		{
			itemType = 0;
			how = "none";

			// Canonical link for critters (Seagull, Gold Goldfish, etc.): NPC.catchItem.
			if (catchItemFromNpc > 0)
			{
				if (catchItemFromNpc >= ItemLoader.ItemCount)
				{
					RbjDiag.Warn(
						$"catchItem out of range npcType={npcType} netId={npcNetId} " +
						$"catchItem={catchItemFromNpc} ItemCount={ItemLoader.ItemCount} — ignoring");
				}
				else
				{
					itemType = catchItemFromNpc;
					how = "npc.catchItem";
					return true;
				}
			}

			int primary = npcNetId != 0 ? npcNetId : npcType;
			int secondary = npcType;

			if (ContentSamples.ItemsByType == null)
				return false;

			foreach (KeyValuePair<int, Item> kv in ContentSamples.ItemsByType)
			{
				Item sample = kv.Value;
				if (sample == null || sample.IsAir || sample.makeNPC == 0)
					continue;

				if (sample.makeNPC == primary)
				{
					itemType = kv.Key;
					how = primary == npcNetId ? "makeNPC=netId" : "makeNPC=type";
					return true;
				}
			}

			if (secondary != 0 && secondary != primary)
			{
				foreach (KeyValuePair<int, Item> kv in ContentSamples.ItemsByType)
				{
					Item sample = kv.Value;
					if (sample == null || sample.IsAir || sample.makeNPC == 0)
						continue;

					if (sample.makeNPC == secondary)
					{
						itemType = kv.Key;
						how = "makeNPC=type-fallback";
						return true;
					}
				}
			}

			return false;
		}

		/// <summary>
		/// True when the item is created by, required by, or in a recipe group of any loaded recipe.
		/// </summary>
		private static bool ItemAppearsInAnyRecipe(int itemType)
		{
			if (itemType <= 0 || Main.recipe == null)
				return false;

			int count = Recipe.numRecipes;
			for (int i = 0; i < count; i++)
			{
				Recipe recipe = Main.recipe[i];
				if (recipe == null)
					continue;

				if (recipe.createItem != null && recipe.createItem.type == itemType)
					return true;

				if (recipe.requiredItem == null)
					continue;

				for (int r = 0; r < recipe.requiredItem.Count; r++)
				{
					Item req = recipe.requiredItem[r];
					if (req != null && !req.IsAir && req.type == itemType)
						return true;
				}

				// Recipe groups referenced by this recipe (portable for cage/bottle kits).
				if (recipe.acceptedGroups != null)
				{
					for (int g = 0; g < recipe.acceptedGroups.Count; g++)
					{
						int groupId = recipe.acceptedGroups[g];
						if (RecipeGroup.recipeGroups.TryGetValue(groupId, out RecipeGroup group)
							&& group != null
							&& group.ContainsItem(itemType))
							return true;
					}
				}
			}

			return false;
		}

		private static bool TrySelectBestiaryNpc(object bestiary, int npcType)
		{
			if (bestiary == null || _bestiaryNpcSlotsField == null)
				return false;

			if (_bestiaryNpcSlotsField.GetValue(bestiary) is not System.Collections.IList slots)
				return false;

			object match = null;
			foreach (object slot in slots)
			{
				if (slot == null)
					continue;

				FieldInfo npcTypeField = slot.GetType().GetField(
					"npcType",
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (npcTypeField == null)
					continue;

				if ((int)npcTypeField.GetValue(slot) == npcType)
				{
					match = slot;
					break;
				}
			}

			if (match == null)
			{
				RbjDiag.Warn($"TrySelectBestiaryNpc: no slot for type={npcType}");
				return false;
			}

			_bestiaryQueryLootNpcField?.SetValue(bestiary, match);
			_bestiarySetNpcMethod?.Invoke(bestiary, new object[] { match });
			_bestiaryUpdateNeededField?.SetValue(bestiary, true);
			_bestiaryUpdateMethod?.Invoke(bestiary, null);

			object npcGrid = _bestiaryNpcGridField?.GetValue(bestiary);
			if (npcGrid != null)
			{
				EnsureNpcGridGoto(npcGrid.GetType());
				if (_npcGridGotoMethod != null)
				{
					try
					{
						// Goto(Func<UIElement, bool>, bool) — scroll to matching slot.
						System.Func<UIElement, bool> predicate = element =>
							element != null && ReferenceEquals(element, match);
						_npcGridGotoMethod.Invoke(npcGrid, new object[] { predicate, true });
					}
					catch (Exception ex)
					{
						RbjDiag.Warn($"TrySelectBestiaryNpc Goto failed: {ex.GetType().Name}: {ex.Message}");
					}
				}
			}

			return true;
		}

		private static void EnsureNpcGridGoto(Type npcGridType)
		{
			if (_npcGridGotoMethod != null || npcGridType == null)
				return;

			foreach (MethodInfo method in npcGridType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
			{
				if (method.Name != "Goto")
					continue;

				ParameterInfo[] parameters = method.GetParameters();
				if (parameters.Length == 2 && parameters[1].ParameterType == typeof(bool))
				{
					_npcGridGotoMethod = method;
					return;
				}
			}
		}

		/// <summary>
		/// True when the cursor is over the Recipe Browser main or favorite panel.
		/// </summary>
		internal static bool IsMouseOverRecipeBrowserPanel()
		{
			return TryGetRecipeBrowserPanelUnderMouse(out _);
		}

		/// <summary>
		/// True only when the cursor is over a Recipe Browser item icon (UIItemSlot).
		/// Panel chrome / empty areas overlapping inventory must NOT count.
		/// </summary>
		internal static bool IsMouseOverRecipeBrowserItemSlot()
		{
			if (!TryGetRecipeBrowserPanelUnderMouse(out UIElement under))
				return false;

			return IsUiItemSlotInParentChain(under);
		}

		private static bool IsUiItemSlotInParentChain(UIElement under)
		{
			TryInitialize();
			if (_rbItemSlotType == null)
				return false;

			UIElement cur = under;
			while (cur != null)
			{
				if (_rbItemSlotType.IsInstanceOfType(cur))
					return true;
				cur = cur.Parent;
			}

			return false;
		}

		/// <summary>
		/// Deepest Recipe Browser UI element under the cursor (main or favorite panel tree).
		/// Same-frame results are memoized for identical mouse coords (hotkey path may query twice).
		/// </summary>
		internal static bool TryGetRecipeBrowserPanelUnderMouse(out UIElement under)
		{
			under = null;
			uint frame = Main.GameUpdateCount;
			int mx = Main.mouseX;
			int my = Main.mouseY;
			if (_rbUnderMouseCacheFrame == frame
				&& _rbUnderMouseCacheX == mx
				&& _rbUnderMouseCacheY == my)
			{
				under = _rbUnderMouseCacheElement;
				return _rbUnderMouseCacheHit;
			}

			TryInitialize();
			object recipeBrowserUi = _recipeBrowserUiInstanceField?.GetValue(null);
			bool hit = false;
			if (recipeBrowserUi != null)
			{
				Microsoft.Xna.Framework.Vector2 mouse = new(mx, my);

				if (GetShowRecipeBrowser()
					&& _mainPanelField?.GetValue(recipeBrowserUi) is UIElement mainPanel
					&& mainPanel.Parent != null
					&& mainPanel.ContainsPoint(mouse))
				{
					under = mainPanel.GetElementAt(mouse) ?? mainPanel;
					hit = true;
				}
				else if (_favoritePanelField?.GetValue(recipeBrowserUi) is UIElement favoritePanel
					&& favoritePanel.Parent != null
					&& favoritePanel.ContainsPoint(mouse))
				{
					under = favoritePanel.GetElementAt(mouse) ?? favoritePanel;
					hit = true;
				}
			}

			_rbUnderMouseCacheFrame = frame;
			_rbUnderMouseCacheX = mx;
			_rbUnderMouseCacheY = my;
			_rbUnderMouseCacheHit = hit;
			_rbUnderMouseCacheElement = under;
			return hit;
		}

		private static bool TryEnsureFilterFields()
		{
			if (_setTextMethod != null && _currentStringField != null && _recipeCatalogueInstanceField != null)
				return true;

			TryInitialize();
			return _setTextMethod != null && _currentStringField != null;
		}

		private static void InvokeReplaceWithFake(object querySlot, int itemType)
		{
			if (querySlot == null || itemType <= 0)
				return;

			MethodInfo replaceWithFake = querySlot.GetType().GetMethod(
				"ReplaceWithFake",
				BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
				null,
				new[] { typeof(int) },
				null);

			replaceWithFake?.Invoke(querySlot, new object[] { itemType });
		}

		internal static void ClearAllSearchFilters()
		{
			if (!TryEnsureFilterFields())
				return;

			ClearFilterField(_recipeCatalogueInstanceField, _recipeNameFilterField);
			ClearFilterField(_recipeCatalogueInstanceField, _recipeDescriptionFilterField);
			ClearFilterField(_itemCatalogueInstanceField, _itemNameFilterField);
			ClearFilterField(_itemCatalogueInstanceField, _itemDescriptionFilterField);
			ClearFilterField(_bestiaryInstanceField, _bestiaryNameFilterField);

			MarkRecipeCatalogueDirty();
			CompositionTracker.Clear();
			ImeTextInputHandler.StopCapturing();
			RecipeBrowserInputHelper.ReleaseIfActiveBoxLostFocus(Patches.RecipeBrowserPatches.IsTextBoxFocused);
		}

		private static void MarkRecipeCatalogueDirty()
		{
			if (_recipeCatalogueInstanceField == null || _recipeUpdateNeededField == null)
				return;

			object recipeCatalogue = _recipeCatalogueInstanceField.GetValue(null);
			if (recipeCatalogue == null)
				return;

			_recipeUpdateNeededField.SetValue(recipeCatalogue, true);
		}

		/// <summary>
		/// Inventory / Magic Storage → Recipe Browser:
		/// show Recipe tab query + Item-tab name search. Does not touch Craft tab query.
		/// </summary>
		internal static void PerformInvMsSyncedQuery(int itemType, bool stickyTooltip = true)
		{
			TryInitialize();
			if (!IsReady)
			{
				RbjDiag.Warn("PerformInvMsSyncedQuery aborted: bridge not ready");
				return;
			}

			if (itemType <= 0)
			{
				RbjDiag.Warn("PerformInvMsSyncedQuery aborted: invalid item type");
				return;
			}

			Item tipSnap = Main.HoverItem != null && !Main.HoverItem.IsAir && Main.HoverItem.type == itemType
				? Main.HoverItem.Clone()
				: null;

			MarkPendingCursorQueryClear();
			ClearAllSearchFilters();

			try
			{
				object recipeBrowserUi = _recipeBrowserUiInstanceField.GetValue(null);
				if (recipeBrowserUi == null)
				{
					RbjDiag.Warn("PerformInvMsSyncedQuery aborted: RecipeBrowserUI.instance null");
					return;
				}

				ShowRecipeBrowser();
				if (!TrySetCurrentPanel(0))
					RbjDiag.Warn("PerformInvMsSyncedQuery: SetPanel(Recipe) failed — continuing");

				HandleRecipeCatalogueQuery(itemType, allowToggleClose: false);

				Item probe = tipSnap;
				if (probe == null)
				{
					probe = new Item();
					probe.SetDefaults(itemType);
				}

				string name = RecipeBrowserNameSearchHelper.GetSearchName(probe);
				bool nameOk = RecipeBrowserNameSearchHelper.TrySetNameSearchOnItemTabFromItem(probe);

				int recipeAfter = GetQueryItem(_recipeQueryItemField?.GetValue(_recipeCatalogueInstanceField?.GetValue(null)))?.type ?? 0;
				bool ok = recipeAfter == itemType;

				RbjDiag.Info(
					$"PerformInvMsSyncedQuery end type={itemType} name='{name}' nameOk={nameOk} " +
					$"recipe={recipeAfter} ok={ok}");

				if (ok)
				{
					Terraria.Audio.SoundEngine.PlaySound(Terraria.ID.SoundID.MenuTick);

					if (stickyTooltip)
					{
						Item tip = tipSnap ?? probe;
						HoverTooltipSuppress.Hold(tip, reason: $"inv/ms→RB sync type={itemType}");
					}
				}
				else
				{
					RbjDiag.Warn(
						$"PerformInvMsSyncedQuery MISMATCH recipe={recipeAfter} expected={itemType}");
				}
			}
			catch (Exception ex)
			{
				RbjDiag.Error("PerformInvMsSyncedQuery crashed", ex);
			}
			finally
			{
				ResetPendingCursorQueryClear();
			}
		}

		/// <param name="stickyTooltip">
		/// When false, skip HoverHold (world placeables / dubious tips must not pin wrong tooltips).
		/// UI slot / MS / RB transfers keep sticky tips.
		/// </param>
		internal static void PerformHoveredItemQuery(
			int? itemTypeOverride = null,
			bool allowToggleClose = true,
			bool stickyTooltip = true)
		{
			TryInitialize();
			if (!IsReady)
			{
				RbjDiag.Warn("PerformHoveredItemQuery aborted: bridge not ready");
				return;
			}

			int hoveredType = itemTypeOverride ?? GetEffectiveHoveredItemType();
			if (hoveredType <= 0)
			{
				RbjDiag.Warn("PerformHoveredItemQuery aborted: invalid item type");
				return;
			}

			// Snapshot tip BEFORE ReplaceWithFake / UI rebuild can clear HoverItem.
			Item tipSnap = Main.HoverItem != null && !Main.HoverItem.IsAir && Main.HoverItem.type == hoveredType
				? Main.HoverItem.Clone()
				: null;

			MarkPendingCursorQueryClear();
			ClearAllSearchFilters();

			try
			{
				object recipeBrowserUi = _recipeBrowserUiInstanceField.GetValue(null);
				if (recipeBrowserUi == null)
				{
					RbjDiag.Warn("PerformHoveredItemQuery aborted: RecipeBrowserUI.instance null");
					return;
				}

				if (!TryGetUiState(out _, out int currentPanel))
				{
					RbjDiag.Warn("PerformHoveredItemQuery aborted: TryGetUiState failed");
					return;
				}

				bool showBefore = GetShowRecipeBrowser();
				RbjDiag.Info($"PerformHoveredItemQuery begin type={hoveredType} panel={currentPanel} showBefore={showBefore} toggleClose={allowToggleClose}");

				bool showRecipeBrowser = true;
				int targetPanel = currentPanel;

				switch (currentPanel)
				{
					case 0:
						showRecipeBrowser = HandleRecipeCatalogueQuery(hoveredType, allowToggleClose);
						break;
					case 1:
						showRecipeBrowser = HandleCraftQuery(hoveredType, allowToggleClose);
						break;
					case 2:
					{
						// Item tab has no query slot — same as MS middle-click: name search only.
						Item probe = new Item();
						probe.SetDefaults(hoveredType);
						string name = RecipeBrowserNameSearchHelper.GetSearchName(probe);
						bool nameOk = RecipeBrowserNameSearchHelper.TrySetNameSearchOnItemTabFromItem(probe);
						RbjDiag.Info(
							$"PerformHoveredItemQuery: item tab name search ok={nameOk} type={hoveredType} name='{name}'");
						break;
					}
					case 3:
						// Bestiary is for NPC/MOB middle-click. Items always go to Recipe catalogue query.
						if (!TrySetCurrentPanel(0))
							RbjDiag.Warn("PerformHoveredItemQuery: SetPanel(Recipe) from Bestiary failed — continuing");
						else
							RbjDiag.Info($"PerformHoveredItemQuery: Bestiary→Recipe panel for item type={hoveredType}");
						targetPanel = 0;
						showRecipeBrowser = HandleRecipeCatalogueQuery(hoveredType, allowToggleClose);
						break;
					default:
						RbjDiag.Warn($"PerformHoveredItemQuery: unknown panel={currentPanel}");
						break;
				}

				SetShowRecipeBrowser(recipeBrowserUi, showRecipeBrowser);

				int queryAfter = targetPanel switch
				{
					0 => GetQueryItem(_recipeQueryItemField?.GetValue(_recipeCatalogueInstanceField?.GetValue(null)))?.type ?? 0,
					1 => GetSlotItem(_craftRecipeResultSlotField?.GetValue(_craftInstanceField?.GetValue(null)))?.type ?? 0,
					3 => GetQueryItem(_bestiaryQueryItemField?.GetValue(_bestiaryInstanceField?.GetValue(null)))?.type ?? 0,
					_ => hoveredType
				};

				bool ok = targetPanel == 2 || queryAfter == hoveredType;
				RbjDiag.Info(
					$"PerformHoveredItemQuery end fromPanel={currentPanel} targetPanel={targetPanel} " +
					$"showAfter={GetShowRecipeBrowser()} querySlotType={queryAfter} expected={hoveredType} " +
					$"expectedName='{Lang.GetItemNameValue(hoveredType)}' ok={ok}");

				if (!ok)
				{
					RbjDiag.Warn(
						$"PerformHoveredItemQuery MISMATCH panel={targetPanel} " +
						$"queryAfter={queryAfter} expected={hoveredType} " +
						$"recipeReplaceMethod={( _recipeQueryReplaceMethod != null ? "ok" : "null" )} " +
						$"catalogue={(_recipeCatalogueInstanceField?.GetValue(null) != null)}");
				}
				if (ok)
				{
					Terraria.Audio.SoundEngine.PlaySound(Terraria.ID.SoundID.MenuTick);

					if (stickyTooltip)
					{
						Item tip = tipSnap;
						if (tip == null)
						{
							tip = new Item();
							tip.SetDefaults(hoveredType);
						}

						HoverTooltipSuppress.Hold(tip, reason: $"inv→RB panel={targetPanel} type={hoveredType}");
					}
					else
					{
						RbjDiag.Info(
							$"HoverHold SKIP type={hoveredType} (stickyTooltip=false world/tip policy)");
						RbjDiagPolicy.NoteHoverHoldSkip();
					}
				}
			}
			catch (Exception ex)
			{
				RbjDiag.Error("PerformHoveredItemQuery crashed", ex);
			}
			finally
			{
				ResetPendingCursorQueryClear();
			}
		}

		private static bool HandleRecipeCatalogueQuery(int hoveredType, bool allowToggleClose = true)
		{
			object recipeCatalogue = _recipeCatalogueInstanceField?.GetValue(null);
			if (recipeCatalogue == null)
			{
				RbjDiag.Warn("HandleRecipeCatalogueQuery: catalogue null");
				return true;
			}

			object queryItem = _recipeQueryItemField?.GetValue(recipeCatalogue);
			if (queryItem == null)
			{
				RbjDiag.Warn("HandleRecipeCatalogueQuery: queryItem slot null");
				return true;
			}

			Item current = GetQueryItem(queryItem);
			int before = current?.type ?? 0;
			if (current != null && current.type == hoveredType)
			{
				if (!allowToggleClose)
				{
					// Re-apply so the slot visually refreshes — "already set" felt like a failed send.
					try
					{
						if (_recipeQueryReplaceMethod != null)
							_recipeQueryReplaceMethod.Invoke(queryItem, new object[] { hoveredType });
						else
							InvokeReplaceWithFake(queryItem, hoveredType);
					}
					catch (Exception ex)
					{
						RbjDiag.Error("HandleRecipeCatalogueQuery same-type reapply failed", ex);
					}

					RbjDiag.Info($"HandleRecipeCatalogueQuery: already type={hoveredType}, reapplied (NOOP visual)");
					return true;
				}

				RbjDiag.Info($"HandleRecipeCatalogueQuery: same type toggle close");
				return !GetShowRecipeBrowser();
			}

			try
			{
				if (_recipeQueryReplaceMethod != null)
					_recipeQueryReplaceMethod.Invoke(queryItem, new object[] { hoveredType });
				else
					InvokeReplaceWithFake(queryItem, hoveredType);
			}
			catch (Exception ex)
			{
				RbjDiag.Error("HandleRecipeCatalogueQuery ReplaceWithFake failed", ex);
			}

			int after = GetQueryItem(queryItem)?.type ?? 0;
			RbjDiag.Info($"HandleRecipeCatalogueQuery ReplaceWithFake {before}→{after} (want {hoveredType})");
			return true;
		}

		private static bool HandleCraftQuery(int hoveredType, bool allowToggleClose = true)
		{
			object craftUi = _craftInstanceField?.GetValue(null);
			if (craftUi == null)
			{
				RbjDiag.Warn("HandleCraftQuery: craftUi null");
				return true;
			}

			object recipeResultSlot = _craftRecipeResultSlotField?.GetValue(craftUi);
			Item current = GetSlotItem(recipeResultSlot);
			if (current != null && current.type == hoveredType)
			{
				if (!allowToggleClose)
					return true;
				return !GetShowRecipeBrowser();
			}

			try
			{
				_craftSetItemMethod?.Invoke(craftUi, new object[] { hoveredType });
			}
			catch (Exception ex)
			{
				RbjDiag.Error("HandleCraftQuery SetItem failed", ex);
			}

			int after = GetSlotItem(_craftRecipeResultSlotField?.GetValue(craftUi))?.type ?? 0;
			RbjDiag.Info($"HandleCraftQuery SetItem → {after} (want {hoveredType})");
			return true;
		}

		private static bool GetShowRecipeBrowser()
		{
			object recipeBrowserUi = _recipeBrowserUiInstanceField?.GetValue(null);
			if (recipeBrowserUi == null)
				return false;

			if (_recipeBrowserShowProperty != null)
				return (bool)_recipeBrowserShowProperty.GetValue(recipeBrowserUi);

			return _recipeBrowserShowField != null && (bool)_recipeBrowserShowField.GetValue(recipeBrowserUi);
		}

		private static void SetShowRecipeBrowser(object recipeBrowserUi, bool show)
		{
			if (_recipeBrowserShowProperty != null)
			{
				_recipeBrowserShowProperty.SetValue(recipeBrowserUi, show);
				return;
			}

			_recipeBrowserShowField?.SetValue(recipeBrowserUi, show);
		}

		private static Item GetQueryItem(object querySlot)
		{
			if (querySlot == null)
				return null;

			FieldInfo itemField = GetCachedItemField(querySlot.GetType());
			return itemField?.GetValue(querySlot) as Item;
		}

		private static Item GetSlotItem(object slot)
		{
			if (slot == null)
				return null;

			FieldInfo itemField = GetCachedItemField(slot.GetType());
			return itemField?.GetValue(slot) as Item;
		}

		private static readonly System.Collections.Generic.Dictionary<Type, FieldInfo> _itemFieldCache = new();

		private static FieldInfo GetCachedItemField(Type type)
		{
			if (_itemFieldCache.TryGetValue(type, out FieldInfo cached))
				return cached;

			FieldInfo found = type.GetField("item", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			_itemFieldCache[type] = found;
			return found;
		}

		private static void ClearFilterField(FieldInfo catalogueInstanceField, FieldInfo filterField)
		{
			if (catalogueInstanceField == null || filterField == null)
				return;

			object catalogue = catalogueInstanceField.GetValue(null);
			if (catalogue == null)
				return;

			object textBox = filterField.GetValue(catalogue);
			if (textBox == null)
				return;

			SetFilterText(textBox, string.Empty);
			UnfocusTextBox(textBox);
		}

		private static void SetFilterText(object textBox, string text)
		{
			if (textBox == null)
			{
				RbjDiag.Warn("SetFilterText aborted: textBox null");
				return;
			}

			if (_setTextMethod == null)
			{
				RbjDiag.Warn("SetFilterText aborted: SetText reflection null (bridge incomplete)");
				return;
			}

			Patches.RecipeBrowserSetTextPatch.IsProgrammaticUpdate = true;
			try
			{
				_setTextMethod.Invoke(textBox, new object[] { text });
				_currentStringField?.SetValue(textBox, text);
			}
			catch (Exception ex)
			{
				RbjDiag.Error("SetFilterText Invoke failed", ex);
			}
			finally
			{
				Patches.RecipeBrowserSetTextPatch.IsProgrammaticUpdate = false;
			}
		}

		private static void UnfocusTextBox(object textBox)
		{
			if (_focusedField == null || _unfocusMethod == null)
				return;

			if (_focusedField.GetValue(textBox) is bool focused && focused)
				_unfocusMethod.Invoke(textBox, null);
		}

		private static bool WasBindingJustPressed(
			ModKeybind keybind,
			KeyboardState previousKeyboard,
			bool previousMouseLeft,
			bool previousMouseRight,
			bool previousMouseMiddle)
		{
			if (_keybindBindingsProperty == null)
				return false;

			if (_keybindBindingsProperty.GetValue(keybind) is not ReadOnlyCollection<string> bindings)
				return false;

			KeyboardState currentKeyboard = Keyboard.GetState();

			foreach (string binding in bindings)
			{
				if (string.IsNullOrEmpty(binding))
					continue;

				if (TryGetMouseJustPressed(binding, previousMouseLeft, previousMouseRight, previousMouseMiddle))
					return true;

				if (!Enum.TryParse(binding, true, out Keys key))
					continue;

				if (currentKeyboard.IsKeyDown(key) && !previousKeyboard.IsKeyDown(key))
					return true;
			}

			return false;
		}

		private static bool TryGetMouseJustPressed(
			string binding,
			bool previousMouseLeft,
			bool previousMouseRight,
			bool previousMouseMiddle)
		{
			switch (binding.ToLowerInvariant())
			{
				case "mouse1":
					return Main.mouseLeft && !previousMouseLeft;
				case "mouse2":
					return Main.mouseRight && !previousMouseRight;
				case "mouse3":
					return Main.mouseMiddle && !previousMouseMiddle;
				default:
					return false;
			}
		}
	}
}
