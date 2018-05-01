using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using System.Reflection;
using System;
using Harmony;
using System.Linq;
using System.Reflection.Emit;

namespace AchtungMod
{
	[StaticConstructorOnStartup]
	public class AchtungLoader
	{
		public static bool IsSameSpotInstalled;

		static AchtungLoader()
		{
			Controller.GetInstance().InstallDefs();

			// HarmonyInstance.DEBUG = true;
			var harmony = HarmonyInstance.Create("net.pardeike.rimworld.mods.achtung");
			harmony.PatchAll(Assembly.GetExecutingAssembly());

			const string sameSpotId = "net.pardeike.rimworld.mod.samespot";
			IsSameSpotInstalled = harmony.GetPatchedMethods()
				.Any(method => harmony.GetPatchInfo(method).Transpilers.Any(transpiler => transpiler.owner == sameSpotId));
		}
	}

	public class Achtung : Mod
	{
		public static AchtungSettings Settings;

		public Achtung(ModContentPack content) : base(content)
		{
			Settings = GetSettings<AchtungSettings>();
		}

		public override void DoSettingsWindowContents(Rect inRect)
		{
			Settings.DoWindowContents(inRect);
		}

		public override string SettingsCategory()
		{
			return "Achtung!";
		}
	}

	// build-in "Ignore Me Passing" functionality
	//
	[HarmonyPatch(typeof(GenConstruct))]
	[HarmonyPatch("BlocksConstruction")]
	static class GenConstruct_BlocksConstruction_Patch
	{
		static bool Prefix(ref bool __result, Thing constructible, Thing t)
		{
			if (t is Pawn)
			{
				__result = false;
				return false;
			}
			return true;
		}
	}

	[HarmonyPatch(typeof(FloatMenuMakerMap))]
	[HarmonyPatch("AddUndraftedOrders")]
	static class FloatMenuMakerMap_AddUndraftedOrders_Patch
	{
		static IEnumerable<CodeInstruction> Transpiler(ILGenerator il, IEnumerable<CodeInstruction> instructions)
		{
			object lastLocalVar = null;
			var nextIsVar = false;

			var c_FloatMenuOption = AccessTools.FirstConstructor(typeof(FloatMenuOption), c => c.GetParameters().Count() > 1);
			var m_ForcedFloatMenuOption = AccessTools.Method(typeof(ForcedFloatMenuOption), nameof(ForcedFloatMenuOption.CreateForcedMenuItem));
			foreach (var instruction in instructions)
			{
				if (instruction.opcode == OpCodes.Isinst && instruction.operand == typeof(WorkGiver_Scanner))
				{
					yield return instruction;
					nextIsVar = true;
					continue;
				}
				if (nextIsVar)
				{
					lastLocalVar = instruction.operand;
					nextIsVar = false;
				}

				if (instruction.opcode == OpCodes.Newobj && instruction.operand == c_FloatMenuOption && lastLocalVar != null)
				{
					instruction.opcode = OpCodes.Call;
					instruction.operand = m_ForcedFloatMenuOption;
					yield return new CodeInstruction(OpCodes.Ldarg_1);
					yield return new CodeInstruction(OpCodes.Ldarg_0);
					yield return new CodeInstruction(OpCodes.Ldloc_S, lastLocalVar);
				}

				yield return instruction;
			}
		}
	}
	//
	[HarmonyPatch(typeof(Pawn_JobTracker))]
	[HarmonyPatch("EndJob")]
	static class Pawn_JobTracker_EndJob_Patch
	{
		static bool HandleForcedAndSkipQueue(Pawn_JobTracker tracker, Pawn pawn, JobCondition condition)
		{
			var forcedWork = Find.World.GetComponent<ForcedWork>();
			if (forcedWork.HasForcedJob(pawn))
			{
				tracker.EndCurrentJob(condition, true);
				return true;
			}
			return false;
		}

