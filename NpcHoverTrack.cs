using Terraria;

namespace RecipeBrowserJPChatSearch
{
	/// <summary>
	/// Tracks an NPC under the cursor (PreHoverInteract + mouse-world fallback).
	/// </summary>
	internal static class NpcHoverTrack
	{
		private static int _npcWhoAmI = -1;
		private static int _npcType;
		private static int _npcNetId;

		internal static bool HoveringNpc => _npcWhoAmI >= 0 && _npcType > 0;

		internal static int HoveredNpcType => _npcType;

		internal static int HoveredNpcNetId => _npcNetId > 0 ? _npcNetId : _npcType;

		internal static void Note(NPC npc)
		{
			if (npc == null || !npc.active || npc.type <= 0)
				return;

			_npcWhoAmI = npc.whoAmI;
			_npcType = npc.type;
			_npcNetId = npc.netID;
		}

		internal static void ClearFrame()
		{
			_npcWhoAmI = -1;
			_npcType = 0;
			_npcNetId = 0;
		}

		/// <summary>
		/// Prefer this-frame PreHover note; otherwise hit-test active NPCs under the cursor.
		/// </summary>
		internal static bool TryGetHoveredNpc(out int npcType, out int npcNetId)
		{
			if (HoveringNpc)
			{
				npcType = _npcType;
				npcNetId = HoveredNpcNetId;
				return true;
			}

			npcType = 0;
			npcNetId = 0;

			Player player = Main.LocalPlayer;
			if (player != null && player.mouseInterface)
				return false;

			var world = RbjCursor.GetWorldUnderCursorTip();
			Microsoft.Xna.Framework.Point mouse = world.ToPoint();
			float bestDist = float.MaxValue;
			NPC best = null;

			for (int i = 0; i < Main.maxNPCs; i++)
			{
				NPC npc = Main.npc[i];
				if (!npc.active || npc.life <= 0 || npc.type <= 0)
					continue;

				if (!npc.Hitbox.Contains(mouse))
					continue;

				float dist = npc.DistanceSQ(world);
				if (dist >= bestDist)
					continue;

				bestDist = dist;
				best = npc;
			}

			if (best == null)
				return false;

			npcType = best.type;
			npcNetId = best.netID;
			return true;
		}
	}
}
