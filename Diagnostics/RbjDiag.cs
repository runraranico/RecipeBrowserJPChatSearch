using System;
using System.IO;
using Terraria;
using Terraria.ModLoader;

namespace RecipeBrowserJPChatSearch
{
	/// <summary>
	/// Diagnostic logging. Optional disk files under Documents/.../Logs/RBJ_Debug_*.txt.
	/// <para>
	/// Tiers:
	/// <list type="bullet">
	/// <item><see cref="Enabled"/> (verbose): detailed Info spam for development.</item>
	/// <item><see cref="FileLoggingEnabled"/>: write RBJ_Debug_latest / session txt. Default false for Workshop.</item>
	/// <item>Warn / Error always go to tModLoader Logger (no RBJ_Debug files when FileLoggingEnabled is false).</item>
	/// </list>
	/// </para>
	/// </summary>
	internal static class RbjDiag
	{
		/// <summary>
		/// Verbose Info logs. Default false for Workshop / public builds.
		/// </summary>
		internal static bool Enabled = false;

		/// <summary>
		/// When false, never create or append <c>RBJ_Debug_*.txt</c> (Workshop default).
		/// Set true temporarily while debugging to disk.
		/// </summary>
		internal static bool FileLoggingEnabled = false;

		private static string _latestPath;
		private static string _sessionPath;
		private static bool _pathsReady;
		private const int MaxLatestBytes = 512 * 1024;

		/// <summary>
		/// Verbose development Info (no-op when <see cref="Enabled"/> is false).
		/// </summary>
		internal static void Info(string message)
		{
			if (!Enabled)
				return;
			Write("INFO", message);
		}

		/// <summary>
		/// Short session / transfer summaries. Disk only when <see cref="FileLoggingEnabled"/>;
		/// tML Logger Info only when <see cref="Enabled"/> (avoids Workshop client.log spam).
		/// </summary>
		internal static void Release(string message)
		{
			if (!Enabled && !FileLoggingEnabled)
				return;
			Write("INFO", message);
		}

		internal static void Warn(string message)
			=> Write("WARN", message);

		internal static void Error(string message, Exception ex = null)
		{
			Write(
				"ERROR",
				ex == null ? message : $"{message} | {ex.GetType().Name}: {ex.Message}");

			try
			{
				ModContent.GetInstance<RecipeBrowserJPChatSearch>()?.Logger.Error(ex == null ? message : ex.ToString());
			}
			catch
			{
				// Logger may be unavailable during unload.
			}
		}

		/// <summary>
		/// Screen vs world pointer snapshot for dual-monitor / offset debugging (verbose).
		/// </summary>
		internal static string PointerSnapshot()
			=> RbjCursor.Snapshot();

		/// <summary>Call from Mod.Load. Creates disk session files only when FileLoggingEnabled.</summary>
		internal static void BeginSession(string note = null)
		{
			_pathsReady = false;
			_latestPath = null;
			_sessionPath = null;
			if (FileLoggingEnabled)
				EnsurePaths();
			if (!string.IsNullOrEmpty(note))
				Release(note);
		}

		private static void Write(string level, string message)
		{
			bool toLogger = level is "WARN" or "ERROR" || Enabled;
			bool toFile = FileLoggingEnabled;

			if (!toLogger && !toFile)
				return;

			string line = $"[{DateTime.Now:HH:mm:ss.fff}] [RBJDiag/{level}] {message}";

			if (toLogger)
			{
				try
				{
					var logger = ModContent.GetInstance<RecipeBrowserJPChatSearch>()?.Logger;
					if (logger != null)
					{
						string payload = $"[RBJDiag] {message}";
						switch (level)
						{
							case "WARN":
								logger.Warn(payload);
								break;
							case "ERROR":
								logger.Error(payload);
								break;
							default:
								logger.Info(payload);
								break;
						}
					}
				}
				catch
				{
					// Logger may be unavailable during unload.
				}
			}

			if (!toFile)
				return;

			try
			{
				EnsurePaths();
				MaybeRotateLatest();
				File.AppendAllText(_latestPath, line + Environment.NewLine);
				File.AppendAllText(_sessionPath, line + Environment.NewLine);
			}
			catch
			{
				// Disk failures must never break gameplay.
			}
		}

		private static void EnsurePaths()
		{
			if (_pathsReady || !FileLoggingEnabled)
				return;

			string logsDir = Path.Combine(Main.SavePath, "Logs");
			Directory.CreateDirectory(logsDir);
			_latestPath = Path.Combine(logsDir, "RBJ_Debug_latest.txt");
			_sessionPath = Path.Combine(logsDir, $"RBJ_Debug_{DateTime.Now:yyyyMMdd_HHmmss_fff}.txt");
			File.WriteAllText(
				_latestPath,
				$"=== RBJ DIAG SESSION {DateTime.Now:yyyy-MM-dd HH:mm:ss} verbose={Enabled} ==={Environment.NewLine}");
			_pathsReady = true;
		}

		private static void MaybeRotateLatest()
		{
			if (!FileLoggingEnabled || _latestPath == null || !File.Exists(_latestPath))
				return;

			try
			{
				if (new FileInfo(_latestPath).Length <= MaxLatestBytes)
					return;

				File.WriteAllText(
					_latestPath,
					$"=== RBJ DIAG ROTATED {DateTime.Now:yyyy-MM-dd HH:mm:ss} (truncated oversized log) verbose={Enabled} ==={Environment.NewLine}");
			}
			catch
			{
				// ignore
			}
		}
	}
}
