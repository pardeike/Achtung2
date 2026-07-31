using RimBridgeServer.Sdk;
using RimWorld;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Verse;

namespace AchtungMod;

public sealed partial class AchtungBridgeTools
{
	sealed class ForcedTargetSaveLoadFixture
	{
		public string targetKind { get; set; }
		public string pawnLoadId { get; set; }
		public string pawnThingId { get; set; }
		public string targetLoadId { get; set; }
		public string targetThingId { get; set; }
		public string targetDefName { get; set; }
		public string expectedWorkgiverDefName { get; set; }
		public string saveName { get; set; }
		public string savePath { get; set; }
		public IntVec3 targetCell { get; set; } = IntVec3.Invalid;
		public int materialScore { get; set; }
	}

	sealed class ForcedTargetSaveLoadSnapshot
	{
		public bool success { get; set; }
		public bool pawnFound { get; set; }
		public bool hasForcedJob { get; set; }
		public bool pawnReferenceMatches { get; set; }
		public bool isThingJob { get; set; }
		public bool targetIsValid { get; set; }
		public bool targetHasThing { get; set; }
		public bool targetIsExpectedType { get; set; }
		public bool targetThingExistsOnMap { get; set; }
		public bool targetThingIdMatches { get; set; }
		public bool targetCellMatches { get; set; }
		public bool materialScoreMatches { get; set; }
		public bool lastAssignedCellMatches { get; set; }
		public bool workgiverMatches { get; set; }
		public int targetCount { get; set; }
		public int targetX { get; set; } = int.MinValue;
		public int targetZ { get; set; } = int.MinValue;
		public int materialScore { get; set; }
		public string pawnThingId { get; set; }
		public string targetThingId { get; set; }
		public string targetLoadId { get; set; }
		public string targetDefName { get; set; }
		public string[] workgivers { get; set; } = [];
	}

	sealed class ForcedTargetSaveLoadCleanup
	{
		public bool forcedJobRemoved { get; set; }
		public bool targetRemoved { get; set; }
		public bool saveDeleted { get; set; }
		public string error { get; set; }
	}

	sealed class ForcedTargetSaveLoadContract
	{
		public bool success { get; set; }
		public string contract { get; set; } = "A real Achtung forced target must retain its Thing reference and target metadata through RimWorld's complete save/load pipeline.";
		public string stage { get; set; }
		public string targetKind { get; set; }
		public string error { get; set; }
		public string saveName { get; set; }
		public string pawnLoadId { get; set; }
		public string pawnThingId { get; set; }
		public string targetLoadId { get; set; }
		public string targetThingId { get; set; }
		public string targetDefName { get; set; }
		public string expectedWorkgiverDefName { get; set; }
		public int targetX { get; set; }
		public int targetZ { get; set; }
		public int expectedMaterialScore { get; set; }
		public bool saveSucceeded { get; set; }
		public bool saveFileCreated { get; set; }
		public object saveError { get; set; }
		public object saveResult { get; set; }
		public bool loadSucceeded { get; set; }
		public object loadError { get; set; }
		public object loadResult { get; set; }
		public ForcedTargetSaveLoadSnapshot actual { get; set; }
		public ForcedTargetSaveLoadCleanup cleanup { get; set; }
	}

	static readonly SemaphoreSlim forcedTargetSaveLoadGate = new(1, 1);

