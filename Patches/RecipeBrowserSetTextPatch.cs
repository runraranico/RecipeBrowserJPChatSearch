using System;
using System.Reflection;
using Terraria.ModLoader;

namespace RecipeBrowserJPChatSearch.Patches
{
	internal static class RecipeBrowserSetTextPatch
	{
		private delegate void orig_SetText(object self, string text);

		private static FieldInfo _currentStringField;

		internal static bool IsProgrammaticUpdate { get; set; }

		public static void Apply(Mod recipeBrowserMod)
		{
			Type textBoxType = recipeBrowserMod.Code.GetType("RecipeBrowser.NewUITextBox");
			if (textBoxType == null)
				return;

			MethodInfo setText = textBoxType.GetMethod("SetText", BindingFlags.Instance | BindingFlags.Public);
			_currentStringField = textBoxType.GetField("currentString", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (setText == null || _currentStringField == null)
				return;

			MonoModHooks.Add(setText, (orig_SetText orig, object self, string text) =>
			{
				if (IsProgrammaticUpdate)
				{
					orig(self, text);
					return;
				}

				string current = _currentStringField.GetValue(self) as string ?? string.Empty;
				if (IsAutoStrip(current, text))
					return;

				orig(self, text);
			});
		}

		private static bool IsAutoStrip(string current, string text)
		{
			return current.Length > 0
				&& text.Length == current.Length - 1
				&& current.StartsWith(text, StringComparison.Ordinal);
		}
	}
}
