using Terraria;
using Terraria.Localization;

namespace RecipeBrowserJPChatSearch
{
	internal static class LocalizedSearchHelper
	{
		public static bool ItemMatches(Item item, string query)
		{
			if (string.IsNullOrEmpty(query))
				return true;

			if (item == null || item.IsAir)
				return false;

			if (item.Name.IndexOf(query, System.StringComparison.OrdinalIgnoreCase) >= 0)
				return true;

			string localized = Lang.GetItemNameValue(item.type);
			if (!string.IsNullOrEmpty(localized) && localized.IndexOf(query, System.StringComparison.OrdinalIgnoreCase) >= 0)
				return true;

			return false;
		}

		public static bool NpcNameMatches(int npcId, string query)
		{
			if (string.IsNullOrEmpty(query))
				return true;

			string localized = Lang.GetNPCNameValue(npcId);
			return !string.IsNullOrEmpty(localized)
				&& localized.IndexOf(query, System.StringComparison.OrdinalIgnoreCase) >= 0;
		}
	}
}
