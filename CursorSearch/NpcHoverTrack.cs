using Terraria;
using Terraria.GameInput;

namespace RecipeBrowserJPChatSearch
{
	internal static class NpcHoverTrack
	{
		private static int _npcWhoAmI = -1;
		private static int _mouseX;
		private static int _mouseY;

		internal static void Note(NPC npc, bool mouseIntersects)
		{
			if (!mouseIntersects || npc == null || !npc.active || npc.type <= 0 || !npc.ShowNameOnHover)
				return;

			_npcWhoAmI = npc.whoAmI;
			// Keep the pointer from the native hit; UI and world zoom change Main.mouseX/Y.
			_mouseX = PlayerInput.MouseX;
			_mouseY = PlayerInput.MouseY;
		}

		// Vanilla records the hit in Mouse Over, which follows PostDrawInterface.
		internal static void ClearFrame()
		{
			_npcWhoAmI = -1;
		}

		internal static bool TryGetNpcWhileRecipeBrowserOpen(out int npcType, out int npcNetId)
		{
			npcType = 0;
			npcNetId = 0;
			if (_npcWhoAmI < 0 || _npcWhoAmI >= Main.maxNPCs ||
				_mouseX != PlayerInput.MouseX || _mouseY != PlayerInput.MouseY ||
				Main.LocalPlayer?.mouseInterface == true)
				return false;

			NPC npc = Main.npc[_npcWhoAmI];
			if (npc == null || !npc.active || npc.life <= 0 || npc.type <= 0)
				return false;

			npcType = npc.type;
			npcNetId = npc.netID;
			return true;
		}
	}
}
