using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;

namespace RecipeBrowserJPChatSearch
{
	/// <summary>
	/// Controls rows (section = mod DisplayName).
	/// Search hotkey: default Mouse3 (middle click). Triggers search on press.
	/// Past-log hotkey: default Back (Back Space). Opens past-log browse on press.
	/// </summary>
	internal static class ModKeybinds
	{
		internal const string SearchActionId = "RecipeBrowserJPChatSearch/SearchChordModifier";
		internal const string PastLogActionId = "RecipeBrowserJPChatSearch/PastLogModifier";
		internal const string SearchDefaultKey = "Mouse3";
		/// <summary>XNA Keys.Back — Controls UI shows "Back Space".</summary>
		internal const string PastLogDefaultKey = "Back";

		internal static ModKeybind SearchHotkey { get; private set; }

		internal static ModKeybind PastLogHotkey { get; private set; }

		internal static void Register(Mod mod)
		{
			// Keep RegisterKeybind names stable (Controls profile / localization keys).
			SearchHotkey = KeybindLoader.RegisterKeybind(mod, "SearchChordModifier", SearchDefaultKey);
			PastLogHotkey = KeybindLoader.RegisterKeybind(mod, "PastLogModifier", PastLogDefaultKey);
			RbjDiag.Info($"ModKeybinds registered (search={SearchDefaultKey}, pastLog={PastLogDefaultKey})");
		}

		/// <summary>
		/// Apply defaults for empty rows, and migrate old Shift/Alt defaults once.
		/// </summary>
		internal static void EnsureDefaultBindingsApplied()
		{
			if (Main.dedServ)
				return;

			try
			{
				if (PlayerInput.CurrentProfile?.InputModes == null)
					return;

				bool search = EnsureSearchDefault();
				bool past = EnsurePastLogDefault();
				if (search || past)
					RbjDiag.Info($"ModKeybinds defaults applied | search={search} pastLog={past}");
			}
			catch (Exception ex)
			{
				RbjDiag.Warn($"EnsureDefaultBindingsApplied failed: {ex.GetType().Name}: {ex.Message}");
			}
		}

		internal static void Unload()
		{
			SearchHotkey = null;
			PastLogHotkey = null;
		}

		/// <summary>Always-on fingerprint of actual keybind assignments (Workshop Release).</summary>
		internal static void LogAssignedBindingsRelease()
		{
			try
			{
				string search = DescribeAssigned(SearchHotkey) ?? "(unbound)";
				string past = DescribeAssigned(PastLogHotkey) ?? "(unbound)";
				RbjDiag.Release(
					$"Keybinds assigned search=[{search}] pastLog=[{past}] " +
					$"(defaults search={SearchDefaultKey} pastLog={PastLogDefaultKey})");
				if (!HasBinding(SearchHotkey))
					RbjDiag.Warn("Search hotkey is unbound — middle-click search will not fire");
				if (!HasBinding(PastLogHotkey))
					RbjDiag.Warn("Past-log hotkey is unbound");
			}
			catch (Exception ex)
			{
				RbjDiag.Warn($"LogAssignedBindingsRelease failed: {ex.GetType().Name}: {ex.Message}");
			}
		}

		private static string DescribeAssigned(ModKeybind keybind)
		{
			if (keybind == null)
				return null;

			List<string> keys = keybind.GetAssignedKeys();
			if (keys == null || keys.Count == 0)
				return null;

			var parts = new List<string>();
			foreach (string key in keys)
			{
				if (string.IsNullOrWhiteSpace(key))
					continue;
				if (key.Equals("None", StringComparison.OrdinalIgnoreCase))
					continue;
				parts.Add(key);
			}

			return parts.Count == 0 ? null : string.Join("+", parts);
		}

		internal static bool HasBinding(ModKeybind keybind)
		{
			if (keybind == null)
				return false;

			try
			{
				List<string> keys = keybind.GetAssignedKeys();
				if (keys == null || keys.Count == 0)
					return false;

				foreach (string key in keys)
				{
					if (string.IsNullOrWhiteSpace(key))
						continue;
					if (key.Equals("None", StringComparison.OrdinalIgnoreCase))
						continue;
					return true;
				}
			}
			catch (Exception ex)
			{
				RbjDiag.Warn($"HasBinding failed: {ex.GetType().Name}: {ex.Message}");
			}

			return false;
		}

		private static int _fireLatchFrame = -1;
		private static bool _fireLatchResult;
		private static long _lastFireTick;

