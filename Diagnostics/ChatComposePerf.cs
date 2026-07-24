using System;
using System.Diagnostics;
using System.Text;
using RecipeBrowserJPChatSearch.Patches;
using Terraria;

namespace RecipeBrowserJPChatSearch
{
	/// <summary>
	/// While chat is open, counts OnHover / PostDraw cost (UniqueDraw is not hooked).
	/// Verbose only — gated by <see cref="RbjDiag.Enabled"/>.
	/// </summary>
	internal static class ChatComposePerf
	{
		private const int FlushIntervalMs = 1500;

		private static long _nextFlushTick;
		private static int _framesInWindow;
		private static long _postDrawUsTotal;
		private static int _postDrawSamples;

		private static int _hoverVanillaFull;
		private static int _hoverRbFull;

		private static bool _onHoverVanillaOk;
		private static bool _onHoverRbOk;
		private static bool _loggedHookStatus;

		internal static void SetHookStatus(bool vanillaOnHover, bool rbOnHover)
		{
			_onHoverVanillaOk = vanillaOnHover;
			_onHoverRbOk = rbOnHover;
		}

		internal static void NoteVanillaOnHover(bool skippedHeavy)
		{
			if (!RbjDiag.Enabled || !Main.drawingPlayerChat || skippedHeavy)
				return;

			_hoverVanillaFull++;
		}

		internal static void NoteRbOnHover(bool skippedHeavy)
		{
			if (!RbjDiag.Enabled || !Main.drawingPlayerChat || skippedHeavy)
				return;

			_hoverRbFull++;
		}

		internal static void AddPostDrawMicros(long microseconds)
		{
			if (!RbjDiag.Enabled || !Main.drawingPlayerChat)
				return;

			_postDrawUsTotal += microseconds;
			_postDrawSamples++;
		}

		internal static long ElapsedMicros(Stopwatch sw)
			=> sw.ElapsedTicks * 1_000_000L / Stopwatch.Frequency;

		/// <summary>Call once per frame from PostDraw while chat is open.</summary>
		internal static void EndChatFrame()
		{
			if (!RbjDiag.Enabled)
				return;

			if (!Main.drawingPlayerChat)
			{
				ResetWindow();
				_nextFlushTick = 0;
				return;
			}

			if (!_loggedHookStatus)
			{
				_loggedHookStatus = true;
				RbjDiag.Info(
					$"ChatComposePerf onHover vanilla={_onHoverVanillaOk} rb={_onHoverRbOk} UniqueDraw=not-hooked");
			}

			_framesInWindow++;
			long now = Environment.TickCount64;
			if (_nextFlushTick == 0)
				_nextFlushTick = now + FlushIntervalMs;

			if (now < _nextFlushTick)
				return;

			_nextFlushTick = now + FlushIntervalMs;
			Flush();
			ResetWindow();
		}

		private static void Flush()
		{
			try
			{
				FlushCore();
			}
			catch (Exception ex)
			{
				RbjDiag.Error("ChatComposePerf.Flush failed", ex);
			}
		}

		private static void FlushCore()
		{
			string chat = Main.chatText ?? string.Empty;
			int len = chat.Length;
			int tagI = CountToken(chat, "[i:");
			int tagISlash = CountToken(chat, "[i/");
			int tagItemhover = CountToken(chat, "[itemhover");
			int tagItem = CountToken(chat, "[item:");
			long avgPostUs = _postDrawSamples > 0 ? _postDrawUsTotal / _postDrawSamples : 0;

			var sb = new StringBuilder(256);
			sb.Append("ChatComposePerf ");
			sb.Append($"frames={_framesInWindow} chatLen={len} ");
			sb.Append($"tags[i:/={tagISlash} i:={tagI} item:={tagItem} itemhover={tagItemhover}] ");
			sb.Append($"hoverV={_hoverVanillaFull} hoverRb={_hoverRbFull} ");
			sb.Append($"postDrawAvgUs={avgPostUs} ");
			sb.Append(ChatParseMessageCache.SnapshotStats());
			RbjDiag.Info(sb.ToString());
		}

		private static int CountToken(string text, string token)
		{
			if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(token))
				return 0;

			int count = 0;
			int i = 0;
			while ((i = text.IndexOf(token, i, StringComparison.OrdinalIgnoreCase)) >= 0)
			{
				count++;
				i += token.Length;
			}

			return count;
		}

		private static void ResetWindow()
		{
			_framesInWindow = 0;
			_postDrawUsTotal = 0;
			_postDrawSamples = 0;
			_hoverVanillaFull = 0;
			_hoverRbFull = 0;
		}
	}
}