	[Tool(
		"achtung/test_forced_target_save_load",
		Description = "Create one temporary Achtung forced target, save and fully reload the game, verify the restored target reference and metadata, then remove the fixture and save file.")]
	public static async Task<object> TestForcedTargetSaveLoad(
		IRimBridgeContext ctx,
		CancellationToken cancellationToken,
		[ToolParameter(Description = "Target representation to exercise: blueprint, frame, or thing.", Required = false, DefaultValue = "blueprint")] string targetKind = "blueprint",
		[ToolParameter(Description = "Maximum wait for the full save reload.", Required = false, DefaultValue = 120000)] int timeoutMs = 120000)
	{
		if (ctx == null)
			return new { success = false, error = "RimBridge context was not injected." };
		if (timeoutMs < 10000 || timeoutMs > 300000)
			return new { success = false, error = "timeoutMs must be between 10000 and 300000." };

		targetKind = targetKind?.Trim().ToLowerInvariant();
		if (targetKind is not ("blueprint" or "frame" or "thing"))
			return new { success = false, error = "targetKind must be blueprint, frame, or thing." };

		await forcedTargetSaveLoadGate.WaitAsync(cancellationToken);
		var result = new ForcedTargetSaveLoadContract
		{
			stage = "setup",
			targetKind = targetKind
		};
		ForcedTargetSaveLoadFixture fixture = null;
		try
		{
			var setupError = null as string;
			await ctx.MainThread.InvokeAsync(() =>
			{
				if (TryCreateForcedTargetSaveLoadFixture(targetKind, out fixture, out var error) == false)
					setupError = error;
			}, cancellationToken);

			if (fixture == null)
			{
				result.error = setupError ?? "Could not create the forced-target save/load fixture.";
				return result;
			}

			result.saveName = fixture.saveName;
			result.pawnLoadId = fixture.pawnLoadId;
			result.pawnThingId = fixture.pawnThingId;
			result.targetLoadId = fixture.targetLoadId;
			result.targetThingId = fixture.targetThingId;
			result.targetDefName = fixture.targetDefName;
			result.expectedWorkgiverDefName = fixture.expectedWorkgiverDefName;
			result.targetX = fixture.targetCell.x;
			result.targetZ = fixture.targetCell.z;
			result.expectedMaterialScore = fixture.materialScore;

			result.stage = "save";
			var save = await ctx.Tools.CallAsync(
				"rimworld/save_game",
				new { saveName = fixture.saveName },
				cancellationToken: cancellationToken);
			result.saveSucceeded = save.Succeeded();
			result.saveError = save.Error;
			result.saveResult = save.Result;
			if (result.saveSucceeded == false)
			{
				result.error = "RimWorld did not save the forced-target fixture.";
				return result;
			}

			result.saveFileCreated = File.Exists(fixture.savePath);
			if (result.saveFileCreated == false)
			{
				result.error = $"The save call succeeded but {fixture.savePath} was not created.";
				return result;
			}

			result.stage = "load";
			var load = await ctx.Tools.CallAsync(
				"rimworld/load_game_ready",
				new
				{
					saveName = fixture.saveName,
					timeoutMs,
					readiness = "playable",
					pauseIfNeeded = true,
					ignoreModCompatibility = false
				},
				cancellationToken: cancellationToken);
			result.loadSucceeded = load.Succeeded();
			result.loadError = load.Error;
			result.loadResult = load.Result;
			if (result.loadSucceeded == false)
			{
				result.error = "RimWorld did not reload the forced-target fixture to playable readiness.";
				return result;
			}

			result.stage = "verify";
			result.actual = await ctx.MainThread.InvokeAsync(
				() => CaptureForcedTargetSaveLoadSnapshot(fixture),
				cancellationToken);
			result.success = result.actual.success;
			if (result.success == false)
				result.error = "The forced target did not retain its complete saved representation.";
			return result;
		}
		catch (Exception ex)
		{
			result.error = $"{ex.GetType().Name}: {ex.Message}";
			return result;
		}
		finally
		{
			try
			{
				if (fixture != null)
				result.cleanup = await CleanupForcedTargetSaveLoadFixture(ctx, fixture);
			}
			catch (Exception ex)
			{
				result.cleanup = new ForcedTargetSaveLoadCleanup
				{
					error = $"{ex.GetType().Name}: {ex.Message}"
				};
			}
			finally
			{
				result.stage = "complete";
				result.success &= result.cleanup?.forcedJobRemoved == true
					&& result.cleanup.targetRemoved
					&& result.cleanup.saveDeleted;
				_ = forcedTargetSaveLoadGate.Release();
			}
		}
	}

