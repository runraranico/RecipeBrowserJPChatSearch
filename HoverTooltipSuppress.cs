using System;
using System.Collections.Generic;
using Terraria;
using Terraria.UI;

namespace RecipeBrowserJPChatSearch
{
	/// <summary>
	/// After a successful search-hotkey transfer, keep the clicked item's tooltip
	/// on screen for a short wall-clock window by re-applying a cloned tip to
	/// <see cref="Main.HoverItem"/> right before Vanilla Mouse Text.
	/// <para>
	/// Safety: never clears HoverItem for transfer resolution. Duration is ms-based
	/// so high FPS cannot burn through the hold.
	/// </para>
	/// </summary>
	internal static class HoverTooltipSuppress
	{
		/// <summary>~0.7s — under 1s as requested.</summary>
		private const int DefaultHoldMs = 700;

		private static Item _held;
		private static long _holdUntilTick;
		private static string _armReason = string.Empty;
		private static bool _applyLogged;

		internal static bool Active =>
			_held != null
			&& !_held.IsAir
			&& Environment.TickCount64 < _holdUntilTick;

		internal static int RemainingMs =>
			Active ? (int)Math.Max(0, _holdUntilTick - Environment.TickCount64) : 0;

		internal static string ArmReason => _armReason;

		internal static int HeldType => Active ? _held.type : 0;

		internal static void Hold(Item item, int holdMs = DefaultHoldMs, string reason = null)
		{
			if (item == null || item.IsAir)
				return;

			// Belt-and-suspenders: never sticky-hold placeable / world-pick reasons.
			string r = reason ?? string.Empty;
			if (r.IndexOf("placeable", StringComparison.OrdinalIgnoreCase) >= 0
				|| r.IndexOf("WorldPick", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				RbjDiag.Info($"HoverHold REJECT reason='{r}' type={item.type}");
				return;
			}

			_held = item.Clone();
			int ms = holdMs > 0 ? holdMs : DefaultHoldMs;
			_holdUntilTick = Environment.TickCount64 + ms;
			_armReason = r;
			_applyLogged = false;
			RbjDiag.Info($"HoverHold ARM type={_held.type} ms={ms} reason='{_armReason}'");
			RbjRenderHealth.Mark($"HoverHold ARM type={_held.type} ms={ms}");
			HoverTooltipLocationProbe.Arm($"Hold:{_armReason}:type={_held.type}");
		}

		internal static void Cancel(string why = null)
		{
			if (_held == null)
				return;

			RbjDiag.Info($"HoverHold CANCEL type={_held.type} why='{why ?? ""}' was='{_armReason}'");
			_held = null;
			_holdUntilTick = 0;
			_armReason = string.Empty;
			_applyLogged = false;
		}

		/// <summary>
		/// Inserts a layer just before Vanilla Mouse Text that re-applies the held tip.
		/// </summary>
		internal static void ApplyDrawOnlyMute(List<GameInterfaceLayer> layers)
		{
			ExpireIfNeeded();
			if (!Active || layers == null)
				return;

			int insertAt = layers.FindIndex(l => l.Name == "Vanilla: Mouse Text");
			if (insertAt < 0)
				insertAt = layers.Count;

			layers.Insert(insertAt, new LegacyGameInterfaceLayer(
				"RecipeBrowserJPChatSearch: ForceHeldHover",
				() =>
				{
					ApplyHeldHoverItem();
					return true;
				},
				InterfaceScaleType.UI));
		}

		/// <summary>
		/// Extra safety after PostDraw transfers: keep HoverItem as the held tip
		/// so the next frame's early UI reads still see it until MouseText runs.
		/// </summary>
		internal static void DrawForcedTooltipIfNeeded()
		{
			ApplyHeldHoverItem();
		}

		internal static void ApplyHeldHoverItem()
		{
			ExpireIfNeeded();
			if (!Active)
				return;

			Main.HoverItem = _held;
			if (!string.IsNullOrEmpty(_held.Name))
				Main.HoverItem.SetNameOverride(_held.Name);

			if (!_applyLogged)
			{
				_applyLogged = true;
				RbjDiag.Info(
					$"HoverHold APPLY type={_held.type} remainingMs={RemainingMs} reason='{_armReason}'");
				RbjRenderHealth.Mark($"HoverHold APPLY type={_held.type}");
			}

			HoverTooltipLocationProbe.NoteHoverItemDelta("ForceHeldHover");
		}

		private static void ExpireIfNeeded()
		{
			if (_held == null)
				return;

			if (Environment.TickCount64 >= _holdUntilTick || _held.IsAir)
			{
				RbjDiag.Info($"HoverHold EXPIRE type={_held.type} reason='{_armReason}'");
				RbjRenderHealth.Mark($"HoverHold EXPIRE type={_held.type}");
				_held = null;
				_holdUntilTick = 0;
				_armReason = string.Empty;
				_applyLogged = false;
			}
		}
	}
}
