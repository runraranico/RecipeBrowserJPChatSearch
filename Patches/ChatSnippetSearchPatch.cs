using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.ModLoader;
using Terraria.UI;

namespace RecipeBrowserJPChatSearch.Patches
{
	// Uses native chat hits and drawn item slots; never changes chat layout or input.
	internal static class ChatSnippetSearchPatch
	{
		private delegate void orig_DrawPlayerChat(Main self);
		private delegate void orig_OnHover(object self);
		private delegate void orig_DrawItemSlot(SpriteBatch batch, ref Item item, int context, Vector2 position, Color lightColor);
		private static FieldInfo _npcTypeField, _netIdField;
		private static bool _active;
		private static int _depth, _itemType, _npcType, _netId, _mouseX, _mouseY;
		private static ulong _frame;
		private static bool _npcTarget, _nativeTarget;
		private static float _itemDistance;
		private static List<Keys> _composeKeys;

		internal static bool Capturing => _active && _depth > 0;

		internal static bool IsCapturedComposeKeyHeld()
		{
			if (_active && Main.hasFocus && _composeKeys != null)
				foreach (Keys key in _composeKeys)
					if (Main.keyState.IsKeyDown(key))
						return true;
			_composeKeys = null;
			return false;
		}

		internal static bool IsSearchHotkeyJustPressed()
		{
			if (RecipeBrowserCursorSearchBridge.IsSearchHotkeyJustPressed())
				return true;
			if (!_active || !Main.drawingPlayerChat || !PlayerInput.WritingText
				|| !Main.hasFocus || Main.gameMenu || Main.blockInput)
				return false;
			// WritingText removes keyboard bindings from Triggers. Read the native snapshots
			// already updated by Main; leave typing, IME and all input flags untouched.
			try
			{
				var bindings = ModKeybinds.SearchHotkey?.GetAssignedKeys();
				if (bindings == null)
					return false;
				foreach (string binding in bindings)
					if (Enum.TryParse(binding, true, out Keys key) && key != Keys.None
						&& Main.keyState.IsKeyDown(key) && !Main.oldKeyState.IsKeyDown(key))
						return true;
			}
			catch { }
			return false;
		}

		private static void RememberComposeKeys()
		{
			if (!Main.drawingPlayerChat || !PlayerInput.WritingText)
				return;
			try
			{
				var bindings = ModKeybinds.SearchHotkey?.GetAssignedKeys();
				if (bindings == null)
					return;
				var keys = new List<Keys>();
				foreach (string binding in bindings)
					if (Enum.TryParse(binding, true, out Keys key) && key != Keys.None)
						keys.Add(key);
				_composeKeys = keys;
			}
			catch { _composeKeys = null; }
		}

		internal static void Apply(Mod recipeBrowserMod)
		{
			Unload();
			const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
			MethodInfo draw = typeof(Main).GetMethod("DrawPlayerChat", instance, null, Type.EmptyTypes, null);
			Type npcSnippet = recipeBrowserMod?.Code.GetType("RecipeBrowser.TagHandlers.NPCTagHandler+NPCSnippet");
			MethodInfo hover = npcSnippet?.GetMethod("OnHover", instance);
			_npcTypeField = npcSnippet?.GetField("npcType", instance);
			_netIdField = npcSnippet?.GetField("netID", instance);
			if (draw == null)
				return;
			try
			{
				if (hover != null && _npcTypeField?.FieldType == typeof(int) && _netIdField?.FieldType == typeof(int))
					MonoModHooks.Add(hover, (orig_OnHover orig, object self) =>
					{
						orig(self);
						if (Capturing)
							CaptureNpc(self);
					});
				MonoModHooks.Add(draw, (orig_DrawPlayerChat orig, Main self) =>
				{
					if (!IsSearchHotkeyJustPressed() || !BeginCapture())
					{
						orig(self);
						return;
					}
					bool completed = false;
					try { orig(self); completed = true; }
					finally { EndCapture(completed); }
					// Native Player Chat may draw after PostDrawInterface. Use this completed hit
					// in its own native context, including compose; history keeps its existing route.
					if (!ChatBrowseHelper.IsDisplayingOverlay)
						try { TryHandleSearch(); }
						catch { } // Search integration must not escape into native chat drawing.
				});
				_active = true;
			}
			catch
			{
				// Optional integration: leave native chat and existing search routes available.
				Unload();
			}

			if (!_active)
				return;
			try
			{
				MethodInfo slotDraw = typeof(ItemSlot).GetMethod("Draw", BindingFlags.Public | BindingFlags.Static,
					null, new[] { typeof(SpriteBatch), typeof(Item).MakeByRefType(), typeof(int), typeof(Vector2), typeof(Color) }, null);
				if (slotDraw != null)
					MonoModHooks.Add(slotDraw, (orig_DrawItemSlot orig, SpriteBatch batch, ref Item item, int context, Vector2 position, Color lightColor) =>
					{
						if (!Capturing || ChatBrowseHelper.IsDisplayingOverlay || context != 14)
						{
							orig(batch, ref item, context, position, lightColor);
							return;
						}
						float nativeScale = Main.inventoryScale;
						int itemType = item?.type ?? 0, stack = item?.stack ?? 0;
						orig(batch, ref item, context, position, lightColor);
						try { CaptureItemSlot(itemType, stack, position, nativeScale); }
						catch { } // Optional hit expansion must never interrupt native rendering.
					});
			}
			catch { } // Keep accepted native OnHover search if this optional hook is unavailable.
		}