	static bool TryCreateForcedTargetSaveLoadFixture(
		string targetKind,
		out ForcedTargetSaveLoadFixture fixture,
		out string error)
	{
		fixture = null;
		error = null;
		Pawn pawn = null;
		Thing targetThing = null;
		try
		{
			if (Current.Game == null || Find.CurrentMap == null || Find.TickManager == null)
			{
				error = "No playable current map is loaded.";
				return false;
			}
			if (Find.TickManager.CurTimeSpeed != TimeSpeed.Paused)
			{
				error = "Pause the game before running the save/load contract.";
				return false;
			}
			if (MultiplayerSupport.IsActive)
			{
				error = "The save/load fixture intentionally mutates local state and cannot run inside an active Multiplayer session.";
				return false;
			}

			var forcedWork = ForcedWork.Instance;
			pawn = Find.CurrentMap.mapPawns.FreeColonistsSpawned
				.Where(candidate => forcedWork.HasForcedJob(candidate, ignorePreparing: true) == false)
				.Where(candidate => forcedWork.IsPreparing(candidate) == false)
				.OrderBy(candidate => candidate.thingIDNumber)
				.FirstOrDefault();
			if (pawn == null)
			{
				error = "No free player colonist without existing Achtung prioritized work is available.";
				return false;
			}
			if (TryFindForcedTargetSaveLoadCell(Find.CurrentMap, pawn.Position, out var targetCell) == false)
			{
				error = "Could not find an empty buildable fixture cell near the colony.";
				return false;
			}

			var expectedWorkgiverDefName = targetKind switch
			{
				"frame" => "ConstructDeliverResourcesToFrames",
				"thing" => "HaulGeneral",
				_ => "ConstructDeliverResourcesToBlueprints"
			};
			var workgiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail(expectedWorkgiverDefName);
			if (workgiver == null)
			{
				error = $"The vanilla {targetKind} construction WorkGiverDef is unavailable.";
				return false;
			}

			if (targetKind == "blueprint")
			{
				targetThing = GenConstruct.PlaceBlueprintForBuild(
					ThingDefOf.Wall,
					targetCell,
					Find.CurrentMap,
					Rot4.North,
					Faction.OfPlayer,
					ThingDefOf.WoodLog,
					null,
					null,
					false);
			}
			else if (targetKind == "frame")
			{
				var frame = ThingMaker.MakeThing(ThingDefOf.Wall.frameDef, ThingDefOf.WoodLog);
				targetThing = GenSpawn.Spawn(frame, targetCell, Find.CurrentMap, Rot4.North);
			}
			else if (targetKind == "thing")
			{
				var thing = ThingMaker.MakeThing(ThingDefOf.Steel);
				targetThing = GenSpawn.Spawn(thing, targetCell, Find.CurrentMap, Rot4.North);
			}
			if (targetThing == null || targetThing.Spawned == false)
				throw new InvalidOperationException($"Could not create a spawned {targetKind} target.");

			_ = forcedWork.AddForcedJob(pawn, [workgiver], new LocalTargetInfo(targetThing), out var forcedJob);
			var forcedTarget = forcedJob.targets.Single();
			var saveName = $"Achtung_ForcedTarget_{targetKind}_{Guid.NewGuid():N}";
			var savePath = GenFilePaths.FilePathForSavedGame(saveName);
			if (File.Exists(savePath))
				throw new IOException($"Refusing to overwrite existing fixture save {savePath}.");

			fixture = new ForcedTargetSaveLoadFixture
			{
				targetKind = targetKind,
				pawnLoadId = pawn.GetUniqueLoadID(),
				pawnThingId = pawn.ThingID,
				targetLoadId = targetThing.GetUniqueLoadID(),
				targetThingId = targetThing.ThingID,
				targetDefName = targetThing.def.defName,
				expectedWorkgiverDefName = expectedWorkgiverDefName,
				targetCell = targetCell,
				materialScore = forcedTarget.materialScore,
				saveName = saveName,
				savePath = savePath
			};
			return true;
		}
		catch (Exception ex)
		{
			if (pawn != null)
				ForcedWork.Instance.Remove(pawn);
			if (targetThing != null && targetThing.Destroyed == false)
				targetThing.Destroy(DestroyMode.Vanish);
			error = $"{ex.GetType().Name}: {ex.Message}";
			fixture = null;
			return false;
		}
	}

	static bool TryFindForcedTargetSaveLoadCell(Map map, IntVec3 pawnCell, out IntVec3 targetCell)
	{
		var roots = new[]
		{
			pawnCell,
			new IntVec3(map.Size.x / 2, 0, map.Size.z / 2)
		};
		foreach (var root in roots)
		{
			foreach (var cell in GenRadial.RadialCellsAround(root, 28f, true))
			{
				if (cell.InBounds(map)
					&& cell.Standable(map)
					&& cell.Fogged(map) == false
					&& cell.GetThingList(map).All(thing => thing.def.category == ThingCategory.Filth))
				{
					targetCell = cell;
					return true;
				}
			}
		}
		targetCell = IntVec3.Invalid;
		return false;
	}

