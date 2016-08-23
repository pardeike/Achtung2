using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using Verse;

namespace AchtungMod
{
	public class HookInjector
	{
		public struct PatchInfo
		{
			public Type SourceType;
			public Type TargetType;

			public MethodInfo SourceMethod;
			public MethodInfo TargetMethod;

			public int TargetSize;

			public IntPtr SourcePtr;
			public IntPtr TargetPtr;
		}

		private Logger _logger = new Logger { MessagePrefix = "HookInjector: ", Verbosity = Logger.Level.Error };
		//private Logger _logger = Globals.Logger;

		private List<PatchInfo> _patches = new List<PatchInfo>();

		private IntPtr _memPtr;
		private long _offset;

		private bool _isInitialized;

		public HookInjector()
		{
			_memPtr = Platform.AllocRWE();

			if (_memPtr == IntPtr.Zero)
			{
				_logger.Error("No memory allocated, injector disabled.");
				return;
			}

			// Patching has to be done after other mods (e.g. CCL) do it in order to handle rerouting
			LongEventHandler.QueueLongEvent(PatchAll, "HookInjector_PatchAll", false, null);
		}

		public void Inject(Type sourceType, string sourceName, Type targetType, string targetName = "")
		{
			MethodInfo sourceMethod;

			sourceMethod = sourceType.GetMethod(sourceName);
			if (sourceMethod == null) sourceMethod = sourceType.GetMethod(sourceName, BindingFlags.Static | BindingFlags.NonPublic);
			if (sourceMethod == null) sourceMethod = sourceType.GetMethod(sourceName, BindingFlags.Instance | BindingFlags.NonPublic);
			if (sourceMethod == null)
			{
				_logger.Error("Source method {0}.{1} not found", sourceType.Name, sourceName);
				return;
			}

			Inject(sourceType, sourceMethod, targetType, targetName);
		}

		public void Inject(Type sourceType, MethodInfo sourceMethod, Type targetType, string targetName = "")
		{
			if (targetName.Length < 1)
			{
				targetName = sourceType.Name + "_" + sourceMethod.Name;
			}

			MethodInfo targetMethod;

			targetMethod = targetType.GetMethod(targetName);
			if (targetMethod == null) targetMethod = targetType.GetMethod(targetName, BindingFlags.Static | BindingFlags.NonPublic);
			if (targetMethod == null) targetMethod = targetType.GetMethod(targetName, BindingFlags.Instance | BindingFlags.NonPublic);
			if (targetMethod == null)
			{
				_logger.Error("Target method {0}.{1} not found", targetType.Name, targetName);
				return;
			}

			Inject(sourceType, sourceMethod, targetType, targetMethod);
		}

		public void Inject(Type sourceType, MethodInfo sourceMethod, Type targetType, MethodInfo targetMethod)
		{
			var pi = new PatchInfo();

			pi.SourceType = sourceType;
			pi.TargetType = targetType;

			pi.SourceMethod = sourceMethod;
			pi.TargetMethod = targetMethod;

			pi.SourcePtr = pi.SourceMethod.MethodHandle.GetFunctionPointer();
			pi.TargetPtr = pi.TargetMethod.MethodHandle.GetFunctionPointer();

			pi.TargetSize = Platform.GetJitMethodSize(pi.TargetPtr);

			_patches.Add(pi);
			if (_isInitialized) Patch(pi);
		}

		void PatchAll()
		{
			foreach (var pi in _patches) Patch(pi);
			_isInitialized = true;
			_logger.Info("Processed {0} methods.", _patches.Count);
		}

		private bool Patch(PatchInfo pi)
		{
			var hookPtr = new IntPtr(_memPtr.ToInt64() + _offset);

			_logger.Debug("Patching via hook @ {0:X}:", hookPtr.ToInt64());
			_logger.Debug("    Source: {0}.{1} @ {2:X}", pi.SourceType.Name, pi.SourceMethod.Name, pi.SourcePtr.ToInt64());
			_logger.Debug("    Target: {0}.{1} @ {2:X}", pi.TargetType.Name, pi.TargetMethod.Name, pi.TargetPtr.ToInt64());

			var s = new AsmHelper(hookPtr);

			// Main proc
			s.WriteJmp(pi.TargetPtr);
			var mainPtr = s.ToIntPtr();

			var src = new AsmHelper(pi.SourcePtr);

			// Check if already patched
			var isAlreadyPatched = false;
			var jmpLoc = src.PeekJmp();
			if (jmpLoc != 0)
			{
				_logger.Debug("    Method already patched, rerouting.");
				pi.SourcePtr = new IntPtr(jmpLoc);
				isAlreadyPatched = true;
			}

			// Jump to detour if called from outside of detour
			var startAddress = pi.TargetPtr.ToInt64();
			var endAddress = startAddress + pi.TargetSize;

			s.WriteMovImmRax(startAddress);
			s.WriteCmpRaxRsp();
			s.WriteJl8(hookPtr);

			s.WriteMovImmRax(endAddress);
			s.WriteCmpRaxRsp();
			s.WriteJg8(hookPtr);

			if (isAlreadyPatched)
			{
				src.WriteJmp(mainPtr);
				s.WriteJmp(pi.SourcePtr);
			}
			else
			{
				// Copy source proc stack alloc instructions
				var stackAlloc = src.PeekStackAlloc();

				if (stackAlloc.Length < 5)
				{
					_logger.Debug("    Stack alloc too small to be patched, attempting full copy.");

					var size = (Platform.GetJitMethodSize(pi.SourcePtr));
					var bytes = new byte[size];
					Marshal.Copy(pi.SourcePtr, bytes, 0, size);
					s.Write(bytes);

					// Write jump to main proc in source proc
					src.WriteJmp(mainPtr);
				}
				else
				{
					s.Write(stackAlloc);
					s.WriteJmp(new IntPtr(pi.SourcePtr.ToInt64() + stackAlloc.Length));

					// Write jump to main proc in source proc
					if (stackAlloc.Length < 12) src.WriteJmpRel32(mainPtr);
					else src.WriteJmp(mainPtr);

					var srcOffset = (int)(src.ToInt64() - pi.SourcePtr.ToInt64());
					src.WriteNop(stackAlloc.Length - srcOffset);
				}
			}

			s.WriteLong(0);

			_offset = s.ToInt64() - _memPtr.ToInt64();

			_logger.Debug("    Successfully patched.");
			return true;
		}
	}
}
