using System;
using System.Reflection;
using Terraria;
using Terraria.GameContent.UI.Chat;
using Terraria.ModLoader;

namespace RecipeBrowserJPChatSearch.Patches
{
	/// <summary>
	/// Vanilla chat item tags: OnHover remembers type for cursor search.
	/// UniqueDraw is not hooked (vanilla ItemSlot frames).
	/// </summary>
	internal static class VanillaItemTagHandlerPatch
	{
		private delegate void orig_OnHover(object self);

		private static FieldInfo _itemField;

		internal static bool OnHoverHooked { get; private set; }

		/// <summary>Always false — UniqueDraw is intentionally not hooked.</summary>
		internal static bool UniqueDrawHooked => false;

		public static void Apply()
		{
			OnHoverHooked = false;
			Type snippetType = FindItemSnippetType();
			if (snippetType == null)
			{
				RbjDiag.Warn("VanillaItemTagHandlerPatch: ItemSnippet type not found");
				return;
			}

			const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
			_itemField = snippetType.GetField("_item", instance)
				?? snippetType.GetField("item", instance)
				?? snippetType.GetField("Item", instance);

			MethodInfo onHover = snippetType.GetMethod("OnHover", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (_itemField == null)
				RbjDiag.Warn($"VanillaItemTagHandlerPatch: item field missing on {snippetType.FullName}");

			if (onHover != null && _itemField != null)
			{
				MonoModHooks.Add(onHover, (orig_OnHover orig, object self) =>
				{
					if (_itemField.GetValue(self) is Item item && !item.IsAir)
						RecipeBrowserCursorSearchBridge.RememberHoveredItem(item.type);

					if (RbjDiag.Enabled)
						ChatComposePerf.NoteVanillaOnHover(skippedHeavy: false);

					orig(self);
				});
				OnHoverHooked = true;
			}

			RbjDiag.Info(
				$"VanillaItemTagHandlerPatch applied type={snippetType.FullName} onHover={OnHoverHooked} UniqueDraw=not-hooked");
		}

		private static Type FindItemSnippetType()
		{
			Type handler = typeof(ItemTagHandler);
			Type[] nestedTypes = handler.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic);

			foreach (Type nested in nestedTypes)
			{
				if (nested.Name == "ItemSnippet" && typeof(Terraria.UI.Chat.TextSnippet).IsAssignableFrom(nested))
					return nested;
			}

			foreach (Type nested in nestedTypes)
			{
				if (!typeof(Terraria.UI.Chat.TextSnippet).IsAssignableFrom(nested))
					continue;

				if (nested.GetMethod("UniqueDraw", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) == null)
					continue;

				if (nested.GetField("_item", BindingFlags.Instance | BindingFlags.NonPublic) != null
					|| nested.GetField("item", BindingFlags.Instance | BindingFlags.NonPublic) != null)
					return nested;
			}

			return null;
		}
	}
}
