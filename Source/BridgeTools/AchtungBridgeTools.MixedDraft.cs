using HarmonyLib;
using RimBridgeServer.Sdk;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using Verse.AI;

namespace AchtungMod;

public sealed partial class AchtungBridgeTools
{
	sealed class MixedDraftFixture
	{
		public Map map;
		public Pawn mechanitor;
		public Pawn combatMech;
		public Pawn laborMech;
		public IntVec3 mechanitorCell;
		public IntVec3 combatStart;
		public IntVec3 laborStart;
		public IntVec3 targetCell;
		public bool previousSeparateCommands;
		public CommandMenuMode previousCommandMenuMode;
		public TimeSpeed previousSpeed;
	}

	sealed class MixedDraftInvocation
	{
		public bool fallbackInvoked;
		public bool showMenuReturned;
		public bool everyoneHasGoto;
		public int nonGotoActionCount;
		public bool combatDrafted;
		public bool laborDrafted;
		public string combatJob;
		public string laborJob;
		public bool combatJobPlayerForced;
		public bool laborJobPlayerForced;
		public bool positioningStarted;
		public bool lineStartMatchesTarget;
		public bool lineEndMatchesTarget;
		public bool combatDesignationValid;
		public bool laborDesignationValid;
	}

	sealed class MixedDraftEligibilityMatrix
	{
		public bool mixedDraftedAccepted;
		public bool allDraftedAccepted;
		public bool allUndraftedRejected;
		public bool success;
	}

	sealed class MixedDraftModifierPhase
	{
		public bool modifierPressed;
		public bool inputReportedPressed;
		public bool mouseDownReturned;
		public int selectedPawnCount;
		public bool combatDrafted;
		public bool laborDrafted;
		public bool laborDraftRequested;
		public bool combatDraftedForPositioning;
		public bool laborDraftedForPositioning;
		public bool laborFormationOffsetRecorded;
		public bool positioningStarted;
		public bool groupMovement;
		public bool lineStartMatchesTarget;
		public bool lineEndMatchesTarget;
		public bool combatDesignationValid;
		public bool laborDesignationValid;
		public bool combatReceivedPlayerGoto;
		public bool laborReceivedPlayerGoto;
		public bool success;
	}

	sealed class MixedDraftModifierContract
	{
		public string simulatedKey;
		public bool inputPatchApplied;
		public bool mousePositionPatchApplied;
		public bool patchesRemoved;
		public bool selectionRestored;
		public bool draftStateRestored;
		public bool settingsRestored;
		public MixedDraftModifierPhase unmodified;
		public MixedDraftModifierPhase modified;
		public bool success;
	}

	static readonly MethodInfo showMenuMethod = AccessTools.Method(typeof(Controller), "ShowMenu");
	static readonly MethodInfo beginPositioningMethod = AccessTools.Method(typeof(Controller), "BeginPositioning");
	static readonly MethodInfo updateLinePositionMethod = AccessTools.Method(typeof(Controller), "UpdateLinePosition");
	static readonly MethodInfo endDraggingMethod = AccessTools.Method(typeof(Controller), "EndDragging");
	static readonly Type achtungCursorType = AccessTools.TypeByName("AchtungMod.AchtungCursor");
	static readonly MethodInfo setCursorMethod = AccessTools.Method(AccessTools.TypeByName("AchtungMod.Tools"), "SetCursor");
	static readonly object defaultCursor = achtungCursorType == null ? null : Enum.Parse(achtungCursorType, "Default");
	static readonly MethodInfo inputGetKeyMethod = AccessTools.Method(typeof(Input), nameof(Input.GetKey), [typeof(KeyCode)]);
	static readonly MethodInfo mouseMapPositionMethod = AccessTools.Method(typeof(UI), nameof(UI.MouseMapPosition), Type.EmptyTypes);
	static readonly MethodInfo inputGetKeyPrefixMethod = AccessTools.Method(typeof(AchtungBridgeTools), nameof(PrefixInputGetKey));
	static readonly MethodInfo mouseMapPositionPrefixMethod = AccessTools.Method(typeof(AchtungBridgeTools), nameof(PrefixMouseMapPosition));
	static readonly SemaphoreSlim mixedDraftModifierContractGate = new(1, 1);
	static bool modifierInputOverrideActive;
	static bool simulatedModifierPressed;
	static AchtungModKey simulatedModifierKey;
	static bool mouseMapPositionOverrideActive;
	static Vector3 simulatedMouseMapPosition;

