using RecipeBrowserJPChatSearch.Patches;
using Terraria.ModLoader;

namespace RecipeBrowserJPChatSearch
{
	public class RecipeBrowserJPChatSearch : Mod
	{
		public override void Load()
		{
			RbjDiag.BeginSession($"Load start version={Version} verbose={RbjDiag.Enabled}");

			ModKeybinds.Register(this);

			RecipeBrowserImePanelPatch.Apply();
			InventoryHoverTrackPatch.Apply();
			// HoverLoc MouseText hooks are verbose-only (not needed for Workshop Release).
			if (RbjDiag.Enabled)
				HoverTooltipDrawPatch.Apply();

			VanillaItemTagHandlerPatch.Apply();
			ChatComposeTagShortener.Apply();
			ChatParseMessageCache.Apply();
			// ChatItemStackNormalizePatch parked — keep file; conflicts with short /s tags.

			ModLoader.TryGetMod("SerousCommonLib", out Mod serous);
			if (serous != null)
				SerousCommonLibPatches.Apply(serous);

			ModLoader.TryGetMod("MagicStorage", out Mod magicStorage);
			ModLoader.TryGetMod("RecipeBrowser", out Mod recipeBrowser);

			MiddleClickTransferPatch.Apply(magicStorage, recipeBrowser);

			if (recipeBrowser != null)
			{
				RecipeBrowserCursorSearchBridge.Initialize(recipeBrowser);
				RecipeBrowserValidatePatches.Apply(recipeBrowser);
				RecipeBrowserSetTextPatch.Apply(recipeBrowser);
				RecipeBrowserFilterPatches.Apply(recipeBrowser);
				RecipeBrowserUnfocusPatch.Apply(recipeBrowser);
				ItemHoverFixTagHandlerPatch.Apply(recipeBrowser);
				RecipeBrowserCursorQueryClearPatch.Apply(recipeBrowser);
				RecipeBrowserCursorSearchPatch.Apply(recipeBrowser);
				RecipeBrowserPatches.Apply(recipeBrowser);
			}
			else
			{
				RbjDiag.Warn("RecipeBrowser not loaded — cursor search / transfers unavailable");
			}

			ChatComposePerf.SetHookStatus(
				VanillaItemTagHandlerPatch.OnHoverHooked,
				ItemHoverFixTagHandlerPatch.OnHoverHooked);

			RbjDiag.Release(
				$"Load complete v={Version} verbose={RbjDiag.Enabled} " +
				$"onHover vanilla={VanillaItemTagHandlerPatch.OnHoverHooked} " +
				$"rb={ItemHoverFixTagHandlerPatch.OnHoverHooked} UniqueDraw=not-hooked " +
				$"compose=short-tags parse-cache=DISABLED");
		}

		public override void PostSetupContent()
		{
			ModKeybinds.EnsureDefaultBindingsApplied();
			RbjRenderHealth.LogStartupConflicts();
			ModKeybinds.LogAssignedBindingsRelease();
		}

		public override void Unload()
		{
			MiddleClickTransferPatch.Unload();
			MagicStorageSearchHelper.Unload();
			RecipeBrowserCursorSearchBridge.Unload();
			ModKeybinds.Unload();
		}
	}
}
