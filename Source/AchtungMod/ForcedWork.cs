using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace AchtungMod
{
	public class ForcedWork : WorldComponent
	{
		Queue<KeyValuePair<Pawn, Job>> JobQueue = new Queue<KeyValuePair<Pawn, Job>>();

		Dictionary<Job, ForcedSettings> forcedJobs = new Dictionary<Job, ForcedSettings>();
		private List<Job> forcedJobsKeysWorkingList;
		private List<ForcedSettings> forcedJobsValuesWorkingList;

		public Dictionary<WorkGiver, JobFunctions> AllJobFunctions = new Dictionary<WorkGiver, JobFunctions>(); // not saved

		public ForcedWork(World world) : base(world) { }

		public override void FinalizeInit()
		{
			AllJobFunctions = new Dictionary<WorkGiver, JobFunctions>();
			typeof(ForcedWork).Assembly.GetTypes()
				.Where(type => (type.IsAbstract == false && typeof(ForcedJob).IsAssignableFrom(type)))
				.Select(type => new JobFunctions(type))
				.ToList().Do(jobFunction =>
				{
					var workGiver = jobFunction.WorkGiver;
					if (workGiver != null)
					{
						Tools.Debug("Registering forced job handling for " + workGiver.GetType().Name);
						AllJobFunctions[jobFunction.WorkGiver] = jobFunction;
					}
				});
		}

		public void AddJob(Pawn pawn, Job job)
		{
			JobQueue.Enqueue(new KeyValuePair<Pawn, Job>(pawn, job));
		}

		public override void WorldComponentTick()
		{
			if (JobQueue.Count > 0)
			{
				var item = JobQueue.Dequeue();
				item.Key.jobs.StartJob(item.Value, JobCondition.Succeeded, null/*pawn.mindState.lastJobGiver*/, false, false, null, null/*pawn.mindState.lastJobTag*/, true);
			}
		}

		public ForcedSettings Settings(Job job)
		{
			if (job == null || forcedJobs.TryGetValue(job, out var settings) == false)
				return null;
			return settings;
		}

		public ForcedSettings CreateSettings(Pawn pawn, WorkGiver_Scanner workGiver, Job job, IntVec3 target)
		{
			var settings = new ForcedSettings(pawn, workGiver, job, target);
			forcedJobs[job] = settings;
			return settings;
		}

		internal void NextJob(Job lastJob, Job job, IntVec3 target)
		{
			var oldSettings = forcedJobs[lastJob];
			var pawn = oldSettings.pawn;
			var workGiver = oldSettings.WorkGiver;
			var newSettings = CreateSettings(pawn, workGiver, job, target);
			newSettings.AddCells(oldSettings.Cells);
			RemoveSettings(pawn, lastJob, "Moving settings from " + lastJob + " to " + job);
		}

		public static Dictionary<Pawn, List<ForcedSettings>> SettingsForMap(Map map)
		{
			var result = new Dictionary<Pawn, List<ForcedSettings>>();
			var forcedWork = Find.World.GetComponent<ForcedWork>();
			var settings = Find.VisibleMap.mapPawns.AllPawnsSpawned
				.Where(pawn => pawn.IsColonist)
				.Select(pawn =>
				{
					var settingsList = pawn.jobs.jobQueue.Select(item => forcedWork.Settings(item.job))
						.Union(new List<ForcedSettings>() { forcedWork.Settings(pawn.CurJob) })
						.Where(setting => setting != null)
						.ToList();
					return new KeyValuePair<Pawn, List<ForcedSettings>>(pawn, settingsList);
				})
				.Do(item => { result[item.Key] = item.Value; });
			return result;
		}

		public static ForcedSettings GetSettings(Job job)
		{
			if (job == null) return null;
			return Find.World.GetComponent<ForcedWork>().Settings(job);
		}

		public void RemoveSettings(Pawn pawn, Job job, string reason)
		{
			Tools.Debug(pawn, reason);
			forcedJobs.Remove(job);
		}

		public override void ExposeData()
		{
			Scribe_Collections.Look(ref forcedJobs, "ForcedJobs", LookMode.Reference, LookMode.Deep, ref forcedJobsKeysWorkingList, ref forcedJobsValuesWorkingList);
		}
	}

	//

	public class ForcedSettings : IExposable
	{
		public Pawn pawn;
		public IntVec3 target;
		List<IntVec3> cells = new List<IntVec3>();
		WorkGiverDef workGiverDef = null;
		JobDef jobDef = null;

		public ForcedSettings()
		{
			cells = new List<IntVec3>();
		}

		public ForcedSettings(Pawn pawn, WorkGiver workGiver, Job job, IntVec3 target)
		{
			this.pawn = pawn;
			this.target = target;
			workGiverDef = workGiver.def;
			jobDef = job.def;
			cells = new List<IntVec3>();
		}

		public void ExposeData()
		{
			Scribe_References.Look(ref pawn, "Pawn", false);
			Scribe_Values.Look(ref target, "Target");
			Scribe_Collections.Look(ref cells, "Cells", LookMode.Value);
			Scribe_Defs.Look(ref workGiverDef, "WorkGiver");
			Scribe_Defs.Look(ref jobDef, "Job");

			/*if (Scribe.mode == LoadSaveMode.PostLoadInit)
			{
				var forcedWork = Find.World.GetComponent<ForcedWork>();
				var jobFunctions = forcedWork.JobFunctions.Values;
				var jobFunction = jobFunctions.FirstOrDefault(jf => jf.WorkGiver.def == workGiverDef);
				var workGiver = jobFunction.WorkGiver;
				if (jobFunction != null)
				{
					if (jobFunction.hasJobOnThingFunc(workGiver, pawn, target))
					{
						Tools.Debug(pawn, "Restoring thing-job with " + workGiver);

						var job = jobFunction.jobOnThingFunc(workGiver, pawn, target, false);
						if (job != null)
						{
							Tools.Debug(pawn, "# " + pawn.NameStringShort + " got new forced job " + job.def + " with " + workGiver + " on " + thing);
							forcedWork.CreateSettings(pawn, workGiver, job, target);
							forcedWork.Settings(job).AddThing(target);
							var things = Tools.UpdateCells(job, target.Cell, jobFunction.hasJobOnThingFunc, jobFunction.hasJobOnCellFunc, jobFunction.thingScoreFunc);
							Tools.Debug(pawn, "Adding " + things.Count + " things to job");
							forcedWork.Settings(job).AddThings(things);
							pawn.jobs.TryTakeOrderedJob(job);
						}
					}
					else if (jobFunction.hasJobOnCellFunc(workGiver, pawn, target.Cell))
					{
						Tools.Debug(pawn, "Restoring cell-job with " + workGiver);

						var job = jobFunction.jobOnCellFunc(workGiver, pawn, target.Cell, false);
						if (job != null)
						{
							Tools.Debug(pawn, "# " + pawn.NameStringShort + " got new forced job " + job.def + " with " + workGiver + " on " + target.Cell);
							forcedWork.CreateSettings(pawn, workGiver, job, target);
							forcedWork.Settings(job).AddCell(target.Cell);
							var things = Tools.UpdateCells(job, target.Cell, jobFunction.hasJobOnThingFunc, jobFunction.hasJobOnCellFunc, jobFunction.thingScoreFunc);
							Tools.Debug(pawn, "Adding " + things.Count + " things to job");
							forcedWork.Settings(job).AddThings(things);
							pawn.jobs.TryTakeOrderedJob(job);
						}
					}
				}
			}*/
		}

		public WorkGiver_Scanner WorkGiver => workGiverDef?.Worker as WorkGiver_Scanner;
		public List<IntVec3> Cells => cells;

		public void AddCell(IntVec3 cell)
		{
			if (cells.Contains(cell) == false)
				cells.Add(cell);
		}

		public void AddCells(IEnumerable<IntVec3> newCells)
		{
			cells = cells.Union(newCells).ToList();
		}

		public void RestrictCells(IEnumerable<IntVec3> onlyCells)
		{
			cells = cells.Intersect(onlyCells).ToList();
		}

		public void RemoveCell(IntVec3 oldCell)
		{
			cells.Remove(oldCell);
		}

		public void RemoveCells(IEnumerable<IntVec3> oldCells)
		{
			cells = cells.Except(oldCells).ToList();
		}

		public string Description()
		{
			return pawn.NameStringShort + "-" + workGiverDef + "." + jobDef + "#" + cells.Count;
		}
	}
}