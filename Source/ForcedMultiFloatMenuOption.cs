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

			var success = options.Any(option =>
			{
				if (option is ForcedFloatMenuOption forceOption)
					return ForceAction(forcedPawns, forceOption.forceWorkgiver, forceOption.forceCell.x, forceOption.forceCell.z);
				if (option.extraPartOnGUI != null)
					return option.extraPartOnGUI(drawRect);
				return false;
			});

			if (success)
				return true;
		}
		return false;
	}

	public static bool ForceAction(List<Pawn> forcedPawns, WorkGiver_Scanner forceWorkgiver, int x, int z)
	{
		var forcedCell = new IntVec3(x, 0, z);
		return forcedPawns.Select(forcePawn =>
		{
			var forcedWork = ForcedWork.Instance;
			forcedWork.Prepare(forcePawn);

			var workgiverDefs = ForcedWork.GetCombinedDefs(forceWorkgiver);
			foreach (var expandSearch in new[] { false, true })
				foreach (var workgiverDef in workgiverDefs)
				{
					var workgiver = workgiverDef.giverClass == null ? null : workgiverDef.Worker as WorkGiver_Scanner;
					if (workgiver == null)
						continue;

					var item = ForcedWork.HasJobItem(forcePawn, workgiver, forcedCell, expandSearch);
					if (item == null)
						continue;

					Tools.CancelWorkOn(forcePawn, item);

					if (forcedWork.AddForcedJob(forcePawn, workgiverDefs, item, out var forcedJob))
					{
						var success = false;
						var firstAvailableTarget = forcedJob.GetSortedTargets([], false).FirstOrDefault();
						if (firstAvailableTarget != null)
						{
							var job = ForcedWork.GetJobItem(forcePawn, workgiver, firstAvailableTarget);
							success = job != null && forcePawn.jobs.TryTakeOrderedJobPrioritizedWork(job, workgiver, firstAvailableTarget.Cell);
						}
						if (success == false)
							continue;
						ForcedWork.Instance.Unprepare(forcePawn);
					}

					forcedJob = ForcedWork.Instance.GetForcedJob(forcePawn);
					MouseTracker.StartDragging(
						forcePawn,
						forcedCell,
						cellRadius =>
						{
							if (forcedJob != null)
								forcedJob.cellRadius = cellRadius;
						},
						() => forcedJob.Start()
					);

					return true;
				}

			forcedWork.RemoveForcedJob(forcePawn);
			Messages.Message("CouldNotFindMoreForcedWork".Translate(forcePawn.Name.ToStringShort), MessageTypeDefOf.RejectInput);
			return false;

		}).ToList().Any(success => success);
	}
}