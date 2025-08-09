using HarmonyLib;
using RimWorld;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace AchtungMod;

public class ForcedJob : IExposable
{
	public HashSet<ForcedTarget> targets = [];
	public List<ForcedTarget> targets_temp_list = [];
	public List<LocalTargetInfo> smartTargetsCached = null;
	public Pawn pawn = null;
	public IntVec3 startCell = IntVec3.Invalid;
	public List<WorkGiverDef> workgiverDefs = [];
	public List<WorkGiver_Scanner> workgiverScanners = [];
	public readonly QuotaCache<Thing, bool> getThingJobCache = new(10);
	public readonly QuotaCache<IntVec3, bool> getCellJobCache = new(10);
	public bool isThingJob = false;
	public bool initialized = false;
	public int cellRadius = 0;
	public bool started = false;
	public bool cancelled = false;
	static readonly Dictionary<BuildableDef, int> TypeScores = new()
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
		{ ThingDefOf.Grave, 1 },
	};

	static readonly HashSet<Type> buildingSmartWorkgiverDefs = [
		typeof(WorkGiver_ConstructDeliverResourcesToFrames),
		typeof(WorkGiver_ConstructDeliverResourcesToBlueprints),
		typeof(WorkGiver_ConstructFinishFrames)
	];

	// default constructor for deserialization
	public ForcedJob()
	{
		workgiverDefs = [];
		workgiverScanners = [];
		targets = [];
		smartTargetsCached = null;
	}

	// called from AddForcedJob during gameplay
	public ForcedJob(Pawn pawn, LocalTargetInfo item, List<WorkGiverDef> workgiverDefs)
	{
		this.pawn = pawn;
		startCell = pawn.Position;
		this.workgiverDefs = [.. workgiverDefs.Where(wgd => wgd?.giverClass != null)];
		workgiverScanners = [.. workgiverDefs.Select(wgd => wgd.Worker).OfType<WorkGiver_Scanner>()];
		targets = [new ForcedTarget(item, MaterialScore(item))];
		smartTargetsCached = null;
		isThingJob = item.HasThing;
	}

	public void Start() => started = true;

	public void Cleanup() => cancelled = true;

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

	public void Add(LocalTargetInfo item)
	{
		_ = targets.Add(new ForcedTarget(item, MaterialScore(item)));
		smartTargetsCached = null;
	}

	public void Replace(Thing thing, Thing replacement)
	{
		if (workgiverDefs.Any(def => def.giverClass == typeof(WorkGiver_ConstructDeliverResourcesToBlueprints)))
		{
			_ = targets.RemoveWhere(target => target.item.thingInt == thing);
			_ = targets.Add(new ForcedTarget(replacement, MaterialScore(replacement)));
			smartTargetsCached = null;
		}
	}

	public IEnumerable<LocalTargetInfo> GetSortedTargets()
	{
		var map = pawn.Map;
		var pos = pawn.Position;
		var pathGrid = map.pathing.For(pawn).pathGrid;
		var mapWidth = map.Size.x;

		var result = targets.Where(target => target.IsValidTarget() && Tools.IsFreeTarget(pawn, target));
		var shouldBeDoneSmart = Achtung.Settings.buildingSmart;
		shouldBeDoneSmart &= workgiverDefs.Any(def => buildingSmartWorkgiverDefs.Contains(def.giverClass));

		if (shouldBeDoneSmart && isThingJob && startCell.IsValid && result.Any())
		{
			if (smartTargetsCached == null)
			{
				var someTarget = result.OrderByDescending(target => target.item.Cell.DistanceTo(startCell)).FirstOrDefault();
				if (someTarget != null)
				{
					var path = map.pathFinder.FindPathNow(startCell, someTarget.item.Cell, TraverseParms.For(pawn, Danger.Deadly));
					if (path != PawnPath.NotFound)
					{
						var itemsByCell = new Dictionary<IntVec3, LocalTargetInfo>();
						foreach (var t in result)
						{
							var cell = t.item.Cell;
							// ignore additional entries for the same cell
							if (!itemsByCell.ContainsKey(cell))
								itemsByCell[cell] = t.item;
						}
						LocalTargetInfo? entry = null;
						var nodes = path.NodesReversed;
						for (var i = nodes.Count - 1; i >= 0; i--)
							if (itemsByCell.TryGetValue(nodes[i], out var item))
							{
								entry = item;
								break;
							}
						if (entry != null)
						{
							var visited = new HashSet<LocalTargetInfo> { entry.Value };
							var queue = new Queue<LocalTargetInfo>();
							queue.Enqueue(entry.Value);
							smartTargetsCached = [];
							var offsets = new[] { new IntVec3(1, 0, 0), new IntVec3(-1, 0, 0), new IntVec3(0, 0, 1), new IntVec3(0, 0, -1) };
							while (queue.Count > 0)
							{
								var item = queue.Dequeue();
								smartTargetsCached.Insert(0, item);
								foreach (var off in offsets)
								{
									var cell = item.Cell + off;
									if (itemsByCell.TryGetValue(cell, out var neighbourItem) && visited.Add(neighbourItem))
										queue.Enqueue(neighbourItem);
								}
							}
						}
					}
					path.ReleaseToPool();
				}
			}
			if (smartTargetsCached != null)
				return smartTargetsCached.Intersect(result.Select(t => t.item));
		}

		return result
			.OrderByDescending(target =>
			{
				var distanceFromStart = target.item.Cell.DistanceToSquared(pawn.Position);
				return 1000 * target.materialScore - distanceFromStart;
			})
			.Select(target => target.item);
	}

	public void ExpandJob(int count)
		=> pawn.Map.Expand(isThingJob ? ExpandThingTargets : ExpandCellTargets, count);

	public bool NonForcedShouldIgnore(IntVec3 cell) => targets.Any(target => target.XY == cell && target.IsBuilding());

	public bool GetNextNonConflictingJob(ForcedWork forcedWork)
	{
		foreach (var workgiverDef in workgiverDefs)
		{
			var workgiver = workgiverDef.giverClass == null ? null : workgiverDef.Worker as WorkGiver_Scanner;
			if (workgiver == null)
				continue;

			var success = false;
			foreach (var target in GetSortedTargets())
			{
				var job = ForcedWork.GetJobItem(pawn, workgiver, target);
				if (job != null)
				{
					if (job.TargetCells().Any(c => pawn.Map.reservationManager.IsReserved(c))) continue;
					if (job.TargetThings().Any(t => pawn.Map.reservationManager.IsReserved(t))) continue;

					success = pawn.jobs.TryTakeOrderedJobPrioritizedWork(job, workgiver, target.Cell);
					if (success) break;
				}
			}
			if (success) return true;
		}

		forcedWork.Remove(pawn);
		return false;
	}

	public bool GetNextJob(out Job job)
	{
		job = null;

		var workGiversByPrio = workgiverScanners.OrderBy(worker =>
		{
			if (worker is WorkGiver_ConstructDeliverResourcesToBlueprints) return 1;
			if (worker is WorkGiver_ConstructDeliverResourcesToFrames) return 2;
			if (worker is WorkGiver_ConstructDeliverResources) return 3;
			if (worker is WorkGiver_Haul) return 4;
			if (worker is WorkGiver_PlantsCut) return 5;
			if (worker is WorkGiver_ConstructFinishFrames) return 6;
			if (worker is WorkGiver_ConstructAffectFloor) return 7;
			return 999;
		});

		var exist = false;
		foreach (var workgiver in workGiversByPrio)
			foreach (var target in GetSortedTargets())
			{
				exist = true;

				if (isThingJob)
				{
					if (Achtung.usedThingsPerTick.Contains(target.thingInt))
						continue;

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
						_ = Achtung.usedThingsPerTick.Add(target.thingInt);
					return true;
				}
			}

		if (exist && Achtung.Settings.forcedEndedLetter)
			Find.LetterStack.ReceiveLetter("NoForcedWork".Translate(), "CouldNotFindMoreForcedWork".Translate(pawn.Name.ToStringShort), LetterDefOf.NeutralEvent, pawn);

		return false;
	}

	public static bool ContinueJob(Pawn pawn, JobCondition condition)
	{
		if (pawn == null || PawnAttackGizmoUtility.CanOrderPlayerPawn(pawn) == false)
			return false;

		var forcedWork = ForcedWork.Instance;
		var forcedJob = forcedWork.GetForcedJob(pawn);
		if (forcedJob == null) return false;
		if (forcedJob.initialized == false)
		{
			forcedJob.initialized = true;
			return false;
		}

		if (condition == JobCondition.InterruptForced)
		{
			forcedJob.ExpandJob(4);
			return forcedJob.GetNextNonConflictingJob(forcedWork);
		}

		while (true)
		{
			if (forcedJob.GetNextJob(out var job))
			{
				job.expiryInterval = 0;
				job.ignoreJoyTimeAssignment = true;
				job.playerForced = true;
				ForcedWork.QueueJob(pawn, job);
				return true;
			}

			forcedWork.RemoveForcedJob(pawn);
			forcedJob = forcedWork.GetForcedJob(pawn);
			if (forcedJob == null)
				break; // exit loop
			forcedJob.ExpandJob(4);
			forcedJob.initialized = true;
		}

		forcedWork.Remove(pawn);
		return false;
	}

	public bool ThingHasJob(Thing thing)
	{
		try
		{
			lock (pawn.Map)
			{
				return getThingJobCache.Get(thing, t => workgiverScanners.Any(scanner => t.GetThingJob(pawn, scanner, true) != null));
			}
		}
		catch
		{
			return false;
		}
	}

	public bool CellHasJob(XY cell)
	{
		try
		{
			lock (pawn.Map)
			{
				return getCellJobCache.Get(cell, c => workgiverScanners.Any(scanner => ((IntVec3)c).GetCellJob(pawn, scanner) != null));
			}
		}
		catch
		{
			return false;
		}
	}

	public IEnumerator<int> ExpandThingTargets(Map map)
	{
		var thingGrid = map.thingGrid;
		var totalAdded = 0;
		if (thingGrid != null)
		{
			var maxCountVerifier = Achtung.Settings.maxForcedItems < AchtungSettings.UnlimitedForcedItems
				? (Func<bool>)(() => targets.Count < Achtung.Settings.maxForcedItems)
				: () => true;
			if (maxCountVerifier())
			{
				var things = targets.Select(target => target.item.thingInt).Where(thing => thing != null && thing.Spawned).ToHashSet();
				var newThings = things
					.SelectMany(thing => thing.AllCells()).Union(targets.Select(target => target.XY)).Distinct()
					.Expand(map, cellRadius + 1)
					.SelectMany(cell => thingGrid.ThingsListAtFast(cell)).Distinct()
					.ToArray();
				yield return -1;

				for (var i = 0; i < newThings.Length && cancelled == false && maxCountVerifier(); i++)
				{
					var newThing = newThings[i];
					if (things.Contains(newThing) == false && ThingHasJob(newThing))
					{
						LocalTargetInfo item = newThing;
						_ = targets.Add(new ForcedTarget(item, MaterialScore(item)));
						smartTargetsCached = null;
						totalAdded++;
					}
					yield return -1;
				}
			}
		}
		yield return totalAdded;
	}

	public IEnumerator<int> ExpandCellTargets(Map map)
	{
		var maxCountVerifier = Achtung.Settings.maxForcedItems < AchtungSettings.UnlimitedForcedItems
			? (Func<bool>)(() => targets.Count < Achtung.Settings.maxForcedItems)
			: () => true;
		var totalAdded = 0;
		if (maxCountVerifier())
		{
			var newCells = targets
				.Select(target => target.XY)
				.Expand(map, cellRadius + 1)
				.ToArray();
			yield return -1;

			for (var i = 0; i < newCells.Length && cancelled == false && maxCountVerifier(); i++)
			{
				var cell = newCells[i];
				if (CellHasJob(cell))
				{
					LocalTargetInfo item = cell;
					_ = targets.Add(new ForcedTarget(item, 0));
					smartTargetsCached = null;
					totalAdded++;
				}
				yield return -1;
			}
		}
		yield return totalAdded;
	}

	public IEnumerator ContractTargets(Map map)
	{
		_ = targets.RemoveWhere(targets => targets.item.thingInt?.Spawned == false);

		var cells = targets.Select(target => target.item.thingInt)
			.OfType<Thing>()
			.SelectMany(thing => thing.AllCells())
			.Union(targets.Select(target => target.XY))
			.Distinct()
			.ToArray();
		yield return null;

		for (var i = 0; i < cells.Length && cancelled == false; i++)
		{
			var cell = cells[i];
			if (cell.InBounds(map) && CellHasJob(cell) == false)
				if (map.thingGrid.ThingsListAtFast(cell).All(thing => thing.Spawned == false || ThingHasJob(thing) == false))
					_ = targets.RemoveWhere(target => target.XY == cell || (target.item.thingInt?.AllCells().Contains(cell) ?? false));
			yield return null;
		}
	}

	public bool IsEmpty() => targets.Count == 0;

	void ScribeTargets()
	{
		if (Scribe.mode == LoadSaveMode.Saving)
			targets_temp_list = [.. targets];

		Scribe_Collections.Look(ref targets_temp_list, "targets", LookMode.Deep);

		if (Scribe.mode == LoadSaveMode.Saving)
			targets_temp_list.Clear();

		if (Scribe.mode == LoadSaveMode.PostLoadInit)
		{
			targets = [.. targets_temp_list];
			smartTargetsCached = null;
			targets_temp_list.Clear();
		}
	}

	public void ExposeData()
	{
		Scribe_References.Look(ref pawn, "pawn");
		Scribe_Values.Look(ref startCell, "startCell", IntVec3.Invalid, true);
		Scribe_Collections.Look(ref workgiverDefs, "workgivers", LookMode.Def);
		ScribeTargets();
		Scribe_Values.Look(ref isThingJob, "thingJob", false, true);
		Scribe_Values.Look(ref initialized, "inited", false, true);
		Scribe_Values.Look(ref cellRadius, "radius", 0, true);

		if (Scribe.mode == LoadSaveMode.PostLoadInit)
			workgiverScanners = [.. workgiverDefs.Select(wgd => wgd.Worker).OfType<WorkGiver_Scanner>()];
	}
}
