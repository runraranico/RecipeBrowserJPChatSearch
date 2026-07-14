using Terraria;

namespace RecipeBrowserJPChatSearch
{
	/// <summary>
	/// Gates chat compose helpers (short tags / parse-cache passthrough).
	/// UniqueDraw stays vanilla/RB framed — no light-draw path.
	/// </summary>
	internal static class ChatItemTagLightPolicy
	{
		/// <summary>True while the player chat input box is open.</summary>
		internal static bool IsComposingChat => Main.drawingPlayerChat;
	}
}
