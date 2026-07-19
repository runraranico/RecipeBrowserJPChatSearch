using System;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;

namespace RecipeBrowserJPChatSearch.Patches
{
	/// <summary>
	/// Search-hotkey transfer (Controls-bound key, JustPressed only):
	/// - Magic Storage item → Recipe Browser (query / item-tab name)
	/// - Recipe Browser item → Magic Storage search bar
	///   · Storage UI open → keep it, set storage search
	///   · Crafting UI open → keep it, set crafting search
	///   · None open → open nearest reachable Crafting Access only, then set search
	/// </summary>
	internal static class MiddleClickTransferPatch
	{
		private static Type _msSlotType;
		private static PropertyInfo _msStoredItemProperty;
		private static Type _rbItemSlotType;
		private static FieldInfo _rbItemField;

		internal static Type MsSlotType => _msSlotType;
		internal static PropertyInfo MsStoredItemProperty => _msStoredItemProperty;

		public static void Apply(Mod magicStorageMod, Mod recipeBrowserMod)
		{
			if (magicStorageMod != null)
			{
				_msSlotType = magicStorageMod.Code.GetType("MagicStorage.UI.MagicStorageItemSlot");
				_msStoredItemProperty = _msSlotType?.GetProperty(
					"StoredItem",
					BindingFlags.Instance | BindingFlags.Public);
				if (_msSlotType == null || _msStoredItemProperty == null)
					RbjDiag.Warn("MiddleClickTransferPatch: Magic Storage slot reflection incomplete");
			}

			if (recipeBrowserMod != null)
			{
				_rbItemSlotType = recipeBrowserMod.Code.GetType("RecipeBrowser.UIElements.UIItemSlot")
					?? recipeBrowserMod.Code.GetType("RecipeBrowser.UIItemSlot");
				_rbItemField = _rbItemSlotType?.GetField(
					"item",
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (_rbItemSlotType == null || _rbItemField == null)
					RbjDiag.Warn("MiddleClickTransferPatch: Recipe Browser UIItemSlot reflection incomplete");
			}

			RbjDiag.Info("MiddleClickTransferPatch ready (search hotkey via PostDraw)");
		}

		internal static void Unload()
		{
			_msSlotType = null;
			_msStoredItemProperty = null;
			_rbItemSlotType = null;
			_rbItemField = null;
		}

		/// <summary>
		/// Called from PostDraw. Returns true when this frame's search hotkey was consumed for MS↔RB.
		/// </summary>
		internal static bool TryHandleSearchHotkeyTransfer()
		{
			if (!RecipeBrowserCursorSearchBridge.IsSearchHotkeyJustPressed())
				return false;

			// Snapshot before anything else touches HoverItem.
			Item hoverSnap = CloneHoverItemOrNull();
			int snapType = hoverSnap?.type ?? 0;

			if (ChatBrowseHelper.BrowseMode)
			{
				SearchHotkeyProbe.LogBlock("transfer-skip", "browseMode");
				return false;
			}

			if (Main.drawingPlayerChat || PlayerInput.WritingText)
			{
				SearchHotkeyProbe.LogBlock("transfer-skip", "chatOrWritingText");
				return false;
			}

			if (RecipeBrowserInputHelper.ActiveRecipeBrowserTextBox != null)
			{
				SearchHotkeyProbe.LogBlock("transfer-skip", "rbTextBoxFocused");
				return false;
			}

			if (RecipeBrowserCursorSearchBridge.IsModifierBlockingCursorSearch())
			{
				SearchHotkeyProbe.LogBlock("transfer-skip", "modifierBlocking");
				return false;
			}

			bool overRb = RecipeBrowserCursorSearchBridge.IsMouseOverRecipeBrowserPanel();
			bool overRbItem = RecipeBrowserCursorSearchBridge.IsMouseOverRecipeBrowserItemSlot();
			bool overMs = MagicStorageSearchHelper.IsMouseOverItemSlot();
			bool msOpen = MagicStorageSearchHelper.IsAnyTargetUiOpen();

			RbjDiag.Info(
				$"TransferAttempt begin snapType={snapType} overRb={overRb} overRbItem={overRbItem} " +
				$"overMs={overMs} msOpen={msOpen} hoverAir={hoverSnap == null}");

			// RB→MS only when cursor is on an RB item icon (not panel chrome overlapping inventory).
			if (overRbItem && TryHandleRecipeBrowserHover(hoverSnap, out string rbHow, out Item rbItem))
			{
				if (rbItem != null && !rbItem.IsAir)
				{
					SearchHotkeyProbe.LogBlock("transfer-ok", $"RB→MS how={rbHow}");
					HoverTooltipSuppress.Hold(rbItem, reason: "RB→MS");
				}
				else
				{
					SearchHotkeyProbe.LogBlock("transfer-ok", $"RB→MS consumed-no-search how={rbHow}");
				}

				return true;
			}

			if (TryHandleMagicStorageHover(hoverSnap, out string msHow, out Item msItem))
			{
				SearchHotkeyProbe.LogBlock("transfer-ok", $"MS→RB how={msHow}");
				HoverTooltipSuppress.Hold(msItem, reason: "MS→RB");
				return true;
			}

			// Log misses only when aiming at MS/RB item UIs.
			if (msOpen && (overRbItem || overMs))
			{
				SearchHotkeyProbe.LogBlock(
					"transfer-miss",
					$"overRbItem={overRbItem} overMs={overMs} snapType={snapType}");
			}

			return false;
		}

		private static bool TryHandleMagicStorageHover(Item hoverSnap, out string how, out Item transferred)
		{
			how = null;
			transferred = null;
			if (_msSlotType == null || _msStoredItemProperty == null)
				return false;

			if (!TryGetHoveredMagicStorageItem(hoverSnap, out Item item, out how) || item.IsAir)
				return false;

			RbjDiag.Info(
				$"MS→RB search-hotkey how={how} type={item.type} name='{RecipeBrowserNameSearchHelper.GetSearchName(item)}'");
			if (!RecipeBrowserNameSearchHelper.TryTransferFromMagicStorage(item))
			{
				RbjDiag.Warn("MS→RB failed to transfer to Recipe Browser");
				SearchHotkeyProbe.LogBlock("transfer-fail", $"MS→RB how={how} type={item.type}");
				return false;
			}

			// Sound is played inside PerformInvMsSyncedQuery.
			transferred = item;
			return true;
		}

		private static bool TryHandleRecipeBrowserHover(Item hoverSnap, out string how, out Item transferred)
		{
			how = null;
			transferred = null;
			if (_rbItemSlotType == null || _rbItemField == null)
				return false;

			if (!TryGetHoveredRecipeBrowserItem(hoverSnap, out Item item, out how) || item.IsAir)
				return false;

			string itemName = RecipeBrowserNameSearchHelper.GetSearchName(item);
			MagicStorageUiKind uiKind = MagicStorageCraftingAccessHelper.GetCurrentUiKind();
			string keyLabel = ModKeybinds.DescribeSearchHotkeyForLog();

			RbjDiag.Info(
				$"RBTransferKeyPressed key=[{keyLabel}] how={how} itemType={item.type} itemName={itemName} " +
				$"CurrentUi={MagicStorageCraftingAccessHelper.UiKindLabel(uiKind)}");

			if (uiKind == MagicStorageUiKind.Storage)
			{
				RbjDiag.Info("Action=SetStorageSearch");
				if (!MagicStorageSearchHelper.TrySetSearchFromItem(item))
				{
					RbjDiag.Info("SetSearchSuccess=false reason=storage-search-fail");
					SearchHotkeyProbe.LogBlock("transfer-fail", $"RB→StorageSearch type={item.type}");
					return false;
				}

				RbjDiag.Info("SetSearchSuccess=true");
				SoundEngine.PlaySound(SoundID.MenuTick);
				transferred = item;
				return true;
			}

			if (uiKind == MagicStorageUiKind.Crafting)
			{
				RbjDiag.Info("Action=SetCraftingSearch");
				if (!MagicStorageSearchHelper.TrySetSearchFromItem(item))
				{
					RbjDiag.Info("SetSearchSuccess=false reason=crafting-search-fail");
					SearchHotkeyProbe.LogBlock("transfer-fail", $"RB→CraftingSearch type={item.type}");
					return false;
				}

				RbjDiag.Info("SetSearchSuccess=true");
				SoundEngine.PlaySound(SoundID.MenuTick);
				transferred = item;
				return true;
			}

			// No MS UI open → Crafting Access only (never auto-open Heart / Storage Access / Remote).
			RbjDiag.Info("Action=OpenNearestCraftingAccessThenSearch");
			if (!MagicStorageCraftingAccessHelper.TryOpenNearestCraftingAccessAndSetSearch(item, out string failReason))
			{
				RbjDiag.Info($"SetSearchSuccess=false reason={failReason}");
				SearchHotkeyProbe.LogBlock("transfer-fail", $"RB→OpenCraft type={item.type} {failReason}");
				// Consume the key on a valid RB icon; do not Hold a tip (search did not run).
				how = failReason;
				transferred = null;
				return true;
			}

			SoundEngine.PlaySound(SoundID.MenuTick);
			transferred = item;
			return true;
		}

		private static bool TryGetHoveredMagicStorageItem(Item hoverSnap, out Item item, out string how)
		{
			item = null;
			how = "no-root";
			if (_msSlotType == null || _msStoredItemProperty == null)
			{
				how = "reflect-missing";
				return false;
			}

			if (MagicStorageSearchHelper.TryGetItemUnderMouseRobust(out item, out how))
				return true;

			// Last resort: snapped HoverItem while cursor is on an MS slot/grid.
			if (MagicStorageSearchHelper.IsMouseOverItemSlot()
				&& hoverSnap != null
				&& !hoverSnap.IsAir)
			{
				item = hoverSnap;
				how = "HoverItem-snap-on-ms-slot";
				return true;
			}

			how = how ?? "no-ms-item";
			return false;
		}

		private static bool TryGetHoveredRecipeBrowserItem(Item hoverSnap, out Item item, out string how)
		{
			item = null;
			how = "no-rb";
			if (_rbItemSlotType == null || _rbItemField == null)
			{
				how = "reflect-missing";
				return false;
			}

			if (RecipeBrowserCursorSearchBridge.TryGetRecipeBrowserPanelUnderMouse(out UIElement under))
			{
				UIElement cur = under;
				while (cur != null)
				{
					if (_rbItemSlotType.IsInstanceOfType(cur))
					{
						item = _rbItemField.GetValue(cur) as Item;
						if (item != null && !item.IsAir)
						{
							how = "UIItemSlot-chain";
							return true;
						}
					}

					cur = cur.Parent;
				}

				// Deep ContainsPoint search for UIItemSlot under the RB panel root.
				if (TryFindRbSlotUnderMouse(under, out item))
				{
					how = "ContainsPoint-UIItemSlot";
					return true;
				}
			}

			// Do NOT fall back to HoverItem while merely over the RB panel.
			// Inventory under an overlapping RB panel still sets HoverItem; using it here
			// stole the click into RB→MS (MS search changed, query slot looked "broken").
			how = "no-rb-UIItemSlot";
			return false;
		}

		private static bool TryFindRbSlotUnderMouse(UIElement start, out Item item)
		{
			item = null;
			if (start == null || _rbItemSlotType == null || _rbItemField == null)
				return false;

			Vector2 mouse = new(Main.mouseX, Main.mouseY);
			UIElement root = start;
			while (root.Parent != null
				&& root.Parent.GetType().Name != "UserInterface"
				&& root.GetType().Name != "RecipeBrowserUI")
			{
				// Climb toward panel but stop at RecipeBrowserUI if present.
				if (root.GetType().Name == "UIDragableElement" || root.GetType().Name == "RecipeBrowserUI")
					break;
				root = root.Parent;
			}

			return TryFindRbSlotRecursive(root, mouse, out item);
		}

		private static bool TryFindRbSlotRecursive(UIElement el, Vector2 mouse, out Item item)
		{
			item = null;
			if (el == null)
				return false;

			foreach (UIElement child in el.Children)
			{
				if (TryFindRbSlotRecursive(child, mouse, out item))
					return true;
			}

			if (_rbItemSlotType.IsInstanceOfType(el) && el.ContainsPoint(mouse))
			{
				item = _rbItemField.GetValue(el) as Item;
				return item != null && !item.IsAir;
			}

			return false;
		}

		private static Item CloneHoverItemOrNull()
		{
			if (Main.HoverItem == null || Main.HoverItem.IsAir)
				return null;

			return Main.HoverItem.Clone();
		}
	}
}
