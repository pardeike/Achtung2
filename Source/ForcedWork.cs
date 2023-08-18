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
		public bool hasForcedJobs = false; //optimization
		private List<Pawn> forcedJobsKeysWorkingList;
		private List<ForcedJobs> forcedJobsValuesWorkingList;
		private int counter;

		public readonly HashSet<Pawn> preparing = new HashSet<Pawn>();
		//readonly Dictionary<Pawn, HashSet<IntVec3>> forbiddenLocations = new Dictionary<Pawn, HashSet<IntVec3>>();

		public ForcedWork(World world) : base(world) { }

		static ForcedWork instance = null;
		public static ForcedWork Instance
		{
			get
			{
				instance ??= Find.World.GetComponent<ForcedWork>();
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
					.Where(def => typeof(T).IsAssignableFrom(def.giverClass)).ToList();
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
			if (pawn == null || allForcedJobs.TryGetValue(pawn, out var forced) == false)
				return new List<ForcedJob>();
			return forced.jobs ?? new List<ForcedJob>();
		}

		public void UpdatePawnForcedJobs(Pawn pawn, ForcedJobs jobs)
		{
			if (pawn.thinker == null)
			{
				pawn.thinker = new Pawn_AchtungThinker(pawn) { forcedJobs = jobs };
				return;
			}
			if (pawn.thinker is Pawn_AchtungThinker achtungThinker)
				achtungThinker.forcedJobs = jobs;
		}

		public void Prepare(Pawn pawn)
		{
			_ = preparing.Add(pawn);
		}

		public void Unprepare(Pawn pawn)
		{
			_ = preparing.Remove(pawn);
		}

		public bool HasForcedJob(Pawn pawn, bool ignorePreparing = false)
		{
			if (pawn == null)
				return false;

			if (ignorePreparing == false && preparing.Contains(pawn))
				return true;

			// optimization
			if (pawn.thinker is Pawn_AchtungThinker achtungThinker)
				return achtungThinker.forcedJobs.count > 0;

			// fallback
			if (allForcedJobs.TryGetValue(pawn, out var forcedJobs) == false)
				return false;
			return forcedJobs.count > 0;
		}

		public ForcedJobs GetForcedJobsInstance(Pawn pawn)
		{
			if (allForcedJobs.TryGetValue(pawn, out var forcedJobs))
				return forcedJobs;
			return new ForcedJobs();
		}

		public ForcedJob GetForcedJob(Pawn pawn)
		{
			if (pawn == null)
				return null;

			ForcedJobs forcedJobs;

			// optimization
			if (pawn.thinker is Pawn_AchtungThinker achtungThinker)
				forcedJobs = achtungThinker.forcedJobs;
			else /* fallback */ if (allForcedJobs.TryGetValue(pawn, out forcedJobs) == false)
				return null;

			return forcedJobs.jobs.FirstOrDefault();
		}

		public void RemoveForcedJob(Pawn pawn)
		{
			Unprepare(pawn);

			if (pawn == null || allForcedJobs.TryGetValue(pawn, out var forcedJobs) == false)
				return;

			if (forcedJobs.count == 0)
				return;

			forcedJobs.jobs[0].Cleanup();
			forcedJobs.jobs.RemoveAt(0);
			forcedJobs.UpdateCount();

			if (forcedJobs.count == 0)
				Remove(pawn);
			UpdatePawnForcedJobs(pawn, forcedJobs);
		}

		public void Remove(Pawn pawn)
		{
			if (allForcedJobs.TryGetValue(pawn, out var forcedJobs))
				forcedJobs.jobs.Do(job => job?.Cleanup());
			_ = allForcedJobs.Remove(pawn);
			hasForcedJobs = allForcedJobs.Count > 0;
			UpdatePawnForcedJobs(pawn, new ForcedJobs());
		}

		public bool AddForcedJob(Pawn pawn, List<WorkGiverDef> workgiverDefs, LocalTargetInfo item)
		{
			if (allForcedJobs.ContainsKey(pawn) == false)
				allForcedJobs[pawn] = new ForcedJobs();

			var firstJob = allForcedJobs[pawn].count == 0;
			if (firstJob)
				Prepare(pawn);
			else
				Unprepare(pawn);

			var forcedJob = new ForcedJob(pawn, item, workgiverDefs);
			allForcedJobs[pawn].jobs.Add(forcedJob);
			allForcedJobs[pawn].UpdateCount();

			hasForcedJobs = allForcedJobs.Count > 0;
			UpdatePawnForcedJobs(pawn, allForcedJobs[pawn]);
			return firstJob;
		}

		public static LocalTargetInfo HasJobItem(Pawn pawn, WorkGiver_Scanner workgiver, IntVec3 pos, bool expandSearch)
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

				if (expandSearch == false)
					break;
			}
			return null;
		}

		public static Job GetJobItem(Pawn pawn, WorkGiver_Scanner workgiver, LocalTargetInfo item)
		{
			if (item.HasThing)
				return item.thingInt.GetThingJob(pawn, workgiver);
			return item.cellInt.GetCellJob(pawn, workgiver);
		}

		public IEnumerable<ForcedJob> ForcedJobsForMap(Map map)
		{
			if (hasForcedJobs == false)
				return new List<ForcedJob>();
			return allForcedJobs
				.Where(pair => pair.Key.Map == map)
				.SelectMany(pair => pair.Value.jobs ?? new List<ForcedJob>());
		}

		public bool NonForcedShouldIgnore(Map map, IntVec3 cell)
		{
			return allForcedJobs
				.Where(pair => pair.Key.Map == map)
				.Any(pair =>
				{
					var forcedJobs = pair.Value;
					if (forcedJobs == null)
						return false;
					return forcedJobs.jobs.Any(job => job.NonForcedShouldIgnore(cell));
				});
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
			if (Find.TickManager.TicksGame % 139 != 0)
				return;
			var pawns = allForcedJobs.Keys.ToArray();
			var n = pawns.Length;
			if (n == 0)
				return;
			var pawn = pawns[++counter % n];
			var map = pawn.Map;
			if (map == null)
				return;

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
				foreach (var forced in allForcedJobs.Values)
				{
					forced.jobs ??= new List<ForcedJob>();
					forced.UpdateCount();
				}

				_ = allForcedJobs.RemoveAll(pair => pair.Value.count == 0);
			}

			Scribe_Collections.Look(ref allForcedJobs, "joblist", LookMode.Reference, LookMode.Deep, ref forcedJobsKeysWorkingList, ref forcedJobsValuesWorkingList);

			if (Scribe.mode == LoadSaveMode.PostLoadInit)
			{
				allForcedJobs ??= new Dictionary<Pawn, ForcedJobs>();

				foreach (var forced in allForcedJobs.Values)
				{
					forced.jobs ??= new List<ForcedJob>();
					forced.UpdateCount();
				}

				_ = allForcedJobs.RemoveAll(pair => pair.Value.count == 0);
				hasForcedJobs = allForcedJobs.Count > 0;
			}
		}

		public void Prepare(Map map)
		{
			allForcedJobs
				.Where(pair => pair.Key?.Map == map)
				.SelectMany(pair => pair.Value.jobs)
				.Do(job => job?.Prepare());
		}

		public void Cleanup(Map map)
		{
			allForcedJobs
				.Where(pair => pair.Key?.Map == map)
				.SelectMany(pair => pair.Value.jobs)
				.Do(job => job?.Cleanup());
		}
	}
}
