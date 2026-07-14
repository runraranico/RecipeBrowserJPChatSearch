using System;
using System.Reflection;
using Terraria;
using Terraria.ModLoader;
using Terraria.UI;

namespace RecipeBrowserJPChatSearch.Patches
{
	/// <summary>
	/// Tracks ItemSlot.MouseHover over inventory, equipment, NPC shop, world chests,
	/// and personal banks (Piggy / Safe / Defender's Forge / Void Vault)
	/// for search-hotkey → Recipe Browser query.
	/// </summary>
	internal static class InventoryHoverTrackPatch
	{
		private delegate void orig_MouseHover(Item[] inv, int context, int slot);

		private static bool _hoveringTrackedSlot;
		private static int _hoveredItemType;
		private static int _hoveredContext;
		private static int _hoveredSlot;
		private static string _hoveredSource;

		// Keep last hover for a few frames — MouseHover can miss a click frame when
		// overlapping UI draws reorder, which made inv→RB look intermittent.
		private const int StickyFrames = 4;
		private static int _stickyTtl;
		private static int _stickyType;
		private static int _stickyContext;
		private static int _stickySlot;
		private static string _stickySource;

		internal static bool HoveringTrackedSlot
			=> _hoveringTrackedSlot || (_stickyTtl > 0 && _stickyType > 0);

		internal static int HoveredItemType
			=> _hoveringTrackedSlot ? _hoveredItemType : (_stickyTtl > 0 ? _stickyType : 0);

		internal static int HoveredContext
			=> _hoveringTrackedSlot ? _hoveredContext : (_stickyTtl > 0 ? _stickyContext : -1);

		internal static int HoveredSlot
			=> _hoveringTrackedSlot ? _hoveredSlot : (_stickyTtl > 0 ? _stickySlot : -1);

		/// <summary>inventory | equip | equipDye | miscEquip | miscDye | shop | chest | piggy | safe | forge | void | ""</summary>
		internal static string HoveredSource
		{
			get
			{
				if (_hoveringTrackedSlot)
					return _hoveredSource ?? string.Empty;
				if (_stickyTtl > 0)
					return _stickySource ?? string.Empty;
				return string.Empty;
			}
		}

		internal static bool UsingStickyTrack
			=> !_hoveringTrackedSlot && _stickyTtl > 0 && _stickyType > 0;

		public static void Apply()
		{
			MethodInfo mouseHover = typeof(ItemSlot).GetMethod(
				"MouseHover",
				BindingFlags.Static | BindingFlags.Public,
				null,
				new[] { typeof(Item[]), typeof(int), typeof(int) },
				null);
			if (mouseHover == null)
			{
				RbjDiag.Warn("InventoryHoverTrackPatch: ItemSlot.MouseHover missing");
				return;
			}

			MonoModHooks.Add(mouseHover, (orig_MouseHover orig, Item[] inv, int context, int slot) =>
			{
				orig(inv, context, slot);

				if (!IsTrackedContext(context) || inv == null || slot < 0 || slot >= inv.Length)
					return;

				Item item = inv[slot];
				if (item == null || item.IsAir)
					return;

				if (!TryClassify(inv, context, out string source))
					return;

				_hoveringTrackedSlot = true;
				_hoveredItemType = item.type;
				_hoveredContext = context;
				_hoveredSlot = slot;
				_hoveredSource = source;

				_stickyTtl = StickyFrames;
				_stickyType = item.type;
				_stickyContext = context;
				_stickySlot = slot;
				_stickySource = source;
			});

			RbjDiag.Info("InventoryHoverTrackPatch hooked ItemSlot.MouseHover (inv/equip/shop/chest/bank)");
		}

		internal static void ClearFrame()
		{
			_hoveringTrackedSlot = false;
			_hoveredItemType = 0;
			_hoveredContext = -1;
			_hoveredSlot = -1;
			_hoveredSource = string.Empty;

			if (_stickyTtl > 0)
				_stickyTtl--;
			else
			{
				_stickyType = 0;
				_stickyContext = -1;
				_stickySlot = -1;
				_stickySource = string.Empty;
			}
		}

