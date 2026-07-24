using Microsoft.Xna.Framework.Input;
using RecipeBrowserJPChatSearch.Patches;
using Terraria;
using Terraria.GameInput;

namespace RecipeBrowserJPChatSearch
{
	/// <summary>
	/// Past-log hotkey (default Back / Back Space) shows the expanded chat log without activating chat input.
	/// </summary>
	internal static class ChatBrowseHelper
	{
		private const int ScrollStepLines = 5;

		private static bool _browseMode;
		private static bool _lingerMode;
		private static int _lingerTimer;
		private static int _scrollOffset;
		private static KeyboardState _previousKeyboard;

		internal static bool BrowseMode => _browseMode;
		internal static bool IsDisplayingOverlay => _browseMode || _lingerMode;

		internal static void Update()
		{
			if (Main.gameMenu)
			{
				EndOverlay(resetOffset: true);
				_previousKeyboard = Keyboard.GetState();
				return;
			}

			KeyboardState keyboard = Keyboard.GetState();

			// Option 1: shorten /d item tags in the compose buffer as soon as they appear.
			ChatComposeTagShortener.TickComposeBuffer();

			if (Main.drawingPlayerChat && IsDisplayingOverlay)
				EndOverlay(resetOffset: true);

			if (_lingerMode)
				MaintainLingerMode(keyboard);
			else if (_browseMode)
				MaintainBrowseMode(keyboard);
			else
				TryBeginHistoryBrowse(keyboard);

			_previousKeyboard = keyboard;
		}

		internal static void PostUpdateSyncScroll()
		{
			if (!IsDisplayingOverlay)
				return;

			ChatMonitorOffsetHelper.ApplyStoredOffset(_scrollOffset);
		}

		internal static void DrawHistoryOverlay()
		{
			if (!IsDisplayingOverlay || Main.gameMenu)
				return;

			ChatMonitorDisplayHelper.ApplyOverlayLineLimit();
			try
			{
				ChatMonitorOffsetHelper.ApplyStoredOffset(_scrollOffset);
				Main.chatMonitor.DrawChat(drawingPlayerChat: true);
			}
			catch (System.Exception ex)
			{
				RbjDiag.Error("DrawHistoryOverlay DrawChat failed", ex);
				RbjRenderHealth.Mark("DrawHistoryOverlay FAILED");
				RbjRenderHealth.DumpTrail("draw-history-fail");
				RbjDiag.Release("DrawHistoryOverlay FAILED — SpriteBatch recovery attempted");
				TryRecoverUiSpriteBatch();
				EndOverlay(resetOffset: true);
			}
			finally
			{
				ChatMonitorDisplayHelper.RestoreLineLimit();
			}
		}

		/// <summary>
		/// If DrawChat threw mid-batch, try to leave SpriteBatch in a usable UI state
		/// so the next frame's world draw is less likely to stay black.
		/// </summary>
		private static void TryRecoverUiSpriteBatch()
		{
			try
			{
				Main.spriteBatch.End();
			}
			catch
			{
				// Already ended / not begun — ignore.
			}

			try
			{
				Main.spriteBatch.Begin(
					Microsoft.Xna.Framework.Graphics.SpriteSortMode.Deferred,
					Microsoft.Xna.Framework.Graphics.BlendState.AlphaBlend,
					Microsoft.Xna.Framework.Graphics.SamplerState.PointClamp,
					Microsoft.Xna.Framework.Graphics.DepthStencilState.None,
					Microsoft.Xna.Framework.Graphics.RasterizerState.CullCounterClockwise,
					null,
					Main.UIScaleMatrix);
			}
			catch (System.Exception ex)
			{
				RbjDiag.Warn($"DrawHistoryOverlay SpriteBatch recover failed: {ex.GetType().Name}");
			}
		}

		internal static void BeginLingerAfterCursorSearch()
		{
			_browseMode = false;
			_lingerMode = true;
			_lingerTimer = ChatMonitorDisplayHelper.GetVanillaChatLingerFrames();
		}

		private static void TryBeginHistoryBrowse(KeyboardState keyboard)
		{
			if (!ModKeybinds.IsPastLogHotkeyJustPressed())
				return;

			if (ShouldIgnoreHistoryBrowseInput())
				return;

			_lingerMode = false;
			_lingerTimer = 0;
			_browseMode = true;
			_scrollOffset = 0;
			PlayerInput.WritingText = false;
			Main.chatMonitor.ResetOffset();
			RbjDiag.Info("Past-log browse opened");
			RbjRenderHealth.Mark("past-log browse opened");
		}

		private static void MaintainBrowseMode(KeyboardState keyboard)
		{
			PlayerInput.WritingText = false;

			if (WasJustPressed(keyboard, Keys.Up))
				_scrollOffset += ScrollStepLines;
			else if (WasJustPressed(keyboard, Keys.Down))
				_scrollOffset -= ScrollStepLines;

			ChatMonitorOffsetHelper.ApplyStoredOffset(_scrollOffset);

			if (TryDismissOverlay(keyboard))
				return;

			if (WasJustPressed(keyboard, Keys.Enter))
				EndOverlay(resetOffset: true);
		}

		private static void MaintainLingerMode(KeyboardState keyboard)
		{
			PlayerInput.WritingText = false;

			if (TryDismissOverlay(keyboard))
				return;

			if (WasJustPressed(keyboard, Keys.Enter))
			{
				EndOverlay(resetOffset: true);
				return;
			}

			_lingerTimer--;
			if (_lingerTimer <= 0)
				EndOverlay(resetOffset: true);
		}

		private static bool TryDismissOverlay(KeyboardState keyboard)
		{
			if (!WasJustPressed(keyboard, Keys.Escape) && !Main.inputTextEscape)
				return false;

			EndOverlay(resetOffset: true);
			Main.inputTextEscape = false;
			return true;
		}

		private static void EndOverlay(bool resetOffset)
		{
			_browseMode = false;
			_lingerMode = false;
			_lingerTimer = 0;
			_scrollOffset = 0;

			if (resetOffset)
				Main.chatMonitor.ResetOffset();
		}

		private static bool ShouldIgnoreHistoryBrowseInput()
		{
			if (Main.blockInput)
				return true;

			if (Main.drawingPlayerChat || PlayerInput.WritingText)
				return true;

			if (RecipeBrowserInputHelper.ActiveRecipeBrowserTextBox != null)
				return true;

			return false;
		}

		private static bool WasJustPressed(KeyboardState keyboard, Keys key) =>
			keyboard.IsKeyDown(key) && !_previousKeyboard.IsKeyDown(key);
	}
}
