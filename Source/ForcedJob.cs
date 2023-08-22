using RimWorld;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace AchtungMod
{
	public class ForcedJobs : IExposable
	{
		public List<ForcedJob> jobs = new List<ForcedJob>();
		public int count; // optimization

		public ForcedJobs()
		{
			jobs = new List<ForcedJob>();
			count = 0;
		}

		public void UpdateCount()
		{
			count = jobs.Count;
		}

		public void ExposeData()
		{
			jobs ??= new List<ForcedJob>();
			_ = jobs.RemoveAll(job => job == null || job.IsEmpty());

			Scribe_Collections.Look(ref jobs, "jobs", LookMode.Deep);

			jobs ??= new List<ForcedJob>();
			_ = jobs.RemoveAll(job => job == null || job.IsEmpty());
			UpdateCount();
		}
	}

	public class ForcedJob : IExposable
	{
		private HashSet<ForcedTarget> targets = new HashSet<ForcedTarget>();
		const int ticksWaitPaused = 4;
		const int ticksWaitRunning = 64;

		public Pawn pawn = null;
		public List<WorkGiverDef> workgiverDefs = new List<WorkGiverDef>();
		public bool isThingJob = false;
		public bool reentranceFlag = false;
		public XY lastLocation = XY.Invalid;
		public Thing lastThing = null;
		public bool initialized = false;
		public int cellRadius = 0;
		public bool buildSmart = Achtung.Settings.buildingSmartDefault;
		public bool started = false;
		public bool cancelled = false;
		public Coroutine expanderThing, expanderCell, contractor;
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

		// default constructor for deserialization
		public ForcedJob()
		{
			workgiverDefs = new List<WorkGiverDef>();
			targets = new HashSet<ForcedTarget>();
			buildSmart = Achtung.Settings.buildingSmartDefault;
			lastLocation = XY.Invalid;
		}

		// called from AddForcedJob during gameplay
		public ForcedJob(Pawn pawn, LocalTargetInfo item, List<WorkGiverDef> workgiverDefs)
		{
			this.pawn = pawn;
			this.workgiverDefs = workgiverDefs.Where(wgd => wgd?.giverClass != null).ToList();
			targets = new HashSet<ForcedTarget>() { new ForcedTarget(item, MaterialScore(item)) };
			buildSmart = Achtung.Settings.buildingSmartDefault;

			lastThing = item.thingInt;
			lastLocation = item.Cell;

			isThingJob = item.HasThing;
			CreateCoroutines();
		}

		// called from ForcedWork.Prepare which is called from Map.FinalizeInit
		public void Prepare()
		{
			CreateCoroutines();
		}

		void CreateCoroutines()
		{
			cancelled = false;
			expanderThing ??= Find.CameraDriver.StartCoroutine(ExpandThingTargets(true));
			expanderCell ??= Find.CameraDriver.StartCoroutine(ExpandCellTargets(true));
			contractor ??= Find.CameraDriver.StartCoroutine(ContractTargets());
		}

		public void Start() => started = true;

		public void Cleanup()
		{
			cancelled = true;

			if (expanderThing != null)
				Find.CameraDriver.StopCoroutine(expanderThing);
			expanderThing = null;

			if (expanderCell != null)
				Find.CameraDriver.StopCoroutine(expanderCell);
			expanderCell = null;

			if (contractor != null)
				Find.CameraDriver.StopCoroutine(contractor);
			contractor = null;
		}

		public IEnumerable<XY> AllCells(bool onlyValid = false)
		{
			var validTargets = onlyValid ? targets.Where(target => target.IsValidTarget()) : targets;
			if (isThingJob)
				return validTargets
					.SelectMany(target => target.item.ThingDestroyed ? null : target.item.thingInt.AllCells())
					.Distinct();
			else
				return validTargets.Select(target => target.XY);
		}

		public IEnumerable<WorkGiver_Scanner> WorkGiverScanners => workgiverDefs
			.Select(wgd => wgd.Worker).OfType<WorkGiver_Scanner>();

		public static int MaterialScore(LocalTargetInfo item)
		{
			var scoreThing = 0;
			var scoreBlueprint = 0;
			var scoreFrame = 0;

			var thing = item.thingInt;
			if (thing != null)
			{
				if (TypeScores.TryGetValue(thing.def, out var n))
					scoreThing = n;

				if (thing is Blueprint_Build blueprint)
				{
					if (TypeScores.TryGetValue(blueprint.def.entityDefToBuild, out n))
						scoreBlueprint = n;
				}

				if (thing is Frame frame)
				{
					if (TypeScores.TryGetValue(frame.def.entityDefToBuild, out n))
						scoreFrame = n;
				}
			}

			return new[] { scoreThing, scoreBlueprint, scoreFrame }.Max();
		}

		public void Replace(Thing thing, Thing replacement)
		{
			if (workgiverDefs.Any(def => def.giverClass == typeof(WorkGiver_ConstructDeliverResourcesToBlueprints)))
			{
				targets.RemoveWhere(target => target.item.thingInt == thing);
				targets.Add(new ForcedTarget(replacement, MaterialScore(replacement)));
			}
		}

		public IEnumerable<Thing> GetUnsortedTargets()
		{
			var mapWidth = pawn.Map.Size.x;
			return targets
				.Where(target => target.IsValidTarget() && Tools.IsFreeTarget(pawn, target))
				.OrderByDescending(target => target.materialScore)
				.Select(target => target.item.thingInt)
				.OfType<Thing>();
		}

		public IEnumerable<LocalTargetInfo> GetSortedTargets(HashSet<int> planned)
		{
			const int maxSquaredDistance = 200 * 200;

			var map = pawn.Map;
			var pos = pawn.Position;
			var pathGrid = map.pathing.For(pawn).pathGrid;
			var mapWidth = map.Size.x;

			var forbiddenCells = buildSmart
				? map.reservationManager.reservations
					.Where(reservation => reservation.claimant != pawn)
					.Select(reservation => reservation.target.Cell)
					.SelectMany(cell => GenRadial.RadialCellsAround(cell, 1, true).ToXY())
					.Distinct().ToHashSet()
				: new HashSet<XY>();

			return targets
				.Where(target =>
				{
					var vec = target.XY;
					var idx = CellIndicesUtility.CellToIndex(vec.x, vec.y, mapWidth);
					return planned.Contains(idx) == false && target.IsValidTarget() && Tools.IsFreeTarget(pawn, target);
				})
				.OrderByDescending(target =>
				{
					var cell = target.XY;
					if (forbiddenCells.Contains(cell))
						return -1;

					var reverseDistance = maxSquaredDistance - pos.DistanceToSquared(cell);
					if (reverseDistance < 0)
						reverseDistance = 0;
					if (buildSmart && isThingJob)
					{
						var willBlock = target.item.WillBlock();
						var neighbourScore = willBlock ? Tools.NeighbourScore(cell, pathGrid, map.reservationManager.ReservationsReadOnly, mapWidth, planned) : 100;
						return target.materialScore + reverseDistance * 10000 + neighbourScore * 100000000;
					}
					return target.materialScore + reverseDistance * 10000;
				})
				.Select(target => target.item);
		}

		public bool NonForcedShouldIgnore(IntVec3 cell)
		{
			return targets.Any(target => target.XY == cell && target.IsBuilding());
		}

		public bool GetNextJob(out Job job)
		{
			job = null;

			var workGiversByPrio = WorkGiverScanners.OrderBy(worker =>
			{
				if (worker is WorkGiver_ConstructDeliverResourcesToBlueprints)
					return 1;
				if (worker is WorkGiver_ConstructDeliverResourcesToFrames)
					return 2;
				if (worker is WorkGiver_ConstructDeliverResources)
					return 3;
				if (worker is WorkGiver_Haul)
					return 4;
				if (worker is WorkGiver_PlantsCut)
					return 5;
				if (worker is WorkGiver_ConstructFinishFrames)
					return 6;
				if (worker is WorkGiver_ConstructAffectFloor)
					return 7;
				return 999;
			});

			var exist = false;
			foreach (var workgiver in workGiversByPrio)
			{
				foreach (var target in GetSortedTargets(new HashSet<int>()))
				{
					exist = true;

					if (isThingJob)
					{
						job = target.thingInt.GetThingJob(pawn, workgiver);
						job ??= target.Cell.GetCellJob(pawn, workgiver);
					}
					else
					{
						job = target.Cell.GetCellJob(pawn, workgiver);
						job ??= target.thingInt?.GetThingJob(pawn, workgiver);
					}

					if (job != null)
					{
						if (isThingJob)
						{
							lastThing = target.thingInt;
							lastLocation = target.thingInt.Position;
						}
						else
							lastLocation = target.Cell;
						return true;
					}
				}
			}

			if (exist)
				Find.LetterStack.ReceiveLetter("NoForcedWork".Translate(), "CouldNotFindMoreForcedWork".Translate(pawn.Name.ToStringShort), LetterDefOf.NeutralEvent, pawn);

			return false;
		}

		public static bool ContinueJob(Pawn_JobTracker tracker, Job lastJob, Pawn pawn, JobCondition condition)
		{
			Performance.ContinueJob_Start();

			_ = tracker;
			_ = lastJob;

			if (pawn == null || (pawn.IsColonist == false && pawn.IsColonyMech == false))
				return false;
			var forcedWork = ForcedWork.Instance;

			var forcedJob = forcedWork.GetForcedJob(pawn);
			Performance.Report(forcedJob, pawn);
			if (forcedJob == null)
				return Performance.ContinueJob_Stop(null, false);
			if (forcedJob.reentranceFlag)
				return Performance.ContinueJob_Stop(forcedJob, false);
			forcedJob.reentranceFlag = true;
			if (forcedJob.initialized == false)
			{
				forcedJob.initialized = true;
				return Performance.ContinueJob_Stop(forcedJob, false);
			}

			if (condition == JobCondition.InterruptForced)
			{
				Messages.Message("ForcedWorkWasInterrupted".Translate(pawn.Name.ToStringShort), MessageTypeDefOf.RejectInput);
				forcedWork.Remove(pawn);
				return Performance.ContinueJob_Stop(forcedJob, false);
			}

			Performance.GetNextJob_Start();
			while (true)
			{
				Performance.GetNextJob_Count();
				if (forcedJob.GetNextJob(out var job))
				{
					job.expiryInterval = 0;
					job.ignoreJoyTimeAssignment = true;
					job.playerForced = true;
					ForcedWork.QueueJob(pawn, job);
					Performance.GetNextJob_Stop();
					return Performance.ContinueJob_Stop(forcedJob, true);
				}

				forcedWork.RemoveForcedJob(pawn);
				forcedJob = forcedWork.GetForcedJob(pawn);
				if (forcedJob == null)
					break;
				forcedJob.initialized = true;
			}
			Performance.GetNextJob_Stop();

			forcedWork.Remove(pawn);
			return Performance.ContinueJob_Stop(forcedJob, false);
		}

		public void ToggleSmartBuilding()
		{
			buildSmart = !buildSmart;
		}

		public bool HasJob(Thing thing)
		{
			try
			{
				lock (pawn.Map)
				{
					return WorkGiverScanners.Any(scanner => thing.GetThingJob(pawn, scanner, true) != null);
				}
			}
			catch
			{
				return false;
			}
		}

		public bool HasJob(XY cell)
		{
			try
			{
				lock (pawn.Map)
				{
					return WorkGiverScanners.Any(scanner => ((IntVec3)cell).GetCellJob(pawn, scanner) != null);
				}
			}
			catch
			{
				return false;
			}
		}

		public IEnumerator ExpandThingTargets(bool iterate)
		{
			var tm = Find.TickManager;
			long counter = 0;
			while (cancelled == false && targets.Count > 0)
			{
				var needsYield = true;
				if (Find.Maps.Count > 0)
				{
					var map = pawn.Map;
					var thingGrid = map?.thingGrid;
					if (thingGrid != null && pawn.Spawned)
					{
						var maxCountVerifier = Achtung.Settings.maxForcedItems < AchtungSettings.UnlimitedForcedItems
							? (Func<bool>)(() => targets.Count < Achtung.Settings.maxForcedItems)
							: () => true;

						var things = targets.Select(target => target.item.thingInt).Where(thing => thing != null && thing.Spawned).ToHashSet();
						var newThings = things
							.SelectMany(thing => thing.AllCells()).Union(targets.Select(target => target.XY)).Distinct()
							.Expand(map, cellRadius + 1)
							.SelectMany(cell => thingGrid.ThingsListAtFast(cell)).Distinct()
							.ToArray();
						for (var i = 0; i < newThings.Length && cancelled == false && maxCountVerifier(); i++)
						{
							var newThing = newThings[i];
							if (things.Contains(newThing) == false && HasJob(newThing))
							{
								var item = new LocalTargetInfo(newThing);
								_ = targets.Add(new ForcedTarget(item, MaterialScore(item)));
							}
							if (iterate && ++counter % (tm.Paused ? ticksWaitPaused : ticksWaitRunning) == 0)
							{
								needsYield = false;
								yield return null;
							}
						}
					}
				}
				if (needsYield)
					yield return null;
				if (iterate == false)
					break;
			}
		}

		public IEnumerator ExpandCellTargets(bool iterate)
		{
			var tm = Find.TickManager;
			long counter = 0;
			while (cancelled == false && targets.Count > 0)
			{
				var needsYield = true;
				if (Find.Maps.Count > 0)
				{
					var map = pawn.Map;
					if (map != null && pawn.Spawned)
					{
						var maxCountVerifier = Achtung.Settings.maxForcedItems < AchtungSettings.UnlimitedForcedItems
							? (Func<bool>)(() => targets.Count < Achtung.Settings.maxForcedItems)
							: () => true;

						var newCells = targets
							.Select(target => target.XY)
							.Expand(map, cellRadius + 1)
							.ToArray();
						for (var i = 0; i < newCells.Length && cancelled == false && maxCountVerifier(); i++)
						{
							var cell = newCells[i];
							if (HasJob(cell))
							{
								var item = (LocalTargetInfo)cell;
								_ = targets.Add(new ForcedTarget(item, MaterialScore(item)));
							}
							if (iterate && ++counter % (tm.Paused ? ticksWaitPaused : ticksWaitRunning) == 0)
							{
								needsYield = false;
								yield return null;
							}
						}
					}
				}
				if (needsYield)
					yield return null;
				if (iterate == false)
					break;
			}
		}

		public IEnumerator ContractTargets()
		{
			var tm = Find.TickManager;
			long counter = 0;
			while (cancelled == false && targets.Count > 0)
			{
				var needsYield = true;
				if (started && Find.Maps.Count > 0)
				{
					var map = pawn.Map;
					if (map != null && pawn.Spawned && ForcedWork.Instance.HasForcedJob(pawn))
					{
						targets.RemoveWhere(targets => targets.item.thingInt?.Spawned == false);

						var cells = targets.Select(target => target.item.thingInt)
							.OfType<Thing>()
							.SelectMany(thing => thing.AllCells())
							.Union(targets.Select(target => target.XY))
							.Distinct()
							.ToArray();
						for (var i = 0; i < cells.Length && cancelled == false; i++)
						{
							var cell = cells[i];
							if (cell.InBounds(map) && HasJob(cell) == false)
								if (map.thingGrid.ThingsListAtFast(cell).All(thing => thing.Spawned == false || HasJob(thing) == false))
									_ = targets.RemoveWhere(target => target.XY == cell || (target.item.thingInt?.AllCells().Contains(cell) ?? false));
							if (++counter % (tm.Paused ? ticksWaitPaused : ticksWaitRunning) == 0)
							{
								needsYield = false;
								yield return null;
							}
						}
					}
				}
				if (needsYield)
					yield return null;
			}
		}

		public bool IsEmpty()
		{
			return targets.Count == 0;
		}

		public void ExposeData()
		{
			if (Scribe.mode == LoadSaveMode.Saving)
				_ = targets.RemoveWhere(target => target == null || target.item == null || target.item.IsValid == false || target.item.ThingDestroyed);

			Scribe_References.Look(ref pawn, "pawn");
			Scribe_Collections.Look(ref workgiverDefs, "workgivers", LookMode.Def);
			Scribe_Collections.Look(ref targets, "targets", LookMode.Deep);
			Scribe_Values.Look(ref isThingJob, "thingJob", false, true);
			Scribe_Values.Look(ref initialized, "inited", false, true);
			Scribe_Values.Look(ref cellRadius, "radius", 0, true);
			Scribe_Values.Look(ref buildSmart, "buildSmart", true, true);
			Scribe_References.Look(ref lastThing, "lastThing");
			Scribe_Values.Look(ref lastLocation, "lastLocation", XY.Invalid, true);

			if (Scribe.mode == LoadSaveMode.PostLoadInit)
				_ = targets.RemoveWhere(target => target == null || target.item == null || target.item.IsValid == false || target.item.ThingDestroyed);
		}
	}

	public class ForcedTarget : IExposable, IEquatable<ForcedTarget>
	{
		public LocalTargetInfo item = LocalTargetInfo.Invalid;
		public XY XY => item.Cell;
		public int materialScore = 0;

		public ForcedTarget()
		{
			item = LocalTargetInfo.Invalid;
			materialScore = 0;
		}

		public ForcedTarget(LocalTargetInfo item, int materialScore)
		{
			this.item = item;
			this.materialScore = materialScore;
		}

		public void ExposeData()
		{
			Scribe_TargetInfo.Look(ref item, false, "item", LocalTargetInfo.Invalid);
			Scribe_Values.Look(ref materialScore, "materialScore", 0, true);
		}

		public bool Equals(ForcedTarget other)
		{
			return item == other.item;
		}

		public override int GetHashCode()
		{
			return item.GetHashCode();
		}

		public bool IsValidTarget()
		{
			return item.HasThing == false || item.ThingDestroyed == false;
		}

		public bool IsBuilding()
		{
			if (item.HasThing == false)
				return false;
			var thing = item.thingInt;
			if (thing is Frame frame)
				return frame.def.entityDefToBuild == ThingDefOf.Wall;
			if (thing is Blueprint_Build blueprint)
				return blueprint.def.entityDefToBuild == ThingDefOf.Wall;
			return false;
		}

		public override string ToString()
		{
			if (item.HasThing)
				return $"{item.thingInt.def.defName}@{item.Cell.x}x{item.Cell.z}({materialScore})";
			return $"{item.Cell.x}x{item.Cell.z}({materialScore})";
		}
	}

	public static class ForcedExtensions
	{
		public static bool Ignorable(this WorkGiver_Scanner workgiver)
		{
			return (false
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
		}

		// fix for forbidden state in cached handlers
		//
		public static bool ShouldBeHaulable(Thing t)
		{
			// vanilla code but added 'Achtung.Settings.ignoreForbidden == false &&'
			if (Achtung.Settings.ignoreForbidden == false && t.IsForbidden(Faction.OfPlayer))
				return false;

			if (!t.def.alwaysHaulable)
			{
				if (!t.def.EverHaulable)
					return false;
				// vanilla code but added 'Achtung.Settings.ignoreForbidden == false &&'
				if (Achtung.Settings.ignoreForbidden == false && t.Map.designationManager.DesignationOn(t, DesignationDefOf.Haul) == null && !t.IsInAnyStorage())
					return false;
			}
			return !t.IsInValidBestStorage();
		}
		//
		public static bool ShouldBeMergeable(Thing t)
		{
			// vanilla code but added 'Achtung.Settings.ignoreForbidden ||'
			return (Achtung.Settings.ignoreForbidden || !t.IsForbidden(Faction.OfPlayer)) && t.GetSlotGroup() != null && t.stackCount != t.def.stackLimit;
		}

		public static Job GetThingJob(this Thing thing, Pawn pawn, WorkGiver_Scanner scanner, bool ignoreReserve = false)
		{
			if (thing == null || thing.Spawned == false)
				return null;
			var potentialWork = scanner.PotentialWorkThingRequest.Accepts(thing);
			if (potentialWork == false)
			{
				var workThingsGlobal = scanner.PotentialWorkThingsGlobal(pawn);
				workThingsGlobal ??= pawn.Map.listerThings.ThingsMatching(scanner.PotentialWorkThingRequest);
				if (workThingsGlobal != null && workThingsGlobal.Contains(thing))
					potentialWork = true;
			}
			if (potentialWork == false && scanner is WorkGiver_Haul)
				potentialWork = ShouldBeHaulable(thing);
			else if (potentialWork == false && scanner is WorkGiver_Merge)
				potentialWork = ShouldBeMergeable(thing);

			if (potentialWork)
				if (scanner.MissingRequiredCapacity(pawn) == null)
					if (scanner.HasJobOnThing(pawn, thing, true))
					{
						var job = scanner.JobOnThing(pawn, thing, true);
						if (job != null)
						{
							var ignorable = scanner.Ignorable();
							if (Achtung.Settings.ignoreForbidden && ignorable || thing.IsForbidden(pawn) == false)
								if (Achtung.Settings.ignoreRestrictions && ignorable || thing.Position.InAllowedArea(pawn))
								{
									if (
										(ignoreReserve == false && pawn.CanReserveAndReach(thing, scanner.PathEndMode, Danger.Deadly))
										||
										(ignoreReserve && pawn.CanReach(thing, scanner.PathEndMode, Danger.Deadly))
									)
										return job;
								}
						}
					}
			return null;
		}

		public static Job GetCellJob(this IntVec3 cell, Pawn pawn, WorkGiver_Scanner workgiver, bool ignoreReserve = false)
		{
			if (cell.IsValid == false)
				return null;
			if (workgiver.PotentialWorkCellsGlobal(pawn).Contains(cell))
				if (workgiver.MissingRequiredCapacity(pawn) == null)
					if (workgiver.HasJobOnCell(pawn, cell, ignoreReserve))
					{
						var job = workgiver.JobOnCell(pawn, cell);
						if (pawn.CanReach(cell, workgiver.PathEndMode, Danger.Deadly))
							return job;
					}
			return null;
		}
	}
}