		private static void CaptureItemSlot(int itemType, int stack, Vector2 position, float nativeScale)
		{
			if (_nativeTarget || itemType <= 0 || stack <= 0 || !(nativeScale > 0f) || !float.IsFinite(nativeScale))
				return;
			var texture = TextureAssets.InventoryBack.Value;
			Vector2 slotSize = new Vector2(texture.Width, texture.Height) * nativeScale;
			Vector2 center = position + slotSize * 0.5f;
			// ItemSlot's 32-unit icon limit; exclude the surrounding empty slot padding.
			Vector2 halfIcon = new Vector2(16f * nativeScale);
			Vector2 mouse = new Vector2(Main.mouseX, Main.mouseY);
			bool inside = mouse.X >= center.X - halfIcon.X && mouse.X <= center.X + halfIcon.X
				&& mouse.Y >= center.Y - halfIcon.Y && mouse.Y <= center.Y + halfIcon.Y;
			if (stack > 1)
			{
				// ItemSlot's native count origin, font and shadow spread, in this draw's units.
				Vector2 countStart = position + new Vector2(10f, 26f) * nativeScale;
				Vector2 countEnd = countStart
					+ FontAssets.ItemStack.Value.MeasureString(stack.ToString()) * nativeScale + new Vector2(nativeScale);
				countStart -= new Vector2(nativeScale);
				inside |= mouse.X >= countStart.X && mouse.X <= countEnd.X
					&& mouse.Y >= countStart.Y && mouse.Y <= countEnd.Y;
			}
			if (!inside)
				return;
			float distance = (mouse - center).LengthSquared();
			if (_itemType > 0 && distance >= _itemDistance)
				return;
			_itemType = itemType;
			_itemDistance = distance;
			RememberPosition();
		}

		internal static bool BeginCapture()
		{
			if (!_active || Main.gameMenu || Main.blockInput || !IsSearchHotkeyJustPressed())
				return false;
			if (_depth++ == 0)
				ClearTarget();
			return true;
		}

		internal static void EndCapture(bool completed = true)
		{
			if (_depth > 0)
				_depth--;
			if (!completed)
				ClearTarget();
		}

		internal static void CaptureItem(int itemType)
		{
			if (!Capturing || itemType <= 0)
				return;
			_itemType = itemType;
			_npcTarget = false;
			_nativeTarget = true;
			RememberPosition();
		}

		private static void CaptureNpc(object snippet)
		{
			try
			{
				_npcType = (int)_npcTypeField.GetValue(snippet);
				_netId = (int)_netIdField.GetValue(snippet);
				_itemType = 0;
				_npcTarget = true;
				_nativeTarget = true;
				RememberPosition();
			}
			catch { ClearTarget(); }
		}

		private static void RememberPosition()
		{
			_frame = Main.GameUpdateCount;
			_mouseX = Main.mouseX;
			_mouseY = Main.mouseY;
		}

		internal static bool TryHandleSearch()
		{
			if (!_active || (!_npcTarget && _itemType <= 0) || _depth != 0
				|| _frame != Main.GameUpdateCount || _mouseX != Main.mouseX || _mouseY != Main.mouseY
				|| Main.gameMenu || Main.blockInput || !IsSearchHotkeyJustPressed()
				|| RecipeBrowserCursorSearchBridge.IsSearchActionAlreadyConsumed()
				|| RecipeBrowserInputHelper.ActiveRecipeBrowserTextBox != null
				|| RecipeBrowserCursorSearchBridge.IsModifierBlockingCursorSearch(ignoreAlt: ChatBrowseHelper.BrowseMode)
				|| (PlayerInput.WritingText && !Main.drawingPlayerChat && !ChatBrowseHelper.BrowseMode))
				return false;

			RememberComposeKeys();
			if (_npcTarget)
			{
				if (_npcType > 0)
					RecipeBrowserCursorSearchBridge.PerformNpcBestiarySearch(_npcType, _netId,
						Lang.GetNPCNameValue(_netId == 0 ? _npcType : _netId));
				// Own this hit even if the optional RB endpoint is unavailable; no stale-item fallback.
				RecipeBrowserCursorSearchBridge.TryConsumeSearchActionThisFrame("chat-npc");
				if (ChatBrowseHelper.BrowseMode)
					ChatBrowseHelper.BeginLingerAfterCursorSearch();
				return true;
			}

			if (!Main.drawingPlayerChat && ChatBrowseHelper.BrowseMode)
				return false; // Existing past-log item route retains its original behavior.
			RecipeBrowserCursorSearchBridge.PerformHoveredItemQuery(_itemType, allowToggleClose: false, stickyTooltip: false);
			RecipeBrowserCursorSearchBridge.TryConsumeSearchActionThisFrame("chat-compose-item");
			return true;
		}

		private static void ClearTarget()
		{
			_itemType = _npcType = _netId = 0;
			_npcTarget = _nativeTarget = false;
			_itemDistance = float.MaxValue;
		}

		internal static void Unload()
		{
			_active = false;
			_depth = 0;
			_composeKeys = null;
			ClearTarget();
			_npcTypeField = _netIdField = null;
		}
	}
}
