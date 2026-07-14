using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace RecipeBrowserJPChatSearch.Patches
{
	internal static class RecipeBrowserImePanelPatch
	{
		private delegate void orig_DrawWindowsIMEPanel(Main self, Vector2 position, float xAnchor);

		public static void Apply()
		{
			MethodInfo method = typeof(Main).GetMethod(
				"DrawWindowsIMEPanel",
				BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

			if (method == null)
				return;

			MonoModHooks.Add(method, (orig_DrawWindowsIMEPanel orig, Main self, Vector2 position, float xAnchor) =>
			{
				if (RecipeBrowserInputHelper.ActiveRecipeBrowserTextBox != null)
					return;

				orig(self, position, xAnchor);
			});
		}
	}
}
