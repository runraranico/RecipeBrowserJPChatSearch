using Terraria.Chat;

namespace RecipeBrowserJPChatSearch.Patches
{
	/// <summary>
	/// PARKED: force stack=1 on send. Disabled — option 1 short tags keep /sN instead.
	/// </summary>
	internal static class ChatItemStackNormalizePatch
	{
		public static void Apply()
		{
			RbjDiag.Info("ChatItemStackNormalizePatch parked (stack-1-on-send disabled)");
		}

		/// <summary>No-op while parked.</summary>
		internal static void TryNormalizeComposeBufferOnEnter()
		{
		}

		/// <summary>No-op while parked.</summary>
		internal static string NormalizeItemStacksToOne(string text) => text;
	}
}
