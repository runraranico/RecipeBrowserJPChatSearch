using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using ReLogic.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.ModLoader;
using Terraria.UI;

namespace RecipeBrowserJPChatSearch.Patches
{
	internal static class RecipeBrowserPatches
	{
		private delegate void orig_DrawSelf(object self, SpriteBatch spriteBatch);
		private delegate string orig_GetInputText(string oldString, bool allowMultiLine);

		private static orig_GetInputText _origGetInputText;

		private static Type _textBoxType;
		private static FieldInfo _focusedField;
		private static FieldInfo _currentStringField;
		private static FieldInfo _maxLengthField;
		private static MethodInfo _setTextMethod;
		private static MethodInfo _unfocusMethod;
		private static MethodInfo _getDimensionsMethod;

		private static object _activeBox;
		private static bool _suppressVanillaInput;

		public static void Apply(Mod recipeBrowserMod)
		{
			_textBoxType = recipeBrowserMod.Code.GetType("RecipeBrowser.NewUITextBox");
			if (_textBoxType == null)
				return;

			if (!CacheReflection())
				return;

			MethodInfo drawSelf = _textBoxType.GetMethod("DrawSelf", BindingFlags.Instance | BindingFlags.NonPublic);
			if (drawSelf == null)
				return;

			MonoModHooks.Add(typeof(Main).GetMethod(nameof(Main.GetInputText), BindingFlags.Static | BindingFlags.Public), (orig_GetInputText orig, string oldString, bool allowMultiLine) =>
			{
				_origGetInputText ??= orig;

				if (_suppressVanillaInput && _activeBox != null)
					return oldString;

				return orig(oldString, allowMultiLine);
			});

			MonoModHooks.Add(drawSelf, (orig_DrawSelf orig, object self, SpriteBatch spriteBatch) =>
			{
				bool focused = IsFocused(self);

				if (focused)
				{
					RecipeBrowserInputHelper.SetActiveRecipeBrowserTextBox(self);
					_activeBox = self;
					ProcessInput(self);
					TryUnfocusFromImeKeys(self, ref focused);
				}

				_suppressVanillaInput = focused;
				KeyboardState? savedOldInputText = null;
				if (focused && ShouldSuppressTabFocusSwitch() && Main.inputText.IsKeyDown(Keys.Tab))
					savedOldInputText = Main.oldInputText;
				try
				{
					if (savedOldInputText.HasValue)
						Main.oldInputText = WithKeyHeld(Main.inputText, Keys.Tab);

					orig(self, spriteBatch);
				}
				finally
				{
					if (savedOldInputText.HasValue)
						Main.oldInputText = savedOldInputText.Value;

					_suppressVanillaInput = false;
				}

				focused = IsFocused(self);
				if (!focused && _activeBox == self)
				{
					RecipeBrowserInputHelper.ReleaseInputLocks();
					_activeBox = null;
				}
				else if (focused)
				{
					string composition = ImeCompositionDrawHelper.GetCompositionString();
					CompositionTracker.Remember(composition);
					DrawComposition(self, spriteBatch, composition);

					if (ImeCompositionDrawHelper.HasComposition())
						Main.oldInputText = Main.inputText;
				}
			});
		}

		private static bool CacheReflection()
		{
			const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

			_focusedField = _textBoxType.GetField("focused", instance);
			_currentStringField = _textBoxType.GetField("currentString", instance);
			_maxLengthField = _textBoxType.GetField("_maxLength", instance);
			_setTextMethod = _textBoxType.GetMethod("SetText", BindingFlags.Instance | BindingFlags.Public);
			_unfocusMethod = _textBoxType.GetMethod("Unfocus", BindingFlags.Instance | BindingFlags.Public);
			_getDimensionsMethod = typeof(UIElement).GetMethod("GetDimensions", BindingFlags.Instance | BindingFlags.Public);

			return _focusedField != null
				&& _currentStringField != null
				&& _maxLengthField != null
				&& _setTextMethod != null
				&& _unfocusMethod != null
				&& _getDimensionsMethod != null;
		}

		private static bool IsFocused(object self)
		{
			return _focusedField != null && _focusedField.GetValue(self) is true;
		}

		private static bool ShouldSuppressTabFocusSwitch()
		{
			return ImeCompositionDrawHelper.HasComposition()
				|| !string.IsNullOrEmpty(CompositionTracker.LastNonEmptyComposition);
		}

