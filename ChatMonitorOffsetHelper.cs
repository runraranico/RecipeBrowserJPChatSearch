using System;
using System.Reflection;
using Terraria;

namespace RecipeBrowserJPChatSearch
{
	internal static class ChatMonitorOffsetHelper
	{
		private static FieldInfo _linesOffsetField;

		internal static void ApplyStoredOffset(int scrollOffset)
		{
			if (scrollOffset == 0)
			{
				Main.chatMonitor.ResetOffset();
				return;
			}

			EnsureOffsetField();
			if (_linesOffsetField != null)
			{
				_linesOffsetField.SetValue(Main.chatMonitor, scrollOffset);
				return;
			}

			Main.chatMonitor.ResetOffset();
			int direction = Math.Sign(scrollOffset);
			for (int i = 0; i < Math.Abs(scrollOffset); i++)
				Main.chatMonitor.Offset(direction);
		}

		private static void EnsureOffsetField()
		{
			if (_linesOffsetField != null || Main.chatMonitor == null)
				return;

			Type monitorType = Main.chatMonitor.GetType();
			string[] preferredNames =
			{
				"_linesOffset",
				"_offset",
				"linesOffset",
				"_chatOffset",
				"_scrollOffset",
			};

			foreach (string name in preferredNames)
			{
				FieldInfo field = monitorType.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
				if (field?.FieldType == typeof(int))
				{
					_linesOffsetField = field;
					return;
				}
			}

			foreach (FieldInfo field in monitorType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
			{
				if (field.FieldType != typeof(int))
					continue;

				if (!field.Name.Contains("offset", StringComparison.OrdinalIgnoreCase))
					continue;

				_linesOffsetField = field;
				return;
			}
		}
	}
}
