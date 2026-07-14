using System;
using System.Reflection;
using System.Text;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using Terraria.UI;

namespace RecipeBrowserJPChatSearch.Patches
{
	/// <summary>
	/// One searchable block in client.log / RBJ_Debug when the search hotkey fires.
	/// </summary>
	internal static class SearchHotkeyProbe
	{
		internal static void LogBlock(string outcome, string detail = null)
		{
			// Always-on short line only for problems; successes stay verbose-only
			// (middle-click hold would otherwise flood Workshop clients every ~180ms).
			bool noteworthy = outcome.IndexOf("fail", StringComparison.OrdinalIgnoreCase) >= 0
				|| outcome.IndexOf("miss", StringComparison.OrdinalIgnoreCase) >= 0
				|| outcome.IndexOf("skip", StringComparison.OrdinalIgnoreCase) >= 0
				|| outcome.IndexOf("unhandled", StringComparison.OrdinalIgnoreCase) >= 0;
			if (RbjDiag.Enabled || noteworthy)
			{
				RbjDiag.Release(
					string.IsNullOrEmpty(detail)
						? $"Transfer {outcome}"
						: $"Transfer {outcome} | {detail}");
			}

			if (!RbjDiag.Enabled)
				return;

			bool msOpen = MagicStorageSearchHelper.IsAnyTargetUiOpen();
			bool overRb = RecipeBrowserCursorSearchBridge.IsMouseOverRecipeBrowserPanel();
			bool overMsName = MagicStorageSearchHelper.IsMouseOverItemSlot();
			bool slotHover = InventoryHoverTrackPatch.HoveringTrackedSlot;
			int slotType = InventoryHoverTrackPatch.HoveredItemType;
			string slotSource = InventoryHoverTrackPatch.HoveredSource;
			int hoverType = Main.HoverItem.IsAir ? 0 : Main.HoverItem.type;
			string hoverName = hoverType > 0 ? Lang.GetItemNameValue(hoverType) : "";

			var sb = new StringBuilder(512);
			sb.AppendLine("=== SearchHotkey Probe ===");
			sb.AppendLine($"outcome={outcome}");
			if (!string.IsNullOrEmpty(detail))
				sb.AppendLine($"detail={detail}");
			sb.AppendLine(
				$"msOpen={msOpen} overMs(name)={overMsName} overRb={overRb} " +
				$"physMid={RecipeBrowserCursorSearchBridge.IsPhysicalMiddleDown()} gameMid={Main.mouseMiddle}");
			sb.AppendLine(RbjDiag.PointerSnapshot());
			sb.AppendLine(
				$"HoverItem type={hoverType} air={Main.HoverItem.IsAir} name='{hoverName}'");
			sb.AppendLine(
				$"invTrack slotHover={slotHover} source={slotSource} type={slotType} " +
				$"playerInv={Main.playerInventory}");
			sb.AppendLine(
				$"hoverSuppress active={HoverTooltipSuppress.Active} remainingMs={HoverTooltipSuppress.RemainingMs} " +
				$"armReason='{HoverTooltipSuppress.ArmReason}'");
			AppendMsUnderCursor(sb);
			AppendRbUnderCursor(sb);
			sb.Append("=== SearchHotkey Probe End ===");
			RbjDiag.Info(sb.ToString());
		}

		private static void AppendMsUnderCursor(StringBuilder sb)
		{
			if (!MagicStorageSearchHelper.TryGetUiRoot(out UIElement root))
			{
				sb.AppendLine("msRoot=null");
				return;
			}

			sb.AppendLine($"msRoot={root.GetType().FullName}");
			UIElement under = root.GetElementAt(new Vector2(Main.mouseX, Main.mouseY));
			sb.AppendLine($"msUnderChain={FormatParentChain(under)}");

			if (TryResolveMsItem(under, out Item item, out string how))
			{
				sb.AppendLine(
					$"msResolved how={how} type={item.type} air={item.IsAir} " +
					$"name='{RecipeBrowserNameSearchHelper.GetSearchName(item)}'");
			}
			else
			{
				sb.AppendLine($"msResolved how=none ({how})");
			}
		}

