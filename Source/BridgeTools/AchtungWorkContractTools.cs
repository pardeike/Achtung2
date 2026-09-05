using HarmonyLib;
using RimBridgeServer.Sdk;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Verse;
using Verse.AI;

namespace AchtungMod;

public sealed partial class AchtungBridgeTools
{
	[Tool("achtung/test_work_hardening_contract", Description = "Without ticking, verify production queued-target enumeration, nested permission-scope exception cleanup, unrelated-job rejection, and extra-menu width. Uses two temporary unspawned items; does not change pawn jobs or settings.")]
	public static object TestWorkHardeningContract(string pawnId = null)
	{
		var pawn = FindPawn(pawnId);
		if (pawn == null) return new { success = false, error = "Select or specify an existing spawned pawn." };
		Thing first = null;
		Thing second = null;
		try
		{
			first = ThingMaker.MakeThing(ThingDefOf.Steel);
			second = ThingMaker.MakeThing(ThingDefOf.WoodLog);
			var a = new IntVec3(1, 0, 1);
			var b = new IntVec3(2, 0, 2);
			var c = new IntVec3(3, 0, 3);
			var job = new Job
			{
				targetA = first,
				targetB = a,
				targetC = LocalTargetInfo.Invalid,
				targetQueueA = [second, b, first],
				targetQueueB = [second, c, a, LocalTargetInfo.Invalid]
			};
			var things = job.TargetThings().ToArray();
			var cells = job.TargetCells().ToArray();
			var queuedThings = things.Length == 2 && things.Contains(first) && things.Contains(second);
			var queuedCells = cells.Length == 3 && cells.Contains(a) && cells.Contains(b) && cells.Contains(c);
			var before = ForcedExtensions.AppliesTo(pawn);
			bool innerRejected = false, outerRestored = false, directRestored = false;
			using (new ForcedExtensions.PermissionScope(pawn, directTarget: first))
			{
				try
				{
					using (new ForcedExtensions.PermissionScope(pawn, job, second))
					{
						innerRejected = !ForcedExtensions.AppliesTo(pawn) && !ForcedExtensions.IsDirectTarget(pawn, second);
						throw new InvalidOperationException("Expected permission-scope fixture exception.");
					}
				}
				catch (InvalidOperationException)
				{
					outerRestored = ForcedExtensions.AppliesTo(pawn);
					directRestored = ForcedExtensions.IsDirectTarget(pawn, first) && !ForcedExtensions.IsDirectTarget(pawn, second);
				}
			}
			var scopeRestored = ForcedExtensions.AppliesTo(pawn) == before;
			var option = new ForcedMultiFloatMenuOption([pawn], [], null, "Width contract", 75f);
			var expectedWidth = 75f + ForcedMultiFloatMenuOption.buttonSpace + ForcedMultiFloatMenuOption.buttonWidth;
			var widthPreserved = Math.Abs(option.extraPartWidth - expectedWidth) < 0.001f;
			return new
			{
				success = queuedThings && queuedCells && innerRejected && outerRestored && directRestored && scopeRestored && widthPreserved,
				queuedThings, queuedCells, innerRejected, outerRestored, directRestored, scopeRestored, widthPreserved,
				actualWidth = option.extraPartWidth, expectedWidth,
				limit = "No GUI rendering, game ticks, settings changes or pawn-job mutations."
			};
		}
		finally
		{
			first?.Destroy();
			second?.Destroy();
		}
	}

