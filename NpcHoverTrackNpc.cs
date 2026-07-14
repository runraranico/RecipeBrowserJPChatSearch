using Terraria;
using Terraria.ModLoader;

namespace RecipeBrowserJPChatSearch
{
	internal class NpcHoverTrackNpc : GlobalNPC
	{
		public override bool PreHoverInteract(NPC npc, bool mouseIntersects)
		{
			if (mouseIntersects)
				NpcHoverTrack.Note(npc);

			return true;
		}
	}
}
