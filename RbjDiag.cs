using System;
using System.IO;
using Terraria;
using Terraria.ModLoader;

namespace RecipeBrowserJPChatSearch
{
	/// <summary>
	/// Diagnostic logging → tModLoader Logger + Documents/.../Logs/RBJ_Debug_*.txt
	/// <para>
	/// Two tiers:
	/// <list type="bullet">
	/// <item><see cref="Enabled"/> (verbose): detailed Info spam for development.</item>
	/// <item>Release: <see cref="Release"/> / Warn / Error always written (Workshop-safe).</item>
	/// </list>
	/// Set <see cref="Enabled"/> false before Workshop publish.
	/// </para>
	/// </summary>
	internal static class RbjDiag
	{
		/// <summary>
		/// Verbose Info logs. Keep true while debugging; set false before Workshop publish.
		/// Warn / Error / Release (startup fingerprint etc.) are NOT gated by this.
		/// </summary>
		internal static bool Enabled = false;

		private static string _latestPath;
		private static string _sessionPath;
		private static bool _pathsReady;
		private const int MaxLatestBytes = 512 * 1024;

		/// <summary>Verbose development Info (no-op when <see cref="Enabled"/> is false).</summary>
		internal static void Info(string message)
		{
			if (!Enabled)
				return;
			Write("INFO", message, forceFile: true);
		}

		/// <summary>Short always-on Info for Workshop builds (session fingerprint, transfer summary).</summary>
		internal static void Release(string message)
			=> Write("INFO", message, forceFile: true);

		internal static void Warn(string message)
			=> Write("WARN", message, forceFile: true);

		internal static void Error(string message, Exception ex = null)
		{
			Write(
				"ERROR",
				ex == null ? message : $"{message} | {ex.GetType().Name}: {ex.Message}",
				forceFile: true);

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

		/// <summary>Force a new latest/session file (call from Mod.Load).</summary>
		internal static void BeginSession(string note = null)
		{
			_pathsReady = false;
			_latestPath = null;
			_sessionPath = null;
			EnsurePaths();
			if (!string.IsNullOrEmpty(note))
				Release(note);
		}

		private static void Write(string level, string message, bool forceFile)
		{
			if (!forceFile && !Enabled)
				return;

			string line = $"[{DateTime.Now:HH:mm:ss.fff}] [RBJDiag/{level}] {message}";
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
							// Release lines still go to tML Info so they appear with Enabled=false.
							logger.Info(payload);
							break;
					}
				}
			}
			catch
			{
				// Logger may be unavailable during unload.
			}

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
			if (_pathsReady)
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
			if (_latestPath == null || !File.Exists(_latestPath))
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
