using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace AchtungMod
{
	public class ForcedWork : WorldComponent
	{
		Dictionary<Pawn, ForcedJobs> allForcedJobs = new Dictionary<Pawn, ForcedJobs>();
		private List<Pawn> forcedJobsKeysWorkingList;
		private List<ForcedJobs> forcedJobsValuesWorkingList;

		readonly HashSet<Pawn> preparing = new HashSet<Pawn>();
		readonly Dictionary<Pawn, HashSet<IntVec3>> forbiddenLocations = new Dictionary<Pawn, HashSet<IntVec3>>();

		public ForcedWork(World world) : base(world)
		{
		}

		private static List<WorkGiverDef> AllWorkerDefs<T>() where T : class
		{
			return DefDatabase<WorkGiverDef>.AllDefsListForReading
					.Where(def => def.IsOfType<T>()).ToList();
		}

		static readonly List<WorkGiverDef> constructionDefs = AllWorkerDefs<WorkGiver_ConstructDeliverResources>().Concat(AllWorkerDefs<WorkGiver_ConstructFinishFrames>()).ToList();
		public static List<WorkGiverDef> GetCombinedDefs(WorkGiver baseWorkgiver)
		{
			if (constructionDefs.Contains(baseWorkgiver.def))
				return constructionDefs.ToList();

			if (baseWorkgiver.IsOfType<WorkGiver_Warden>())
				return AllWorkerDefs<WorkGiver_Warden>();

			return new List<WorkGiverDef> { baseWorkgiver.def };
		}

		public static void QueueJob(Pawn pawn, Job job)
		{
			var tracker = pawn.jobs;
			tracker?.StartJob(job, JobCondition.Succeeded, null, false, false, null, null, true);
		}

		public List<ForcedJob> GetForcedJobs(Pawn pawn)
		{
			if (pawn == null || allForcedJobs.TryGetValue(pawn, out var forcedJobs) == false)
				return new List<ForcedJob>();
			return forcedJobs.jobs;
		}

		public void Prepare(Pawn pawn)
		{
			_ = preparing.Add(pawn);
		}

		public void Unprepare(Pawn pawn)
		{
			_ = preparing.Remove(pawn);
		}

		public bool HasForcedJob(Pawn pawn)
		{
			if (pawn == null)
				return false;

			if (preparing.Contains(pawn))
				return true;

			if (allForcedJobs.TryGetValue(pawn, out var forcedJobs) == false)
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
			Unprepare(pawn);

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
			_ = allForcedJobs.Remove(pawn);
		}

		public bool AddForcedJob(Pawn pawn, List<WorkGiverDef> workgiverDefs, LocalTargetInfo item)
		{
			Unprepare(pawn);

			var forcedJob = new ForcedJob() { pawn = pawn, workgiverDefs = workgiverDefs, isThingJob = item.HasThing };
			forcedJob.AddTarget(item);
			if (allForcedJobs.ContainsKey(pawn) == false)
				allForcedJobs[pawn] = new ForcedJobs();
			allForcedJobs[pawn].jobs.Add(forcedJob);
			return allForcedJobs[pawn].jobs.Count == 1;
		}

		public static LocalTargetInfo HasJobItem(Pawn pawn, WorkGiver_Scanner workgiver, IntVec3 pos)
		{
			var radial = GenRadial.ManualRadialPattern;
			for (var i = 0; i < radial.Length; i++)
			{
				var cell = pos + radial[i];

				if (cell.GetCellJob(pawn, workgiver, true) != null)
					return new LocalTargetInfo(cell);
				var things = pawn.Map.thingGrid.ThingsAt(cell);
				foreach (var thing in things)
				{
					if (thing.GetThingJob(pawn, workgiver, true) != null)
						return new LocalTargetInfo(thing);
				}
			}
			return null;
		}

		public static Job GetJobItem(Pawn pawn, WorkGiver_Scanner workgiver, LocalTargetInfo item)
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

		public bool IsForbiddenCell(Map map, IntVec3 cell)
		{
			var jobs = allForcedJobs.Values.SelectMany(forcedJobs => forcedJobs.jobs);
			return jobs.Any(job => job.IsForbiddenCell(map, cell));
		}

		public void AddForbiddenLocation(Pawn pawn, IntVec3 cell)
		{
			if (forbiddenLocations.TryGetValue(pawn, out var cells) == false)
			{
				cells = new HashSet<IntVec3>();
				forbiddenLocations.Add(pawn, cells);
			}
			_ = cells.Add(cell);
		}

		public void RemoveForbiddenLocations(Pawn pawn)
		{
			_ = forbiddenLocations.Remove(pawn);
		}

		public HashSet<IntVec3> GetForbiddenLocations()
		{
			var result = new HashSet<IntVec3>();
			forbiddenLocations.Do(pair => result.UnionWith(pair.Value));
			return result;
		}

		public bool IsForbiddenLocation(IntVec3 cell)
		{
			return forbiddenLocations
				.Any(pair => pair.Value.Contains(cell));
		}

		public override void ExposeData()
		{
			if (Scribe.mode == LoadSaveMode.Saving)
			{
				allForcedJobs
					.Where(pair => pair.Value.jobs.Count == 0)
					.Select(pair => pair.Key)
					.Do(pawn => Remove(pawn));
			}

			Scribe_Collections.Look(ref allForcedJobs, "joblist", LookMode.Reference, LookMode.Deep, ref forcedJobsKeysWorkingList, ref forcedJobsValuesWorkingList);

			if (Scribe.mode == LoadSaveMode.PostLoadInit)
			{
				if (allForcedJobs == null)
					allForcedJobs = new Dictionary<Pawn, ForcedJobs>();

				allForcedJobs
					.Where(pair => pair.Value.jobs.Count == 0)
					.Select(pair => pair.Key)
					.Do(pawn => Remove(pawn));
			}
		}
	}
}