	[Tool("achtung/test_mixed_draft_menu_contract", Description = "Run the mixed drafted/undrafted Go-here fallback contract against real Biotech mech types without advancing game time.")]
	public static async Task<object> TestMixedDraftMenuContract(IRimBridgeContext ctx, CancellationToken cancellationToken)
	{
		if (ctx == null)
			return new { success = false, error = "RimBridge context was not injected." };

		MixedDraftFixture fixture = null;
		var setupError = null as string;
		try
		{
			await ctx.MainThread.InvokeAsync(() =>
			{
				if (TryCreateMixedDraftFixture(out fixture, out var error) == false)
					setupError = error;
			}, cancellationToken);

			if (fixture == null)
				return new { success = false, error = setupError ?? "Could not create the mixed-draft fixture." };

			var invocation = await ctx.MainThread.InvokeAsync(
				() => InvokeMixedDraftFallback(fixture, false),
				cancellationToken);
			var eligibilityMatrix = await ctx.MainThread.InvokeAsync(
				() => EvaluateMixedDraftEligibilityMatrix(fixture),
				cancellationToken);
			var success = MixedDraftContractPassed(invocation) && eligibilityMatrix.success;

			return new
			{
				success,
				contract = "Only pawns drafted before the command contribute to implicit Go-here eligibility, and fallback dispatch does not draft other selected pawns.",
				fixture = DescribeMixedDraftFixture(fixture),
				invocation,
				eligibilityMatrix
			};
		}
		finally
		{
			if (fixture != null)
				await ctx.MainThread.InvokeAsync(() => CleanupMixedDraftFixture(fixture), CancellationToken.None);
		}
	}

	[Tool("achtung/test_mixed_draft_modifier_contract", Description = "Run unmodified and modifier-held mixed-draft positioning through Achtung's real MouseDown path using callback-scoped input shims.")]
	public static async Task<object> TestMixedDraftModifierContract(IRimBridgeContext ctx, CancellationToken cancellationToken)
	{
		if (ctx == null)
			return new { success = false, error = "RimBridge context was not injected." };

		await mixedDraftModifierContractGate.WaitAsync(cancellationToken);
		MixedDraftFixture fixture = null;
		var setupError = null as string;
		try
		{
			await ctx.MainThread.InvokeAsync(() =>
			{
				if (TryCreateMixedDraftFixture(out fixture, out var error) == false)
					setupError = error;
			}, cancellationToken);

			if (fixture == null)
				return new { success = false, error = setupError ?? "Could not create the mixed-draft fixture." };

			try
			{
				var contract = await ctx.MainThread.InvokeAsync(
					() => RunMixedDraftModifierContract(fixture),
					cancellationToken);
				return new
				{
					success = contract.success,
					contract = "Plain mixed-selection positioning preserves the undrafted pawn, while the configured Achtung modifier drafts and positions the whole selection.",
					fixture = DescribeMixedDraftFixture(fixture),
					shims = new
					{
						input = "UnityEngine.Input.GetKey(KeyCode)",
						mouseMapPosition = "Verse.UI.MouseMapPosition()",
						scope = "One serialized main-thread callback with no tick or frame boundary"
					},
					result = contract
				};
			}
			catch (Exception ex)
			{
				return new
				{
					success = false,
					error = $"{ex.GetType().Name}: {ex.Message}",
					fixture = DescribeMixedDraftFixture(fixture)
				};
			}
		}
		finally
		{
			try
			{
				if (fixture != null)
					await ctx.MainThread.InvokeAsync(() => CleanupMixedDraftFixture(fixture), CancellationToken.None);
			}
			finally
			{
				_ = mixedDraftModifierContractGate.Release();
			}
		}
	}

