using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using Verse;

namespace BrrainzTools
{
	struct ModInfo
	{
		internal MethodBase method;
		internal ModMetaData metaData;
	}

	class ExceptionAnalyser
	{
		readonly Exception exception;
		static readonly Dictionary<Assembly, ModMetaData> MetaDataCache = new();
		static readonly string RimworldAssemblyName = typeof(Pawn).Assembly.GetName().Name;

		static readonly AccessTools.FieldRef<StackTrace, StackTrace[]> captured_traces_ref = AccessTools.FieldRefAccess<StackTrace, StackTrace[]>("captured_traces");
		static readonly AccessTools.FieldRef<StackFrame, string> internalMethodName_ref = AccessTools.FieldRefAccess<StackFrame, string>("internalMethodName");
		static readonly AccessTools.FieldRef<StackFrame, long> methodAddress_ref = AccessTools.FieldRefAccess<StackFrame, long>("methodAddress");

		delegate void GetFullNameForStackTrace(StackTrace instance, StringBuilder sb, MethodBase mi);
		static readonly MethodInfo m_GetFullNameForStackTrace = AccessTools.Method(typeof(StackTrace), "GetFullNameForStackTrace");
		static readonly GetFullNameForStackTrace getFullNameForStackTrace = AccessTools.MethodDelegate<GetFullNameForStackTrace>(m_GetFullNameForStackTrace);

		delegate uint GetMethodIndex(StackFrame instance);
		static readonly MethodInfo m_GetMethodIndex = AccessTools.Method(typeof(StackFrame), "GetMethodIndex");
		static readonly GetMethodIndex getMethodIndex = AccessTools.MethodDelegate<GetMethodIndex>(m_GetMethodIndex);

		delegate string GetSecureFileName(StackFrame instance);
		static readonly MethodInfo m_GetSecureFileName = AccessTools.Method(typeof(StackFrame), "GetSecureFileName");
		static readonly GetSecureFileName getSecureFileName = AccessTools.MethodDelegate<GetSecureFileName>(m_GetSecureFileName);

		delegate string GetAotId();
		static readonly MethodInfo m_GetAotId = AccessTools.Method(typeof(StackTrace), "GetAotId");
		static readonly GetAotId getAotId = AccessTools.MethodDelegate<GetAotId>(m_GetAotId);

		delegate string GetClassName(Exception instance);
		static readonly MethodInfo m_GetClassName = AccessTools.Method(typeof(Exception), "GetClassName");
		static readonly GetClassName getClassName = AccessTools.MethodDelegate<GetClassName>(m_GetClassName);

		delegate string ToStringBoolBool(Exception instance, bool needFileLineInfo, bool needMessage);
		static readonly MethodInfo m_ToString = AccessTools.Method(typeof(Exception), "ToString", new[] { typeof(bool), typeof(bool) });
		static readonly ToStringBoolBool ExceptionToString = AccessTools.MethodDelegate<ToStringBoolBool>(m_ToString);

		public ExceptionAnalyser(Exception exception)
		{
			this.exception = exception;
		}

		public IEnumerable<ModInfo> GetInvolvedMods(string[] excludePackageIds)
		{
			return GetAllModInfos(exception, 0)
				.Where(info => info.metaData.IsCoreMod == false && (excludePackageIds?.Contains(info.metaData.meta.packageId) ?? false) == false);
		}

		public string GetStacktrace()
		{
			var sb = new StringBuilder();
			_ = sb.Append("Exception");
			var trace = new StackTrace(exception);
			if (trace != null && trace.FrameCount > 0)
			{
				var method = GetExpandedMethod(trace.GetFrame(trace.FrameCount - 1), out _);
				_ = sb.Append($" in {method.DeclaringType.FullName}.{method.Name}");
			}
			_ = sb.Append($": {getClassName(exception)}");

			var message = exception.Message;
			if (message != null && message.Length > 0)
				_ = sb.Append($": {message}");

			if (exception.InnerException != null)
			{
				var txt = ExceptionToString(exception.InnerException, true, true);
				_ = sb.Append($" ---> {txt}\n   --- End of inner exception stack trace ---");
			}

			var stackTrace = WithHarmonyString(trace);
			if (stackTrace != null)
				_ = sb.Append($"\n{stackTrace}");

			return sb.ToString();
		}

