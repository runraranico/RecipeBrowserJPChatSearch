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

		private static Type _cachedStateType;
		private static PropertyInfo _hasFocusProp;
		private static PropertyInfo _isActiveProp;
		private static PropertyInfo _hideContentsProp;
		private static PropertyInfo _hasTextProp;
		private static PropertyInfo _inputTextProp;
		private static PropertyInfo _cursorLocationProp;
		private static bool _statePropsFailed;

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

			if (!EnsureStateProperties(state.GetType()))
				return;

			try
			{
				bool hasFocus = (bool)_hasFocusProp.GetValue(state);
				bool isActive = (bool)_isActiveProp.GetValue(state);
				bool hideContents = (bool)_hideContentsProp.GetValue(state);

				if (!hasFocus || !isActive || hideContents)
					return;

				bool hasText = (bool)_hasTextProp.GetValue(state);
				string committedText = hasText ? (string)_inputTextProp.GetValue(state) : string.Empty;
				int cursor = (int)_cursorLocationProp.GetValue(state);

				var dimensions = (CalculatedStyle)_getDimensionsMethod.Invoke(self, null);
				ImeCompositionDrawHelper.DrawInBar(spriteBatch, dimensions, committedText, cursor);
			}
			catch (Exception ex)
			{
				RbjDiag.Error("Serous DrawComposition failed", ex);
			}
		}

		private static bool EnsureStateProperties(Type stateType)
		{
			if (stateType == null || _statePropsFailed)
				return false;

			if (_cachedStateType == stateType
				&& _hasFocusProp != null
				&& _isActiveProp != null
				&& _hideContentsProp != null
				&& _hasTextProp != null
				&& _inputTextProp != null
				&& _cursorLocationProp != null)
				return true;

			const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
			_cachedStateType = stateType;
			_hasFocusProp = stateType.GetProperty("HasFocus", flags);
			_isActiveProp = stateType.GetProperty("IsActive", flags);
			_hideContentsProp = stateType.GetProperty("HideContents", flags);
			_hasTextProp = stateType.GetProperty("HasText", flags);
			_inputTextProp = stateType.GetProperty("InputText", flags);
			_cursorLocationProp = stateType.GetProperty("CursorLocation", flags);

			if (_hasFocusProp == null
				|| _isActiveProp == null
				|| _hideContentsProp == null
				|| _hasTextProp == null
				|| _inputTextProp == null
				|| _cursorLocationProp == null)
			{
				_statePropsFailed = true;
				RbjDiag.Warn("Serous TextInputState property cache incomplete");
				return false;
			}

			return true;
		}

		internal static void Unload()
		{
			_stateProperty = null;
			_getDimensionsMethod = null;
			_cachedStateType = null;
			_hasFocusProp = null;
			_isActiveProp = null;
			_hideContentsProp = null;
			_hasTextProp = null;
			_inputTextProp = null;
			_cursorLocationProp = null;
			_statePropsFailed = false;
		}
	}
}
