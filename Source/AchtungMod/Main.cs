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
			Controller.getInstance().InstallDefs();

			var harmony = HarmonyInstance.Create("net.pardeike.rimworld.mods.achtung");
			harmony.PatchAll(Assembly.GetExecutingAssembly());

			const string sameSpotId = "net.pardeike.rimworld.mod.samespot";
			IsSameSpotInstalled = harmony.GetPatchedMethods()
				.Any(method => harmony.IsPatched(method).Transpilers.Any(transpiler => transpiler.owner == sameSpotId));
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

	// Building uninterrupted
	//
	[HarmonyPatch(typeof(Pawn_JobTracker))]
	[HarmonyPatch("EndCurrentJob")]
	static class Pawn_JobTracker_EndCurrentJob_Patch
	{
		static MethodInfo m_CleanupCurrentJob = AccessTools.Method(typeof(Pawn_JobTracker), "CleanupCurrentJob");
		static MethodInfo m_ContinueJob = AccessTools.Method(typeof(Pawn_JobTracker_EndCurrentJob_Patch), "ContinueJob");
		static FieldInfo f_pawn = AccessTools.Field(typeof(Pawn_JobTracker), "pawn");

		static bool ContinueJob(Pawn_JobTracker tracker, Job lastJob, Pawn pawn, JobCondition condition)
		{
			if (tracker.FromColonist() == false) return false;
			var lastLord = lastJob.IsThoroughly();
			if (lastLord == null) return false;
			if (condition == JobCondition.InterruptForced) return false;

			pawn.ClearReservationsForJob(lastJob);

			if (Tools.GetPawnBreakLevel(pawn)())
			{
				pawn.Map.pawnDestinationReservationManager.ReleaseAllClaimedBy(pawn);
				var jobName = "WorkUninterrupted".Translate();
				var label = "JobInterruptedLabel".Translate(jobName);
				Find.LetterStack.ReceiveLetter(LetterMaker.MakeLetter(label, "JobInterruptedBreakdown".Translate(pawn.NameStringShort), LetterDefOf.NegativeEvent, pawn));
				return false;
			}

			if (Tools.GetPawnHealthLevel(pawn)())
			{
				pawn.Map.pawnDestinationReservationManager.ReleaseAllClaimedBy(pawn);
				var jobName = "WorkUninterrupted".Translate();
				Find.LetterStack.ReceiveLetter(LetterMaker.MakeLetter("JobInterruptedLabel".Translate(jobName), "JobInterruptedBadHealth".Translate(pawn.NameStringShort), LetterDefOf.NegativeEvent, pawn));
				return false;
			}

			var things = Tools.UpdateCells(lastLord.extraForbiddenThings, pawn, lastJob.targetB.Cell);
			foreach (var thing in things)
			{
				if (pawn.Position.IsInside(thing) && lastLord.extraForbiddenThings.Count > 1)
					continue;

				if (pawn.CanReserveAndReach(thing, PathEndMode.ClosestTouch, Danger.Deadly) == false)
					continue;

				WorkGiver_Scanner workGiverScanner = null;
				Job job = null;

				if (thing is Frame)
				{
					workGiverScanner = (WorkGiver_Scanner)Controller.WorkGiver_ConstructDeliverResourcesToFramesAllDef.Worker;
					job = workGiverScanner.JobOnThing(pawn, thing, false);

					if (job == null)
					{
						workGiverScanner = (WorkGiver_Scanner)Controller.WorkGiver_ConstructFinishFramesAllDef.Worker;
						job = workGiverScanner.JobOnThing(pawn, thing, false);
					}
				}

				if (thing is Blueprint)
				{
					workGiverScanner = (WorkGiver_Scanner)Controller.WorkGiver_ConstructDeliverResourcesToBlueprintsAllDef.Worker;
					job = workGiverScanner.JobOnThing(pawn, thing, false);
				}

				if (job != null)
				{
					job.lord = lastLord;
					job.expiryInterval = 0;
					job.ignoreJoyTimeAssignment = true;
					job.locomotionUrgency = LocomotionUrgency.Sprint;
					job.playerForced = true;
					tracker.StartJob(job, JobCondition.Succeeded, null, false, false, null, JobTag.MiscWork, false);
					return true;
				}
			}

			return false;
		}

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

	// ignore failures in reservations
	//
	[HarmonyPatch(typeof(ReservationUtility))]
	[HarmonyPatch("ReserveAsManyAsPossible")]
	static class ReservationUtility_ReserveAsManyAsPossible_Patch
	{
		static void Prefix(Pawn p, ref List<LocalTargetInfo> target, Job job)
		{
			var lord = job?.IsThoroughly();
			if (lord != null && p.Spawned)
				target = new List<LocalTargetInfo>();
		}
	}
	//
	[HarmonyPatch(typeof(ReservationManager))]
	[HarmonyPatch("Reserve")]
	static class ReservationManager_Reserve_Patch
	{
		static bool Prefix(ReservationManager __instance, ref bool __result, Pawn claimant, Job job, LocalTargetInfo target)
		{
			var lord = job?.IsThoroughly();
			if (lord != null && target.IsValid && target.ThingDestroyed == false)
			{
				if (__instance.ReservedBy(target, claimant, job))
				{
					__result = true;
					return false;
				}

				if (__instance.CanReserve(claimant, target) == false)
				{
					claimant.jobs.EndJob(job, JobCondition.Incompletable);
					__result = true;
					return false;
				}
			}
			return true;
		}
	}
	//
	[HarmonyPatch(typeof(Pawn_JobTracker))]
	[HarmonyPatch("TryTakeOrderedJobPrioritizedWork")]
	static class Pawn_JobTracker_TryTakeOrderedJobPrioritizedWork_Patch
	{
		static void Postfix(Pawn_JobTracker __instance, bool __result, Job job, WorkGiver giver, IntVec3 cell)
		{
			if (__result)
				return;

			var lord = job?.IsThoroughly();
			if (lord == null)
				return;


		}
	}

	// ignore think treee when building uninterrupted
	//
	[HarmonyPatch(typeof(Pawn_JobTracker))]
	[HarmonyPatch("ShouldStartJobFromThinkTree")]
	static class Pawn_JobTracker_ShouldStartJobFromThinkTree_Patch
	{
		static bool Prefix(Pawn_JobTracker __instance, ref bool __result)
		{
			if (__instance.curJob?.IsThoroughly() == null)
				return true;

			__result = false;
			return false;
		}
	}

	// better context menu labels for uninterrupted jobs
	//
	[HarmonyPatch(typeof(FloatMenuMakerMap))]
	[HarmonyPatch("AddUndraftedOrders")]
	static class FloatMenuMakerMap_AddUndraftedOrders_Patch
	{
		static MethodInfo m_GetLabelText = AccessTools.Method(typeof(FloatMenuMakerMap_AddUndraftedOrders_Patch), "GetLabelText");

		static string GetLabelText(WorkGiver workGiver, Job job, Thing thing)
		{
			if (job.lord is ThoroughlyLord)
				return "WorkUninterrupted".Translate();

			return "PrioritizeGeneric".Translate(new object[] { workGiver.def.gerund, thing.Label });
		}

		// ordersCAnonStorey15.label = "PrioritizeGeneric".Translate((object) workGiverScanner.def.gerund, (object) t.Label);
		// ordersCAnonStorey15.label = FloatMenuMakerMap_AddUndraftedOrders_Patch.GetLabelText(workGiverScanner, other, t);
		// ------------------------------------------------------------------------------------------------------------------

		// start

		// 00: IL_0538: ldloc.s ordersCAnonStorey15

		// 01: IL_053a: ldstr "PrioritizeGeneric"
		// 02: IL_053f: ldc.i4.2     
		// 03: IL_0540: newarr[mscorlib] System.Object

		// 04: IL_0545: dup
		// 05: IL_0546: ldc.i4.0     

		// 06: IL_0547: ldloc.s workGiverScanner

		// 07: IL_0549: ldfld WorkGiverDef WorkGiver::def

		// 08: IL_054e: ldfld WorkGiverDef::gerund
		// 09: IL_0553: stelem.ref
		// 10: IL_0554: dup
		// 11: IL_0555: ldc.i4.1     

		// 12: IL_0556: ldloc.s t

		// 13: IL_0558: callvirt Entity::get_Label()
		// 14: IL_055d: stelem.ref
		// 15: IL_055e: call Translator::Translate(string, object[])
		// 16: IL_0563: stfld FloatMenuMakerMap/'<AddUndraftedOrders>c__AnonStorey15'::label

		// end

		// 17: IL_0568: ldloc.s ordersCAnonStorey14
		// 18: IL_056a: ldloc.s other
		// 19: IL_056c: stfld FloatMenuMakerMap/'<AddUndraftedOrders>c__AnonStorey14'::localJob

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instr)
		{
			var instructions = instr.ToList();
			var start = instructions.FindIndex(ins => ((ins.operand as string) == "PrioritizeGeneric")) - 1;

			var end = -1;
			for (var i = start; start > 0 && i < instructions.Count(); i++)
				if (instructions[i].opcode == OpCodes.Stfld) { end = i + 1; break; }

			if (start < 0 || end < 0)
				Log.Error("Cannot find expected 'PrioritizeGeneric' section in AddUndraftedOrders (" + start + ", " + end + ")");
			else
			{
				var original = instructions.GetRange(start, end - start + 2); // +2 to get original[18]
				var extra = new List<CodeInstruction>()
				{
					original[0],  // ldloc.s ordersCAnonStorey15
					original[6],  // ldloc.s workGiverScanner
					original[18], // ldloc.s other
					original[12], // ldloc.s t
					new CodeInstruction(OpCodes.Call, m_GetLabelText),
					original[16]  // stfld FloatMenuMakerMap/'<AddUndraftedOrders>c__AnonStorey15'::label
				};

				var prefix = instructions.GetRange(0, start);
				var postfix = instructions.GetRange(end, instructions.Count - end);
				instructions = new List<CodeInstruction>();
				instructions.AddRange(prefix);
				instructions.AddRange(extra);
				instructions.AddRange(postfix);
			}

			foreach (var instruction in instructions)
				yield return instruction;
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
			Controller.getInstance().HandleEvents();
		}
	}

	// handle drawing
	//
	[HarmonyPatch(typeof(SelectionDrawer))]
	[HarmonyPatch("DrawSelectionOverlays")]
	static class SelectionDrawer_DrawSelectionOverlays_Patch
	{
		static void Prefix()
		{
			Controller.getInstance().HandleDrawing();
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
			Controller.getInstance().HandleDrawingOnGUI();
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

	// custom context menu
	//
	[HarmonyPatch(typeof(FloatMenuMakerMap))]
	[HarmonyPatch("ChoicesAtFor")]
	static class FloatMenuMakerMap_ChoicesAtFor_Patch
	{
		static void Postfix(List<FloatMenuOption> __result, Vector3 clickPos, Pawn pawn)
		{
			if (pawn != null && pawn.Drafted == false)
				__result.AddRange(Controller.getInstance().AchtungChoicesAtFor(clickPos, pawn));
		}
	}
}