		private static void AppendRbUnderCursor(StringBuilder sb)
		{
			if (!RecipeBrowserCursorSearchBridge.TryGetRecipeBrowserPanelUnderMouse(out UIElement under))
			{
				sb.AppendLine("rbUnder=none");
				return;
			}

			sb.AppendLine($"rbUnderChain={FormatParentChain(under)}");
		}

		internal static bool TryResolveMsItem(UIElement start, out Item item, out string how)
		{
			item = null;
			how = "no-start";
			if (start == null)
			{
				how = "under-null";
				return false;
			}

			Type msSlotType = MiddleClickTransferPatch.MsSlotType;
			PropertyInfo storedProp = MiddleClickTransferPatch.MsStoredItemProperty;
			if (msSlotType == null || storedProp == null)
			{
				how = "ms-slot-reflect-missing";
				return false;
			}

			UIElement cur = start;
			while (cur != null)
			{
				if (msSlotType.IsInstanceOfType(cur))
				{
					item = storedProp.GetValue(cur) as Item;
					if (item != null && !item.IsAir)
					{
						how = "MagicStorageItemSlot.StoredItem";
						return true;
					}

					how = $"MagicStorageItemSlot.empty type={(item?.type ?? -1)}";
					return false;
				}

				if (TryItemFromNewUiSlotZone(cur, out item, out string zoneHow))
				{
					how = zoneHow;
					return true;
				}

				cur = cur.Parent;
			}

			how = "no-ms-slot-in-chain";
			return false;
		}

		private static bool TryItemFromNewUiSlotZone(UIElement el, out Item item, out string how)
		{
			item = null;
			how = null;
			if (el == null || el.GetType().Name != "NewUISlotZone")
				return false;

			try
			{
				PropertyInfo hoverSlotProp = el.GetType().GetProperty(
					"HoverSlot",
					BindingFlags.Instance | BindingFlags.Public);
				PropertyInfo slotsProp = el.GetType().GetProperty(
					"Slots",
					BindingFlags.Instance | BindingFlags.Public);
				PropertyInfo colsProp = el.GetType().GetProperty(
					"NumColumns",
					BindingFlags.Instance | BindingFlags.Public);
				if (hoverSlotProp == null || slotsProp == null || colsProp == null)
				{
					how = "NewUISlotZone.reflect-incomplete";
					return false;
				}

				int hoverSlot = (int)hoverSlotProp.GetValue(el);
				int cols = (int)colsProp.GetValue(el);
				if (hoverSlot < 0 || cols <= 0)
				{
					how = $"NewUISlotZone.HoverSlot={hoverSlot} cols={cols}";
					return false;
				}

				Array slots = slotsProp.GetValue(el) as Array;
				if (slots == null || slots.Rank != 2)
				{
					how = "NewUISlotZone.Slots-bad";
					return false;
				}

				int row = hoverSlot / cols;
				int col = hoverSlot % cols;
				object slotObj = slots.GetValue(row, col);
				if (slotObj == null)
				{
					how = "NewUISlotZone.slot-null";
					return false;
				}

				PropertyInfo stored = MiddleClickTransferPatch.MsStoredItemProperty
					?? slotObj.GetType().GetProperty("StoredItem", BindingFlags.Instance | BindingFlags.Public);
				item = stored?.GetValue(slotObj) as Item;
				if (item != null && !item.IsAir)
				{
					how = $"NewUISlotZone.HoverSlot={hoverSlot}";
					return true;
				}

				how = $"NewUISlotZone.HoverSlot={hoverSlot}-empty";
				return false;
			}
			catch (Exception ex)
			{
				how = $"NewUISlotZone.ex={ex.GetType().Name}";
				return false;
			}
		}

		private static string FormatParentChain(UIElement under, int max = 8)
		{
			if (under == null)
				return "(null)";

			var parts = new StringBuilder();
			UIElement cur = under;
			int n = 0;
			while (cur != null && n < max)
			{
				if (n > 0)
					parts.Append(" < ");
				parts.Append(cur.GetType().Name);
				if (cur.IsMouseHovering)
					parts.Append('*');
				cur = cur.Parent;
				n++;
			}

			if (cur != null)
				parts.Append(" < …");
			return parts.ToString();
		}
	}
}
