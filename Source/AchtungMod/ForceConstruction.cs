using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using UnityEngine;
using RimWorld;
using Verse.AI.Group;
using System.Linq;

namespace AchtungMod
{
	public class ThoroughlyLord : Lord
	{
		public ThoroughlyLord()
		{
			loadID = Find.UniqueIDsManager.GetNextLordID();
			extraForbiddenThings = new List<Thing>();
		}
	}

	public class ForceConstruction
	{
		static HashSet<Type> workGiverTypes = new HashSet<Type>()
		{
				typeof(WorkGiver_ConstructDeliverResourcesToBlueprints),
				typeof(WorkGiver_ConstructDeliverResourcesToFrames),
				typeof(WorkGiver_ConstructFinishFrames)
		};

		static IEnumerable<WorkGiver_Scanner> GetWorkGivers(Pawn pawn)
		{
			return DefDatabase<WorkTypeDef>.AllDefsListForReading
				.SelectMany(def => def.workGiversByPriority)
				.Select(workGiverDef => workGiverDef.Worker)
				.OfType<WorkGiver_Scanner>()
				.Where(workGiver => workGiverTypes.Contains(workGiver?.GetType()) && workGiver.def.directOrderable && !workGiver.ShouldSkip(pawn));
		}

		public static Job JobOnThing(Pawn pawn, Thing thing, bool forced)
		{
			foreach (var workGiver in GetWorkGivers(pawn))
			{
				var disabled1 = pawn.story?.WorkTagIsDisabled(workGiver.def.workTags) ?? false;
				var disabled2 = pawn.story?.WorkTypeIsDisabled(workGiver.def.workType) ?? false;
				if (disabled1 == false && disabled2 == false)
				{
					if (thing.IsForbidden(pawn) == false && pawn.CanReach(thing, workGiver.PathEndMode, Danger.Deadly, false, TraverseMode.ByPawn))
					{
						var job = workGiver.JobOnThing(pawn, thing, forced);
						if (job != null)
						{
							job.lord = new ThoroughlyLord();
							job.lord.extraForbiddenThings.Add(thing);
							job.lord.extraForbiddenThings = Tools.UpdateCells(job.lord.extraForbiddenThings, pawn, thing.Position);
							return job;
						}
					}
				}
			}
			return null;
		}

		public static void AddForcedBuild(List<FloatMenuOption> options, Vector3 clickPos, Pawn pawn)
		{
			var clickCell = IntVec3.FromVector3(clickPos);
			var driver = (JobDriver_Thoroughly)Activator.CreateInstance(typeof(JobDriver_SowAll));
			foreach (var thing in pawn.Map.thingGrid.ThingsAt(clickCell))
			{
				var job = JobOnThing(pawn, thing, true);
				if (job != null)
					options.Add(new FloatMenuOption("WorkUninterrupted".Translate(), () => pawn.jobs.TryTakeOrderedJob(job), MenuOptionPriority.Low));
			}
		}
	}
}