using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using ReLogic.Localization.IME;
using ReLogic.OS;
using Terraria;
using Terraria.GameContent;
using Terraria.UI;

namespace RecipeBrowserJPChatSearch
{
	/// <summary>
	/// Draws IME composition text the same way Terraria chat does.
	/// Search UIs often only render committed text and omit CompositionString.
	/// </summary>
	internal static class ImeCompositionDrawHelper
	{
		public static readonly Color CompositionColor = new Color(255, 0, 255);

		public static string GetCompositionString()
		{
			return Platform.Get<IImeService>().CompositionString ?? string.Empty;
		}

		public static bool HasComposition()
		{
			string composition = GetCompositionString();
			return !string.IsNullOrEmpty(composition);
		}

		public static void DrawInBar(
			SpriteBatch spriteBatch,
			CalculatedStyle dimensions,
			string committedText,
			int cursorIndex)
		{
			string composition = GetCompositionString();
			if (string.IsNullOrEmpty(composition))
				return;

			committedText ??= string.Empty;
			if (cursorIndex < 0)
				cursorIndex = 0;
			if (cursorIndex > committedText.Length)
				cursorIndex = committedText.Length;

			int innerHeight = (int)dimensions.Height - 8;
			if (innerHeight <= 0)
				return;

			DynamicSpriteFont font = FontAssets.MouseText.Value;
			Vector2 basePosition = new Vector2(dimensions.X + 4f, dimensions.Y + 4f);

			string textBeforeCursor = committedText.Substring(0, cursorIndex);
			float scale = innerHeight / font.MeasureString("Hg").Y;
			float cursorOffset = font.MeasureString(textBeforeCursor).X * scale;

			Vector2 compositionPosition = basePosition + new Vector2(cursorOffset, 0f);
			DynamicSpriteFontExtensionMethods.DrawString(spriteBatch, font, composition, compositionPosition, CompositionColor, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
		}
	}
}
