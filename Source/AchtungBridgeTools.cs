using RimBridgeServer.Annotations;
using RimWorld;
using System;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace AchtungMod;

public sealed class AchtungBridgeTools
{
	static Pawn FindPawn(string pawnId)
	{
		if (pawnId.NullOrEmpty())
			return Find.Selector.SingleSelectedThing as Pawn;
		return Find.CurrentMap?.mapPawns?.AllPawnsSpawned?.FirstOrDefault(pawn =>
			pawn.ThingID == pawnId
			|| $"Thing_{pawn.ThingID}" == pawnId
			|| pawn.GetUniqueLoadID() == pawnId);
	}

	[Tool("achtung/get_selected_pawn_forced_state", Description = "Read Achtung forced-work state for the currently selected pawn.")]
	public object GetSelectedPawnForcedState()
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
				targetsPreview = forcedJob.targets
					.Take(8)
					.Select(target => new
					{
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

	[Tool("achtung/force_work_at_cell", Description = "Invoke Achtung's actual force-work button path for a pawn at a map cell, optionally matching a menu label fragment.")]
	public object ForceWorkAtCell(int x, int z, string pawnId = null, string labelContains = null, int cellRadius = -1, int expandCount = 0)
	{
		var pawn = FindPawn(pawnId);
		if (pawn == null)
		{
			return new
			{
				success = false,
				error = "Pawn not found or no single pawn selected."
			};
		}

		var clickPos = new Vector3(x, 0f, z);
		var options = new System.Collections.Generic.List<FloatMenuOption>();
		var existingLabels = new System.Collections.Generic.HashSet<string>();
		var draftState = pawn.Drafted;

		void AddOptionsForCurrentDraftState()
		{
			FloatMenuOptionProvider_EnterMapPortal_GetSingleOptionFor_Patch.currentPawn = pawn;
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
				_ = Tools.SetDraftStatus(pawn, !draftState);
				AddOptionsForCurrentDraftState();
			}
			finally
			{
				_ = Tools.SetDraftStatus(pawn, draftState);
			}
		}

		var forcedOptions = options.OfType<ForcedFloatMenuOption>().ToList();

		if (forcedOptions.Count == 0)
		{
			return new
			{
				success = false,
				error = "No Achtung force options were available at that cell.",
				availableLabels = options.Select(option => option.Label).ToArray()
			};
		}

		var chosen = labelContains.NullOrEmpty()
			? forcedOptions.FirstOrDefault()
			: forcedOptions.FirstOrDefault(option => option.Label.IndexOf(labelContains, StringComparison.OrdinalIgnoreCase) >= 0);

		if (chosen == null)
		{
			return new
			{
				success = false,
				error = $"No Achtung force option matched '{labelContains}'.",
				availableLabels = forcedOptions.Select(option => option.Label).ToArray()
			};
		}

		var success = ForcedMultiFloatMenuOption.ForceAction(pawn, chosen.forceWorkgiver, chosen.forceCell);
		var forcedJob = ForcedWork.Instance.GetForcedJob(pawn);
		if (success && forcedJob != null)
		{
			if (cellRadius >= 0)
			{
				forcedJob.cellRadius = cellRadius;
				forcedJob.Start();
			}
			if (expandCount > 0)
				_ = forcedJob.ExpandJob(expandCount);
		}

		return new
		{
			success,
			pawnId = pawn.ThingID,
			pawnName = pawn.Name?.ToStringShort ?? pawn.LabelShort,
			label = chosen.Label,
			workgiver = chosen.forceWorkgiver?.def?.defName,
			forceCell = chosen.forceCell.ToString(),
			hasForcedJob = ForcedWork.Instance.HasForcedJob(pawn, ignorePreparing: true),
			cellRadius = forcedJob?.cellRadius ?? 0,
			started = forcedJob?.started ?? false,
			targetCount = forcedJob?.targets.Count ?? 0,
			lastAssignedCell = forcedJob?.lastAssignedCell.ToString()
		};
	}
}
