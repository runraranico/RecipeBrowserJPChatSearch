using System;
using System.Reflection;
using Terraria;
using Terraria.ModLoader;

namespace RecipeBrowserJPChatSearch.Patches
{
	internal static class RecipeBrowserUnfocusPatch
	{
		private delegate void orig_Unfocus(object self);

		public static void Apply(Mod recipeBrowserMod)
		{
			Type textBoxType = recipeBrowserMod.Code.GetType("RecipeBrowser.NewUITextBox");
			if (textBoxType == null)
				return;

			MethodInfo unfocus = textBoxType.GetMethod("Unfocus", BindingFlags.Instance | BindingFlags.Public);
			if (unfocus == null)
				return;

			MonoModHooks.Add(unfocus, (orig_Unfocus orig, object self) =>
			{
				orig(self);
				RecipeBrowserInputHelper.ReleaseInputLocks();
				if (!Main.drawingPlayerChat)
					RecipeBrowserInputBlocker.SuppressChatFrames = 4;
			});

			MethodInfo openChat = typeof(Main).GetMethod("OpenPlayerChat", BindingFlags.Static | BindingFlags.Public);
			if (openChat != null)
			{
				MonoModHooks.Add(openChat, (Action orig) =>
				{
					if (RecipeBrowserInputBlocker.SuppressChatFrames > 0)
						return;

					orig();
				});
			}
		}
	}
}