	[Tool("achtung/run_mixed_draft_mech_scenario", Description = "Spawn a drafted centipede and undrafted cleansweeper, dispatch Achtung's implicit line move, and run the game asynchronously until the centipede moves or the scenario deadline is reached.")]
	public static async Task<object> RunMixedDraftMechScenario(
		IRimBridgeContext ctx,
		CancellationToken cancellationToken,
		[ToolParameter(Description = "Maximum real-time wait in milliseconds.", Required = false, DefaultValue = 30000)] int timeoutMs = 30000,
		[ToolParameter(Description = "RimWorld play speed while waiting: Normal, Fast, Superfast, or Ultrafast.", Required = false, DefaultValue = "Ultrafast")] string speed = "Ultrafast")
	{
		if (ctx == null)
			return new { success = false, error = "RimBridge context was not injected." };
		if (TryParseScenarioSpeed(speed, out var scenarioSpeed, out var speedError) == false)
			return new { success = false, error = speedError };

		MixedDraftFixture fixture = null;
		var setupError = null as string;
		MixedDraftInvocation invocation = null;
		var laborEverDrafted = false;
		var laborEverReceivedPlayerGoto = false;
		var combatMoved = false;
		var deadlineReached = false;
		var startTick = 0;
		var deadlineTick = 0;
		try
		{
			await ctx.MainThread.InvokeAsync(() =>
			{
				if (TryCreateMixedDraftFixture(out fixture, out var error) == false)
				{
					setupError = error;
					return;
				}
				if (Find.TickManager.ForcePaused)
				{
					setupError = "RimWorld is force-paused by loading or modal UI. Wait for visual readiness before running the realtime contract.";
					return;
				}

				startTick = Find.TickManager.TicksGame;
				deadlineTick = startTick + 1800;
				invocation = InvokeMixedDraftFallback(fixture, true);
				laborEverDrafted = fixture.laborMech.Drafted;
				laborEverReceivedPlayerGoto = HasPlayerGoto(fixture.laborMech, fixture.targetCell);
				Find.TickManager.CurTimeSpeed = scenarioSpeed;
			}, cancellationToken);

			if (fixture == null)
				return new { success = false, error = setupError ?? "Could not create the mixed-draft fixture." };
			if (setupError != null)
				return new { success = false, error = setupError, fixture = DescribeMixedDraftFixture(fixture) };
			if (MixedDraftContractPassed(invocation) == false)
			{
				return new
				{
					success = false,
					error = "The no-tick mixed-draft contract failed before the realtime phase.",
					fixture = DescribeMixedDraftFixture(fixture),
					invocation
				};
			}

			var wait = await ctx.Game.RunUntilAsync(() =>
			{
				laborEverDrafted |= fixture.laborMech?.Drafted == true;
				laborEverReceivedPlayerGoto |= HasPlayerGoto(fixture.laborMech, fixture.targetCell);
				combatMoved = fixture.combatMech != null
					&& fixture.combatMech.Spawned
					&& fixture.combatMech.Position.DistanceToSquared(fixture.combatStart) >= 4;
				deadlineReached = Find.TickManager.TicksGame >= deadlineTick;
				return laborEverDrafted || laborEverReceivedPlayerGoto || combatMoved || deadlineReached;
			}, new RimBridgeWaitOptions
			{
				TimeoutMs = Math.Max(1000, timeoutMs),
				FailIfBusy = true
			}, cancellationToken);

			return await ctx.MainThread.InvokeAsync(() =>
			{
				laborEverDrafted |= fixture.laborMech.Drafted;
				laborEverReceivedPlayerGoto |= HasPlayerGoto(fixture.laborMech, fixture.targetCell);
				combatMoved |= fixture.combatMech.Position.DistanceToSquared(fixture.combatStart) >= 4;
				deadlineReached |= Find.TickManager.TicksGame >= deadlineTick;
				var success = wait.Success
					&& combatMoved
					&& laborEverDrafted == false
					&& laborEverReceivedPlayerGoto == false
					&& fixture.combatMech.Drafted
					&& fixture.laborMech.Drafted == false;

				return new
				{
					success,
					contract = "The drafted centipede receives and executes the implicit line move; the selected undrafted cleansweeper remains undrafted and receives no player-forced GoTo job.",
					fixture = DescribeMixedDraftFixture(fixture),
					invocation,
					observation = new
					{
						combatMoved,
						combatPosition = DescribeCell(fixture.combatMech.Position),
						combatJob = fixture.combatMech.CurJobDef?.defName,
						laborEverDrafted,
						laborEverReceivedPlayerGoto,
						laborPosition = DescribeCell(fixture.laborMech.Position),
						laborJob = fixture.laborMech.CurJobDef?.defName,
						deadlineReached,
						startTick,
						endTick = Find.TickManager.TicksGame,
						advancedTicks = Find.TickManager.TicksGame - startTick
					},
					wait = new
					{
						wait.Success,
						wait.Status,
						wait.Message,
						wait.ElapsedFrames,
						wait.StartTicksGame,
						wait.EndTicksGame,
						wait.AdvancedTicks
					}
				};
			}, cancellationToken);
		}
		finally
		{
			if (fixture != null)
				await ctx.MainThread.InvokeAsync(() => CleanupMixedDraftFixture(fixture), CancellationToken.None);
		}
	}

