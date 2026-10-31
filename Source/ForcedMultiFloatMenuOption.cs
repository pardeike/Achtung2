using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace AchtungMod;

[StaticConstructorOnStartup]
public class ForcedMultiFloatMenuOption : FloatMenuOption
{
	public static readonly Texture2D[] Forcing =
	[
		ContentFinder<Texture2D>.Get("Forcing0", true),
		ContentFinder<Texture2D>.Get("Forcing1", true)
	];

	public readonly List<Pawn> forcedPawns;
	public readonly List<FloatMenuOption> options;
	public bool actionSelected = false;
	readonly bool canForce;

	public static float buttonSpace = 10;
	public static float buttonWidth = 32;

	public ForcedMultiFloatMenuOption(List<Pawn> forcedPawns, List<FloatMenuOption> options, Func<Rect, bool> originalExtraPartOnGUI, string label, float originalExtraPartWidth = 0f)
		: base(label, null, MenuOptionPriority.Default, null, null, 0f, null, null, false, 0)
	{
		this.forcedPawns = forcedPawns;
		this.options = options;
		canForce = options.OfType<ForcedFloatMenuOption>().Any(option => CanForceWork(option.forcePawn, option.forceWorkgiver, option.forceCell));
		extraPartOnGUI = extraPartRect =>
		{
			var originalRect = extraPartRect;
			originalRect.xMax -= buttonSpace + buttonWidth;
			if (originalExtraPartOnGUI != null && originalExtraPartOnGUI(originalRect))
				return true;
			return RenderExtraPartOnGui(extraPartRect);
		};
		// somehow necessary or else 'extraPartWidth' will be 0
		extraPartWidth = originalExtraPartWidth + buttonSpace + buttonWidth;
	}

	public bool RenderExtraPartOnGui(Rect drawRect)
	{
		var rect = drawRect;
		rect.xMin = rect.xMax - buttonWidth;
		rect = rect.Rounded();
		var buttonRect = rect;
		var padding = Mathf.FloorToInt((rect.height - 16) / 2);
		rect.y += padding;
		rect.height -= 2 * padding;

		var isOver = canForce && Mouse.IsOver(buttonRect);
		var oldColor = GUI.color;
		if (!canForce)
			GUI.color = new Color(oldColor.r, oldColor.g, oldColor.b, oldColor.a * 0.35f);
		GUI.DrawTexture(rect, Forcing[isOver ? 1 : 0], ScaleMode.ScaleToFit);
		GUI.color = oldColor;

		var selected = isOver && Input.GetMouseButtonDown(0);
		if (selected)
			return ActivateForceAction(drawRect);
		return false;
	}

	public bool ActivateForceAction(Rect drawRect)
	{
		if (!canForce)
			return false;
		if (actionSelected)
			return true;
		actionSelected = true;

		if (MultiplayerSupport.IsActive)
		{
			var forceOption = options.OfType<ForcedFloatMenuOption>().FirstOrDefault(option => CanForceWork(option.forcePawn, option.forceWorkgiver, option.forceCell));
			return forceOption != null
				? BeginForceWork(forcedPawns, forceOption.forceWorkgiver, forceOption.forceCell)
				: false;
		}

		return options.OfType<ForcedFloatMenuOption>().Any(forceOption =>
		{
			var forced = false;
			foreach (var pawn in forcedPawns.Distinct().Where(pawn => CanForceWork(pawn, forceOption.forceWorkgiver, forceOption.forceCell)))
				forced |= ForceAction(pawn, forceOption.forceWorkgiver, forceOption.forceCell);
			return forced;
		});
	}

	static bool BeginForceWork(List<Pawn> pawns, WorkGiver_Scanner forceWorkgiver, IntVec3 clickedCell)
	{
		if (forceWorkgiver?.def == null)
			return false;

		var validPawns = pawns
			.Where(pawn => CanForceWork(pawn, forceWorkgiver, clickedCell))
			.Distinct()
			.OrderBy(pawn => pawn.thingIDNumber)
			.ToList();
		if (validPawns.Count == 0)
			return false;

		var cellRadius = 0;
		MouseTracker.StartDragging(
			validPawns[0],
			clickedCell,
			radius => cellRadius = radius,
			() => MultiplayerSupport.ForceWork(validPawns, forceWorkgiver.def, clickedCell, cellRadius));
		return true;
	}