		static IEnumerable<CodeInstruction> Transpiler(ILGenerator il, IEnumerable<CodeInstruction> instructions)
		{
			var label = il.DefineLabel();

			yield return new CodeInstruction(OpCodes.Ldarg_0);
			yield return new CodeInstruction(OpCodes.Ldarg_0);
			yield return new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(Pawn_JobTracker), "pawn"));
			yield return new CodeInstruction(OpCodes.Ldarg_2);
			yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Pawn_JobTracker_EndJob_Patch), nameof(Pawn_JobTracker_EndJob_Patch.HandleForcedAndSkipQueue)));
			yield return new CodeInstruction(OpCodes.Brfalse, label);
			yield return new CodeInstruction(OpCodes.Ret);

			var instr = instructions.ToList();
			instr[0].labels.Add(label);
			foreach (var inst in instr)
				yield return inst;
		}
	}
	//
	[HarmonyPatch(typeof(Pawn_JobTracker))]
	[HarmonyPatch("EndCurrentJob")]
	static class Pawn_JobTracker_EndCurrentJob_Patch
	{
		static MethodInfo m_CleanupCurrentJob = AccessTools.Method(typeof(Pawn_JobTracker), "CleanupCurrentJob");
		static MethodInfo m_ContinueJob = AccessTools.Method(typeof(ForcedJob), "ContinueJob");
		static FieldInfo f_pawn = AccessTools.Field(typeof(Pawn_JobTracker), "pawn");

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var instrList = instructions.ToList();
			for (var i = 0; i < instrList.Count; i++)
			{
				var instruction = instrList[i];
				yield return instruction;

				if (instruction.operand != m_CleanupCurrentJob)
					continue;

				if (instrList[i + 1].opcode != OpCodes.Ldarg_2 || instrList[i + 2].opcode != OpCodes.Brfalse)
				{
					Log.Error("Unexpected opcodes while transpiling Pawn_JobTracker.EndCurrentJob");
					continue;
				}

				var endLabel = instrList[i + 2].operand;

				yield return instrList[++i];
				yield return instrList[++i];

				yield return new CodeInstruction(OpCodes.Ldarg_0);
				yield return new CodeInstruction(OpCodes.Ldloc_1);
				yield return new CodeInstruction(OpCodes.Ldarg_0);
				yield return new CodeInstruction(OpCodes.Ldfld, f_pawn);
				yield return new CodeInstruction(OpCodes.Ldarg_1);
				yield return new CodeInstruction(OpCodes.Call, m_ContinueJob);

				yield return new CodeInstruction(OpCodes.Brtrue, endLabel);
			}
		}
	}

	[HarmonyPatch(typeof(Pawn))]
	[HarmonyPatch("DeSpawn")]
	static class Pawn_DeSpawn_Patch
	{
		static void Postfix(Pawn __instance)
		{
			var forcedWork = Find.World.GetComponent<ForcedWork>();
			forcedWork.Remove(__instance);
		}
	}

	[HarmonyPatch(typeof(PriorityWork))]
	[HarmonyPatch("GetGizmos")]
	[StaticConstructorOnStartup]
	static class PriorityWork_GetGizmos_Patch
	{
		public static readonly Texture2D ForceRadiusExpand = ContentFinder<Texture2D>.Get("ForceRadiusExpand", true);
		public static readonly Texture2D ForceRadiusShrink = ContentFinder<Texture2D>.Get("ForceRadiusShrink", true);
		public static readonly Texture2D ForceRadiusShrinkOff = ContentFinder<Texture2D>.Get("ForceRadiusShrinkOff", true);

		static IEnumerable<Gizmo> AddForceGizmo(IEnumerable<Gizmo> gizmos, Pawn pawn)
		{
			var gizmoList = gizmos.ToList();
			foreach (var gizmo in gizmos)
				yield return gizmo;

			var forcedWork = Find.World.GetComponent<ForcedWork>();
			var forcedJob = forcedWork.GetForcedJob(pawn);
			if (forcedJob == null)
				yield break;

			var radius = forcedJob.cellRadius;

			yield return new Command_Action
			{
				defaultLabel = "IncreaseForceRadius".Translate(),
				defaultDesc = "IncreaseForceRadiusDesc".Translate(radius),
				icon = ForceRadiusExpand,
				activateSound = SoundDefOf.DesignateAreaAdd,
				action = delegate
				{
					forcedJob = forcedWork.GetForcedJob(pawn);
					forcedJob?.ChangeCellRadius(1);
				}
			};

			yield return new Command_Action
			{
				defaultLabel = "DecreaseForceRadius".Translate(),
				defaultDesc = "DecreaseForceRadiusDesc".Translate(radius),
				icon = radius > 0 ? ForceRadiusShrink : ForceRadiusShrinkOff,
				activateSound = radius > 0 ? SoundDefOf.DesignateAreaAdd : SoundDefOf.DesignateFailed,
				action = delegate
				{
					forcedJob = forcedWork.GetForcedJob(pawn);
					if (forcedJob != null && forcedJob.cellRadius > 0)
						forcedJob.ChangeCellRadius(-1);
				}
			};
		}

		static IEnumerable<CodeInstruction> Transpiler(ILGenerator generator, IEnumerable<CodeInstruction> instructions)
		{
			foreach (var instruction in instructions)
			{
				if (instruction.opcode == OpCodes.Ret)
				{
					yield return new CodeInstruction(OpCodes.Ldarg_0);
					yield return new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(PriorityWork), "pawn"));
					yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(PriorityWork_GetGizmos_Patch), "AddForceGizmo"));
				}
				yield return instruction;
			}
		}
	}

	// ignore think treee when building uninterrupted
	//
	[HarmonyPatch(typeof(Pawn_JobTracker))]
	[HarmonyPatch("ShouldStartJobFromThinkTree")]
	static class Pawn_JobTracker_ShouldStartJobFromThinkTree_Patch
	{
		static bool OverwriteResult(bool result, Pawn pawn)
		{
			var forcedWork = Find.World.GetComponent<ForcedWork>();
			if (result && forcedWork.HasForcedJob(pawn))
				result = false;
			return result;
		}

		static IEnumerable<CodeInstruction> Transpiler(ILGenerator generator, IEnumerable<CodeInstruction> instructions)
		{
			var endLabel = generator.DefineLabel();

			foreach (var instruction in instructions)
			{
				if (instruction.opcode == OpCodes.Ret)
				{
					instruction.opcode = OpCodes.Br;
					instruction.operand = endLabel;
				}
				yield return instruction;
			}

			yield return new CodeInstruction(OpCodes.Ldarg_0) { labels = new List<Label>() { endLabel } };
			yield return new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(Pawn_JobTracker), "pawn"));
			yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Pawn_JobTracker_ShouldStartJobFromThinkTree_Patch), "OverwriteResult"));
			yield return new CodeInstruction(OpCodes.Ret);
		}
	}

	// handle events early
	//
	[HarmonyPatch(typeof(MainTabsRoot))]
	[HarmonyPatch("HandleLowPriorityShortcuts")]
	static class MainTabsRoot_HandleLowPriorityShortcuts_Patch
	{
		static void Prefix()
		{
			Controller.GetInstance().HandleEvents();
		}
	}

	// handle drawing
	//
	[HarmonyPatch(typeof(SelectionDrawer))]
	[HarmonyPatch("DrawSelectionOverlays")]
	static class SelectionDrawer_DrawSelectionOverlays_Patch
	{
		static void Postfix()
		{
			Controller.GetInstance().HandleDrawing();
		}
	}

	// handle gui
	//
	[HarmonyPatch(typeof(ThingOverlays))]
	[HarmonyPatch("ThingOverlaysOnGUI")]
	static class ThingOverlays_ThingOverlaysOnGUI_Patch
	{
		static void Postfix()
		{
			Controller.GetInstance().HandleDrawingOnGUI();
		}
	}

	// turn reservation error into warning inject
	//
	[HarmonyPatch(typeof(ReservationManager))]
	[HarmonyPatch("LogCouldNotReserveError")]
	static class ReservationManager_LogCouldNotReserveError_Patch
	{
		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var fromMethod = AccessTools.Method(typeof(Log), "Error", new Type[] { typeof(string) });
			var toMethod = AccessTools.Method(typeof(Log), "Warning", new Type[] { typeof(string) });
			return instructions.MethodReplacer(fromMethod, toMethod);
		}
	}

	// pawn inspector panel
	//
	[HarmonyPatch(typeof(Pawn))]
	[HarmonyPatch("GetInspectString")]
	static class Pawn_GetInspectString_Patch
	{
		static void Postfix(Pawn __instance, ref string __result)
		{
			var forcedWork = Find.World.GetComponent<ForcedWork>();
			if (forcedWork.HasForcedJob(__instance))
				__result = __result + "\n" + "ForcedCommandState".Translate();
		}
	}

	// custom context menu
	//
	[HarmonyPatch(typeof(FloatMenuMakerMap))]
	[HarmonyPatch("ChoicesAtFor")]
	static class FloatMenuMakerMap_ChoicesAtFor_Patch
	{
		static void Postfix(List<FloatMenuOption> __result, Vector3 clickPos, Pawn pawn)
		{
			if (pawn != null && pawn.Drafted == false)
				__result.AddRange(Controller.GetInstance().AchtungChoicesAtFor(clickPos, pawn));
		}
	}
}