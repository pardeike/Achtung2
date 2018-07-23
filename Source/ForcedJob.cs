using RimWorld;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Verse;
using Verse.AI;

namespace AchtungMod
{
	public class ForcedJobs : IExposable
	{
		public List<ForcedJob> jobs = new List<ForcedJob>();

		public ForcedJobs()
		{
			jobs = new List<ForcedJob>();
		}

		public void ExposeData()
		{
			Scribe_Collections.Look(ref jobs, "jobs", LookMode.Deep);

			if (Scribe.mode == LoadSaveMode.ResolvingCrossRefs)
			{
				if (jobs == null)
					jobs = new List<ForcedJob>();
			}
		}
	}

	public class ForcedJob : IExposable
	{
		public Pawn pawn = null;
		public List<WorkGiverDef> workgiverDefs = new List<WorkGiverDef>();
		public HashSet<ForcedTarget> targets = new HashSet<ForcedTarget>();
		public bool isThingJob = false;
		public bool initialized = false;
		public int cellRadius = 0;

		static Dictionary<BuildableDef, int> TypeScores = new Dictionary<BuildableDef, int>
		{
			{ ThingDefOf.PowerConduit, 1000 },
			{ ThingDefOf.Sandbags, 200 },
			{ ThingDefOf.Turret_MiniTurret, 150 },
			{ ThingDefOf.Wall, 100 },
			{ ThingDefOf.Door, 50 },
			{ ThingDefOf.TrapDeadfall, 20 },
			{ ThingDefOf.Bed, 10 },
			{ ThingDefOf.Bedroll, 9 },
			{ ThingDefOf.WoodFiredGenerator, 5 },
			{ ThingDefOf.Battery, 5 },
			{ ThingDefOf.SolarGenerator, 5 },
			{ ThingDefOf.WindTurbine, 5 },
			{ ThingDefOf.GeothermalGenerator, 5 },
			{ ThingDefOf.Cooler, 2 },
			{ ThingDefOf.Heater, 2 },
			{ ThingDefOf.PassiveCooler, 2 },
			{ ThingDefOf.Turret_Mortar, 2 },
			{ ThingDefOf.StandingLamp, 1 },
			{ ThingDefOf.TorchLamp, 1 },
		};

		public ForcedJob()
		{
			pawn = null;
			workgiverDefs = new List<WorkGiverDef>();
			targets = new HashSet<ForcedTarget>();
			isThingJob = false;
			initialized = false;
			cellRadius = 0;
		}

		public IEnumerable<WorkGiver_Scanner> WorkGivers => workgiverDefs.Select(wgd => (WorkGiver_Scanner)wgd.Worker);

		public static int ItemPriority(ref LocalTargetInfo item, Pawn pawn, ref IntVec3 nearTo, int totalCount)
		{
			var itemCell = item.Cell;
			var itemScore = ItemScore(ref itemCell, pawn, totalCount);
			var typeScore = TypeScore(ref item, ref nearTo);
			var otherScore = OthersScore(ref itemCell, pawn);
			return typeScore + itemScore * 10 + otherScore;
		}

		public static int ItemScore(ref IntVec3 pos, Pawn pawn, int totalCount)
		{
			var pathGrid = pawn.Map.pathGrid;
			var left = pathGrid.Walkable(pos + new IntVec3(-1, 0, 0)) ? 1 : 0;
			var right = pathGrid.Walkable(pos + new IntVec3(1, 0, 0)) ? 1 : 0;
			var up = pathGrid.Walkable(pos + new IntVec3(0, 0, -1)) ? 1 : 0;
			var down = pathGrid.Walkable(pos + new IntVec3(0, 0, 1)) ? 1 : 0;
			var score = left + right + up + down;
			if (score > 2)
				return score * totalCount; // 3x or 4x total
			if (score == 2)
			{
				for (var i = 0; i < 4; i++)
				{
					var count = Tools.IsEnclosed(pawn, totalCount, pos, GenAdj.CardinalDirections[i]);
					if (count > 0 && count < totalCount)
						return totalCount + count; // 1x - 2x total
				}
				return totalCount; // 1x total
			}
			return 0; // 0
		}

