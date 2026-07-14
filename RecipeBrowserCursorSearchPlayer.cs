using Terraria;
using Terraria.ModLoader;

namespace RecipeBrowserJPChatSearch
{
	internal class RecipeBrowserCursorSearchPlayer : ModPlayer
	{
		public override void PreUpdate()
		{
			if (Main.gameMenu || Player != Main.LocalPlayer)
				return;

			RecipeBrowserCursorSearchBridge.TryInitialize();

			if (!Main.HoverItem.IsAir)
				RecipeBrowserCursorSearchBridge.RememberHoveredItem(Main.HoverItem.type);
		}
	}
}
