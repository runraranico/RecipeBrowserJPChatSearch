using System;
using System.Reflection;
using Terraria.ModLoader;

namespace RecipeBrowserJPChatSearch.Patches
{
	internal static class RecipeBrowserValidatePatches
	{
		private delegate void orig_Validate(object self);

		public static void Apply(Mod recipeBrowserMod)
		{
			DisableCharacterStripping(recipeBrowserMod, "RecipeBrowser.BestiaryUI", "ValidateNPCFilter");
			DisableCharacterStripping(recipeBrowserMod, "RecipeBrowser.ItemCatalogueUI", "ValidateItemFilter");
			DisableCharacterStripping(recipeBrowserMod, "RecipeBrowser.RecipeCatalogueUI", "ValidateItemFilter");
		}

		private static void DisableCharacterStripping(Mod recipeBrowserMod, string typeName, string methodName)
		{
			Type type = recipeBrowserMod.Code.GetType(typeName);
			if (type == null)
				return;

			MethodInfo method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
			FieldInfo updateNeeded = type.GetField("updateNeeded", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (method == null || updateNeeded == null)
				return;

			MonoModHooks.Add(method, (orig_Validate orig, object self) =>
			{
				updateNeeded.SetValue(self, true);
			});
		}
	}
}
