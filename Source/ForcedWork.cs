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
		Dictionary<Pawn, ForcedJobs> allForcedJobs = new Dictionary<Pawn, ForcedJobs>();
		private List<Pawn> forcedJobsKeysWorkingList;
		private List<ForcedJobs> forcedJobsValuesWorkingList;
		private int counter;

		readonly HashSet<Pawn> preparing = new HashSet<Pawn>();
		//readonly Dictionary<Pawn, HashSet<IntVec3>> forbiddenLocations = new Dictionary<Pawn, HashSet<IntVec3>>();

		public ForcedWork(World world) : base(world) { }

		static ForcedWork instance = null;
		public static ForcedWork Instance
		{
			get
			{
				if (instance == null)
					instance = Find.World.GetComponent<ForcedWork>();
				return instance;
			}
			set
			{
				instance = value;
			}
		}

		private static List<WorkGiverDef> AllWorkerDefs<T>() where T : class
		{
			try
			{
				return DefDatabase<WorkGiverDef>.AllDefsListForReading
					.Where(def => def.IsOfType<T>()).ToList();
			}
			catch (Exception ex)
			{
				Log.Error($"Achtung cannot fetch a list of WorkGiverDefs for {typeof(T).FullName}: {ex}");
				return new List<WorkGiverDef>();
			}
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
			return ForcedJobsForMap(map)
				.Any(job => job.IsForbiddenCell(cell));
		}

		/*public void AddForbiddenLocation(Pawn pawn, IntVec3 cell)
		{
			if (forbiddenLocations.TryGetValue(pawn, out var cells) == false)
			{
				cells = new HashSet<IntVec3>();
				forbiddenLocations.Add(pawn, cells);
			}
			_ = cells.Add(cell);
		}*/

		/*public void RemoveForbiddenLocations(Pawn pawn)
		{
			_ = forbiddenLocations.Remove(pawn);
		}*/

		/*public HashSet<IntVec3> GetForbiddenLocations()
		{
			var result = new HashSet<IntVec3>();
			forbiddenLocations.Do(pair => result.UnionWith(pair.Value));
			return result;
		}*/

		/*public bool IsForbiddenLocation(IntVec3 cell)
		{
			return forbiddenLocations
				.Any(pair => pair.Value.Contains(cell));
		}*/

		public override void WorldComponentTick()
		{
			if (Find.TickManager.TicksGame % 139 != 0) return;
			var pawns = allForcedJobs.Keys.ToArray();
			var n = pawns.Length;
			if (n == 0) return;
			var pawn = pawns[++counter % n];
			var map = pawn.Map;
			if (map == null) return;

			void ShowNote(string txt)
			{
				map.pawnDestinationReservationManager.ReleaseAllClaimedBy(pawn);
				var jobName = pawn.jobs?.curJob.GetReport(pawn).CapitalizeFirst() ?? "-";
				var label = "JobInterruptedLabel".Translate(jobName);
				Find.LetterStack.ReceiveLetter(LetterMaker.MakeLetter(label, txt, LetterDefOf.NegativeEvent, pawn));
			}

			var breakNote = Tools.PawnOverBreakLevel(pawn);
			if (breakNote != null)
			{
				if (breakNote.Length > 0)
					ShowNote("JobInterruptedBreakdown".Translate(pawn.Name.ToStringShort, breakNote));
				Remove(pawn);
				pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced, true);
				return;
			}

			if (Tools.PawnOverHealthLevel(pawn))
			{
				ShowNote("JobInterruptedBadHealth".Translate(pawn.Name.ToStringShort));
				Remove(pawn);
				pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced, true);
				return;
			}
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
