using System.Collections.Generic;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RecipeBrowserJPChatSearch.Patches;
using Terraria;
using Terraria.ModLoader;
using Terraria.UI;

namespace RecipeBrowserJPChatSearch
{
	internal class RecipeBrowserCursorSearchSystem : ModSystem
	{
		public override void OnWorldLoad()
		{
			RecipeBrowserCursorSearchBridge.TryInitialize();
			ModKeybinds.EnsureDefaultBindingsApplied();
		}

		public override void PreUpdateEntities()
		{
			if (Main.gameMenu)
				return;

			ChatBrowseHelper.Update();
		}

		public override void PostUpdateEverything()
		{
			if (Main.gameMenu)
				return;

			ChatBrowseHelper.PostUpdateSyncScroll();
			if (!Main.drawingPlayerChat)
			{
				WorldPlacedItemHover.TickSticky();
			}

			RecipeBrowserCursorSearchBridge.TickRememberedHoveredItem();
		}

		public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
		{
			if (Main.gameMenu || Main.drawingPlayerChat)
				return;

			// Before Vanilla Mouse Text: re-apply the transferred item tip.
			HoverTooltipSuppress.ApplyDrawOnlyMute(layers);
		}

		public override void PostDrawInterface(SpriteBatch spriteBatch)
		{
			if (Main.gameMenu)
				return;

			RbjRenderHealth.NotifyPostDrawBegin(
				Main.drawingPlayerChat ? "chat" : "world");

			// Chat compose with many item tags is already draw-heavy (RB icons).
			// Skip world-zoom mouse capture/restore and use a lighter PostDraw path.
			if (Main.drawingPlayerChat)
			{
				try
				{
					PostDrawInterfaceWhileChatting();
				}
				catch (System.Exception ex)
				{
					RbjDiag.Error("PostDrawInterface (chat) crashed", ex);
					RbjDiag.Warn($"RbjRenderHealth AFTER chat-crash {RbjRenderHealth.Snapshot("chat-crash")}");
				}
				finally
				{
					RbjRenderHealth.TickAfterPostDraw("chat");
				}

				return;
			}

			// World-pick / PointerSnapshot temporarily remaps Main.mouseX/Y.
			// Hover tips use those coords — restore before this method ends or tip jumps.
			RbjCursor.MouseSnap mouseAtEntry = RbjCursor.CaptureMouse();
			try
			{
				PostDrawInterfaceCore(spriteBatch);
			}
			catch (System.Exception ex)
			{
				// Never let hotkey/UI reflection take down the client draw loop.
				RbjDiag.Error("PostDrawInterface crashed (hotkey/transfer path)", ex);
				RbjDiag.Warn($"RbjRenderHealth AFTER world-crash {RbjRenderHealth.Snapshot("world-crash")}");
			}
			finally
			{
				RbjCursor.RestoreMouse(mouseAtEntry, logIfChanged: true, reason: "PostDrawInterface");
				RbjRenderHealth.TickAfterPostDraw("world", mouseAtEntry);
			}
		}

		/// <summary>
		/// While the chat input is open: keep middle-click search / sticky memory,
		/// skip world-pick hint capture and MouseRestore diagnostics.
		/// </summary>
		private void PostDrawInterfaceWhileChatting()
		{
			var sw = System.Diagnostics.Stopwatch.StartNew();
			try
			{
				RecipeBrowserCursorSearchBridge.TryInitialize();

				bool hotkey = RecipeBrowserCursorSearchBridge.IsSearchHotkeyJustPressed();
				bool handled = false;
				if (hotkey)
				{
					handled = MiddleClickTransferPatch.TryHandleSearchHotkeyTransfer()
						|| RecipeBrowserCursorSearchBridge.TryHandleHoverItemMiddleClickQuery();
				}

				if (!Main.HoverItem.IsAir)
					RecipeBrowserCursorSearchBridge.RememberHoveredItem(Main.HoverItem.type);

				RecipeBrowserCursorSearchBridge.RememberInputState(
					Keyboard.GetState(),
					Main.mouseLeft,
					Main.mouseRight,
					Main.mouseMiddle);

				InventoryHoverTrackPatch.ClearFrame();
				NpcHoverTrack.ClearFrame();
				WorldPlacedItemHover.ClearFrame();

				if (handled)
					HoverTooltipSuppress.DrawForcedTooltipIfNeeded();
			}
			finally
			{
				sw.Stop();
				ChatComposePerf.AddPostDrawMicros(ChatComposePerf.ElapsedMicros(sw));
				ChatComposePerf.EndChatFrame();
			}
		}

