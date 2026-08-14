using RimBridgeServer.Sdk;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using Verse.AI;

namespace AchtungMod;

public sealed partial class AchtungBridgeTools
{
	sealed class ForceWorkSelection
	{
		public Pawn pawn;
		public ForcedFloatMenuOption option;
		public string error;
		public string[] availableLabels = [];
	}

	sealed class ForceWorkDispatch
	{
		public bool dispatched;
		public bool multiplayerActive;
		public Pawn pawn;
		public string label;
		public string workgiver;
		public IntVec3 forceCell;
		public int cellRadius;
		public string error;
		public string[] availableLabels = [];
	}

	sealed class ForceCellSnapshot
	{
		public int x;
		public int z;
		public string cell;
	}

	sealed class ForceSpreadSnapshot
	{
		public int ticksGame;
		public int elapsedTicks;
		public bool hasForcedJob;
		public bool started;
		public bool cancelled;
		public int targetCount;
		public ForceCellSnapshot[] forceMarkerCells = [];
	}

	static Pawn FindPawn(string pawnId)
	{
		if (pawnId.NullOrEmpty())
			return Find.Selector.SingleSelectedThing as Pawn;
		return Find.CurrentMap?.mapPawns?.AllPawnsSpawned?.FirstOrDefault(pawn =>
			pawn.ThingID == pawnId
			|| $"Thing_{pawn.ThingID}" == pawnId
			|| pawn.GetUniqueLoadID() == pawnId);
	}

	static bool SetDraftStatus(Pawn pawn, bool drafted)
	{
		pawn.drafter ??= new Pawn_DraftController(pawn);
		var previousStatus = pawn.drafter.Drafted;
		if (previousStatus != drafted)
			pawn.drafter.draftedInt = drafted;
		return previousStatus;
	}

	[Tool("achtung/get_selected_pawn_forced_state", Description = "Read Achtung forced-work state for the currently selected pawn.")]
	public static object GetSelectedPawnForcedState()
	{
		var pawn = FindPawn(null);
		if (pawn == null)
		{
			return new
			{
				success = false,
				error = "No single pawn is selected."
			};
		}

		var forcedWork = ForcedWork.Instance;
		var forcedJob = forcedWork.GetForcedJob(pawn);
		var currentJob = pawn.jobs?.curJob;

		return new
		{
			success = true,
			pawnId = pawn.ThingID,
			pawnName = pawn.Name?.ToStringShort ?? pawn.LabelShort,
			drafted = pawn.Drafted,
			currentJob = currentJob?.def?.defName,
			currentJobReport = currentJob?.GetReport(pawn),
			hasForcedJob = forcedWork.HasForcedJob(pawn),
			hasForcedJobIgnoringPrepare = forcedWork.HasForcedJob(pawn, ignorePreparing: true),
			isPreparing = forcedWork.IsPreparing(pawn),
			forcedJob = forcedJob == null ? null : new
			{
				started = forcedJob.started,
				cancelled = forcedJob.cancelled,
				isThingJob = forcedJob.isThingJob,
				cellRadius = forcedJob.cellRadius,
				startCell = forcedJob.startCell.ToString(),
				lastAssignedCell = forcedJob.lastAssignedCell.ToString(),
				targetCount = forcedJob.targets.Count,
				targets = forcedJob.targets
					.OrderBy(target => target.XY.x)
					.ThenBy(target => target.XY.y)
					.Select(target => new
					{
						x = (int)target.XY.x,
						z = (int)target.XY.y,
						cell = target.XY.ToString(),
						hasThing = target.item.HasThing,
						thingId = target.item.thingInt?.ThingID,
						label = target.item.thingInt?.LabelCap
					})
					.ToArray(),
				workgivers = forcedJob.workgiverDefs
					.Select(def => def?.defName)
					.Where(name => name != null)
					.ToArray()
			}
		};
	}

