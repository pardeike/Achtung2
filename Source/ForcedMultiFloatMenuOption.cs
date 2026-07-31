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

	public static float buttonSpace = 10;
	public static float buttonWidth = 32;

	public ForcedMultiFloatMenuOption(List<Pawn> forcedPawns, List<FloatMenuOption> options, Func<Rect, bool> originalExtraPartOnGUI, string label)
		: base(label, null, MenuOptionPriority.Default, null, null, 0f, null, null, false, 0)
	{
		this.forcedPawns = forcedPawns;
		this.options = options;
		extraPartOnGUI = extraPartRect =>
		{
			if (originalExtraPartOnGUI != null && originalExtraPartOnGUI(extraPartRect))
				return true;
			return RenderExtraPartOnGui(extraPartRect);
		};
		// somehow necessary or else 'extraPartWidth' will be 0
		extraPartWidth = buttonSpace + buttonWidth;
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

		var isOver = Mouse.IsOver(buttonRect);
		GUI.DrawTexture(rect, Forcing[isOver ? 1 : 0], ScaleMode.ScaleToFit);

		var selected = isOver && Input.GetMouseButtonDown(0);
		if (selected)
		{
			if (actionSelected)
				return true;
			actionSelected = true;

			bool success;
			if (MultiplayerSupport.IsActive)
			{
				var forceOption = options.OfType<ForcedFloatMenuOption>().FirstOrDefault();
				success = forceOption != null
					? BeginForceWork(forcedPawns, forceOption.forceWorkgiver, forceOption.forceCell)
					: options.Any(option => option.extraPartOnGUI?.Invoke(drawRect) == true);
			}
			else
			{
				success = options.Any(option =>
				{
					if (option is ForcedFloatMenuOption forceOption)
					{
						var forced = false;
						foreach (var pawn in forcedPawns)
							forced |= ForceAction(pawn, forceOption.forceWorkgiver, forceOption.forceCell);
						return forced;
					}
					if (option.extraPartOnGUI != null)
						return option.extraPartOnGUI(drawRect);
					return false;
				});
			}

			if (success)
				return true;
		}
		return false;
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
			forcedWork.Unprepare(pawn);
		}
	}

	public static bool ForceAction(Pawn pawn, WorkGiver_Scanner forceWorkgiver, IntVec3 clickedCell)
	{
		var forcedWork = ForcedWork.Instance;
		forcedWork.Prepare(pawn);

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

				if (forcedWork.AddForcedJob(pawn, workgiverDefs, jobItem, out var forcedJob))
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

		forcedWork.RemoveForcedJob(pawn);
		Messages.Message("CouldNotFindMoreForcedWork".Translate(pawn.Name.ToStringShort), MessageTypeDefOf.RejectInput);
		return false;
	}

	internal static bool ApplyForceWork(Pawn pawn, WorkGiver_Scanner forceWorkgiver, IntVec3 clickedCell, int initialExpansionCount, int cellRadius)
	{
		var forcedWork = ForcedWork.Instance;
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

					if (forcedWork.AddForcedJob(pawn, workgiverDefs, jobItem, out var forcedJob))
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

			forcedWork.RemoveForcedJob(pawn);
			if (MultiplayerSupport.ShouldShowCommandFeedback)
				Messages.Message("CouldNotFindMoreForcedWork".Translate(pawn.Name.ToStringShort), MessageTypeDefOf.RejectInput);
			return false;
		}
		finally
		{
			forcedWork.Unprepare(pawn);
		}
	}
}
