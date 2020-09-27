using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using Verse;

namespace Brrainz
{
	[StaticConstructorOnStartup]
	static class ExceptionHandler
	{
		const string harmony_id = "brrainz.exception.helper";

		static readonly string NameOfThisMod = GetModMetaData(typeof(ExceptionHandler).Assembly).Name;
		static readonly string RimworldAssemblyName = typeof(Pawn).Assembly.GetName().Name;
		static readonly Dictionary<Assembly, ModMetaData> MetaDataCache = new Dictionary<Assembly, ModMetaData>();
		static readonly HarmonyMethod ExceptionTranspiler = new HarmonyMethod(SymbolExtensions.GetMethodInfo(() => Transpiler(default, default))) { priority = int.MinValue };
		static readonly MethodInfo OriginalLogError = SymbolExtensions.GetMethodInfo(() => Log.Error("", false));
		static readonly MethodInfo Handle_Exception = SymbolExtensions.GetMethodInfo(() => HandleException(default, default, default));
		static readonly HashSet<string> SeenErrors = new HashSet<string>();

		static void HandleException(string text, bool ignoreStopLoggingLimit, Exception ex)
		{
			Log.Error(text, ignoreStopLoggingLimit);
			if (SeenErrors.Add(text) == false) return;

			var n = 0;
			var exception = ex;
			MethodBase topMethod = null;
			var seenAssemblies = new HashSet<Assembly>();
			var result = new StringBuilder();
			while (exception != null)
			{
				var st = new StackTrace(exception);
				st.GetFrames().Select(frame => frame.GetMethod())
					.DoIf(method => method != null, method =>
					{
						topMethod = topMethod ?? method;
						var assembly = method.DeclaringType.Assembly;
						if (IsModMethod(method) && seenAssemblies.Add(assembly))
						{
							var metaData = GetModMetaData(assembly);
							if (metaData != null && metaData.IsCoreMod == false)
							{
								if (n == 0)
								{
									_ = result.Append($"Mod {NameOfThisMod} has installed an exception analyzer\n" +
									$"### The following information is extracted from the error and might give you some insights\n" +
									$"### in the mods involved in the exception.\n" +
									$"### ATTENTION: This list is not the truth! Mods listed here might not be the cause at all.\n" +
									$"### They are listed in order of most likely to least likey:\n" +
									$"### [MOD] [STEAM_ID] [URL] [LAST_METHOD_EXECUTED]\n");
								}
								_ = result.Append($"### {++n}) [{metaData.Name} by {metaData.Author}] [{metaData.SteamAppId}] [{metaData.Url}] [{method.DeclaringType.FullName}::{method.Name}]\n");
							}
						}
					});
				exception = exception.InnerException;
			}
			if (n > 0)
			{
				_ = result.Append("### Please copy the error with the full stacktrace and these lines into");
				_ = result.Append("### a message to the support of the mod author(s).");
				Log.Warning(result.ToString());
			}
		}

		static bool IsModMethod(MethodBase method)
		{
			var references = method.DeclaringType.Assembly.GetReferencedAssemblies();
			return references.Any(assemblyName => assemblyName.Name == RimworldAssemblyName);
		}

		static ModMetaData GetModMetaData(Assembly assembly)
		{
			if (MetaDataCache.TryGetValue(assembly, out var metaData) == false)
			{
				var contentPack = LoadedModManager.RunningMods
					.FirstOrDefault(m => m.assemblies.loadedAssemblies.Contains(assembly));
				if (contentPack != null)
					metaData = new ModMetaData(contentPack.RootDir);
				MetaDataCache.Add(assembly, metaData);
			}
			return metaData;
		}

		static void ReplaceInstruction(this List<CodeInstruction> instructions, int index, CodeInstruction[] replacement)
		{
			instructions[index].opcode = replacement[0].opcode;
			instructions[index].operand = replacement[0].operand;
			instructions.InsertRange(index + 1, replacement.Skip(1));
		}

		// [HarmonyDebug]
		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
		{
			var list = instructions.ToList();
			for (var i = 0; i < list.Count; i++)
			{
				var instruction = list[i];
				if (instruction.blocks.All(block => block.blockType != ExceptionBlockType.BeginCatchBlock))
					continue;
				var catchBlockStart = i;
				var catchBlockEnd = list.FindIndex(catchBlockStart, instr => instr.blocks.Any(block => block.blockType == ExceptionBlockType.EndExceptionBlock));
				if (catchBlockEnd == -1)
					continue;
				var catchInstructions = list.GetRange(catchBlockStart, catchBlockEnd - catchBlockStart);
				var logErrorOffset = catchInstructions.FindIndex(instr => instr.Calls(OriginalLogError));
				if (logErrorOffset >= 0)
				{
					var ex = generator.DeclareLocal(typeof(Exception));
					list.ReplaceInstruction(catchBlockStart, new[]
					{
						new CodeInstruction(OpCodes.Dup),
						new CodeInstruction(OpCodes.Stloc, ex),
						list[catchBlockStart].Clone()
					});
					list.ReplaceInstruction(catchBlockStart + 2 + logErrorOffset, new[]
					{
						new CodeInstruction(OpCodes.Ldloc, ex),
						new CodeInstruction(OpCodes.Call, Handle_Exception)
					});
					catchBlockEnd += 3;
				}
				i = catchBlockEnd + 1;
			}
			return list.AsEnumerable();
		}

		static ExceptionHandler()
		{
			if (Harmony.HasAnyPatches(harmony_id)) return;
			var harmony = new Harmony(harmony_id);
			var watch = new Stopwatch();
			watch.Start();
			var n = 0;
			typeof(Pawn).Assembly.DefinedTypes
				.Where(type => type.IsGenericType == false)
				.SelectMany(type => type.DeclaredMethods)
				.DoIf(method => method.IsGenericMethod == false && method.ContainsGenericParameters == false, (Action<MethodInfo>)(method =>
				{
					try
					{
						var body = method.GetMethodBody();
						if (body == null || body.ExceptionHandlingClauses.Count == 0)
							return;
						var info = PatchProcessor.ReadMethodBody(method).ToList();
						for (var i = 0; i < info.Count - 1; i++)
						{
							var opcode = info[i].Key;
							if (opcode == OpCodes.Leave || opcode == OpCodes.Leave_S)
							{
								if (info[i + 1].Key.StackBehaviourPop == StackBehaviour.Pop1)
								{
									var foundLogError = false;
									for (var j = i + 2; j < info.Count; j++)
									{
										opcode = info[j].Key;
										if (opcode == OpCodes.Call && info[j].Value as MethodInfo == OriginalLogError)
											foundLogError = true;
										if (opcode == OpCodes.Leave || opcode == OpCodes.Leave_S)
											break;
									}
									if (foundLogError)
									{
										n++;
										_ = harmony.Patch(method, transpiler: ExceptionTranspiler);
										break;
									}
								}
							}
						}
					}
					finally { }
				}));
			Log.Message($"# Patching {n} exception message handling locations took {watch.ElapsedMilliseconds} ms");
		}
	}
}