	static bool TryCreateMixedDraftFixture(out MixedDraftFixture fixture, out string error)
	{
		fixture = null;
		error = null;
		if (Current.ProgramState != ProgramState.Playing || Current.Game == null || Find.CurrentMap == null || Find.TickManager == null)
		{
			error = "No playable current map is loaded.";
			return false;
		}
		if (ModsConfig.BiotechActive == false)
		{
			error = "Biotech is not active. Enable ludeon.rimworld.biotech and restart RimWorld before running this contract.";
			return false;
		}
		if (Achtung.Settings == null)
		{
			error = "Achtung settings are not initialized.";
			return false;
		}

		var combatKind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Mech_CentipedeBlaster");
		var laborKind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Mech_Cleansweeper");
		if (combatKind == null || laborKind == null)
		{
			error = "The Biotech centipede or cleansweeper PawnKindDef is unavailable.";
			return false;
		}

		var map = Find.CurrentMap;
		if (TryFindMixedDraftCells(map, out var mechanitorCell, out var combatCell, out var laborCell) == false)
		{
			error = "Could not find three clear fixture cells near the colony.";
			return false;
		}

		fixture = new MixedDraftFixture
		{
			map = map,
			mechanitorCell = mechanitorCell,
			combatStart = combatCell,
			laborStart = laborCell,
			previousSeparateCommands = Achtung.Settings.keepDraftedAndUndraftedCommandsSeparate,
			previousCommandMenuMode = Achtung.Settings.forceCommandMenuMode,
			previousSpeed = Find.TickManager.CurTimeSpeed
		};

		try
		{
			Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
			Achtung.Settings.keepDraftedAndUndraftedCommandsSeparate = false;
			Achtung.Settings.forceCommandMenuMode = CommandMenuMode.Delayed;
			fixture.mechanitor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(fixture.mechanitor, mechanitorCell, map, Rot4.South);
			fixture.mechanitor.health.AddHediff(HediffDefOf.MechlinkImplant, fixture.mechanitor.health.hediffSet.GetBrain());
			PawnComponentsUtility.AddAndRemoveDynamicComponents(fixture.mechanitor);
			if (fixture.mechanitor.mechanitor == null)
				throw new InvalidOperationException("The temporary colonist did not become a mechanitor.");

			fixture.combatMech = PawnGenerator.GeneratePawn(combatKind, Faction.OfPlayer);
			fixture.laborMech = PawnGenerator.GeneratePawn(laborKind, Faction.OfPlayer);
			GenSpawn.Spawn(fixture.combatMech, combatCell, map, Rot4.South);
			GenSpawn.Spawn(fixture.laborMech, laborCell, map, Rot4.South);
			fixture.mechanitor.relations.AddDirectRelation(PawnRelationDefOf.Overseer, fixture.combatMech);
			fixture.mechanitor.relations.AddDirectRelation(PawnRelationDefOf.Overseer, fixture.laborMech);

			if (fixture.combatMech.GetOverseer() != fixture.mechanitor || fixture.laborMech.GetOverseer() != fixture.mechanitor)
				throw new InvalidOperationException("The temporary mechanitor did not become overseer of both mechs.");

			_ = SetDraftStatus(fixture.combatMech, true);
			_ = SetDraftStatus(fixture.laborMech, false);
			if (TryFindMixedDraftTarget(fixture, out var targetCell) == false)
				throw new InvalidOperationException("Could not find a plain Go-here target accepted by the drafted centipede.");
			fixture.targetCell = targetCell;
			return true;
		}
		catch (Exception ex)
		{
			error = $"{ex.GetType().Name}: {ex.Message}";
			CleanupMixedDraftFixture(fixture);
			fixture = null;
			return false;
		}
	}

	static bool TryFindMixedDraftCells(Map map, out IntVec3 mechanitorCell, out IntVec3 combatCell, out IntVec3 laborCell)
	{
		mechanitorCell = IntVec3.Invalid;
		combatCell = IntVec3.Invalid;
		laborCell = IntVec3.Invalid;
		var roots = new List<IntVec3>();
		var existingColonist = map.mapPawns.FreeColonistsSpawned.FirstOrDefault();
		if (existingColonist != null)
			roots.Add(existingColonist.Position);
		roots.Add(new IntVec3(map.Size.x / 2, 0, map.Size.z / 2));

		foreach (var root in roots)
		{
			var candidates = GenRadial.RadialCellsAround(root, 22f, true)
				.Where(cell => IsClearFixtureCell(map, cell))
				.ToList();
			if (candidates.Count < 3)
				continue;
			var fixtureMechanitorCell = candidates[0];
			var fixtureCombatCell = candidates.FirstOrDefault(cell => cell.DistanceToSquared(fixtureMechanitorCell) >= 4);
			var fixtureLaborCell = candidates.FirstOrDefault(cell => cell != fixtureCombatCell && cell.DistanceToSquared(fixtureMechanitorCell) >= 4);
			mechanitorCell = fixtureMechanitorCell;
			combatCell = fixtureCombatCell;
			laborCell = fixtureLaborCell;
			if (mechanitorCell.IsValid && combatCell.IsValid && laborCell.IsValid)
				return true;
		}
		return false;
	}