		List<ModInfo> GetAllModInfos(Exception ex, int level)
		{
			var modInfos = new List<ModInfo>();
			var inner = ex.InnerException;
			if (inner != null)
				modInfos.AddRange(GetAllModInfos(inner, level + 1));

			var trace = new StackTrace(ex, 0, true);
			foreach (var frame in trace.GetFrames())
			{
				var method = Harmony.GetMethodFromStackframe(frame);
				var patches = FindPatches(method);
				//modInfos.AddRange(GetFinalizers(patches));
				modInfos.AddRange(GetPostfixes(patches));
				modInfos.AddRange(GetPrefixes(patches));
				modInfos.AddRange(GetTranspilers(patches));
				var metaData = GetMetadataIfMod(method);
				if (metaData != null)
					modInfos.Add(new ModInfo { method = method, metaData = metaData });
			}
			return modInfos;
		}

		static Patches FindPatches(MethodBase method)
		{
			if (method is MethodInfo replacement)
			{
				var original = Harmony.GetOriginalMethod(replacement);
				if (original == null)
					return null;
				return Harmony.GetPatchInfo(original);
			}
			return null;
		}

		static ModMetaData GetMetadataIfMod(MethodBase method)
		{
			if (method == null)
				return null;
			var assembly = method.DeclaringType?.Assembly;
			if (assembly == null)
				return null;
			var references = assembly.GetReferencedAssemblies();
			if (references.Any(assemblyName => assemblyName.Name == RimworldAssemblyName) == false)
				return null;
			var metaData = GetModMetaData(assembly);
			if (metaData == null || metaData.IsCoreMod)
				return null;
			return metaData;
		}

		static IEnumerable<ModInfo> GetPrefixes(Patches info)
		{
			if (info == null)
				return new List<ModInfo>().AsEnumerable();
			return AddMetadata(info.Prefixes.OrderBy(t => t.priority).Select(t => t.PatchMethod));
		}

		static IEnumerable<ModInfo> GetPostfixes(Patches info)
		{
			if (info == null)
				return new List<ModInfo>().AsEnumerable();
			return AddMetadata(info.Postfixes.OrderBy(t => t.priority).Select(t => t.PatchMethod));
		}

		static IEnumerable<ModInfo> GetTranspilers(Patches info)
		{
			if (info == null)
				return new List<ModInfo>().AsEnumerable();
			return AddMetadata(info.Transpilers.OrderBy(t => t.priority).Select(t => t.PatchMethod));
		}

		/*static IEnumerable<ModInfo> GetFinalizers(Patches info)
		{
			if (info == null) return new List<ModInfo>().AsEnumerable();
			return AddMetadata(info.Finalizers.OrderBy(t => t.priority).Select(t => t.PatchMethod));
		}*/

		static ModMetaData GetModMetaData(Assembly assembly)
		{
			if (MetaDataCache.TryGetValue(assembly, out var metaData) == false)
			{
				var contentPack = LoadedModManager.RunningMods
					.FirstOrDefault(m => m.IsCoreMod == false && m.assemblies.loadedAssemblies.Contains(assembly));
				if (contentPack != null)
					metaData = ModsConfig.ActiveModsInLoadOrder.FirstOrDefault(meta => meta.PackageId == contentPack.PackageId);
				MetaDataCache.Add(assembly, metaData);
			}
			return metaData;
		}

