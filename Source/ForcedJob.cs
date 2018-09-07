using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace AchtungMod
{
	/*[HarmonyPatch(typeof(WorkGiver_ConstructDeliverResources))]
	[HarmonyPatch("ResourceDeliverJobFor")]
	static class DEBUG1
	{
		static void Prefix()
		{
			Log.Warning("### ResourceDeliverJobFor");
		}
	}

	[HarmonyPatch(typeof(WorkGiver_ConstructDeliverResources))]
	[HarmonyPatch("FindNearbyNeeders")]
	static class DEBUG2
	{
		static void Prefix()
		{
			Log.Warning("### FindNearbyNeeders");
		}
	}*/

	// 

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
		private HashSet<ForcedTarget> targets = new HashSet<ForcedTarget>();
		private readonly Dictionary<LocalTargetInfo, int> targetBaseScoreCache = new Dictionary<LocalTargetInfo, int>();

		public Pawn pawn = null;
		public List<WorkGiverDef> workgiverDefs = new List<WorkGiverDef>();
		public bool isThingJob = false;
		public bool initialized = false;
		public int cellRadius = 0;

		static readonly Dictionary<BuildableDef, int> TypeScores = new Dictionary<BuildableDef, int>
		{
			{ ThingDefOf.PowerConduit, 1000 },
			{ ThingDefOf.Wall, 900 },
			{ ThingDefOf.TrapSpike, 300 },
			{ ThingDefOf.Sandbags, 200 },
			{ ThingDefOf.Turret_MiniTurret, 150 },
			{ ThingDefOf.Door, 50 },
			{ ThingDefOf.Bed, 10 },
			{ ThingDefOf.Bedroll, 9 },
			{ ThingDefOf.Campfire, 6 },
			{ ThingDefOf.TorchLamp, 6 },
			{ ThingDefOf.Table2x2c, 6 },
			{ ThingDefOf.DiningChair, 6 },
			{ ThingDefOf.Battery, 5 },
			{ ThingDefOf.WoodFiredGenerator, 5 },
			{ ThingDefOf.SolarGenerator, 5 },
			{ ThingDefOf.WindTurbine, 5 },
			{ ThingDefOf.GeothermalGenerator, 5 },
			{ ThingDefOf.WatermillGenerator, 5 },
			{ ThingDefOf.Cooler, 2 },
			{ ThingDefOf.Heater, 2 },
			{ ThingDefOf.FirefoamPopper, 2 },
			{ ThingDefOf.PassiveCooler, 2 },
			{ ThingDefOf.Turret_Mortar, 2 },
			{ ThingDefOf.StandingLamp, 1 },
			{ ThingDefOf.PlantPot, 1 },
			{ ThingDefOf.Grave, 1 },
		};

		public ForcedJob()
		{
			pawn = null;
			workgiverDefs = new List<WorkGiverDef>();
			targets = new HashSet<ForcedTarget>();
			targetBaseScoreCache = new Dictionary<LocalTargetInfo, int>();
			isThingJob = false;
			initialized = false;
			cellRadius = 0;
		}

		public void AddTarget(LocalTargetInfo item)
		{
			var target = new ForcedTarget(item);
			target.materialScore = MaterialScore(target.item);
			targets.Add(target);
			UpdateCells();
		}

		public IEnumerable<IntVec3> AllCells(bool onlyValid = false)
		{
			var validTargets = onlyValid ? targets.Where(target => target.IsValidTarget()) : targets;
			if (isThingJob)
				return validTargets
					.SelectMany(target => target.item.Thing.AllCells());
			else
				return validTargets.Select(target => target.item.Cell);
		}

		public IEnumerable<WorkGiver_Scanner> WorkGivers => workgiverDefs.Select(wgd => (WorkGiver_Scanner)wgd.Worker);

		/*public static int ItemBasePriority(Map map, ref LocalTargetInfo item, int totalCount)
		{
			var itemCell = item.Cell;

			var thing = item.Thing;
			var baseScore = (thing as Blueprint) != null || (thing as Frame) != null ? 0 : 1000;

			return 10000 * (baseScore + ItemScore(ref itemCell, map, totalCount));
		}*/

		/*public static int ItemExtraPriority(ref LocalTargetInfo item, ref IntVec3 nearTo)
		{
			var typeScore = 0; // TypeScore(item);
			var nearTo2 = nearTo;
			var nearScore = NearScore(ref item, ref nearTo2);
			//var itemCell = item.Cell;
			//var otherScore = OthersScore(ref itemCell, pawn);
			return typeScore + nearScore; // + otherScore;
		}*/

		/*public static float BaseScore(IntVec3 pos, Map map, int totalCount)
		{
			var pathGrid = map.pathGrid;
			var left = pathGrid.Walkable(pos + new IntVec3(-1, 0, 0)) ? 0 : 1;
			var right = pathGrid.Walkable(pos + new IntVec3(1, 0, 0)) ? 0 : 1;
			var up = pathGrid.Walkable(pos + new IntVec3(0, 0, -1)) ? 0 : 1;
			var down = pathGrid.Walkable(pos + new IntVec3(0, 0, 1)) ? 0 : 1;
			var score = left + right + up + down;
			if (score > 2)
				return 1f;
			if (score == 2)
			{
				if (up + right == 0)
					if (pathGrid.Walkable(pos + new IntVec3(1, 0, -1)))
						return 0.5f;
				if (right + down == 0)
					if (pathGrid.Walkable(pos + new IntVec3(1, 0, 1)))
						return 0.5f;
				if (down + left == 0)
					if (pathGrid.Walkable(pos + new IntVec3(-1, 0, 1)))
						return 0.5f;
				if (left + up == 0)
					if (pathGrid.Walkable(pos + new IntVec3(-1, 0, -1)))
						return 0.5f;

				var min = totalCount + 1;
				for (var i = 0; i < 4; i++)
				{
					var count = Tools.IsEnclosed(map, totalCount + 1, pos, GenAdj.CardinalDirections[i]);
					if (count > 0 && count < totalCount + 1)
						min = Math.Min(min, count);
				}
				return 0.5f + 0.5f * (totalCount + 1 - min) / (totalCount + 1);
			}
			return score / 2f;
		}*/

		/*public static int ItemScore(ref IntVec3 pos, Map map, int totalCount)
		{
			var pathGrid = map.pathGrid;
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
					var count = Tools.IsEnclosed(map, totalCount, pos, GenAdj.CardinalDirections[i]);
					if (count > 0 && count < totalCount)
						return totalCount + count; // 1x - 2x total
				}
				return totalCount; // 1x total
			}
			return 0; // 0
		}*/

		/*public static int NearScore(ref LocalTargetInfo item, ref IntVec3 nearTo)
		{
			return IntVec3Utility.ManhattanDistanceFlat(item.Cell, nearTo); // nearTo.DistanceToSquared(item.Cell);
		}*/

		public static int MaterialScore(LocalTargetInfo item)
		{
			var scoreBlueprint = 0;
			var scoreFrame = 0;

			var thing = item.Thing;
			if (thing != null)
			{
				var blueprint = thing as Blueprint_Build;
				if (blueprint != null)
				{
					if (TypeScores.TryGetValue(blueprint.def.entityDefToBuild, out var n))
						scoreBlueprint = n;
				}

				var frame = thing as Frame;
				if (frame != null)
				{
					if (TypeScores.TryGetValue(frame.def.entityDefToBuild, out var n))
						scoreFrame = n;
				}
			}

			return Math.Max(scoreBlueprint, scoreFrame);
		}

		/*public static int OthersScore(ref IntVec3 cell, Pawn currentPawn)
		{
			var pawnPositions = new HashSet<IntVec3>(currentPawn.Map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer)
				.Except(currentPawn)
				.Select(pawn => pawn.Position));

			var cell2 = cell;
			return GenRadial.RadialPatternInRadius(3f) //GenAdj.AdjacentCellsAndInside
				.Select(v => v + cell2)
				.Count(c => pawnPositions.Contains(c));
		}*/

		public void PrepareTargets()
		{
			return;

			/* -------------------------------------------------------------------------

			targetBaseScoreCache = new Dictionary<LocalTargetInfo, int>();
			targets
				.Select(target => target.item)
				.Where(item => item.HasThing == false || (item.Thing != null && item.Thing.Spawned && item.Thing.Destroyed == false))
				.Do(item => targetBaseScoreCache.Add(item, ItemBasePriority(pawn.Map, ref item, targets.Count)));

			var scores = "";
			targetBaseScoreCache
				.OrderBy(pair => pair.Value)
				.Do(pair => { scores += $" {pair.Key.Cell.x}x{pair.Key.Cell.z}={pair.Value}"; });
			// Log.Error($"### {pawn.Name.ToStringShort} [{targets.Count}]{scores}");
			*/
		}

		private bool FreeTarget(ForcedTarget target)
		{
			var allReservations = Tools.Reservations(pawn.Map.reservationManager);
			return allReservations.Any(res => res.Target.Cell == target.item.Cell && res.Claimant != pawn) == false;
		}

		public List<LocalTargetInfo> GetSortedTargets()
		{
			var result = new List<ForcedTarget>();
			var temp = targets.ToList();
			// List<ForcedTarget> extract;

			return temp
				.Where(target => target.IsValidTarget() && FreeTarget(target))
				.OrderByDescending(target => target.materialScore)
				.Select(target => target.item)
				.ToList();
			/* ----------------------------------------------------------------------------------------
			
			// ### ENABLE AGAIN FOR SMART BUILDING
			temp.Do(target =>
			{
				var valid = target.IsValidTarget();
				target.baseScore = valid ? BaseScore(target.item.Cell, target.item.Thing.Map, temp.Count) : -1;
				target.typeScore = ((target.item.Thing as Blueprint) != null ? 0.25f : 0f) + ((target.item.Thing as Frame) != null ? 0.5f : 0f);
				target.materialScore = MaterialScore(target.item) / 1000f;
			});
			
			// important pieces
			extract = temp.Where(target => target.baseScore > 0.5f).OrderByDescending(target => target.baseScore).ToList();
			result.AddRange(extract);
			temp = temp.Except(extract).ToList();

			if (result.Count == 0)
				return extract
				.Select(target => target.item)
				.ToList();

			// blueprints and frames
			extract = temp.Where(target => target.typeScore > 0f).OrderByDescending(target => target.typeScore).ToList();
			result.AddRange(extract);
			temp = temp.Except(extract).ToList();

			// remainder
			result.AddRange(temp.OrderByDescending(target => target.materialScore));

			return extract
				.Select(target => target.item)
				.ToList();*/

			/*int[] CalcScore(LocalTargetInfo item, ref IntVec3 near)
			{
				var baseScore = 0;
				targetBaseScoreCache.TryGetValue(item, out baseScore);
				var near2 = near;
				return new int[] { baseScore, ItemExtraPriority(ref item, ref near2) };
			}

			var nearTo = pawn.Position;
			var targetCount = targets.Count;
			var items = targets
				.Select(target => target.item)
				.Where(item => item.HasThing == false || (item.Thing != null && item.Thing.Spawned && item.Thing.Destroyed == false))
				.OrderBy(item => { var scores = CalcScore(item, ref nearTo); return scores[0] + scores[1]; })
				.ToList();

			return items;*/
		}

		public bool GetNextJob(out Job job, out LocalTargetInfo atItem)
		{
			job = null;
			atItem = LocalTargetInfo.Invalid;

			var thingGrid = pawn.Map.thingGrid;
			var sortedItems = GetSortedTargets();

			/*var allThings = sortedItems
				.Where(item => item.HasThing)
				.Select(item => item.Thing);
			var containsBlueprintsOrFrames = allThings.Any(t => (t is Blueprint) || (t is Frame))
				&& allThings.All(t => t is Frame) == false;*/

			foreach (var item in sortedItems)
			{
				foreach (var workgiver in WorkGivers)
				{
					//if (containsBlueprintsOrFrames && workgiver.IsOfType<WorkGiver_ConstructFinishFrames>())
					//	continue;

					if (isThingJob)
					{
						job = item.Thing.GetThingJob(pawn, workgiver);
						//if (job != null)
						//	Log.Warning($"-> job {job} (A={job.targetA}, B={job.targetB}) on thing {item.Thing} at {item.Thing.Position}");
					}
					else
					{
						job = item.Cell.GetCellJob(pawn, workgiver);
						//if (job != null)
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
			if (forcedJob == null)
				return false;
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

			// pawn.ClearReservationsForJob(lastJob);

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
			return WorkGivers.Any(workgiver => thing.GetThingJob(pawn, workgiver, true) != null);
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
			// Log.Warning($"UPDATE CELLS {pawn.Name.ToStringShort}, old target count = {targets.Count}");

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

					addedThings.AddRange(newThings.Where(HasJob).ToList()); // keep termination with 'ToList()' here
				}
				while (count < addedThings.Count());
				things = things.Where(thing => thing.Spawned).Where(HasJob).Union(addedThings.Except(things));
				targets = new HashSet<ForcedTarget>(things.Select(thing =>
				{
					var item = new LocalTargetInfo(thing);
					return new ForcedTarget(item) { materialScore = MaterialScore(item) };
				}));
				PrepareTargets();

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
			targets = new HashSet<ForcedTarget>(cells.Select(cell =>
			{
				var item = new LocalTargetInfo(cell);
				return new ForcedTarget(item) { materialScore = MaterialScore(item) };
			}));
			PrepareTargets();
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
			{
				targets.RemoveWhere(target => target.item.ThingDestroyed);
				PrepareTargets();
			}
		}
	}

	public class ForcedTarget : IExposable, IEquatable<ForcedTarget>
	{
		public LocalTargetInfo item = LocalTargetInfo.Invalid;
		//public float baseScore = 0f;
		//public float typeScore = 0f;
		public float materialScore = 0f;

		public ForcedTarget()
		{
			item = LocalTargetInfo.Invalid;
			//baseScore = 0f;
			//typeScore = 0f;
			materialScore = 0f;
		}

		public ForcedTarget(LocalTargetInfo item)
		{
			this.item = item;
		}

		public void ExposeData()
		{
			Scribe_TargetInfo.Look(ref item, false, "item", LocalTargetInfo.Invalid);
			//Scribe_Values.Look(ref baseScore, "baseScore", 0f, true);
			//Scribe_Values.Look(ref typeScore, "typeScore", 0f, true);
			Scribe_Values.Look(ref materialScore, "materialScore", 0f, true);
		}

		public bool Equals(ForcedTarget other)
		{
			return item.Equals(other.item);
		}

		public bool IsValidTarget()
		{
			return item.HasThing == false || (item.Thing != null && item.ThingDestroyed == false);
		}

		public override string ToString()
		{
			if (item.HasThing)
				return $"{item.Thing.def.defName}@{item.Cell.x}x{item.Cell.z}({materialScore})";
			return $"{item.Cell.x}x{item.Cell.z}({materialScore})";

			//if (item.HasThing)
			//	return $"{item.Thing.def.defName}@{item.Cell.x}x{item.Cell.z}({baseScore})";
			//return $"{item.Cell.x}x{item.Cell.z}({baseScore})";
		}
	}

	public static class ForcedExtensions
	{
		public static Job GetThingJob(this Thing thing, Pawn pawn, WorkGiver_Scanner workgiver, bool ignoreReserve = false)
		{
			var ignoreRestrictions = (false
				|| (workgiver as WorkGiver_Haul) != null
				|| (workgiver as WorkGiver_Repair) != null
				|| (workgiver as WorkGiver_ConstructAffectFloor) != null
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
						// Log.Warning($"{pawn.Name.ToStringShort} can {workgiver.def.defName} on {thing.def.defName} at {thing.Position}");
						var job = workgiver.JobOnThing(pawn, thing, true);
						if (job != null)
						{
							// Log.Warning($"{pawn.Name.ToStringShort} has {job.def.defName} on {thing.def.defName} at {thing.Position}");
							if (ignoreRestrictions || thing.IsForbidden(pawn) == false)
								if (ignoreRestrictions || thing.Position.InAllowedArea(pawn))
								{
									var ok1 = (ignoreReserve == false && pawn.CanReserveAndReach(thing, workgiver.PathEndMode, Danger.Deadly));
									var ok2 = (ignoreReserve && pawn.CanReach(thing, workgiver.PathEndMode, Danger.Deadly));
									if (ok1 || ok2)
									{
										// Log.Warning($"{pawn.Name.ToStringShort} got job on {thing.def.defName} at {thing.Position}");
										return job;
									}
								}
						}
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