	static bool TryFindMixedDraftTarget(MixedDraftFixture fixture, out IntVec3 targetCell)
	{
		targetCell = IntVec3.Invalid;
		var colonists = new List<Colonist>
		{
			new(fixture.combatMech),
			new(fixture.laborMech)
		};
		foreach (var candidate in GenRadial.RadialCellsAround(fixture.mechanitorCell, 18f, true))
		{
			if (IsClearFixtureCell(fixture.map, candidate) == false)
				continue;
			if (candidate.DistanceToSquared(fixture.combatStart) < 64)
				continue;
			if (ReachabilityUtility.CanReach(fixture.combatMech, candidate, PathEndMode.OnCell, Danger.Deadly) == false)
				continue;

			var actions = new MultiActions(colonists, candidate.ToVector3Shifted());
			if (actions.EveryoneHasGoto && actions.Count(false) == 0)
			{
				targetCell = candidate;
				return true;
			}
		}
		return false;
	}

	static bool IsClearFixtureCell(Map map, IntVec3 cell)
		=> cell.InBounds(map)
			&& cell.Standable(map)
			&& cell.Fogged(map) == false
			&& cell.GetThingList(map).All(thing => thing is not Pawn);

	static MixedDraftInvocation InvokeMixedDraftFallback(MixedDraftFixture fixture, bool issueOrders)
	{
		if (showMenuMethod == null || beginPositioningMethod == null || updateLinePositionMethod == null || endDraggingMethod == null || setCursorMethod == null || defaultCursor == null)
			throw new MissingMethodException("Achtung mixed-draft test hooks no longer match Controller.");

		var combatColonist = new Colonist(fixture.combatMech);
		var laborColonist = new Colonist(fixture.laborMech);
		var colonists = new List<Colonist> { combatColonist, laborColonist };
		var actions = new MultiActions(colonists, fixture.targetCell.ToVector3Shifted());
		var controller = new Controller
		{
			colonists = colonists
		};
		var fallbackInvoked = false;
		void Dispatch()
		{
			fallbackInvoked = true;
			_ = beginPositioningMethod.Invoke(controller, [fixture.targetCell.ToVector3Shifted(), false]);
		}

		var previousEvent = Event.current;
		try
		{
			Event.current = new Event { type = EventType.MouseDown, button = 1 };
			var returned = (bool)showMenuMethod.Invoke(controller, [actions, false, (Action)Dispatch, false]);
			var target = fixture.targetCell;
			var invocation = new MixedDraftInvocation
			{
				fallbackInvoked = fallbackInvoked,
				showMenuReturned = returned,
				everyoneHasGoto = actions.EveryoneHasGoto,
				nonGotoActionCount = actions.Count(false),
				combatDrafted = fixture.combatMech.Drafted,
				laborDrafted = fixture.laborMech.Drafted,
				positioningStarted = controller.isDragging,
				lineStartMatchesTarget = controller.lineStart.ToIntVec3() == target,
				lineEndMatchesTarget = controller.lineEnd.ToIntVec3() == target,
				combatDesignationValid = combatColonist.designation.IsValid,
				laborDesignationValid = laborColonist.designation.IsValid
			};
			if (issueOrders)
				_ = updateLinePositionMethod.Invoke(controller, [fixture.targetCell.ToVector3Shifted(), true]);
			invocation.combatJob = fixture.combatMech.CurJobDef?.defName;
			invocation.laborJob = fixture.laborMech.CurJobDef?.defName;
			invocation.combatJobPlayerForced = fixture.combatMech.CurJob?.playerForced == true;
			invocation.laborJobPlayerForced = fixture.laborMech.CurJob?.playerForced == true;
			return invocation;
		}
		finally
		{
			if (controller.isDragging)
				_ = endDraggingMethod.Invoke(controller, null);
			_ = setCursorMethod.Invoke(null, [defaultCursor]);
			Event.current = previousEvent;
		}
	}