		static IEnumerable<ModInfo> AddMetadata(IEnumerable<MethodInfo> methods)
		{
			return methods.Select(method =>
			{
				var assembly = method.DeclaringType.Assembly;
				var metaData = GetModMetaData(assembly);
				return new ModInfo { method = method, metaData = metaData };
			});
		}

		static bool AddHarmonyFrames(StackTrace trace, StringBuilder sb)
		{
			if (trace.FrameCount == 0)
				return false;
			for (var i = 0; i < trace.FrameCount; i++)
			{
				var frame = trace.GetFrame(i);
				if (i > 0)
					_ = sb.Append('\n');
				_ = sb.Append("  at ");

				var method = GetExpandedMethod(frame, out var patches);

				if (method == null)
				{
					var internalMethodName = internalMethodName_ref(frame);
					if (internalMethodName != null)
						_ = sb.Append(internalMethodName);
					else
						_ = sb.AppendFormat("<0x{0:x5} + 0x{1:x5}> <unknown method>", methodAddress_ref(frame), frame.GetNativeOffset());
				}
				else
				{
					getFullNameForStackTrace(trace, sb, method);
					if (frame.GetILOffset() == -1)
					{
						_ = sb.AppendFormat(" <0x{0:x5} + 0x{1:x5}>", methodAddress_ref(frame), frame.GetNativeOffset());
						if (getMethodIndex(frame) != 16777215U)
							_ = sb.AppendFormat(" {0}", getMethodIndex(frame));
					}
					else
						_ = sb.AppendFormat(" [0x{0:x5}]", frame.GetILOffset());

					var fileName = getSecureFileName(frame);
					if (fileName[0] == '<')
					{
						var versionId = method.Module.ModuleVersionId.ToString("N");
						var aotId = getAotId();
						if (frame.GetILOffset() != -1 || aotId == null)
							fileName = string.Format("<{0}>", versionId);
						else
							fileName = string.Format("<{0}#{1}>", versionId, aotId);
					}
					_ = sb.AppendFormat(" in {0}:{1} ", fileName, frame.GetFileLineNumber());

					void AppendPatch(IEnumerable<Patch> fixes, string name)
					{
						foreach (var patch in PatchProcessor.GetSortedPatchMethods(method, fixes.ToArray()))
						{
							var owner = fixes.First(p => p.PatchMethod == patch).owner;
							var parameters = patch.GetParameters().Join(p => $"{p.ParameterType.Name} {p.Name}");
							_ = sb.AppendFormat("\n     - {0} {1}: {2} {3}:{4}({5})", name, owner, patch.ReturnType.Name, patch.DeclaringType.FullName, patch.Name, parameters);
						}
					}
					AppendPatch(patches.Transpilers, "transpiler");
					AppendPatch(patches.Prefixes, "prefix");
					AppendPatch(patches.Postfixes, "postfix");
					AppendPatch(patches.Finalizers, "finalizer");
				}
			}
			return true;
		}

		static string WithHarmonyString(StackTrace trace)
		{
			var sb = new StringBuilder();
			if (captured_traces_ref(trace) != null)
			{
				var array = captured_traces_ref(trace);
				for (int i = 0; i < array.Length; i++)
				{
					if (AddHarmonyFrames(array[i], sb))
						_ = sb.Append("\n--- End of stack trace from previous location where exception was thrown ---\n");
				}
			}
			_ = AddHarmonyFrames(trace, sb);
			return sb.ToString();
		}

		static MethodBase GetExpandedMethod(StackFrame frame, out Patches patches)
		{
			patches = new Patches(new Patch[0], new Patch[0], new Patch[0], new Patch[0]);
			var method = Harmony.GetMethodFromStackframe(frame);
			if (method != null && method is MethodInfo replacement)
			{
				var original = Harmony.GetOriginalMethod(replacement);
				if (original != null)
				{
					method = original;
					patches = Harmony.GetPatchInfo(method);
				}
			}
			return method;
		}
	}
}
