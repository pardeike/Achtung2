using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Verse;

namespace AchtungMod;

public static class Performance
{
	static readonly bool enabled;
	static readonly string logPath = Path.Combine(GenFilePaths.SaveDataFolderPath, "AchtungPerformance.txt");

	static readonly Stopwatch w_EndCurrentJob = new();
	static readonly Stopwatch w_ContinueJob = new();
	static readonly Stopwatch w_GetNextJob = new();

	static int w_GetNextJobCount = 0;
	static int logNextTicks = 0;

	static long EndCurrentJob_Max = 0;
	static long ContinueJob_Max = 0;
	static long GetNextJob_Max = 0;

	static Performance()
	{
		enabled = File.Exists(logPath);
		if (enabled == false) return;
		File.AppendAllText(logPath, $"### Achtung v{GetModVersionString()} performance {DateTime.Now}\r\n");
		File.AppendAllText(logPath, $"### XXX_ms are max milliseconds per 30 ticks\r\n");
		File.AppendAllText(logPath, $"### [Timestamp yyyyMMddHHmmss.ffff] [Type] [Pawn] [#GetNextJob] [GetNextJob_ms / ContinueJob_ms / EndCurrentJob_ms] [Workgivers]\r\n");
	}

	public static string GetModVersionString()
	{
		var assembly = Assembly.GetAssembly(typeof(Tools));
		var t_attribute = typeof(AssemblyFileVersionAttribute);
		var attribute = (AssemblyFileVersionAttribute)Attribute.GetCustomAttribute(assembly, t_attribute, false);
		return attribute.Version;
	}

	public static void EndCurrentJob_Start()
	{
		if (enabled)
			w_EndCurrentJob.Restart();
	}

	public static void EndCurrentJob_Stop()
	{
		if (enabled == false) return;
		var elapsed = w_EndCurrentJob.ElapsedMilliseconds;
		EndCurrentJob_Max = Math.Max(EndCurrentJob_Max, elapsed);
	}

	public static void ContinueJob_Start()
	{
		if (enabled)
			w_ContinueJob.Restart();
	}

	public static bool ContinueJob_Stop(ForcedJob forcedJob, bool result)
	{
		if (enabled)
		{
			var elapsed = w_ContinueJob.ElapsedMilliseconds;
			ContinueJob_Max = Math.Max(ContinueJob_Max, elapsed);
		}
		if (forcedJob != null)
			forcedJob.reentranceFlag = false;
		return result;
	}

	public static void GetNextJob_Start()
	{
		if (enabled)
			w_GetNextJob.Restart();
	}

	public static void GetNextJob_Count()
	{
		if (enabled)
			w_GetNextJobCount++;
	}

	public static void GetNextJob_Stop()
	{
		if (enabled == false) return;
		var elapsed = w_GetNextJob.ElapsedMilliseconds;
		GetNextJob_Max = Math.Max(GetNextJob_Max, elapsed);
	}

	public static void Report(ForcedJob forcedJob, Pawn pawn)
	{
		if (enabled == false) return;

		var ticks = GenTicks.TicksAbs;
		if (ticks > logNextTicks)
		{
			logNextTicks = ticks + 30;

			var description = forcedJob == null ? "" : forcedJob.workgiverDefs.Join(wgd => wgd?.defName);
			if (description.Contains("DeliverResourcesToBlueprints"))
				description = "<construct>";

			File.AppendAllText(logPath, $"[{DateTime.Now:yyyyMMddHHmmss.ffff}] [EndCurrentJob] [{pawn.LabelShortCap}] [{w_GetNextJobCount}] [{GetNextJob_Max}/{ContinueJob_Max}/{EndCurrentJob_Max}] [{description}]\r\n");

			w_GetNextJobCount = 0;
			GetNextJob_Max = 0;
			ContinueJob_Max = 0;
			EndCurrentJob_Max = 0;
		}
	}

	static readonly HashSet<string> knownExceptions = [];
	public static void Log(Exception exception)
	{
		if (enabled == false) return;
		var str = exception.ToString();
		if (knownExceptions.Add(str))
			File.AppendAllText(logPath, $"EXCEPTION: {exception}\r\n");
	}
}