	static bool MixedDraftContractPassed(MixedDraftInvocation invocation)
		=> invocation != null
			&& invocation.fallbackInvoked
			&& invocation.showMenuReturned
			&& invocation.everyoneHasGoto
			&& invocation.nonGotoActionCount == 0
			&& invocation.combatDrafted
			&& invocation.laborDrafted == false
			&& invocation.positioningStarted
			&& invocation.lineStartMatchesTarget
			&& invocation.lineEndMatchesTarget
			&& invocation.combatDesignationValid
			&& invocation.laborDesignationValid == false;

	static MixedDraftModifierContract RunMixedDraftModifierContract(MixedDraftFixture fixture)
	{
		if (inputGetKeyMethod == null || mouseMapPositionMethod == null || inputGetKeyPrefixMethod == null || mouseMapPositionPrefixMethod == null || endDraggingMethod == null || setCursorMethod == null || defaultCursor == null)
			throw new MissingMethodException("Achtung modifier test hooks no longer match Input, UI, or Controller.");

		var harmony = new Harmony($"brrainz.achtung.bridgetools.mixed-draft-modifier.{Guid.NewGuid():N}");
		var selector = Find.Selector;
		var previousSelection = selector.SelectedObjectsListForReading.ToList();
		var previousEvent = Event.current;
		var previousPositioningEnabled = Achtung.Settings.positioningEnabled;
		var previousMaxForcedItems = Achtung.Settings.maxForcedItems;
		var previousAchtungKey = Achtung.Settings.achtungKey;
		var contract = new MixedDraftModifierContract
		{
			simulatedKey = AchtungModKey.Alt.ToString()
		};
		Exception cleanupError = null;

		try
		{
			Achtung.Settings.positioningEnabled = true;
			Achtung.Settings.maxForcedItems = Math.Max(2, Achtung.Settings.maxForcedItems);
			Achtung.Settings.achtungKey = AchtungModKey.Alt;
			selector.ClearSelection();
			selector.Select(fixture.combatMech, false, false);
			selector.Select(fixture.laborMech, false, false);

			simulatedModifierKey = Achtung.Settings.achtungKey;
			simulatedMouseMapPosition = fixture.targetCell.ToVector3Shifted();
			modifierInputOverrideActive = true;
			mouseMapPositionOverrideActive = true;
			harmony.Patch(inputGetKeyMethod, prefix: new HarmonyMethod(inputGetKeyPrefixMethod));
			contract.inputPatchApplied = true;
			harmony.Patch(mouseMapPositionMethod, prefix: new HarmonyMethod(mouseMapPositionPrefixMethod));
			contract.mousePositionPatchApplied = true;

			contract.unmodified = InvokeMixedDraftModifierPhase(fixture, false);
			contract.modified = InvokeMixedDraftModifierPhase(fixture, true);
		}
		finally
		{
			modifierInputOverrideActive = false;
			mouseMapPositionOverrideActive = false;
			simulatedModifierPressed = false;
			TryCleanup(() => harmony.UnpatchAll(harmony.Id), ref cleanupError);
			TryCleanup(() => Achtung.Settings.positioningEnabled = previousPositioningEnabled, ref cleanupError);
			TryCleanup(() => Achtung.Settings.maxForcedItems = previousMaxForcedItems, ref cleanupError);
			TryCleanup(() => Achtung.Settings.achtungKey = previousAchtungKey, ref cleanupError);
			TryCleanup(() => _ = SetDraftStatus(fixture.combatMech, true), ref cleanupError);
			TryCleanup(() => _ = SetDraftStatus(fixture.laborMech, false), ref cleanupError);
			TryCleanup(() => Event.current = previousEvent, ref cleanupError);
			TryCleanup(() => _ = setCursorMethod.Invoke(null, [defaultCursor]), ref cleanupError);
			TryCleanup(selector.ClearSelection, ref cleanupError);
			foreach (var selected in previousSelection)
				TryCleanup(() => selector.Select(selected, false, false), ref cleanupError);

			TryCleanup(() => contract.patchesRemoved = Harmony.HasAnyPatches(harmony.Id) == false, ref cleanupError);
			TryCleanup(() => contract.selectionRestored = selector.SelectedObjectsListForReading.SequenceEqual(previousSelection), ref cleanupError);
			TryCleanup(() => contract.draftStateRestored = fixture.combatMech.Drafted && fixture.laborMech.Drafted == false, ref cleanupError);
			TryCleanup(() => contract.settingsRestored = Achtung.Settings.positioningEnabled == previousPositioningEnabled
				&& Achtung.Settings.maxForcedItems == previousMaxForcedItems
				&& Achtung.Settings.achtungKey == previousAchtungKey, ref cleanupError);
		}

		if (cleanupError != null)
			throw new InvalidOperationException("Could not fully restore game state after the temporary modifier input patches.", cleanupError);

		contract.success = contract.inputPatchApplied
			&& contract.mousePositionPatchApplied
			&& contract.patchesRemoved
			&& contract.selectionRestored
			&& contract.draftStateRestored
			&& contract.settingsRestored
			&& contract.unmodified?.success == true
			&& contract.modified?.success == true;
		return contract;
	}