		public static int TypeScore(ref LocalTargetInfo item, ref IntVec3 nearTo)
		{
			var typeScore = IntVec3Utility.ManhattanDistanceFlat(item.Cell, nearTo); // nearTo.DistanceToSquared(item.Cell);
			var thing = item.Thing;
			if (thing != null)
			{
				var blueprint = thing as Blueprint_Build;
				if (blueprint != null)
				{
					if (TypeScores.TryGetValue(blueprint.def.entityDefToBuild, out var n))
						typeScore += (1000 - n) * 10;
				}

				var frame = thing as Frame;
				if (frame != null)
				{
					if (TypeScores.TryGetValue(frame.def.entityDefToBuild, out var n))
						typeScore += (1000 - n) * 100;
				}
			}

			return typeScore;
		}

		public static int OthersScore(ref IntVec3 cell, Pawn currentPawn)
		{
			var pawnPositions = new HashSet<IntVec3>(currentPawn.Map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer)
				.Except(currentPawn)
				.Select(pawn => pawn.Position));

			var cell2 = cell;
			return GenRadial.RadialPatternInRadius(3f) //GenAdj.AdjacentCellsAndInside
				.Select(v => v + cell2)
				.Count(c => pawnPositions.Contains(c));
		}

		public List<LocalTargetInfo> GetSortedTargets()
		{
			var stopwatch = new Stopwatch();
			var nearTo = pawn.Position;
			var targetCount = targets.Count;
			var items = targets
				.Select(target => target.item)
				.Where(item => item.HasThing == false || (item.Thing != null && item.Thing.Spawned && item.Thing.Destroyed == false))
				.OrderBy(item => ItemPriority(ref item, pawn, ref nearTo, targetCount))
				.ToList();
			Log.Warning($"{stopwatch.ElapsedMilliseconds}ms for {items.Count}");
			// Log.Warning("Items " + items.Select(si => { var pos = pawn.Position; return new KeyValuePair<string, int>($"{si.Cell.x}x{si.Cell.z}", ItemPriority(ref si, pawn, ref pos)); }).Aggregate("", (prev, pair) => $"{prev}{" , ".Substring(0, Math.Min(1, 5 * prev.Length))}{pair.Key}#{pair.Value}"));
			return items;
		}

		public bool GetNextJob(out Job job, out LocalTargetInfo atItem)
		{
			job = null;
			atItem = LocalTargetInfo.Invalid;

			var thingGrid = pawn.Map.thingGrid;
			var sortedItems = GetSortedTargets();
			foreach (var item in sortedItems)
			{
				foreach (var workgiver in WorkGivers)
				{
					if (isThingJob)
					{
						job = item.Thing.GetThingJob(pawn, workgiver);
						// if (job != null)
						//	Log.Warning($"-> job {job} (A={job.targetA}, B={job.targetB}) on thing {item.Thing} at {item.Thing.Position}");
					}
					else
					{
						job = item.Cell.GetCellJob(pawn, workgiver);
						// if (job != null)
						//	Log.Warning($"-> job {job} (A={job.targetA}, B={job.targetB}) on cell {item.Cell}");
					}

					if (job != null)
					{
						atItem = item;
						return true;
					}
				}
			}
			if (sortedItems.Count > 0)
				Find.LetterStack.ReceiveLetter("No forced work", pawn.Name.ToStringShort + " could not find more forced work. The remaining work is most likely reserved or not accessible.", LetterDefOf.NeutralEvent, pawn);

			return false;
		}