	[Tool("achtung/force_work_at_cell", Description = "Resolve Achtung's force-work menu option at a cell and invoke the lightning-button's original local path outside Multiplayer or its synchronized path in an active session.")]
	public static object ForceWorkAtCell(int x, int z, string pawnId = null, string labelContains = null, int cellRadius = 0)
	{
		var dispatch = DispatchForceWork(x, z, pawnId, labelContains, cellRadius);
		if (dispatch.dispatched == false)
		{
			return new
			{
				success = false,
				error = dispatch.error,
				availableLabels = dispatch.availableLabels
			};
		}

		var forcedJob = ForcedWork.Instance.GetForcedJob(dispatch.pawn);
		return new
		{
			success = true,
			commandDispatched = true,
			multiplayerActive = dispatch.multiplayerActive,
			execution = dispatch.multiplayerActive ? "queued synchronized command" : "executed immediately",
			pawnId = dispatch.pawn.ThingID,
			pawnName = dispatch.pawn.Name?.ToStringShort ?? dispatch.pawn.LabelShort,
			dispatch.label,
			dispatch.workgiver,
			forceCell = dispatch.forceCell.ToString(),
			dispatch.cellRadius,
			hasForcedJobNow = ForcedWork.Instance.HasForcedJob(dispatch.pawn, ignorePreparing: true),
			currentTargetCount = forcedJob?.targets.Count ?? 0
		};
	}

	[Tool("achtung/activate_force_menu_button_at_cell", Description = "Build the same merged context-menu option used by Achtung's visible lightning button, activate that button semantically, and complete its radius drag without desktop input.")]
	public static object ActivateForceMenuButtonAtCell(
		int x,
		int z,
		string pawnId = null,
		string labelContains = null)
	{
		var pawn = FindPawn(pawnId);
		if (pawn?.Map == null)
			return new { success = false, error = "The requested pawn is not spawned on the current map." };
		if (Achtung.Settings.ForceCommandsEnabled == false)
			return new { success = false, error = "Forced-work commands are disabled in Achtung's settings." };
		if (ForcedWork.Instance.HasForcedJob(pawn, ignorePreparing: true))
			return new { success = false, error = "The pawn already has Achtung prioritized work." };

		var cell = new IntVec3(x, 0, z);
		if (cell.InBounds(pawn.Map) == false)
			return new { success = false, error = "The requested cell is outside the pawn's map." };

		var menuOptions = new MultiActions([new Colonist(pawn)], cell.ToVector3Shifted()).GetOptions();
		var forcedOptions = menuOptions.OfType<ForcedMultiFloatMenuOption>().ToList();
		var option = labelContains.NullOrEmpty()
			? forcedOptions.FirstOrDefault()
			: forcedOptions.FirstOrDefault(candidate => candidate.Label.IndexOf(labelContains, StringComparison.OrdinalIgnoreCase) >= 0);
		if (option == null)
		{
			return new
			{
				success = false,
				error = "The merged context menu did not contain a matching lightning-button option.",
				availableLabels = menuOptions.Select(candidate => candidate.Label).ToArray()
			};
		}

		var multiplayerActive = MultiplayerSupport.IsActive;
		var buttonActivated = option.ActivateForceAction(Rect.zero);
		var dragCompleted = buttonActivated && MouseTracker.GetInstance().CompleteDragging();
		var commandDispatched = buttonActivated && dragCompleted;
		var forcedJob = ForcedWork.Instance.GetForcedJob(pawn);
		var hasForcedJobNow = ForcedWork.Instance.HasForcedJob(pawn, ignorePreparing: true);

		return new
		{
			success = commandDispatched && (multiplayerActive || hasForcedJobNow),
			customPositioningEnabled = Achtung.Settings.CustomPositioningEnabled,
			forceCommandsEnabled = Achtung.Settings.ForceCommandsEnabled,
			menuUsesLightningOption = true,
			buttonActivated,
			dragCompleted,
			commandDispatched,
			multiplayerActive,
			execution = multiplayerActive ? "queued synchronized command" : "executed immediately",
			pawnId = pawn.ThingID,
			pawnName = pawn.Name?.ToStringShort ?? pawn.LabelShort,
			label = option.Label,
			forceCell = cell.ToString(),
			hasForcedJobNow,
			forcedJobStartedNow = forcedJob?.started ?? false,
			currentTargetCount = forcedJob?.targets.Count ?? 0
		};
	}

