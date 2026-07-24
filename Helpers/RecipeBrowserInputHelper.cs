using Terraria;
using Terraria.GameInput;

namespace RecipeBrowserJPChatSearch
{
	internal static class RecipeBrowserInputHelper
	{
		internal static object ActiveRecipeBrowserTextBox { get; set; }

		internal static void SetActiveRecipeBrowserTextBox(object textBox)
		{
			ActiveRecipeBrowserTextBox = textBox;
		}

		internal static void ReleaseInputLocks()
		{
			Main.blockInput = false;
			PlayerInput.WritingText = false;
			Main.inputTextEnter = false;
			Main.inputTextEscape = false;
			ImeTextInputHandler.StopCapturing();
			CompositionTracker.Clear();
			ActiveRecipeBrowserTextBox = null;
		}

		internal static void ReleaseIfActiveBoxLostFocus(System.Func<object, bool> isFocused)
		{
			if (ActiveRecipeBrowserTextBox == null || isFocused == null)
				return;

			if (!isFocused(ActiveRecipeBrowserTextBox))
				ReleaseInputLocks();
		}
	}
}