	static bool CanForceWork(Pawn pawn, WorkGiver_Scanner forceWorkgiver, IntVec3 clickedCell)
	{
		if (pawn?.Map == null || forceWorkgiver == null || clickedCell.InBounds(pawn.Map) == false)
			return false;

		var forcedWork = ForcedWork.Instance;
		var wasPreparing = forcedWork.IsPreparing(pawn);
		forcedWork.Prepare(pawn);
		try
		{
			return ForcedWork.GetCombinedDefs(forceWorkgiver)
				.Select(def => def?.giverClass == null ? null : def.Worker as WorkGiver_Scanner)
				.Where(workgiver => workgiver != null)
				.Any(workgiver => ForcedWork.HasJobItem(pawn, workgiver, clickedCell, false) != null
					|| ForcedWork.HasJobItem(pawn, workgiver, clickedCell, true) != null);
		}
		finally
		{
			if (wasPreparing) forcedWork.Prepare(pawn);
			else forcedWork.Unprepare(pawn);
		}
	}

	public static bool ForceAction(Pawn pawn, WorkGiver_Scanner forceWorkgiver, IntVec3 clickedCell)
	{
		var forcedWork = ForcedWork.Instance;
		var wasPreparing = forcedWork.IsPreparing(pawn);
		forcedWork.Prepare(pawn);
		try
		{
			var workgiverDefs = ForcedWork.GetCombinedDefs(forceWorkgiver);
			foreach (var expandSearch in new[] { false, true })
				foreach (var workgiverDef in workgiverDefs)
				{
					var workgiver = workgiverDef.giverClass == null ? null : workgiverDef.Worker as WorkGiver_Scanner;
					if (workgiver == null)
						continue;

					var jobItem = ForcedWork.HasJobItem(pawn, workgiver, clickedCell, expandSearch);
					if (jobItem == null)
						continue;

					Tools.CancelWorkOn(pawn, jobItem);

					var firstJob = forcedWork.AddForcedJob(pawn, workgiverDefs, jobItem, out var forcedJob);
					forcedJob.orderStartCell = clickedCell;
					forcedJob.explicitTarget = jobItem.HasThing && jobItem.Thing.OccupiedRect().Contains(clickedCell) ? jobItem.Thing : null;
					if (firstJob)
					{
						forcedJob.ExpandJob(1 + Find.Selector.SelectedPawns.Count * 2);
						var success = forcedJob.GetNextNonConflictingJob(forcedWork);
						if (success == false)
							continue;
						else
							forcedWork.Unprepare(pawn);
					}

					MouseTracker.StartDragging(
						pawn,
						clickedCell,
						cellRadius => forcedJob?.cellRadius = cellRadius,
						() => forcedJob.Start()
					);

					return true;
				}

			Messages.Message("CouldNotFindMoreForcedWork".Translate(pawn.Name.ToStringShort), MessageTypeDefOf.RejectInput);
			return false;
		}
		finally
		{
			if (wasPreparing) forcedWork.Prepare(pawn);
			else forcedWork.Unprepare(pawn);
		}
	}

	internal static bool ApplyForceWork(Pawn pawn, WorkGiver_Scanner forceWorkgiver, IntVec3 clickedCell, int initialExpansionCount, int cellRadius)
	{
		var forcedWork = ForcedWork.Instance;
		var wasPreparing = forcedWork.IsPreparing(pawn);
		forcedWork.Prepare(pawn);

		try
		{
			var workgiverDefs = ForcedWork.GetCombinedDefs(forceWorkgiver);
			foreach (var expandSearch in new[] { false, true })
				foreach (var workgiverDef in workgiverDefs)
				{
					var workgiver = workgiverDef.giverClass == null ? null : workgiverDef.Worker as WorkGiver_Scanner;
					if (workgiver == null)
						continue;

					var jobItem = ForcedWork.HasJobItem(pawn, workgiver, clickedCell, expandSearch);
					if (jobItem == null)
						continue;

					Tools.CancelWorkOn(pawn, jobItem);

					var firstJob = forcedWork.AddForcedJob(pawn, workgiverDefs, jobItem, out var forcedJob);
					forcedJob.orderStartCell = clickedCell;
					forcedJob.explicitTarget = jobItem.HasThing && jobItem.Thing.OccupiedRect().Contains(clickedCell) ? jobItem.Thing : null;
					if (firstJob)
					{
						forcedJob.ExpandJob(initialExpansionCount);
						if (forcedJob.GetNextNonConflictingJob(forcedWork) == false)
							continue;
						forcedWork.Unprepare(pawn);
					}

					forcedJob.cellRadius = cellRadius;
					forcedJob.Start();
					return true;
				}

			if (MultiplayerSupport.ShouldShowCommandFeedback)
				Messages.Message("CouldNotFindMoreForcedWork".Translate(pawn.Name.ToStringShort), MessageTypeDefOf.RejectInput);
			return false;
		}
		finally
		{
			if (wasPreparing) forcedWork.Prepare(pawn);
			else forcedWork.Unprepare(pawn);
		}
	}
}