		public static bool ContinueJob(Pawn_JobTracker tracker, Job lastJob, Pawn pawn, JobCondition condition)
		{
			if (pawn.IsColonist == false) return false;
			var forcedWork = Find.World.GetComponent<ForcedWork>();

			var forcedJob = forcedWork.GetForcedJob(pawn);
			if (forcedJob == null) return false;
			if (forcedJob.initialized == false)
			{
				forcedJob.initialized = true;
				return false;
			}

			if (condition == JobCondition.InterruptForced)
			{
				Find.LetterStack.ReceiveLetter("Forced work interrupted", "Forced work of " + pawn.Name.ToStringShort + " was interrupted.", LetterDefOf.NeutralEvent, pawn);
				forcedWork.Remove(pawn);
				return false;
			}

			pawn.ClearReservationsForJob(lastJob);

			while (true)
			{
				forcedJob.UpdateCells();

				if (forcedJob.GetNextJob(out var job, out var item))
				{
					job.expiryInterval = 0;
					job.ignoreJoyTimeAssignment = true;
					job.locomotionUrgency = LocomotionUrgency.Sprint;
					job.playerForced = true;
					forcedWork.QueueJob(pawn, job);
					return true;
				}

				forcedWork.RemoveForcedJob(pawn);
				forcedJob = forcedWork.GetForcedJob(pawn);
				if (forcedJob == null)
					break;
				forcedJob.initialized = true;
			}

			forcedWork.Remove(pawn);
			return false;
		}

		public void ChangeCellRadius(int delta)
		{
			cellRadius += delta;
			UpdateCells();
		}

		public bool HasJob(Thing thing)
		{
			return WorkGivers.Any(workgiver => thing.GetThingJob(pawn, workgiver) != null);
		}

		public bool HasJob(ref IntVec3 cell)
		{
			var cell2 = cell;
			return WorkGivers.Any(workgiver => cell2.GetCellJob(pawn, workgiver) != null);
		}

		private IEnumerable<IntVec3> Nearby(ref IntVec3 cell)
		{
			if (cellRadius > 0 && cellRadius <= GenRadial.MaxRadialPatternRadius)
				return GenRadial.RadialCellsAround(cell, cellRadius, true);
			var cell2 = cell;
			return GenAdj.AdjacentCellsAndInside.Select(vec => cell2 + vec);
		}

		public void UpdateCells()
		{
			var count = 0;
			if (isThingJob)
			{
				var thingGrid = pawn.Map.thingGrid;

				var things = targets.Select(target => target.item.Thing);
				var addedThings = things.ToList();
				do
				{
					count = addedThings.Count();

					var currentThingCells = addedThings
						.SelectMany(thing => thing.AllCells())
						.Distinct();

					var surroundingCells = currentThingCells
						.SelectMany(cell => Nearby(ref cell))
						.Distinct();
					// reinclude current cells so non-complete items are completed
					//.Except(currentThingCells);

					var newThings = surroundingCells
						.SelectMany(cell => thingGrid.ThingsAt(cell))
						.Distinct()
						.Except(addedThings);

					addedThings.AddRange(newThings.Where(HasJob).ToList()); // need termination with 'ToList()' here
				}
				while (count < addedThings.Count());
				things = things.Where(thing => thing.Spawned).Where(HasJob).Union(addedThings.Except(things));
				targets = new HashSet<ForcedTarget>(things.Select(thing => new ForcedTarget(new LocalTargetInfo(thing))));

				return;
			}

			var cells = targets.Select(target => target.item.Cell);
			var addedCells = cells.ToList();
			do
			{
				count = addedCells.Count();

				var surroundingCells = addedCells
					.SelectMany(cell => Nearby(ref cell))
					.Distinct()
					.Except(addedCells);

				var addCells = surroundingCells
					.Where(cell => HasJob(ref cell));

				addedCells.AddRange(addCells.ToList());
			}
			while (count < addedCells.Count());
			cells = cells.Where(cell => HasJob(ref cell)).Union(addedCells.Except(cells));
			targets = new HashSet<ForcedTarget>(cells.Select(cell => new ForcedTarget(new LocalTargetInfo(cell))));
		}

