using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria.ModLoader;
using Terraria.UI.Chat;

namespace RecipeBrowserJPChatSearch.Patches
{
	/// <summary>
	/// Option 2 was a shared-list ParseMessage cache while composing.
	/// Returning the same <see cref="List{TextSnippet}"/> instance is unsafe: chat / UI
	/// may mutate snippets during draw, corrupting later frames (missing icons, worse).
	/// Hooks stay registered but always pass through to vanilla until a safe cache is redesigned.
	/// </summary>
	internal static class ChatParseMessageCache
	{
		private delegate List<TextSnippet> orig_ParseMessage(string text, Color baseColor);

		private static bool _passthroughLogged;
		private static int _callsWhileComposing;

		public static void Apply()
		{
			MethodInfo parse = typeof(ChatManager).GetMethod(
				"ParseMessage",
				BindingFlags.Static | BindingFlags.Public,
				null,
				new[] { typeof(string), typeof(Color) },
				null);

			if (parse == null)
			{
				RbjDiag.Warn("ChatParseMessageCache: ParseMessage(string, Color) not found");
				return;
			}

			MonoModHooks.Add(parse, (orig_ParseMessage orig, string text, Color baseColor) =>
			{
				// Disabled shared cache — always fresh parse (safe).
				if (ChatItemTagLightPolicy.IsComposingChat)
					_callsWhileComposing++;

				if (!_passthroughLogged)
				{
					_passthroughLogged = true;
					RbjDiag.Info("ChatParseMessageCache: shared-list cache DISABLED (passthrough only)");
				}

				return orig(text, baseColor);
			});

			RbjDiag.Info("ChatParseMessageCache hooked ParseMessage (passthrough; no shared cache)");
		}

		internal static void Invalidate()
		{
			// No cached entries while disabled.
		}

		internal static string SnapshotStats()
			=> $"parseCache=disabled composeCalls={_callsWhileComposing}";
	}
}