	static ForcedTargetSaveLoadSnapshot CaptureForcedTargetSaveLoadSnapshot(ForcedTargetSaveLoadFixture fixture)
	{
		var pawn = FindPawn(fixture.pawnLoadId);
		var forcedJob = ForcedWork.Instance.GetForcedJob(pawn);
		var forcedTarget = forcedJob?.targets.FirstOrDefault();
		var item = forcedTarget?.item ?? LocalTargetInfo.Invalid;
		var targetThing = item.thingInt;
		var targetThingExistsOnMap = Find.Maps
			.SelectMany(map => map.listerThings.AllThings)
			.Any(thing => thing.ThingID == fixture.targetThingId);
		var workgivers = forcedJob?.workgiverDefs
			.Select(def => def?.defName)
			.Where(defName => defName != null)
			.ToArray() ?? [];
		var snapshot = new ForcedTargetSaveLoadSnapshot
		{
			pawnFound = pawn != null,
			hasForcedJob = forcedJob != null,
			pawnReferenceMatches = forcedJob?.pawn == pawn,
			isThingJob = forcedJob?.isThingJob == true,
			targetCount = forcedJob?.targets.Count ?? 0,
			targetIsValid = item.IsValid,
			targetHasThing = item.HasThing,
			targetIsExpectedType = fixture.targetKind switch
			{
				"blueprint" => targetThing is Blueprint_Build,
				"frame" => targetThing is Frame,
				"thing" => targetThing?.def == ThingDefOf.Steel
					&& targetThing is not Blueprint_Build
					&& targetThing is not Frame,
				_ => false
			},
			targetThingExistsOnMap = targetThingExistsOnMap,
			targetThingIdMatches = targetThing?.ThingID == fixture.targetThingId,
			targetCellMatches = item.Cell == fixture.targetCell,
			materialScoreMatches = forcedTarget?.materialScore == fixture.materialScore,
			lastAssignedCellMatches = forcedJob?.lastAssignedCell == fixture.targetCell,
			workgiverMatches = workgivers.Contains(fixture.expectedWorkgiverDefName),
			targetX = item.Cell.x,
			targetZ = item.Cell.z,
			materialScore = forcedTarget?.materialScore ?? int.MinValue,
			pawnThingId = pawn?.ThingID,
			targetThingId = targetThing?.ThingID,
			targetLoadId = targetThing?.GetUniqueLoadID(),
			targetDefName = targetThing?.def?.defName,
			workgivers = workgivers
		};
		snapshot.success = snapshot.pawnFound
			&& snapshot.hasForcedJob
			&& snapshot.pawnReferenceMatches
			&& snapshot.isThingJob
			&& snapshot.targetCount == 1
			&& snapshot.targetIsValid
			&& snapshot.targetHasThing
			&& snapshot.targetIsExpectedType
			&& snapshot.targetThingExistsOnMap
			&& snapshot.targetThingIdMatches
			&& snapshot.targetCellMatches
			&& snapshot.materialScoreMatches
			&& snapshot.lastAssignedCellMatches
			&& snapshot.workgiverMatches;
		return snapshot;
	}

	static async Task<ForcedTargetSaveLoadCleanup> CleanupForcedTargetSaveLoadFixture(
		IRimBridgeContext ctx,
		ForcedTargetSaveLoadFixture fixture)
	{
		var cleanup = await ctx.MainThread.InvokeAsync(() =>
		{
			var currentPawn = FindPawn(fixture.pawnLoadId);
			if (currentPawn != null)
				ForcedWork.Instance.Remove(currentPawn);

			var currentTarget = Find.Maps
				.SelectMany(map => map.listerThings.AllThings)
				.FirstOrDefault(thing => thing.ThingID == fixture.targetThingId);
			if (currentTarget != null && currentTarget.Destroyed == false)
				currentTarget.Destroy(DestroyMode.Vanish);

			return new ForcedTargetSaveLoadCleanup
			{
				forcedJobRemoved = currentPawn == null
					|| ForcedWork.Instance.HasForcedJob(currentPawn, ignorePreparing: true) == false,
				targetRemoved = Find.Maps
					.SelectMany(map => map.listerThings.AllThings)
					.All(thing => thing.ThingID != fixture.targetThingId)
			};
		}, CancellationToken.None);

		if (File.Exists(fixture.savePath))
			File.Delete(fixture.savePath);
		cleanup.saveDeleted = File.Exists(fixture.savePath) == false;
		return cleanup;
	}
}