	[Tool("achtung/test_rescue_priority_roundtrip", Description = "Toggle the real Rescue setting off/on twice and verify every existing pawn's indexed work priorities. Leaves Rescue enabled; its priority is copied from Doctor. Does not create pawns or restore earlier settings.")]
	public static async Task<object> TestRescuePriorityRoundtrip(IRimBridgeContext ctx, CancellationToken cancellationToken)
	{
		return await ctx.MainThread.InvokeAsync<object>(() =>
		{
			if (Current.Game == null || Find.CurrentMap == null)
				return new { success = false, error = "Load a game first." };
			if (MultiplayerSupport.IsActive)
				return new { success = false, error = "Run this settings contract outside an active multiplayer session." };
			var pawns = PawnsFinder.All_AliveOrDead.ToList();
			var held = new List<Pawn>();
			foreach (var map in Find.Maps)
			{
				ThingOwnerUtility.GetAllThingsRecursively(map, ThingRequest.ForGroup(ThingRequestGroup.Pawn), held, allowUnreal: true, alsoGetSpawnedThings: false);
				pawns.AddRange(held);
			}
			pawns = pawns.Where(p => p.workSettings?.priorities != null).Distinct().ToList();
			if (pawns.Count == 0)
				return new { success = false, error = "No existing pawn has initialized work priorities." };
			var rescueName = Tools.RescuingWorkTypeDef.defName;
			var originalDefs = DefDatabase<WorkTypeDef>.AllDefsListForReading.Where(d => d.defName != rescueName).ToList();
			var baseline = pawns.ToDictionary(p => p, p => originalDefs.ToDictionary(d => d.defName, d => p.workSettings.priorities.values[d.index]));
			var toggle = AccessTools.Method(typeof(AchtungSettings), "ToggleRescue");
			var phases = new List<object>();
			var errors = new List<string>();
			foreach (var enabled in new[] { false, true, false, true })
			{
				Achtung.Settings.rescueEnabled = enabled;
				toggle.Invoke(null, new object[] { enabled });
				var rescue = DefDatabase<WorkTypeDef>.GetNamedSilentFail(rescueName);
				var expectedCount = DefDatabase<WorkTypeDef>.AllDefsListForReading.Count;
				var phaseErrors = new List<string>();
				if ((rescue != null) != enabled)
					phaseErrors.Add("Rescue definition presence differs from the requested setting.");
				foreach (var pawn in pawns)
				{
					var values = pawn.workSettings.priorities.values;
					if (values.Count != expectedCount)
					{
						phaseErrors.Add($"{pawn.ThingID}: priority count {values.Count}, expected {expectedCount}.");
						continue;
					}
					foreach (var def in originalDefs)
						if (values[def.index] != baseline[pawn][def.defName])
							phaseErrors.Add($"{pawn.ThingID}/{def.defName}: priority {values[def.index]}, expected {baseline[pawn][def.defName]}.");
					if (enabled && rescue != null && values[rescue.index] != values[WorkTypeDefOf.Doctor.index])
						phaseErrors.Add($"{pawn.ThingID}: Rescue did not copy Doctor priority.");
				}
				phases.Add(new { enabled, expectedCount, errors = phaseErrors.ToArray() });
				errors.AddRange(phaseErrors);
			}
			return new
			{
				success = errors.Count == 0,
				pawnCount = pawns.Count,
				spawned = pawns.Count(p => p.Spawned),
				unspawned = pawns.Count(p => !p.Spawned),
				dead = pawns.Count(p => p.Dead),
				phases,
				errors,
				rescueEnabled = Achtung.Settings.rescueEnabled,
				settingChange = "Rescue remains enabled and its priorities are newly copied from Doctor."
			};
		}, cancellationToken);
	}

	[Tool("achtung/get_thorough_work_state", Description = "Read the current cleaning/firefighting driver target, original room anchor, partial work, movement and cleaning-speed inputs. Compare this state before/after a normal game save/load.")]
	public static object GetThoroughWorkState(string pawnId = null)
	{
		var pawn = FindPawn(pawnId);
		if (pawn?.jobs?.curDriver is not JobDriver_Thoroughly driver)
			return new { success = false, error = "The selected/requested pawn is not doing Achtung thorough work.", currentJob = pawn?.CurJob?.def?.defName };
		var filth = driver.currentItem.Thing as Filth;
		var terrainFactor = filth?.Map == null ? 0f : filth.Position.GetTerrain(filth.Map).GetStatValueAbstract(StatDefOf.CleaningTimeFactor);
		var speed = pawn.GetStatValue(StatDefOf.CleaningSpeed);
		var anchor = driver.job.targetB.Cell;
		var room = anchor.IsValid && anchor.InBounds(pawn.Map) ? anchor.GetRoom(pawn.Map) : null;
		return new
		{
			success = true,
			pawnId = pawn.ThingID,
			ticksGame = Find.TickManager.TicksGame,
			driver = driver.GetType().Name,
			currentThingId = driver.currentItem.Thing?.ThingID,
			currentCell = driver.currentItem.Cell.ToString(),
			jobTargetA = driver.job.targetA.ToString(),
			roomAnchor = anchor.ToString(),
			anchorRoomCells = room?.Cells.OrderBy(c => c.x).ThenBy(c => c.z).Select(c => c.ToString()).ToArray(),
			driver.isMoving,
			patherMoving = pawn.pather.Moving,
			driver.subCounter,
			driver.currentWorkCount,
			driver.totalWorkCount,
			workLocations = driver.workLocations?.OrderBy(c => c.x).ThenBy(c => c.z).Select(c => c.ToString()).ToArray(),
			filthThickness = filth?.thickness,
			cleaningSpeed = speed,
			terrainFactor,
			expectedCleaningWorkPerTick = terrainFactor == 0f ? speed : speed / terrainFactor,
			workToThin = filth?.def.filth.cleaningWorkToReduceThickness
		};
	}