		private static KeyboardState WithKeyHeld(KeyboardState state, Keys key)
		{
			if (state.IsKeyDown(key))
				return state;

			var keys = new List<Keys>();
			foreach (Keys pressed in Enum.GetValues<Keys>())
			{
				if (state.IsKeyDown(pressed))
					keys.Add(pressed);
			}

			keys.Add(key);
			return new KeyboardState(keys.ToArray());
		}

		private static void TryUnfocusFromImeKeys(object self, ref bool focused)
		{
			if (ShouldSuppressTabFocusSwitch() && Main.inputTextEscape)
			{
				Main.inputTextEscape = false;
				return;
			}

			if (!Main.inputTextEnter && !Main.inputTextEscape)
				return;

			if (Main.inputTextEnter)
			{
				RecipeBrowserInputBlocker.SuppressChatFrames = 8;
				Main.drawingPlayerChat = false;
			}

			_unfocusMethod.Invoke(self, null);
			RecipeBrowserInputHelper.ReleaseInputLocks();
			focused = false;
		}

		private static void ProcessInput(object self)
		{
			string current = _currentStringField.GetValue(self) as string ?? string.Empty;
			int maxLength = (int)_maxLengthField.GetValue(self);
			string compositionBeforeIme = ImeCompositionDrawHelper.GetCompositionString();
			if (string.IsNullOrEmpty(compositionBeforeIme))
				compositionBeforeIme = CompositionTracker.LastNonEmptyComposition;

			CompositionTracker.Remember(compositionBeforeIme);

			Main.blockInput = true;
			PlayerInput.WritingText = true;
			ImeTextInputHandler.BeginCapturing();

			Main.inputTextEnter = false;
			Main.inputTextEscape = false;

			var buffer = new StringBuilder(current);
			int cursor = buffer.Length;
			ImeTextInputHandler.Handle(buffer, ref cursor);

			string updated = buffer.ToString();

			string compositionAfterIme = ImeCompositionDrawHelper.GetCompositionString();
			bool compositionCommitted = !string.IsNullOrEmpty(compositionBeforeIme) && string.IsNullOrEmpty(compositionAfterIme);
			if (compositionCommitted)
			{
				updated = CommitComposition(current, updated, compositionBeforeIme);

				if (string.IsNullOrEmpty(updated) || updated == current)
					updated = CompositionTracker.ConsumeForCommit(current);

				updated = Truncate(updated, maxLength);
				ApplyText(self, updated);
				CompositionTracker.Clear();
				return;
			}

			updated = Truncate(updated, maxLength);

			if (updated != current)
				ApplyText(self, updated);
		}

		private static void ApplyText(object self, string text)
		{
			RecipeBrowserSetTextPatch.IsProgrammaticUpdate = true;
			try
			{
				_setTextMethod.Invoke(self, new object[] { text });
			}
			finally
			{
				RecipeBrowserSetTextPatch.IsProgrammaticUpdate = false;
			}
		}

		private static string CommitComposition(string current, string updated, string composition)
		{
			if (string.IsNullOrEmpty(composition))
				return updated;

			if (updated.Contains(composition, StringComparison.Ordinal))
				return updated;

			if (updated.Length > current.Length)
				return updated;

			return current + composition;
		}

		private static void DrawComposition(object self, SpriteBatch spriteBatch, string composition)
		{
			if (string.IsNullOrEmpty(composition))
				return;

			string committedText = _currentStringField.GetValue(self) as string ?? string.Empty;
			var dimensions = (CalculatedStyle)_getDimensionsMethod.Invoke(self, null);
			Vector2 basePosition = dimensions.Position() + new Vector2(4f, 2f);
			DynamicSpriteFont font = FontAssets.MouseText.Value;
			float committedWidth = font.MeasureString(committedText).X;

			DynamicSpriteFontExtensionMethods.DrawString(
				spriteBatch,
				font,
				composition,
				basePosition + new Vector2(committedWidth, 0f),
				ImeCompositionDrawHelper.CompositionColor);
		}

		private static string Truncate(string text, int maxLength)
		{
			if (text.Length <= maxLength)
				return text;

			return text.Substring(0, maxLength);
		}

		internal static void ReleaseStaleInputCapture()
		{
			RecipeBrowserInputHelper.ReleaseIfActiveBoxLostFocus(IsTextBoxFocused);
		}

		internal static bool IsTextBoxFocused(object self)
		{
			return IsFocused(self);
		}
	}
}
