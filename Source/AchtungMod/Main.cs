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

				var job = ForceConstruction.JobOnThing(pawn, thing, false);
				if (job != null)
				{
					job.lord = lastLord;
					job.expiryInterval = 0;
					job.ignoreJoyTimeAssignment = true;
					job.locomotionUrgency = LocomotionUrgency.Sprint;
					job.playerForced = true;
					tracker.TryTakeOrderedJob(job);
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
		static void Prefix()
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