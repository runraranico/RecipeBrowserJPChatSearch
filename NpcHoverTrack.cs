using Terraria;

namespace RecipeBrowserJPChatSearch
{
	/// <summary>
	/// NPC middle-click while Recipe Browser is open:
	/// B = vanilla SmartInteract / talkNPC;
	/// enemy = PreHover name-hover identity (mouseIntersects + ShowNameOnHover).
	/// Tip hit-test is NOT used — ForcedMinimumZoom made tip≠cursor and npcHover stayed false.
	/// Note is sticky a few frames so PostDraw middle-click still sees the name-hover NPC.
	/// World NPC while RB is closed is disabled (policy A).
	/// </summary>
	internal static class NpcHoverTrack
	{
		private const int StickyFrames = 4;

		private static int _npcWhoAmI = -1;
		private static int _npcType;
		private static int _npcNetId;
		private static int _stickyTtl;

		internal static bool HoveringNpc => _stickyTtl > 0 && _npcWhoAmI >= 0 && _npcType > 0;

		/// <summary>
		/// PreHover with mouseIntersects — same gate vanilla uses for drawing the NPC name.
		/// </summary>
		internal static void Note(NPC npc, bool mouseIntersects)
		{
			if (npc == null || !npc.active || npc.type <= 0 || !mouseIntersects)
				return;

			if (!npc.ShowNameOnHover)
				return;

			_npcWhoAmI = npc.whoAmI;
			_npcType = npc.type;
			_npcNetId = npc.netID;
			_stickyTtl = StickyFrames;
		}

		/// <summary>Decay sticky note (call once per PostDraw).</summary>
		internal static void ClearFrame()
		{
			if (_stickyTtl > 0)
				_stickyTtl--;

			if (_stickyTtl > 0)
				return;

			_npcWhoAmI = -1;
			_npcType = 0;
			_npcNetId = 0;
		}

		/// <summary>
		/// Policy B: trust vanilla focus only.
		/// </summary>
		internal static bool TryGetVanillaFocusNpc(out int npcType, out int npcNetId)
		{
			npcType = 0;
			npcNetId = 0;

			if (TryReadWhoAmI(Main.SmartInteractNPC, out npcType, out npcNetId))
			{
				RbjDiag.Info(
					$"NpcHover OK vanilla-smart who={Main.SmartInteractNPC} type={npcType} netId={npcNetId}");
				return true;
			}

			Player player = Main.LocalPlayer;
			if (player != null && TryReadWhoAmI(player.talkNPC, out npcType, out npcNetId))
			{
				RbjDiag.Info(
					$"NpcHover OK vanilla-talk who={player.talkNPC} type={npcType} netId={npcNetId}");
				return true;
			}

			return false;
		}

		/// <summary>
		/// Name-showing NPC from PreHover sticky (same identity as the drawn name).
		/// No tip SetZoom.
		/// </summary>
		internal static bool TryGetNamedHoverNpc(out int npcType, out int npcNetId)
		{
			npcType = 0;
			npcNetId = 0;

			Player player = Main.LocalPlayer;
			if (player != null && player.mouseInterface)
			{
				RbjDiag.Info("NpcHover skip named: mouseInterface");
				return false;
			}

			if (!HoveringNpc || !TryReadWhoAmI(_npcWhoAmI, out npcType, out npcNetId))
			{
				RbjDiag.Info(
					$"NpcHover miss named: stickyTtl={_stickyTtl} who={_npcWhoAmI} type={_npcType} " +
					$"smart={Main.SmartInteractNPC} talk={(Main.LocalPlayer?.talkNPC ?? -1)}");
				return false;
			}

			RbjDiag.Info(
				$"NpcHover OK named-preHover who={_npcWhoAmI} type={npcType} netId={npcNetId} " +
				$"stickyTtl={_stickyTtl}");
			return true;
		}

		/// <summary>RB-open NPC pick: cursor name-hover first, then SmartInteract, then talkNPC.</summary>
		internal static bool TryGetNpcWhileRecipeBrowserOpen(out int npcType, out int npcNetId)
		{
			// Prefer the NPC under the cursor whose name is showing. While chatting,
			// talkNPC stays on the shopkeeper — using it first made every middle-click
			// open that NPC even when hovering someone else.
			if (TryGetNamedHoverNpc(out npcType, out npcNetId))
				return true;

			if (TryGetVanillaFocusNpc(out npcType, out npcNetId))
				return true;

			return false;
		}

		private static bool TryReadWhoAmI(int who, out int npcType, out int npcNetId)
		{
			npcType = 0;
			npcNetId = 0;
			if (who < 0 || who >= Main.maxNPCs)
				return false;

			NPC npc = Main.npc[who];
			if (npc == null || !npc.active || npc.life <= 0 || npc.type <= 0)
				return false;

			npcType = npc.type;
			npcNetId = npc.netID;
			return true;
		}
	}
}
