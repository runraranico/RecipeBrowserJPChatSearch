using System;
using System.Reflection;
using Microsoft.Xna.Framework.Graphics;
using Terraria.ModLoader;
using Terraria.UI;

namespace RecipeBrowserJPChatSearch.Patches
{
	/// <summary>
	/// Magic Storage search bars inherit SerousCommonLib.UI.TextInputBar.
	/// TextInputBar.DrawText only renders committed text from TextInputState.
	/// </summary>
	internal static class SerousCommonLibPatches
	{
		private delegate void orig_DrawText(object self, SpriteBatch spriteBatch);

		private static PropertyInfo _stateProperty;
		private static MethodInfo _getDimensionsMethod;

		public static void Apply(Mod serousMod)
		{
			Type textInputBarType = serousMod.Code.GetType("SerousCommonLib.UI.TextInputBar");
			if (textInputBarType == null)
				return;

			MethodInfo drawText = textInputBarType.GetMethod("DrawText", BindingFlags.Instance | BindingFlags.NonPublic);
			if (drawText == null)
				return;

			_stateProperty = textInputBarType.GetProperty("State", BindingFlags.Instance | BindingFlags.Public);
			_getDimensionsMethod = textInputBarType.GetMethod("GetDimensions", BindingFlags.Instance | BindingFlags.Public);
			if (_stateProperty == null || _getDimensionsMethod == null)
				return;

			MonoModHooks.Add(drawText, (orig_DrawText orig, object self, SpriteBatch spriteBatch) =>
			{
				orig(self, spriteBatch);
				DrawComposition(self, spriteBatch);
			});
		}

		private static void DrawComposition(object self, SpriteBatch spriteBatch)
		{
			if (!ImeCompositionDrawHelper.HasComposition())
				return;

			object state = _stateProperty.GetValue(self);
			if (state == null)
				return;

			Type stateType = state.GetType();
			bool hasFocus = (bool)stateType.GetProperty("HasFocus").GetValue(state);
			bool isActive = (bool)stateType.GetProperty("IsActive").GetValue(state);
			bool hideContents = (bool)stateType.GetProperty("HideContents").GetValue(state);

			if (!hasFocus || !isActive || hideContents)
				return;

			bool hasText = (bool)stateType.GetProperty("HasText").GetValue(state);
			string committedText = hasText ? (string)stateType.GetProperty("InputText").GetValue(state) : string.Empty;
			int cursor = (int)stateType.GetProperty("CursorLocation").GetValue(state);

			var dimensions = (CalculatedStyle)_getDimensionsMethod.Invoke(self, null);
			ImeCompositionDrawHelper.DrawInBar(spriteBatch, dimensions, committedText, cursor);
		}
	}
}
