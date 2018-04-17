using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace AchtungMod
{
	public abstract class ForcedJob
	{
		static bool FindNextJob(Pawn pawn, List<IntVec3> cells, ForcedSettings settings, JobFunctions jobFuncs, out Job job, out IntVec3 atCell)
		{
			job = null;
			atCell = IntVec3.Invalid;

			foreach (var cell in cells)
			{
				var workGiver = settings.WorkGiver;

				var things = pawn.Map.thingGrid.ThingsAt(cell);
				foreach (var thing in things)
				{
					Tools.Debug(pawn, "Testing " + thing + " at " + cell + " (" + pawn.Position.DistanceTo(cell) + ")");

					if (pawn.Position.IsInside(thing) && cells.Count > 1)
						continue;
					if (pawn.CanReserveAndReach(thing, workGiver.PathEndMode, Danger.Deadly) == false)
						continue;
					job = jobFuncs.jobOnThingFunc(workGiver, pawn, thing, false);
				}

				if (job == null)
				{
					Tools.Debug(pawn, "Testing " + cell + " (" + pawn.Position.DistanceTo(cell) + ")");

					if (pawn.CanReach(cell, workGiver.PathEndMode, Danger.Deadly))
						job = jobFuncs.jobOnCellFunc(workGiver, pawn, cell, false);
				}

				if (job != null)
				{
					atCell = cell;
					break;
				}
			}

			return job != null;
		}

		static JobFunctions GetJobFunctions(ForcedWork forcedWork, ForcedSettings settings)
		{
			var workGiver = settings.WorkGiver;
			if (workGiver == null) workGiver = ForcedConstructionWorkGiver.Instance;

			var jobFunctions = forcedWork.AllJobFunctions;
			var result = jobFunctions[ForcedConstructionWorkGiver.Instance];
			if (jobFunctions.TryGetValue(workGiver, out var functions))
				result = functions;

			return result;
		}

		public static bool ContinueJob(Pawn_JobTracker tracker, Job lastJob, Pawn pawn, JobCondition condition)
		{
			if (tracker.FromColonist() == false) return false;

			var forcedWork = Find.World.GetComponent<ForcedWork>();
			var settings = forcedWork.Settings(lastJob);
			if (settings == null) return false;

			var lastCell = settings.target;

			Tools.Debug(pawn, "- " + pawn.NameStringShort + " ended forced job " + lastJob.def + " (" + condition + ")");

			if (condition == JobCondition.InterruptForced)
			{
				forcedWork.RemoveSettings(pawn, lastJob, "#### " + pawn.NameStringShort + " stopped forced job because it was interrupted");
				return false;
			}

			pawn.ClearReservationsForJob(lastJob);

			if (Tools.GetPawnBreakLevel(pawn)())
			{
				pawn.Map.pawnDestinationReservationManager.ReleaseAllClaimedBy(pawn);
				var jobName = "WorkUninterrupted".Translate();
				var label = "JobInterruptedLabel".Translate(jobName);
				Find.LetterStack.ReceiveLetter(LetterMaker.MakeLetter(label, "JobInterruptedBreakdown".Translate(pawn.NameStringShort), LetterDefOf.NegativeEvent, pawn));

				forcedWork.RemoveSettings(pawn, lastJob, "#### " + pawn.NameStringShort + " stopped forced job because of break level");
				return false;
			}

			if (Tools.GetPawnHealthLevel(pawn)())
			{
				pawn.Map.pawnDestinationReservationManager.ReleaseAllClaimedBy(pawn);
				var jobName = "WorkUninterrupted".Translate();
				Find.LetterStack.ReceiveLetter(LetterMaker.MakeLetter("JobInterruptedLabel".Translate(jobName), "JobInterruptedBadHealth".Translate(pawn.NameStringShort), LetterDefOf.NegativeEvent, pawn));

				forcedWork.RemoveSettings(pawn, lastJob, "#### " + pawn.NameStringShort + " stopped forced job because of health level");
				return false;
			}

			var jobFunctions = GetJobFunctions(forcedWork, settings);
			var cells = Tools.UpdateCells(lastJob, settings.target, jobFunctions);
			Tools.Debug(pawn, "Finding next cell for " + pawn.NameStringShort + " at " + pawn.Position + " (" + cells.Count + ")");

			if (FindNextJob(pawn, cells, settings, jobFunctions, out var job, out var cell))
			{
				job.expiryInterval = 0;
				job.ignoreJoyTimeAssignment = true;
				job.locomotionUrgency = LocomotionUrgency.Sprint;
				job.playerForced = true;
				forcedWork.NextJob(lastJob, job, cell);
				forcedWork.AddJob(pawn, job);
				// tracker.StartJob(job, JobCondition.Succeeded, null/*pawn.mindState.lastJobGiver*/, false, false, null, null/*pawn.mindState.lastJobTag*/, true);
				Tools.Debug(pawn, "# " + pawn.NameStringShort + " got new forced job " + job.def + " on " + cell);
				return true;
			}

			forcedWork.RemoveSettings(pawn, lastJob, "#### " + pawn.NameStringShort + " back to normal jobs (last thing #" + cells.Count + ")");
			return false;
		}

		public static void AddToJobMenu(List<FloatMenuOption> options, Vector3 clickPos, Pawn pawn)
		{
			var forcedWork = Find.World.GetComponent<ForcedWork>();
			var jobFunctions = forcedWork.AllJobFunctions.Values;

			var clickCell = IntVec3.FromVector3(clickPos);

			foreach (var thing in pawn.Map.thingGrid.ThingsAt(clickCell))
				foreach (var jobFunction in jobFunctions)
				{
					var workGiver = jobFunction.WorkGiver;
					if (jobFunction.hasJobOnThingFunc(workGiver, pawn, thing))
					{
						Tools.Debug(pawn, "Possible thing-job with " + workGiver);

						var label = jobFunction.menuLabelFunc(thing);
						options.Add(new FloatMenuOption(label, () =>
						{
							var job = jobFunction.jobOnThingFunc(workGiver, pawn, thing, false);
							if (job != null)
							{
								Tools.Debug(pawn, "# " + pawn.NameStringShort + " got new forced job " + job.def + " with " + workGiver + " on " + thing);
								forcedWork.CreateSettings(pawn, workGiver, job, clickCell);
								forcedWork.Settings(job).AddCell(clickCell);
								// var cells = Tools.UpdateCells(job, clickCell, jobFunction.hasJobOnThingFunc, jobFunction.hasJobOnCellFunc, jobFunction.thingScoreFunc);
								//Tools.Debug(pawn, "Adding " + cells.Count + " things to job");
								//forcedWork.Settings(job).AddCells(cells);
								pawn.jobs.TryTakeOrderedJob(job);
							}
						}, MenuOptionPriority.Low));
					}
				}
			foreach (var jobFunction in jobFunctions)
			{
				var workGiver = jobFunction.WorkGiver;
				if (jobFunction.hasJobOnCellFunc(workGiver, pawn, clickCell))
				{
					Tools.Debug(pawn, "Possible cell-job with " + workGiver);

					var label = jobFunction.menuLabelFunc(null);
					options.Add(new FloatMenuOption(label, () =>
					{
						var job = jobFunction.jobOnCellFunc(workGiver, pawn, clickCell, false);
						if (job != null)
						{
							Tools.Debug(pawn, "# " + pawn.NameStringShort + " got new forced job " + job.def + " with " + workGiver + " on " + clickCell);
							forcedWork.CreateSettings(pawn, workGiver, job, clickCell);
							forcedWork.Settings(job).AddCell(clickCell);
							//var cells = Tools.UpdateCells(job, clickCell, jobFunction.hasJobOnThingFunc, jobFunction.hasJobOnCellFunc, jobFunction.thingScoreFunc);
							//Tools.Debug(pawn, "Adding " + cells.Count + " things to job");
							//forcedWork.Settings(job).AddCells(cells);
							pawn.jobs.TryTakeOrderedJob(job);
						}
					}, MenuOptionPriority.Low));
				}
			}
		}

		internal abstract Type GetWorkGiverType();

		WorkGiver_Scanner cachedWorkGiver = null;
		internal virtual WorkGiver_Scanner GetWorkGiver()
		{
			if (cachedWorkGiver == null)
			{
				cachedWorkGiver = DefDatabase<WorkGiverDef>.AllDefsListForReading
					.Select(def => def.Worker)
					.OfType<WorkGiver_Scanner>()
					.FirstOrDefault(workGiver => GetWorkGiverType() == workGiver.GetType());
			}
			return cachedWorkGiver;
		}

		internal virtual int ThingScore(LocalTargetInfo thing, IntVec3 closeToCell)
		{
			return thing.Cell.DistanceToSquared(closeToCell);
		}

		internal virtual bool HasJobOnCell(WorkGiver_Scanner workGiver, Pawn pawn, IntVec3 cell)
		{
			return workGiver.HasJobOnCell(pawn, cell);
		}

		internal virtual Job JobOnCell(WorkGiver_Scanner workGiver, Pawn pawn, IntVec3 cell, bool forced)
		{
			if (workGiver == null)
				return null;
			if (workGiver.HasJobOnCell(pawn, cell) == false)
				return null;
			return workGiver.JobOnCell(pawn, cell);
		}

		internal virtual bool HasJobOnThing(WorkGiver_Scanner workGiver, Pawn pawn, LocalTargetInfo thing)
		{
			return workGiver.HasJobOnThing(pawn, thing.Thing, false);
		}

		internal virtual Job JobOnThing(WorkGiver_Scanner workGiver, Pawn pawn, LocalTargetInfo thing, bool forced)
		{
			if (workGiver == null)
				return null;
			if (workGiver.HasJobOnThing(pawn, thing.Thing, forced) == false)
				return null;
			return workGiver.JobOnThing(pawn, thing.Thing, forced);
		}

		internal virtual string MenuLabel(LocalTargetInfo thing)
		{
			var workGiver = GetWorkGiver();
			if (workGiver == null) return "";
			if (thing == null || thing.Thing == null)
				return "ForcedGeneric1".Translate(new object[] { workGiver.def.gerund });
			return "ForcedGeneric2".Translate(new object[] { workGiver.def.gerund, thing.Thing.Label });
		}
	}
}