	[Tool("achtung/start_thorough_work_at_cell", Description = "Start the actual Achtung cleaning or firefighting command for an existing pawn at an existing target. Creates no map fixture. Use state readback, ordinary game ticks and save/load to verify continuation.")]
	public static object StartThoroughWorkAtCell(int x, int z, string workType = "clean", string pawnId = null)
	{
		var pawn = FindPawn(pawnId);
		if (pawn?.Map == null)
			return new { success = false, error = "Select or specify a spawned pawn." };
		if (MultiplayerSupport.IsActive)
			return new { success = false, error = "Run this fixture command outside an active multiplayer session." };
		JobDriver_Thoroughly driver;
		if (workType == "clean") driver = new JobDriver_CleanRoom();
		else if (workType == "fire") driver = new JobDriver_FightFire();
		else return new { success = false, error = "workType must be clean or fire." };
		var cell = new IntVec3(x, 0, z);
		if (!cell.InBounds(pawn.Map) || driver.CanStart(pawn, cell) == null)
		{
			var room = cell.InBounds(pawn.Map) ? RegionAndRoomQuery.RoomAt(cell, pawn.Map) : null;
			return new
			{
				success = false, error = "The production command cannot start for this pawn and cell.",
				workType = driver.WorkType?.defName,
				workTypeDisabled = driver.WorkType != null && pawn.WorkTypeIsDisabled(driver.WorkType),
				workPriority = driver.WorkType == null ? null : pawn.workSettings?.GetPriority(driver.WorkType),
				Achtung.Settings.ignoreAssignments,
				pawnPosition = pawn.Position.ToString(),
				room = room == null ? null : new { room.IsHuge, room.TouchesMapEdge, room.RegionCount, cellCount = room.Cells.Count() }
			};
		}
		driver.StartJob(pawn, cell, cell);
		return new { success = pawn.CurJob?.def.driverClass == driver.GetType(), state = GetThoroughWorkState(pawn.ThingID) };
	}

	[Tool("achtung/test_cleaning_work_tick", Description = "Advance one real game tick for an existing active Achtung cleaner and verify progress against vanilla's pawn-speed/terrain calculation. Requires a reachable current filth target adjacent to the pawn.")]
	public static async Task<object> TestCleaningWorkTick(IRimBridgeContext ctx, CancellationToken cancellationToken, string pawnId = null)
	{
		Pawn pawn = null;
		JobDriver_CleanRoom driver = null;
		Filth filth = null;
		float expected = 0f;
		float before = 0f;
		int thickness = 0;
		var ready = await ctx.MainThread.InvokeAsync(() =>
		{
			pawn = FindPawn(pawnId);
			driver = pawn?.jobs?.curDriver as JobDriver_CleanRoom;
			filth = driver?.currentItem.Thing as Filth;
			if (filth == null || driver.CurrentItemInvalid() || !pawn.Position.AdjacentTo8WayOrInside(filth)) return false;
			var factor = filth.Position.GetTerrain(filth.Map).GetStatValueAbstract(StatDefOf.CleaningTimeFactor);
			expected = pawn.GetStatValue(StatDefOf.CleaningSpeed);
			if (factor != 0f) expected /= factor;
			before = driver.subCounter;
			thickness = filth.thickness;
			return true;
		}, cancellationToken);
		if (!ready) return new { success = false, error = "Cleaner must have an allowed current filth target and be adjacent to it." };
		await ctx.Game.StepTicksAsync(1, cancellationToken: cancellationToken);
		return await ctx.MainThread.InvokeAsync<object>(() =>
		{
			var thins = before + expected > filth.def.filth.cleaningWorkToReduceThickness;
			var expectedCounter = thins ? 0f : before + expected;
			var actualThickness = filth.Destroyed ? 0 : filth.thickness;
			var sameDriver = pawn.jobs.curDriver == driver;
			return new
			{
				success = sameDriver && Math.Abs(driver.subCounter - expectedCounter) < 0.0001f && actualThickness == thickness - (thins ? 1 : 0),
				sameDriver,
				before,
				expectedWork = expected,
				expectedCounter,
				actualCounter = driver.subCounter,
				expectedThickness = thickness - (thins ? 1 : 0),
				actualThickness,
				state = GetThoroughWorkState(pawn.ThingID)
			};
		}, cancellationToken);
	}
}