		public void ExposeData()
		{
			Scribe_References.Look(ref pawn, "pawn");
			Scribe_Collections.Look(ref workgiverDefs, "workgivers", LookMode.Def);
			Scribe_Collections.Look(ref targets, "targets", LookMode.Deep);
			Scribe_Values.Look(ref isThingJob, "thingJob", false, true);
			Scribe_Values.Look(ref initialized, "inited", false, true);
			Scribe_Values.Look(ref cellRadius, "radius", 0, true);

			if (Scribe.mode == LoadSaveMode.ResolvingCrossRefs)
				targets.RemoveWhere(target => target.item.IsValid == false);
		}
	}

	public class ForcedTarget : IExposable, IEquatable<ForcedTarget>
	{
		public LocalTargetInfo item = LocalTargetInfo.Invalid;
		public int lastChecked = 0;

		public ForcedTarget()
		{
			item = LocalTargetInfo.Invalid;
			lastChecked = 0;
		}

		public ForcedTarget(LocalTargetInfo item)
		{
			this.item = item;
			lastChecked = Find.TickManager.TicksGame;
		}

		public void ExposeData()
		{
			Scribe_TargetInfo.Look(ref item, false, "item", LocalTargetInfo.Invalid);
			Scribe_Values.Look(ref lastChecked, "checked", 0, true);
		}

		public bool Equals(ForcedTarget other)
		{
			return item.Equals(other.item);
		}

		public override string ToString()
		{
			return item.ToString() + "#" + lastChecked;
		}
	}

	public static class ForcedExtensions
	{
		public static Job GetThingJob(this Thing thing, Pawn pawn, WorkGiver_Scanner workgiver)
		{
			var ignoreRestrictions = ((workgiver as WorkGiver_Haul) != null || (workgiver as WorkGiver_Repair) != null);
			ignoreRestrictions |= (
				(workgiver as WorkGiver_ConstructAffectFloor) != null
				|| (workgiver as WorkGiver_ConstructDeliverResources) != null
				|| (workgiver as WorkGiver_ConstructFinishFrames) != null
				|| (workgiver as WorkGiver_Flick) != null
				|| (workgiver as WorkGiver_Miner) != null
				|| (workgiver as WorkGiver_Refuel) != null
				|| (workgiver as WorkGiver_RemoveRoof) != null
				|| (workgiver as WorkGiver_Strip) != null
				|| (workgiver as WorkGiver_TakeToBed) != null
				|| (workgiver as WorkGiver_RemoveBuilding) != null
			);

			if (workgiver.PotentialWorkThingRequest.Accepts(thing) || (workgiver.PotentialWorkThingsGlobal(pawn) != null && workgiver.PotentialWorkThingsGlobal(pawn).Contains(thing)))
				if (workgiver.MissingRequiredCapacity(pawn) == null)
					if (workgiver.HasJobOnThing(pawn, thing, true))
					{
						var job = workgiver.JobOnThing(pawn, thing, true);
						if (job != null)
							if (ignoreRestrictions || thing.IsForbidden(pawn) == false)
								if (ignoreRestrictions || thing.Position.InAllowedArea(pawn))
									if (pawn.CanReserveAndReach(thing, workgiver.PathEndMode, Danger.Deadly))
										return job;
					}
			return null;
		}

		public static Job GetCellJob(this IntVec3 cell, Pawn pawn, WorkGiver_Scanner workgiver)
		{
			if (workgiver.PotentialWorkCellsGlobal(pawn).Contains(cell))
				if (workgiver.MissingRequiredCapacity(pawn) == null)
					if (workgiver.HasJobOnCell(pawn, cell))
					{
						var job = workgiver.JobOnCell(pawn, cell);
						if (pawn.CanReach(cell, workgiver.PathEndMode, Danger.Deadly))
							return job;
					}
			return null;
		}
	}
}