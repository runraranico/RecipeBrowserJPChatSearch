using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Xna.Framework.Input;
using ReLogic.Localization.IME;
using ReLogic.OS;
using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;

namespace RecipeBrowserJPChatSearch
{
	/// <summary>
	/// IME-aware text input used by Recipe Browser search boxes.
	/// Based on the same approach as SerousCommonLib.RawTextIME.
	/// </summary>
	internal class ImeTextInputHandler : ModSystem
	{
		private static bool _hasListener;
		private static readonly Queue<char> InputQueue = new Queue<char>();

		private static int _backSpaceCount;
		private static float _backSpaceRate;

		internal static bool IsCapturing { get; private set; }

		public override void Load()
		{
			if (Main.dedServ)
				return;

			Platform.Get<IImeService>().AddKeyListener(Listen);
			_hasListener = true;
		}

		public override void Unload()
		{
			if (!_hasListener)
				return;

			Platform.Get<IImeService>().RemoveKeyListener(Listen);
			_hasListener = false;
			InputQueue.Clear();
			IsCapturing = false;
		}

		private static void Listen(char key)
		{
			if (!IsCapturing || Main.gameMenu)
			{
				InputQueue.Clear();
				return;
			}

			if (Main.instance == null)
				return;

			Main.keyCount = 0;
			InputQueue.Enqueue(key);
			if (InputQueue.Count > 10)
				InputQueue.Dequeue();
		}

		public static void BeginCapturing()
		{
			IsCapturing = true;
		}

		public static void Handle(StringBuilder text, ref int cursor)
		{
			IsCapturing = true;

			try
			{
				PlayerInput.WritingText = true;
				Main.instance.HandleIME();

				if (!Main.hasFocus)
					return;

				Main.inputTextEnter = false;
				Main.inputTextEscape = false;
				Main.oldInputText = Main.inputText;
				Main.inputText = Keyboard.GetState();

				if (Utils.PressingControl(Main.inputText))
					HandleControlShortcuts(text, ref cursor);
				else if (Utils.PressingShift(Main.inputText))
					HandleShiftShortcuts(text, ref cursor);
				else
					HandleNormalInput(text, ref cursor);
			}
			finally
			{
				InputQueue.Clear();
			}
		}

		public static void StopCapturing()
		{
			IsCapturing = false;
			InputQueue.Clear();
			PlayerInput.WritingText = false;
		}

		private static void HandleControlShortcuts(StringBuilder text, ref int cursor)
		{
			if (KeyTyped(Keys.Z))
			{
				text.Length = 0;
				cursor = 0;
			}
			else if (KeyTyped(Keys.X))
			{
				Platform.Get<IClipboard>().Value = text.ToString();
				text.Length = 0;
				cursor = 0;
			}
			else if (KeyTyped(Keys.C) || KeyTyped(Keys.Insert))
			{
				Platform.Get<IClipboard>().Value = text.ToString();
			}
			else if (KeyTyped(Keys.V))
			{
				string clip = Platform.Get<IClipboard>().Value;
				text.Insert(cursor, clip);
				cursor += clip.Length;
			}
			else if (KeyTyped(Keys.Back))
			{
				DeletePreviousWord(text, ref cursor);
			}
		}

		private static void HandleShiftShortcuts(StringBuilder text, ref int cursor)
		{
			if (KeyTyped(Keys.Delete))
			{
				Platform.Get<IClipboard>().Value = text.ToString();
				text.Length = 0;
				cursor = 0;
			}
			else if (KeyTyped(Keys.Insert))
			{
				string clip = Platform.Get<IClipboard>().Value;
				text.Insert(cursor, clip);
				cursor += clip.Length;
			}
		}

		private static void HandleNormalInput(StringBuilder text, ref int cursor)
		{
			bool backspace = KeyTyped(Keys.Back);

			if (KeyHeld(Keys.Back))
			{
				_backSpaceRate -= 0.05f;
				if (_backSpaceRate < 0f)
					_backSpaceRate = 0f;

				if (_backSpaceCount <= 0)
				{
					_backSpaceCount = (int)Math.Round(_backSpaceRate);
					backspace = true;
				}

				_backSpaceCount--;
			}
			else
			{
				_backSpaceRate = 7f;
				_backSpaceCount = 15;
			}

			if (KeyTyped(Keys.Left))
			{
				if (cursor > 0)
					cursor--;
			}
			else if (KeyTyped(Keys.Right))
			{
				if (cursor < text.Length)
					cursor++;
			}
			else if (KeyTyped(Keys.Home))
			{
				cursor = 0;
			}
			else if (KeyTyped(Keys.End))
			{
				cursor = text.Length;
			}
			else if (backspace)
			{
				if (cursor > 0 && cursor <= text.Length)
				{
					cursor--;
					text.Remove(cursor, 1);
				}
			}
			else if (KeyTyped(Keys.Enter))
			{
				Main.inputTextEnter = true;
			}
			else if (KeyTyped(Keys.Escape))
			{
				Main.inputTextEscape = true;
			}
			else
			{
				while (InputQueue.TryDequeue(out char result))
				{
					if (result == '\r')
					{
						Main.inputTextEnter = true;
					}
					else if (result == '\u001b')
					{
						Main.inputTextEscape = true;
					}
					else if (result == '\t')
					{
						// IME often emits tab while composition is open; do not unfocus or switch fields.
					}
					else if (result >= ' ' && result != '\u007f')
					{
						text.Insert(cursor++, result);
					}
				}
			}
		}

		private static void DeletePreviousWord(StringBuilder text, ref int cursor)
		{
			if (cursor <= 0)
				return;

			ReadOnlySpan<char> span = text.ToString();
			int index = cursor - 1;

			while (index >= 0 && char.IsWhiteSpace(span[index]))
				index--;

			while (index >= 0 && !char.IsWhiteSpace(span[index]))
				index--;

			if (index < 0)
			{
				text.Remove(0, cursor);
				cursor = 0;
			}
			else
			{
				int start = index + 1;
				text.Remove(start, cursor - start);
				cursor = start;
			}
		}

		private static bool KeyHeld(Keys key)
		{
			return Main.inputText.IsKeyDown(key) && Main.oldInputText.IsKeyDown(key);
		}

		private static bool KeyTyped(Keys key)
		{
			return Main.inputText.IsKeyDown(key) && !Main.oldInputText.IsKeyDown(key);
		}
	}
}