	static void TryCleanup(Action cleanup, ref Exception firstError)
	{
		try
		{
			cleanup();
		}
		catch (Exception ex)
		{
			firstError ??= ex;
		}
	}

	static MixedDraftModifierPhase InvokeMixedDraftModifierPhase(MixedDraftFixture fixture, bool modifierPressed)
	{
		_ = SetDraftStatus(fixture.combatMech, true);
		_ = SetDraftStatus(fixture.laborMech, false);
		simulatedModifierPressed = modifierPressed;
		var controller = new Controller();
		var previousEvent = Event.current;
		try
		{
			Event.current = new Event { type = EventType.MouseDown, button = 1 };
			var returned = controller.MouseDown(fixture.targetCell.ToVector3Shifted(), 1, false);
			var combatColonist = controller.colonists.FirstOrDefault(colonist => colonist.pawn == fixture.combatMech);
			var laborColonist = controller.colonists.FirstOrDefault(colonist => colonist.pawn == fixture.laborMech);
			var target = fixture.targetCell;
			var phase = new MixedDraftModifierPhase
			{
				modifierPressed = modifierPressed,
				inputReportedPressed = Input.GetKey(KeyCode.LeftAlt),
				mouseDownReturned = returned,
				selectedPawnCount = controller.colonists.Count,
				combatDrafted = fixture.combatMech.Drafted,
				laborDrafted = fixture.laborMech.Drafted,
				laborDraftRequested = laborColonist?.draftRequestedForPositioning == true,
				combatDraftedForPositioning = combatColonist?.DraftedForPositioning == true,
				laborDraftedForPositioning = laborColonist?.DraftedForPositioning == true,
				laborFormationOffsetRecorded = laborColonist != null && laborColonist.offsetFromCenter != Vector3.zero,
				positioningStarted = controller.isDragging,
				groupMovement = controller.groupMovement,
				lineStartMatchesTarget = controller.lineStart.ToIntVec3() == target,
				lineEndMatchesTarget = controller.lineEnd.ToIntVec3() == target,
				combatDesignationValid = combatColonist?.designation.IsValid == true,
				laborDesignationValid = laborColonist?.designation.IsValid == true,
				combatReceivedPlayerGoto = fixture.combatMech.CurJobDef == JobDefOf.Goto && fixture.combatMech.CurJob?.playerForced == true,
				laborReceivedPlayerGoto = fixture.laborMech.CurJobDef == JobDefOf.Goto && fixture.laborMech.CurJob?.playerForced == true
			};

			phase.success = phase.inputReportedPressed == modifierPressed
				&& phase.mouseDownReturned
				&& phase.selectedPawnCount == 2
				&& phase.combatDrafted
				&& phase.laborDraftRequested == modifierPressed
				&& phase.combatDraftedForPositioning
				&& phase.laborDraftedForPositioning == modifierPressed
				&& phase.laborFormationOffsetRecorded == modifierPressed
				&& phase.positioningStarted
				&& phase.combatDesignationValid
				&& phase.laborDrafted == modifierPressed
				&& phase.groupMovement == modifierPressed
				&& phase.laborDesignationValid == modifierPressed
				&& (modifierPressed
					? phase.combatReceivedPlayerGoto && phase.laborReceivedPlayerGoto
					: phase.lineStartMatchesTarget && phase.lineEndMatchesTarget && phase.combatReceivedPlayerGoto == false && phase.laborReceivedPlayerGoto == false);
			return phase;
		}
		finally
		{
			if (controller.isDragging)
				_ = endDraggingMethod.Invoke(controller, null);
			_ = setCursorMethod.Invoke(null, [defaultCursor]);
			Event.current = previousEvent;
			_ = SetDraftStatus(fixture.combatMech, true);
			_ = SetDraftStatus(fixture.laborMech, false);
		}
	}

	static bool PrefixInputGetKey(KeyCode __0, ref bool __result)
	{
		if (modifierInputOverrideActive == false || IsSimulatedModifierKey(__0) == false)
			return true;
		__result = simulatedModifierPressed;
		return false;
	}

