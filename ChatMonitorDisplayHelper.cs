using System;
using System.Reflection;
using Terraria;

namespace RecipeBrowserJPChatSearch
{
	internal static class ChatMonitorDisplayHelper
	{
		internal const int OverlayVisibleLines = 20;

		private static FieldInfo _visibleLinesField;
		private static int? _savedVisibleLines;

		internal static void ApplyOverlayLineLimit()
		{
			EnsureVisibleLinesField();
			if (_visibleLinesField == null)
				return;

			if (!_savedVisibleLines.HasValue)
				_savedVisibleLines = ReadVisibleLines();

			WriteVisibleLines(OverlayVisibleLines);
		}

		internal static void RestoreLineLimit()
		{
			if (_visibleLinesField == null || !_savedVisibleLines.HasValue)
				return;

			WriteVisibleLines(_savedVisibleLines.Value);
			_savedVisibleLines = null;
		}

		internal static int GetVanillaChatLingerFrames()
		{
			FieldInfo chatLength = typeof(Main).GetField("chatLength", BindingFlags.Static | BindingFlags.Public);
			if (chatLength?.GetValue(null) is int frames && frames > 0)
				return frames;

			return 600;
		}

		private static void EnsureVisibleLinesField()
		{
			if (_visibleLinesField != null || Main.chatMonitor == null)
				return;

			Type monitorType = Main.chatMonitor.GetType();
			string[] preferredNames =
			{
				"_amountOfMessagesToShowWhenChatIsOpen",
				"_amountToShowWhenChatIsOpen",
				"_maxShownMessagesWhenChatIsOpen",
				"_shownMessagesWhenChatOpen",
				"_visibleLinesWhenChatOpen",
				"amountToDisplay",
			};

			foreach (string name in preferredNames)
			{
				FieldInfo field = monitorType.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
				if (field?.FieldType == typeof(int))
				{
					_visibleLinesField = field;
					return;
				}
			}

			foreach (FieldInfo field in monitorType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
			{
				if (field.FieldType != typeof(int))
					continue;

				string name = field.Name;
				if (!name.Contains("Line", StringComparison.OrdinalIgnoreCase)
					&& !name.Contains("Message", StringComparison.OrdinalIgnoreCase)
					&& !name.Contains("Shown", StringComparison.OrdinalIgnoreCase)
					&& !name.Contains("Visible", StringComparison.OrdinalIgnoreCase)
					&& !name.Contains("Amount", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				int value = (int)field.GetValue(Main.chatMonitor);
				if (value is >= 5 and <= 40)
				{
					_visibleLinesField = field;
					return;
				}
			}
		}

		private static int ReadVisibleLines() => (int)_visibleLinesField.GetValue(Main.chatMonitor);

		private static void WriteVisibleLines(int value) => _visibleLinesField.SetValue(Main.chatMonitor, value);
	}
}