	[Tool("achtung/test_force_work_spread_at_cell", Description = "Invoke the real local or synchronized force-work path, then sample its target/marker cells every 15 ticks to verify propagation to neighbouring work items such as a wall blueprint line.")]
	public static async Task<object> TestForceWorkSpreadAtCell(
		IRimBridgeContext ctx,
		CancellationToken cancellationToken,
		[ToolParameter(Description = "Target map cell x coordinate.")] int x,
		[ToolParameter(Description = "Target map cell z coordinate.")] int z,
		[ToolParameter(Description = "Stable pawn id; when omitted, use the single selected pawn.", Required = false)] string pawnId = null,
		[ToolParameter(Description = "Optional case-insensitive force-menu label fragment.", Required = false)] string labelContains = null,
		[ToolParameter(Description = "UX drag radius passed to the real force command.", Required = false, DefaultValue = 0)] int cellRadius = 0,
		[ToolParameter(Description = "Total deterministic ticks to observe after the synchronized command executes.", Required = false, DefaultValue = 120)] int expansionTicks = 120,
		[ToolParameter(Description = "Tick interval between target/marker snapshots.", Required = false, DefaultValue = 15)] int sampleEveryTicks = 15,
		[ToolParameter(Description = "Minimum peak target count required by the contract.", Required = false, DefaultValue = 6)] int expectedMinimumTargets = 6,
		[ToolParameter(Description = "Maximum wait for Multiplayer to execute the synchronized command.", Required = false, DefaultValue = 10000)] int timeoutMs = 10000)
	{
		if (ctx == null)
			return new { success = false, error = "RimBridge context was not injected." };
		if (sampleEveryTicks <= 0 || sampleEveryTicks > 600)
			return new { success = false, error = "sampleEveryTicks must be between 1 and 600." };
		if (expansionTicks < sampleEveryTicks || expansionTicks > 3600)
			return new { success = false, error = "expansionTicks must be at least one sample interval and no more than 3600." };
		if (expectedMinimumTargets < 2)
			return new { success = false, error = "expectedMinimumTargets must be at least 2." };

		var dispatch = await ctx.MainThread.InvokeAsync(() =>
		{
			var candidatePawn = FindPawn(pawnId);
			if (candidatePawn != null && ForcedWork.Instance.HasForcedJob(candidatePawn, ignorePreparing: true))
			{
				return new ForceWorkDispatch
				{
					pawn = candidatePawn,
					error = "The pawn already has Achtung prioritized work. Clear it through the normal game command before running this contract."
				};
			}
			return DispatchForceWork(x, z, pawnId, labelContains, cellRadius);
		}, cancellationToken);

		if (dispatch.dispatched == false)
		{
			return new
			{
				success = false,
				error = dispatch.error,
				availableLabels = dispatch.availableLabels
			};
		}

		var commandTick = await ctx.MainThread.InvokeAsync(() => Find.TickManager.TicksGame, cancellationToken);
		var wait = await ctx.Game.RunUntilAsync(
			() => ForcedWork.Instance.HasForcedJob(dispatch.pawn, ignorePreparing: true),
			new RimBridgeWaitOptions
			{
				TimeoutMs = Math.Max(1000, timeoutMs),
				FailIfBusy = true
			},
			cancellationToken);

		var snapshots = new List<ForceSpreadSnapshot>();
		var initial = await ctx.MainThread.InvokeAsync(
			() => CaptureForceSpreadSnapshot(dispatch.pawn, commandTick),
			cancellationToken);
		snapshots.Add(initial);

		if (wait.Success && initial.hasForcedJob)
		{
			for (var elapsed = 0; elapsed < expansionTicks; elapsed += sampleEveryTicks)
			{
				var ticks = Math.Min(sampleEveryTicks, expansionTicks - elapsed);
				await ctx.Game.StepTicksAsync(ticks, cancellationToken: cancellationToken);
				var snapshot = await ctx.MainThread.InvokeAsync(
					() => CaptureForceSpreadSnapshot(dispatch.pawn, commandTick),
					cancellationToken);
				snapshots.Add(snapshot);
			}
		}

		var seen = initial.forceMarkerCells
			.Select(cell => new XY(cell.x, cell.z))
			.ToHashSet();
		var adjacentAdditions = new List<ForceCellSnapshot>();
		foreach (var snapshot in snapshots.Skip(1))
		{
			var current = snapshot.forceMarkerCells.Select(cell => new XY(cell.x, cell.z)).ToArray();
			var additions = current.Where(cell => seen.Contains(cell) == false).ToArray();
			foreach (var cell in additions)
			{
				if (seen.Any(previous => Math.Abs(previous.x - cell.x) <= 1
					&& Math.Abs(previous.y - cell.y) <= 1
					&& previous != cell))
				{
					adjacentAdditions.Add(new ForceCellSnapshot
					{
						x = cell.x,
						z = cell.y,
						cell = cell.ToString()
					});
				}
			}
			seen.UnionWith(additions);
		}

		var peakTargetCount = snapshots.Max(snapshot => snapshot.targetCount);
		var expanded = peakTargetCount > initial.targetCount;
		var spreadToNeighbour = adjacentAdditions.Count > 0;
		var success = wait.Success
			&& initial.hasForcedJob
			&& expanded
			&& spreadToNeighbour
			&& peakTargetCount >= expectedMinimumTargets;

		return new
		{
			success,
			contract = dispatch.multiplayerActive
				? "The lightning-button command is synchronized by Multiplayer, and Achtung's deterministic 15-tick expansion adds neighbouring work targets; the force markers render from these same cells."
				: "The lightning-button command uses Achtung's original single-player path, and its frame-driven expansion adds neighbouring work targets; the force markers render from these same cells.",
			command = new
			{
				dispatch.multiplayerActive,
				execution = dispatch.multiplayerActive ? "queued synchronized command" : "executed original single-player path",
				pawnId = dispatch.pawn.ThingID,
				pawnName = dispatch.pawn.Name?.ToStringShort ?? dispatch.pawn.LabelShort,
				dispatch.label,
				dispatch.workgiver,
				forceCell = dispatch.forceCell.ToString(),
				dispatch.cellRadius,
				commandTick
			},
			assertions = new
			{
				synchronizedCommandExecuted = wait.Success && initial.hasForcedJob,
				expandedBeyondInitialTargets = expanded,
				spreadToAdjacentNeighbour = spreadToNeighbour,
				reachedExpectedMinimum = peakTargetCount >= expectedMinimumTargets,
				initialTargetCount = initial.targetCount,
				peakTargetCount,
				expectedMinimumTargets
			},
			adjacentAdditions = adjacentAdditions
				.GroupBy(cell => cell.cell)
				.Select(group => group.First())
				.OrderBy(cell => cell.x)
				.ThenBy(cell => cell.z)
				.ToArray(),
			snapshots,
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
	}

	static ForceWorkDispatch DispatchForceWork(int x, int z, string pawnId, string labelContains, int cellRadius)
	{
		var selection = ResolveForceWorkSelection(x, z, pawnId, labelContains);
		if (selection.option == null)
		{
			return new ForceWorkDispatch
			{
				pawn = selection.pawn,
				error = selection.error,
				availableLabels = selection.availableLabels
			};
		}

		var radius = Math.Max(0, Math.Min(cellRadius, (int)GenRadial.MaxRadialPatternRadius - 1));
		var multiplayerActive = MultiplayerSupport.IsActive;
		if (multiplayerActive)
		{
			MultiplayerSupport.ForceWork(
				[selection.pawn],
				selection.option.forceWorkgiver.def,
				selection.option.forceCell,
				radius);
		}
		else
		{
			var success = ForcedMultiFloatMenuOption.ForceAction(
				selection.pawn,
				selection.option.forceWorkgiver,
				selection.option.forceCell);
			if (success == false)
			{
				return new ForceWorkDispatch
				{
					pawn = selection.pawn,
					error = "Achtung's original single-player force-work path rejected the selected option.",
					availableLabels = selection.availableLabels
				};
			}
			var forcedJob = ForcedWork.Instance.GetForcedJob(selection.pawn);
			if (forcedJob != null)
				forcedJob.cellRadius = radius;
		}

		return new ForceWorkDispatch
		{
			dispatched = true,
			multiplayerActive = multiplayerActive,
			pawn = selection.pawn,
			label = selection.option.Label,
			workgiver = selection.option.forceWorkgiver.def.defName,
			forceCell = selection.option.forceCell,
			cellRadius = radius,
			availableLabels = selection.availableLabels
		};
	}

	static ForceWorkSelection ResolveForceWorkSelection(int x, int z, string pawnId, string labelContains)
	{
		var pawn = FindPawn(pawnId);
		if (pawn == null)
		{
			return new ForceWorkSelection
			{
				error = "Pawn not found or no single pawn selected."
			};
		}

		var clickPos = new Vector3(x, 0f, z);
		var options = new List<FloatMenuOption>();
		var existingLabels = new HashSet<string>();
		var draftState = pawn.Drafted;

		void AddOptionsForCurrentDraftState()
		{
			foreach (var option in FloatMenuMakerMap.GetOptions([pawn], clickPos, out _))
			{
				if (existingLabels.Add(option.Label))
					options.Add(option);
			}
		}

		AddOptionsForCurrentDraftState();
		if (Achtung.Settings.keepDraftedAndUndraftedCommandsSeparate == false)
		{
			try
			{
				_ = SetDraftStatus(pawn, !draftState);
				AddOptionsForCurrentDraftState();
			}
			finally
			{
				_ = SetDraftStatus(pawn, draftState);
			}
		}

		var forcedOptions = options.OfType<ForcedFloatMenuOption>().ToList();

		if (forcedOptions.Count == 0)
		{
			return new ForceWorkSelection
			{
				pawn = pawn,
				error = "No Achtung force options were available at that cell.",
				availableLabels = options.Select(option => option.Label).ToArray()
			};
		}

		var chosen = labelContains.NullOrEmpty()
			? forcedOptions.FirstOrDefault()
			: forcedOptions.FirstOrDefault(option => option.Label.IndexOf(labelContains, StringComparison.OrdinalIgnoreCase) >= 0);

		if (chosen == null)
		{
			return new ForceWorkSelection
			{
				pawn = pawn,
				error = $"No Achtung force option matched '{labelContains}'.",
				availableLabels = forcedOptions.Select(option => option.Label).ToArray()
			};
		}

		return new ForceWorkSelection
		{
			pawn = pawn,
			option = chosen,
			availableLabels = forcedOptions.Select(option => option.Label).ToArray()
		};
	}

	static ForceSpreadSnapshot CaptureForceSpreadSnapshot(Pawn pawn, int commandTick)
	{
		var forcedJob = ForcedWork.Instance.GetForcedJob(pawn);
		var ticksGame = Find.TickManager?.TicksGame ?? 0;
		return new ForceSpreadSnapshot
		{
			ticksGame = ticksGame,
			elapsedTicks = ticksGame - commandTick,
			hasForcedJob = ForcedWork.Instance.HasForcedJob(pawn, ignorePreparing: true),
			started = forcedJob?.started ?? false,
			cancelled = forcedJob?.cancelled ?? false,
			targetCount = forcedJob?.targets.Count ?? 0,
			forceMarkerCells = forcedJob?.AllCells(onlyValid: true)
				.Distinct()
				.OrderBy(cell => cell.x)
				.ThenBy(cell => cell.y)
				.Select(cell => new ForceCellSnapshot
				{
					x = cell.x,
					z = cell.y,
					cell = cell.ToString()
				})
				.ToArray() ?? []
		};
	}
}
