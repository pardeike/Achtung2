﻿using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace AchtungMod
{
	public class ForcedWork : WorldComponent
	{
		Queue<KeyValuePair<Pawn, Job>> jobQueue = new Queue<KeyValuePair<Pawn, Job>>();

		Dictionary<Pawn, ForcedJobs> allForcedJobs = new Dictionary<Pawn, ForcedJobs>();
		private List<Pawn> forcedJobsKeysWorkingList;
		private List<ForcedJobs> forcedJobsValuesWorkingList;

		public ForcedWork(World world) : base(world)
		{
			jobQueue = new Queue<KeyValuePair<Pawn, Job>>();
		}

		private static List<WorkGiverDef> AllWorkerDefs<T>() where T : class
		{
			return DefDatabase<WorkGiverDef>.AllDefsListForReading
					.Where(def => def.IsOfType<T>()).ToList();
		}

		static HashSet<WorkGiverDef> constructionDefs = new HashSet<WorkGiverDef>(AllWorkerDefs<WorkGiver_ConstructFinishFrames>().Union(AllWorkerDefs<WorkGiver_ConstructDeliverResources>()));
		public List<WorkGiverDef> GetCombinedDefs(WorkGiver baseWorkgiver)
		{
			if (constructionDefs.Contains(baseWorkgiver.def))
				return constructionDefs.ToList();

			if (baseWorkgiver.IsOfType<WorkGiver_Warden>())
				return AllWorkerDefs<WorkGiver_Warden>();

			return new List<WorkGiverDef> { baseWorkgiver.def };
		}

		public void QueueJob(Pawn pawn, Job job)
		{
			// TODO: find out if we still get stackoverflows if we start jobs directly
			// tracker.StartJob(job, JobCondition.Succeeded, null/*pawn.mindState.lastJobGiver*/, false, false, null, null/*pawn.mindState.lastJobTag*/, true);

			jobQueue.Enqueue(new KeyValuePair<Pawn, Job>(pawn, job));
		}

		public override void WorldComponentTick()
		{
			if (jobQueue.Count > 0)
			{
				var item = jobQueue.Dequeue();
				// TODO: setting last lastJobGiver and lastJobTag lead to errors when loading a saved game
				item.Key.jobs.StartJob(item.Value, JobCondition.Succeeded, null/*pawn.mindState.lastJobGiver*/, false, false, null, null/*pawn.mindState.lastJobTag*/, true);
			}
		}

		public List<ForcedJob> GetForcedJobs(Pawn pawn)
		{
			if (pawn == null || allForcedJobs.TryGetValue(pawn, out var forcedJobs) == false)
				return new List<ForcedJob>();
			return forcedJobs.jobs;
		}

		public bool HasForcedJob(Pawn pawn)
		{
			if (pawn == null || allForcedJobs.TryGetValue(pawn, out var forcedJobs) == false)
				return false;
			return (forcedJobs.jobs.Count > 0);
		}

		public ForcedJob GetForcedJob(Pawn pawn)
		{
			if (pawn == null || allForcedJobs.TryGetValue(pawn, out var forcedJobs) == false)
				return null;
			if (forcedJobs.jobs.Count == 0)
				return null;
			return forcedJobs.jobs.First();
		}

		public void RemoveForcedJob(Pawn pawn)
		{
			if (pawn == null || allForcedJobs.TryGetValue(pawn, out var forcedJobs) == false)
				return;
			if (forcedJobs.jobs.Count == 0)
				return;
			forcedJobs.jobs.RemoveAt(0);
			if (forcedJobs.jobs.Count == 0)
				Remove(pawn);
		}

		public void Remove(Pawn pawn)
		{
			allForcedJobs.Remove(pawn);
		}

		public bool AddForcedJob(Pawn pawn, List<WorkGiverDef> workgiverDefs, LocalTargetInfo item)
		{
			var forcedJob = new ForcedJob() { pawn = pawn, workgiverDefs = workgiverDefs, isThingJob = item.HasThing };
			forcedJob.targets.Add(new ForcedTarget(item));
			forcedJob.UpdateCells();
			if (allForcedJobs.ContainsKey(pawn) == false)
				allForcedJobs[pawn] = new ForcedJobs();
			allForcedJobs[pawn].jobs.Add(forcedJob);
			return allForcedJobs[pawn].jobs.Count == 1;
		}

		public LocalTargetInfo HasJobItem(Pawn pawn, WorkGiver_Scanner workgiver, IntVec3 pos)
		{
			var radial = GenRadial.ManualRadialPattern;
			for (var i = 0; i < radial.Length; i++)
			{
				var cell = pos + radial[i];

				if (cell.GetCellJob(pawn, workgiver) != null)
					return new LocalTargetInfo(cell);
				var things = pawn.Map.thingGrid.ThingsAt(cell);
				foreach (var thing in things)
				{
					if (thing.GetThingJob(pawn, workgiver) != null)
						return new LocalTargetInfo(thing);
				}
			}
			return null;
		}

		public Job GetJobItem(Pawn pawn, WorkGiver_Scanner workgiver, LocalTargetInfo item)
		{
			if (item.HasThing)
				return item.Thing.GetThingJob(pawn, workgiver);
			return item.Cell.GetCellJob(pawn, workgiver);
		}

		public IEnumerable<ForcedJob> ForcedJobsForMap(Map map)
		{
			return allForcedJobs
				.Where(pair => pair.Key.Map == map)
				.SelectMany(pair => pair.Value.jobs);
		}

		public override void ExposeData()
		{
			Scribe_Collections.Look(ref allForcedJobs, "joblist", LookMode.Reference, LookMode.Deep, ref forcedJobsKeysWorkingList, ref forcedJobsValuesWorkingList);

			if (Scribe.mode == LoadSaveMode.PostLoadInit)
			{
				if (allForcedJobs == null)
					allForcedJobs = new Dictionary<Pawn, ForcedJobs>();
			}
		}
	}
}