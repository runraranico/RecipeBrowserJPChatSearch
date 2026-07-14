using System;
using System.Reflection;
using Terraria;
using Terraria.ModLoader;

namespace RecipeBrowserJPChatSearch.Patches
{
	/// <summary>
	/// Recipe Browser [itemhover] tags: OnHover remembers type for cursor search.
	/// UniqueDraw is not hooked (RB framed draw stays as upstream).
	/// </summary>
	internal static class ItemHoverFixTagHandlerPatch
	{
		private delegate void orig_OnHover(object self);

		private static FieldInfo _itemField;

		internal static bool OnHoverHooked { get; private set; }

		/// <summary>Always false — UniqueDraw is intentionally not hooked.</summary>
		internal static bool UniqueDrawHooked => false;

		public static void Apply(Mod recipeBrowserMod)
		{
			OnHoverHooked = false;
			Type snippetType = recipeBrowserMod.Code.GetType("RecipeBrowser.TagHandlers.ItemHoverFixTagHandler+ItemHoverFixSnippet");
			if (snippetType == null)
			{
				RbjDiag.Warn("ItemHoverFixTagHandlerPatch: ItemHoverFixSnippet not found");
				return;
			}

			const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
			MethodInfo onHover = snippetType.GetMethod("OnHover", BindingFlags.Instance | BindingFlags.Public);
			_itemField = snippetType.GetField("_item", instance);
			if (onHover == null || _itemField == null)
			{
				RbjDiag.Warn("ItemHoverFixTagHandlerPatch: OnHover/_item missing");
				return;
			}

			MonoModHooks.Add(onHover, (orig_OnHover orig, object self) =>
			{
				if (_itemField.GetValue(self) is Item item && !item.IsAir)
					RecipeBrowserCursorSearchBridge.RememberHoveredItem(item.type);

				if (RbjDiag.Enabled)
					ChatComposePerf.NoteRbOnHover(skippedHeavy: false);

				orig(self);

				if (ChatItemTagLightPolicy.IsComposingChat)
					return;

				if (_itemField.GetValue(self) is not Item again || again.IsAir)
					return;

				if (Main.HoverItem.IsAir)
				{
					Main.HoverItem = again.Clone();
					Main.HoverItem.SetNameOverride(again.Name);
				}
			});

			OnHoverHooked = true;
			RbjDiag.Info("ItemHoverFixTagHandlerPatch applied (OnHover only; UniqueDraw not hooked)");
		}
	}
}
