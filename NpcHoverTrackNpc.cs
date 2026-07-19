using Terraria;
using Terraria.ModLoader;

namespace RecipeBrowserJPChatSearch
{
	internal class NpcHoverTrackNpc : GlobalNPC
	{
		public override bool PreHoverInteract(NPC npc, bool mouseIntersects)
		{
			NpcHoverTrack.Note(npc, mouseIntersects);
			return true;
		}
	}
}