		private void PostDrawInterfaceCore(SpriteBatch spriteBatch)
		{
			WorldPlacedItemHover.CaptureFrameHints();

			RecipeBrowserCursorSearchBridge.TryInitialize();

			ChatBrowseHelper.DrawHistoryOverlay();

			bool hotkey = RecipeBrowserCursorSearchBridge.IsSearchHotkeyJustPressed();
			if (hotkey)
			{
				RbjRenderHealth.MarkWithSnap(
					$"search-hotkey edge inv={Main.playerInventory} " +
					$"slot={InventoryHoverTrackPatch.HoveredSource}:{InventoryHoverTrackPatch.HoveredItemType}",
					"hotkey");
				HoverTooltipLocationProbe.Arm(
					$"hotkeyEdge source={InventoryHoverTrackPatch.HoveredSource} " +
					$"slotType={InventoryHoverTrackPatch.HoveredItemType} " +
					$"hoverType={(Main.HoverItem.IsAir ? 0 : Main.HoverItem.type)}");
				RbjDiag.Info(
					$"SearchHotkeyEdge PostDraw " +
					$"physMid={RecipeBrowserCursorSearchBridge.IsPhysicalMiddleDown()} " +
					$"gameMid={Main.mouseMiddle} " +
					$"slotHover={InventoryHoverTrackPatch.HoveringTrackedSlot} " +
					$"sticky={InventoryHoverTrackPatch.UsingStickyTrack} " +
					$"source={InventoryHoverTrackPatch.HoveredSource} " +
					$"slotType={InventoryHoverTrackPatch.HoveredItemType} " +
					$"hoverType={(Main.HoverItem.IsAir ? 0 : Main.HoverItem.type)} " +
					$"playerInv={Main.playerInventory} " +
					$"cursor=({Main.mouseX},{Main.mouseY}) " +
					$"region={HoverTooltipLocationProbe.ClassifyScreenRegion(Main.mouseX, Main.mouseY)}");
			}

			bool handled = false;
			if (ChatBrowseHelper.BrowseMode)
			{
				handled = RecipeBrowserCursorSearchBridge.TryHandleChatCursorSearch();
			}
			else if (MiddleClickTransferPatch.TryHandleSearchHotkeyTransfer())
			{
				handled = true;
			}
			else
			{
				handled = RecipeBrowserCursorSearchBridge.TryHandleHoverItemMiddleClickQuery();
			}

			if (hotkey && !handled)
			{
				SearchHotkeyProbe.LogBlock(
					"hotkey-unhandled",
					$"sticky={InventoryHoverTrackPatch.UsingStickyTrack} " +
					$"source={InventoryHoverTrackPatch.HoveredSource} " +
					$"slotType={InventoryHoverTrackPatch.HoveredItemType}");
			}

			if (hotkey || HoverTooltipLocationProbe.Capturing)
			{
				HoverTooltipLocationProbe.NoteHoverItemDelta(
					handled ? "postDraw-handled" : "postDraw-idle");
			}

			if (!Main.HoverItem.IsAir)
				RecipeBrowserCursorSearchBridge.RememberHoveredItem(Main.HoverItem.type);
			else
				RecipeBrowserCursorSearchBridge.ClearRememberedHoveredItem();

			RecipeBrowserCursorSearchBridge.RememberInputState(
				Keyboard.GetState(),
				Main.mouseLeft,
				Main.mouseRight,
				Main.mouseMiddle);

			InventoryHoverTrackPatch.ClearFrame();
			NpcHoverTrack.ClearFrame();
			WorldPlacedItemHover.ClearFrame();

			// Keep the transferred item's tooltip stable for <1s (does not clear HoverItem for next clicks).
			HoverTooltipSuppress.DrawForcedTooltipIfNeeded();
		}
	}
}