	static bool PrefixMouseMapPosition(ref Vector3 __result)
	{
		if (mouseMapPositionOverrideActive == false)
			return true;
		__result = simulatedMouseMapPosition;
		return false;
	}

	static bool IsSimulatedModifierKey(KeyCode code)
		=> simulatedModifierKey switch
		{
			AchtungModKey.Alt => code == KeyCode.LeftAlt || code == KeyCode.RightAlt,
			AchtungModKey.Ctrl => code == KeyCode.LeftControl || code == KeyCode.RightControl,
			AchtungModKey.Shift => code == KeyCode.LeftShift || code == KeyCode.RightShift,
			AchtungModKey.Meta => code == KeyCode.LeftWindows || code == KeyCode.RightWindows || code == KeyCode.LeftCommand || code == KeyCode.RightCommand || code == KeyCode.LeftApple || code == KeyCode.RightApple,
			_ => false
		};

	static MixedDraftEligibilityMatrix EvaluateMixedDraftEligibilityMatrix(MixedDraftFixture fixture)
	{
		bool EveryoneHasGoto(bool combatDrafted, bool laborDrafted)
		{
			_ = SetDraftStatus(fixture.combatMech, combatDrafted);
			_ = SetDraftStatus(fixture.laborMech, laborDrafted);
			var actions = new MultiActions(
				[new Colonist(fixture.combatMech), new Colonist(fixture.laborMech)],
				fixture.targetCell.ToVector3Shifted());
			return actions.EveryoneHasGoto;
		}

		try
		{
			var mixedDraftedAccepted = EveryoneHasGoto(true, false);
			var allDraftedAccepted = EveryoneHasGoto(true, true);
			var allUndraftedRejected = EveryoneHasGoto(false, false) == false;
			return new MixedDraftEligibilityMatrix
			{
				mixedDraftedAccepted = mixedDraftedAccepted,
				allDraftedAccepted = allDraftedAccepted,
				allUndraftedRejected = allUndraftedRejected,
				success = mixedDraftedAccepted && allDraftedAccepted && allUndraftedRejected
			};
		}
		finally
		{
			_ = SetDraftStatus(fixture.combatMech, true);
			_ = SetDraftStatus(fixture.laborMech, false);
		}
	}

	static bool HasPlayerGoto(Pawn pawn, IntVec3 targetCell)
		=> pawn?.CurJobDef == JobDefOf.Goto
			&& pawn.CurJob?.playerForced == true
			&& pawn.CurJob.targetA.Cell == targetCell;

	static bool TryParseScenarioSpeed(string value, out TimeSpeed speed, out string error)
	{
		error = null;
		if (Enum.TryParse(value, true, out speed) == false || Enum.IsDefined(typeof(TimeSpeed), speed) == false || speed == TimeSpeed.Paused)
		{
			error = $"Unknown running speed '{value}'. Use Normal, Fast, Superfast, or Ultrafast.";
			return false;
		}
		return true;
	}

	static object DescribeMixedDraftFixture(MixedDraftFixture fixture)
		=> new
		{
			mechanitor = DescribePawn(fixture.mechanitor),
			combatMech = DescribePawn(fixture.combatMech),
			laborMech = DescribePawn(fixture.laborMech),
			targetCell = DescribeCell(fixture.targetCell)
		};

	static object DescribePawn(Pawn pawn)
		=> pawn == null ? null : new
		{
			id = pawn.ThingID,
			kind = pawn.kindDef?.defName,
			label = pawn.LabelShort,
			drafted = pawn.Drafted,
			position = DescribeCell(pawn.Position),
			job = pawn.CurJobDef?.defName,
			jobPlayerForced = pawn.CurJob?.playerForced == true
		};

	static object DescribeCell(IntVec3 cell)
		=> new { x = cell.x, z = cell.z };

	static void CleanupMixedDraftFixture(MixedDraftFixture fixture)
	{
		if (fixture == null)
			return;
		if (Find.TickManager != null)
			Find.TickManager.CurTimeSpeed = fixture.previousSpeed;
		if (Achtung.Settings != null)
		{
			Achtung.Settings.keepDraftedAndUndraftedCommandsSeparate = fixture.previousSeparateCommands;
			Achtung.Settings.forceCommandMenuMode = fixture.previousCommandMenuMode;
		}
		DestroyFixturePawn(fixture.laborMech);
		DestroyFixturePawn(fixture.combatMech);
		DestroyFixturePawn(fixture.mechanitor);
	}

	static void DestroyFixturePawn(Pawn pawn)
	{
		if (pawn != null && pawn.Destroyed == false)
			pawn.Destroy(DestroyMode.Vanish);
	}
}