		private static bool IsTrackedContext(int context)
			=> context is ItemSlot.Context.InventoryItem
				or ItemSlot.Context.InventoryCoin
				or ItemSlot.Context.InventoryAmmo
				or ItemSlot.Context.ShopItem
				or ItemSlot.Context.ChestItem
				or ItemSlot.Context.BankItem
				or ItemSlot.Context.VoidItem
				or ItemSlot.Context.EquipArmor
				or ItemSlot.Context.EquipArmorVanity
				or ItemSlot.Context.EquipAccessory
				or ItemSlot.Context.EquipAccessoryVanity
				or ItemSlot.Context.EquipDye
				or ItemSlot.Context.EquipGrapple
				or ItemSlot.Context.EquipMount
				or ItemSlot.Context.EquipMinecart
				or ItemSlot.Context.EquipPet
				or ItemSlot.Context.EquipLight
				or ItemSlot.Context.EquipMiscDye
				or ItemSlot.Context.ModdedAccessorySlot
				or ItemSlot.Context.ModdedVanityAccessorySlot
				or ItemSlot.Context.ModdedDyeSlot;

		private static bool TryClassify(Item[] inv, int context, out string source)
		{
			source = string.Empty;
			Player player = Main.LocalPlayer;
			if (player == null)
				return false;

			if (context is ItemSlot.Context.InventoryItem
				or ItemSlot.Context.InventoryCoin
				or ItemSlot.Context.InventoryAmmo)
			{
				if (!ReferenceEquals(inv, player.inventory))
					return false;
				source = "inventory";
				return true;
			}

			if (IsEquipmentContext(context))
			{
				if (ReferenceEquals(inv, player.armor))
				{
					source = "equip";
					return true;
				}

				if (ReferenceEquals(inv, player.dye))
				{
					source = "equipDye";
					return true;
				}

				if (ReferenceEquals(inv, player.miscEquips))
				{
					source = "miscEquip";
					return true;
				}

				if (ReferenceEquals(inv, player.miscDyes))
				{
					source = "miscDye";
					return true;
				}

				// Extra accessory slots / other mod equipment arrays — item is still in inv[slot].
				if (context is ItemSlot.Context.ModdedAccessorySlot
					or ItemSlot.Context.ModdedVanityAccessorySlot
					or ItemSlot.Context.ModdedDyeSlot)
				{
					source = "modEquip";
					return true;
				}

				return false;
			}

			if (context == ItemSlot.Context.ShopItem)
			{
				if (Main.npcShop <= 0)
					return false;
				source = "shop";
				return true;
			}

			if (context == ItemSlot.Context.ChestItem)
			{
				int chestIndex = player.chest;
				if (chestIndex < 0 || chestIndex >= Main.chest.Length)
					return false;
				Chest chest = Main.chest[chestIndex];
				if (chest?.item == null || !ReferenceEquals(inv, chest.item))
					return false;
				source = "chest";
				return true;
			}

			// Piggy Bank / Safe / Defender's Forge share BankItem;
			// Void Vault uses VoidItem (or still BankItem on some paths) + bank4.
			if (context is ItemSlot.Context.BankItem or ItemSlot.Context.VoidItem)
			{
				if (ReferenceEquals(inv, player.bank))
				{
					source = "piggy";
					return true;
				}

				if (ReferenceEquals(inv, player.bank2))
				{
					source = "safe";
					return true;
				}

				if (ReferenceEquals(inv, player.bank3))
				{
					source = "forge";
					return true;
				}

				if (ReferenceEquals(inv, player.bank4))
				{
					source = "void";
					return true;
				}
			}

			return false;
		}

		private static bool IsEquipmentContext(int context)
			=> context is ItemSlot.Context.EquipArmor
				or ItemSlot.Context.EquipArmorVanity
				or ItemSlot.Context.EquipAccessory
				or ItemSlot.Context.EquipAccessoryVanity
				or ItemSlot.Context.EquipDye
				or ItemSlot.Context.EquipGrapple
				or ItemSlot.Context.EquipMount
				or ItemSlot.Context.EquipMinecart
				or ItemSlot.Context.EquipPet
				or ItemSlot.Context.EquipLight
				or ItemSlot.Context.EquipMiscDye
				or ItemSlot.Context.ModdedAccessorySlot
				or ItemSlot.Context.ModdedVanityAccessorySlot
				or ItemSlot.Context.ModdedDyeSlot;
	}
}
