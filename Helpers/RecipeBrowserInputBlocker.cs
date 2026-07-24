using Terraria;
using Terraria.ModLoader;

namespace RecipeBrowserJPChatSearch
{
	internal class RecipeBrowserInputBlocker : ModSystem
	{
		internal static int SuppressChatFrames { get; set; }

		public override void PreUpdateEntities()
		{
			if (SuppressChatFrames <= 0)
				return;

			Main.oldInputText = Main.inputText;
			Main.drawingPlayerChat = false;
			SuppressChatFrames--;
		}

		public override void PostUpdateEverything()
		{
			if (SuppressChatFrames > 0)
				Main.drawingPlayerChat = false;

			Patches.RecipeBrowserPatches.ReleaseStaleInputCapture();
		}
	}
}