		/// <summary>
		/// True when the Controls-bound search hotkey should fire this frame.
		/// Default binding is Mouse3 (hardware middle). Remapped keys use ModKeybind.JustPressed.
		/// <para>
		/// Also re-fires every ~180ms while held so incomplete middle-button release / mash
		/// at the same spot still sends Inv↔RB↔MS (JustPressed alone felt like total failure).
		/// Result is latched per <see cref="Main.GameUpdateCount"/> so multiple calls in one
		/// frame (layer mute checks + PostDraw) agree.
		/// </para>
		/// </summary>
		internal static bool IsSearchHotkeyJustPressed()
		{
			if (!HasBinding(SearchHotkey))
				return false;

			int frame = (int)Main.GameUpdateCount;
			if (_fireLatchFrame == frame)
				return _fireLatchResult;

			bool down;
			bool edge;
			if (IsMouse3OnlyBinding(SearchHotkey))
			{
				down = IsPhysicalMiddleDown();
				edge = down && !RecipeBrowserCursorSearchBridge.PreviousPhysicalMiddle;
				// Fallback: Main.mouseMiddle edge when hardware / remap disagree.
				if (!edge && Main.mouseMiddle && !RecipeBrowserCursorSearchBridge.PreviousMouseMiddle)
					edge = true;
			}
			else
			{
				down = SearchHotkey.Current;
				edge = SearchHotkey.JustPressed;
			}

			const int repeatMs = 180;
			long now = Environment.TickCount64;
			bool repeat = down && _lastFireTick > 0 && (now - _lastFireTick) >= repeatMs;

			_fireLatchResult = edge || repeat;
			_fireLatchFrame = frame;

			if (_fireLatchResult)
			{
				_lastFireTick = now;
				RbjDiag.Info($"SearchHotkeyFire edge={edge} repeat={repeat} down={down}");
			}
			else if (!down)
			{
				_lastFireTick = 0;
			}

			return _fireLatchResult;
		}

		/// <summary>True while the Controls-bound search hotkey is held.</summary>
		internal static bool IsSearchHotkeyHeld()
		{
			if (!HasBinding(SearchHotkey))
				return false;

			if (IsMouse3OnlyBinding(SearchHotkey))
				return IsPhysicalMiddleDown();

			return SearchHotkey.Current;
		}

		/// <summary>True on the frame the past-log hotkey fires. Unbound → never.</summary>
		internal static bool IsPastLogHotkeyJustPressed()
		{
			if (!HasBinding(PastLogHotkey))
				return false;

			return PastLogHotkey.JustPressed;
		}

		private static bool EnsureSearchDefault()
		{
			bool changed = false;
			changed |= MigrateOrAssignSearch(InputMode.Keyboard);
			changed |= MigrateOrAssignSearch(InputMode.KeyboardUI);
			return changed;
		}

		private static bool EnsurePastLogDefault()
		{
			bool changed = false;
			changed |= MigrateOrAssignPastLog(InputMode.Keyboard);
			changed |= MigrateOrAssignPastLog(InputMode.KeyboardUI);
			return changed;
		}

		private static bool MigrateOrAssignSearch(InputMode mode)
		{
			if (!PlayerInput.CurrentProfile.InputModes.TryGetValue(mode, out KeyConfiguration config)
				|| config?.KeyStatus == null)
				return false;

			if (!config.KeyStatus.TryGetValue(SearchActionId, out List<string> list) || list == null)
			{
				config.KeyStatus[SearchActionId] = new List<string> { SearchDefaultKey };
				return true;
			}

			if (IsEffectivelyUnbound(list) || IsOnlyLegacyShift(list))
			{
				list.Clear();
				list.Add(SearchDefaultKey);
				return true;
			}

			return false;
		}

		private static bool MigrateOrAssignPastLog(InputMode mode)
		{
			if (!PlayerInput.CurrentProfile.InputModes.TryGetValue(mode, out KeyConfiguration config)
				|| config?.KeyStatus == null)
				return false;

			if (!config.KeyStatus.TryGetValue(PastLogActionId, out List<string> list) || list == null)
			{
				config.KeyStatus[PastLogActionId] = new List<string> { PastLogDefaultKey };
				return true;
			}

			// Empty / None / old Alt-only default → Back Space.
			if (IsEffectivelyUnbound(list) || IsOnlyLegacyAlt(list))
			{
				list.Clear();
				list.Add(PastLogDefaultKey);
				return true;
			}

			return false;
		}

		private static bool IsEffectivelyUnbound(List<string> list)
		{
			foreach (string existing in list)
			{
				if (string.IsNullOrWhiteSpace(existing))
					continue;
				if (existing.Equals("None", StringComparison.OrdinalIgnoreCase))
					continue;
				return false;
			}

			return true;
		}

		private static bool IsOnlyLegacyShift(List<string> list)
		{
			string sole = GetSoleBinding(list);
			return sole != null
				&& (sole.Equals("LeftShift", StringComparison.OrdinalIgnoreCase)
					|| sole.Equals("RightShift", StringComparison.OrdinalIgnoreCase));
		}

		private static bool IsOnlyLegacyAlt(List<string> list)
		{
			string sole = GetSoleBinding(list);
			return sole != null
				&& (sole.Equals("LeftAlt", StringComparison.OrdinalIgnoreCase)
					|| sole.Equals("RightAlt", StringComparison.OrdinalIgnoreCase));
		}

		private static string GetSoleBinding(List<string> list)
		{
			string found = null;
			foreach (string existing in list)
			{
				if (string.IsNullOrWhiteSpace(existing))
					continue;
				if (existing.Equals("None", StringComparison.OrdinalIgnoreCase))
					continue;
				if (found != null)
					return null;
				found = existing;
			}

			return found;
		}

		private static bool IsMouse3OnlyBinding(ModKeybind keybind)
		{
			List<string> keys;
			try
			{
				keys = keybind.GetAssignedKeys();
			}
			catch
			{
				return false;
			}

			if (keys == null)
				return false;

			string sole = GetSoleBinding(keys);
			return sole != null && sole.Equals("Mouse3", StringComparison.OrdinalIgnoreCase);
		}

		private static bool IsPhysicalMiddleDown()
			=> Mouse.GetState().MiddleButton == ButtonState.Pressed;
	}
}
