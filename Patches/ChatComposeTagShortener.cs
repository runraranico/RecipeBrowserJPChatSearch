using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent.UI.Chat;
using Terraria.ModLoader;
using Terraria.UI.Chat;

namespace RecipeBrowserJPChatSearch.Patches
{
	/// <summary>
	/// Option 1: while composing chat, keep item tags short ([i/sN:type]) instead of
	/// full [i/d…] ItemIO blobs — huge win for ParseMessage + draw.
	/// </summary>
	internal static class ChatComposeTagShortener
	{
		private delegate string orig_GenerateTag(Item item);

		private static string _lastChatText;
		private static FieldInfo _itemSnippetItemField;
		private static Type _itemSnippetType;

		public static void Apply()
		{
			CacheSnippetReflection();

			MethodInfo generateTag = typeof(ItemTagHandler).GetMethod(
				"GenerateTag",
				BindingFlags.Static | BindingFlags.Public,
				null,
				new[] { typeof(Item) },
				null);

			if (generateTag != null)
			{
				MonoModHooks.Add(generateTag, (orig_GenerateTag orig, Item item) =>
				{
					if (ChatItemTagLightPolicy.IsComposingChat
						&& item != null
						&& !item.IsAir)
						return BuildShortITag(item);

					return orig(item);
				});
				RbjDiag.Info("ChatComposeTagShortener hooked ItemTagHandler.GenerateTag(Item)");
			}
			else
			{
				RbjDiag.Warn("ChatComposeTagShortener: GenerateTag(Item) not found — buffer rewrite only");
			}
		}

		/// <summary>Call each update while chat may be open — rewrite any long /d tags in the compose buffer.</summary>
		internal static void TickComposeBuffer()
		{
			if (!ChatItemTagLightPolicy.IsComposingChat)
			{
				_lastChatText = null;
				return;
			}

			string text = Main.chatText;
			if (string.IsNullOrEmpty(text) || text.IndexOf('[') < 0)
			{
				_lastChatText = text;
				return;
			}

			// Skip if unchanged since last successful shorten.
			if (text == _lastChatText)
				return;

			if (text.IndexOf("/d", StringComparison.OrdinalIgnoreCase) < 0
				&& text.IndexOf(",d", StringComparison.OrdinalIgnoreCase) < 0)
			{
				_lastChatText = text;
				return;
			}

			try
			{
				string shortened = ShortenItemTagsInText(text);
				if (!string.Equals(text, shortened, StringComparison.Ordinal))
				{
					Main.chatText = shortened;
					_lastChatText = shortened;
					RbjDiag.Info($"ChatComposeTagShortener buffer {text.Length}→{shortened.Length}");
					RbjRenderHealth.Mark($"shorten {text.Length}→{shortened.Length}");
					ChatParseMessageCache.Invalidate();
				}
				else
				{
					_lastChatText = text;
				}
			}
			catch (Exception ex)
			{
				RbjDiag.Error("ChatComposeTagShortener TickComposeBuffer failed", ex);
				_lastChatText = text;
			}
		}

		internal static string BuildShortITag(Item item)
		{
			int id = item.netID != 0 ? item.netID : item.type;
			int prefix = item.prefix;
			int stack = Math.Max(1, item.stack);

			if (prefix != 0 && stack > 1)
				return $"[i/p{prefix},s{stack}:{id}]";
			if (prefix != 0)
				return $"[i/p{prefix}:{id}]";
			if (stack > 1)
				return $"[i/s{stack}:{id}]";
			return $"[i:{id}]";
		}

		internal static string ShortenItemTagsInText(string text)
		{
			var sb = new StringBuilder(Math.Min(text.Length, text.Length / 2 + 64));
			int i = 0;
			while (i < text.Length)
			{
				if (text[i] != '[')
				{
					sb.Append(text[i]);
					i++;
					continue;
				}

				int close = text.IndexOf(']', i + 1);
				if (close < 0)
				{
					sb.Append(text, i, text.Length - i);
					break;
				}

				string tag = text.Substring(i, close - i + 1);
				sb.Append(ShortenOneTag(tag));
				i = close + 1;
			}

			return sb.ToString();
		}

		private static string ShortenOneTag(string tag)
		{
			if (tag.Length < 4 || tag[0] != '[' || tag[^1] != ']')
				return tag;

			int colon = tag.IndexOf(':');
			if (colon < 0)
				return tag;

			string head = tag.Substring(1, colon - 1);
			int slash = head.IndexOf('/');
			string name = slash < 0 ? head : head.Substring(0, slash);
			string opts = slash < 0 ? null : head.Substring(slash + 1);

			bool isItem = name.Equals("i", StringComparison.OrdinalIgnoreCase)
				|| name.Equals("item", StringComparison.OrdinalIgnoreCase)
				|| name.Equals("itemhover", StringComparison.OrdinalIgnoreCase);
			if (!isItem)
				return tag;

			// Already short (no serialized blob).
			if (string.IsNullOrEmpty(opts)
				|| (opts.IndexOf('d') < 0 && opts.IndexOf('D') < 0))
				return tag;

			if (!TryGetItemFromTag(tag, out Item item) || item == null || item.IsAir)
				return tag;

			if (name.Equals("itemhover", StringComparison.OrdinalIgnoreCase))
				return BuildShortItemhoverTag(item);

			return BuildShortITag(item);
		}

		private static string BuildShortItemhoverTag(Item item)
		{
			int id = item.netID != 0 ? item.netID : item.type;
			int prefix = item.prefix;
			int stack = Math.Max(1, item.stack);

			if (prefix != 0 && stack > 1)
				return $"[itemhover/p{prefix},s{stack}:{id}]";
			if (prefix != 0)
				return $"[itemhover/p{prefix}:{id}]";
			if (stack > 1)
				return $"[itemhover/s{stack}:{id}]";
			return $"[itemhover:{id}]";
		}

		private static bool TryGetItemFromTag(string tag, out Item item)
		{
			item = null;
			List<TextSnippet> snippets = ChatManager.ParseMessage(tag, Color.White);
			if (snippets == null)
				return false;

			foreach (TextSnippet snip in snippets)
			{
				if (snip == null)
					continue;

				if (_itemSnippetType != null
					&& _itemSnippetItemField != null
					&& _itemSnippetType.IsInstanceOfType(snip))
				{
					item = _itemSnippetItemField.GetValue(snip) as Item;
					if (item != null && !item.IsAir)
						return true;
				}

				FieldInfo field = snip.GetType().GetField(
					"_item",
					BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
				if (field == null)
					continue;

				item = field.GetValue(snip) as Item;
				if (item != null && !item.IsAir)
					return true;
			}

			return false;
		}

		private static void CacheSnippetReflection()
		{
			try
			{
				Type handler = typeof(ItemTagHandler);
				foreach (Type nested in handler.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
				{
					if (nested.Name != "ItemSnippet")
						continue;

					_itemSnippetType = nested;
					_itemSnippetItemField = nested.GetField(
						"_item",
						BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
					break;
				}
			}
			catch
			{
				// Buffer rewrite may still work via generic _item field walk.
			}
		}
	}
}
