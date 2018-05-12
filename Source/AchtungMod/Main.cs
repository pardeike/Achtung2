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

	// forced hauling outside of allowed area
	//
	[HarmonyPatch(typeof(HaulAIUtility))]
	[HarmonyPatch("PawnCanAutomaticallyHaulFast")]
	static class HaulAIUtility_PawnCanAutomaticallyHaulFast_Patch
	{
		static bool Prefix(Pawn p, Thing t, bool forced, ref bool __result)
		{
			var forcedWork = Find.World.GetComponent<ForcedWork>();
			if (forced || forcedWork.HasForcedJob(p))
			{
				__result = true;
				return false;
			}
			return true;
		}
	}

	// forced repair outside of allowed area
	//
	[HarmonyPatch(typeof(WorkGiver_Repair))]
	[HarmonyPatch("HasJobOnThing")]
	static class WorkGiver_Repair_HasJobOnThing_Patch
	{
		static IEnumerable<CodeInstruction> Transpiler(ILGenerator il, IEnumerable<CodeInstruction> instructions)
		{
			var instr = instructions.ToList();

			var f_NotInHomeAreaTrans = AccessTools.Field(typeof(WorkGiver_FixBrokenDownBuilding), "NotInHomeAreaTrans");
			var i = instr.FirstIndexOf(inst => inst.opcode == OpCodes.Ldsfld && inst.operand == f_NotInHomeAreaTrans);
			if (i > 0 && instr[i - 1].opcode == OpCodes.Brtrue)
			{
				var label = instr[i - 1].operand;
				instr.Insert(i++, new CodeInstruction(OpCodes.Ldarg_3));
				instr.Insert(i++, new CodeInstruction(OpCodes.Brtrue, label));
			}
			else
				Log.Error("Cannot find ldsfld RimWorld.WorkGiver_FixBrokenDownBuilding::NotInHomeAreaTrans");

			foreach (var inst in instr)
				yield return inst;
		}
	}

	[HarmonyPatch(typeof(ReservationManager))]
	[HarmonyPatch("CanReserve")]
	static class ReservationUtility_CanReserve_Patch
	{
		static bool Prefix(Pawn claimant, LocalTargetInfo target, bool ignoreOtherReservations, ref bool __result)
		{
			var forcedWork = Find.World.GetComponent<ForcedWork>();
			if (forcedWork.HasForcedJob(claimant))
			{
				__result = true;
				return false;
			}
			return true;
		}
	}

	[HarmonyPatch(typeof(ForbidUtility))]
	[HarmonyPatch("IsForbidden")]
	[HarmonyPatch(new Type[] { typeof(Thing), typeof(Pawn) })]
	static class ForbidUtility_IsForbidden_Patch
	{
		static bool Prefix(Thing t, Pawn pawn, ref bool __result)
		{
			var forcedWork = Find.World.GetComponent<ForcedWork>();
			if (forcedWork.HasForcedJob(pawn))
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
		static void Prefix(Pawn pawn, out ForcedWork __state)
		{
			__state = Find.World.GetComponent<ForcedWork>();
			__state.Prepare(pawn);
		}

		static void Postfix(Pawn pawn, ForcedWork __state)
		{
			__state.Unprepare(pawn);
		}

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
		static bool Prefix(Pawn_JobTracker __instance, Pawn ___pawn, JobCondition condition)
		{
			var forcedWork = Find.World.GetComponent<ForcedWork>();
			if (forcedWork.HasForcedJob(___pawn))
			{
				__instance.EndCurrentJob(condition, true);
				return false;
			}
			return true;
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

		static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> gizmos, Pawn ___pawn)
		{
			var gizmoList = gizmos.ToList();
			foreach (var gizmo in gizmos)
				yield return gizmo;

			var forcedWork = Find.World.GetComponent<ForcedWork>();
			var forcedJob = forcedWork.GetForcedJob(___pawn);
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
					forcedJob = forcedWork.GetForcedJob(___pawn);
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
					forcedJob = forcedWork.GetForcedJob(___pawn);
					if (forcedJob != null && forcedJob.cellRadius > 0)
						forcedJob.ChangeCellRadius(-1);
				}
			};
		}
	}

	// ignore think treee when building uninterrupted
	//
	[HarmonyPatch(typeof(Pawn_JobTracker))]
	[HarmonyPatch("ShouldStartJobFromThinkTree")]
	static class Pawn_JobTracker_ShouldStartJobFromThinkTree_Patch
	{
		static void Postfix(Pawn ___pawn, ref bool __result)
		{
			var forcedWork = Find.World.GetComponent<ForcedWork>();
			if (__result && forcedWork.HasForcedJob(___pawn))
				__result = false;
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