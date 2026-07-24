namespace RecipeBrowserJPChatSearch
{
	internal static class CompositionTracker
	{
		internal static string LastNonEmptyComposition { get; set; } = string.Empty;

		internal static void Remember(string composition)
		{
			if (!string.IsNullOrEmpty(composition))
				LastNonEmptyComposition = composition;
		}

		internal static string ConsumeForCommit(string current)
		{
			if (string.IsNullOrEmpty(LastNonEmptyComposition))
				return current;

			string committed = current + LastNonEmptyComposition;
			LastNonEmptyComposition = string.Empty;
			return committed;
		}

		internal static void Clear()
		{
			LastNonEmptyComposition = string.Empty;
		}
	}
}
