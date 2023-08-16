using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace AchtungMod
{
	public class ForcedMultiFloatMenuOption : ForcedFloatMenuOption
	{
		public List<ForcedFloatMenuOption> options;

		public ForcedMultiFloatMenuOption(List<Pawn> pawns, string label) : base(pawns, label, null, MenuOptionPriority.Default, null, null, 0f, null, null, false, 0) { }

		public override bool ForceAction(bool useTracking)
		{
			var forcedWork = ForcedWork.Instance;
			var sharedCells = new List<IntVec3>();

			var result = false;
			foreach (var option in options)
			{
				if (sharedCells.Count > 0)
				{
					foreach (var cell in sharedCells)
					{
						option.forceCell = cell;
						if (option.ForceAction(useTracking))
							break;
					}
				}
				else
				{
					if (option.ForceAction(useTracking))
					{
						var jobItem = forcedWork.GetForcedJobs(option.forcePawn).FirstOrDefault();
						if (jobItem != null)
						{
							sharedCells = jobItem.GetSortedTargets(new HashSet<int>()).Select(item => item.Cell).ToList();
							result = true;
						}
					}
				}
			}
			return result;
		}
	}

	[StaticConstructorOnStartup]
	public class ForcedFloatMenuOption : FloatMenuOption
	{
		public static readonly Texture2D[] Forcing = new[]
		{
			ContentFinder<Texture2D>.Get("Forcing0", true),
			ContentFinder<Texture2D>.Get("Forcing1", true)
		};

		public Pawn forcePawn;
		public IntVec3 forceCell;
		public WorkGiver_Scanner forceWorkgiver;

		public static TaggedString ForcedLabelText = Translator.CanTranslate("ForceMenuButtonText") ? "ForceMenuButtonText".Translate() : new TaggedString(" ! ");
		public static float buttonSpace = 10;
		public static float buttonSize = 16;

		public static FloatMenuOption CreateForcedMenuItem(string label, Action action, MenuOptionPriority priority, Action<Rect> mouseoverGuiAction, Thing revalidateClickTarget, float extraPartWidth, Func<Rect, bool> extraPartOnGUI, WorldObject revalidateWorldClickTarget, bool playSelectionSound, int orderInPriority, Pawn pawn, Vector3 clickPos, WorkGiver_Scanner workgiver)
		{
			if (action == null)
				return new FloatMenuOption(label, action, priority, mouseoverGuiAction, revalidateClickTarget, extraPartWidth, extraPartOnGUI, revalidateWorldClickTarget, playSelectionSound, orderInPriority);

			var option = new ForcedFloatMenuOption(new List<Pawn> { pawn }, label, action, priority, mouseoverGuiAction, revalidateClickTarget, extraPartWidth, extraPartOnGUI, revalidateWorldClickTarget, playSelectionSound, orderInPriority)
			{
				forcePawn = pawn,
				forceCell = IntVec3.FromVector3(clickPos),
				forceWorkgiver = workgiver
			};
			option.extraPartOnGUI = extraPartRect => option.RenderExtraPartOnGui(extraPartRect);
			return option;
		}

		static Action ActionRunner(List<Pawn> pawns, Action action)
		{
			return () =>
			{
				foreach (var pawn in pawns)
					ForcedWork.Instance.Unprepare(pawn);

				action(); // run original command

				if (Achtung.Settings.ignoreForbidden)
				{
					var reservations = pawns.Where(pawn => ForcedWork.Instance.HasForcedJob(pawn) == false)
						.SelectMany(pawn => new List<Thing> { pawn.CurJob.targetA.Thing, pawn.CurJob.targetB.Thing, pawn.CurJob.targetC.Thing }.OfType<Thing>())
						.Where(thing => thing.IsForbidden(Faction.OfPlayer))
						.SelectMany(thing => thing.Map.reservationManager.reservations.Select(reservation => (thing, reservation))
						.Where(item => item.reservation.Target == item.thing))
						.ToList();
					reservations.Do(item => item.reservation.Claimant.jobs.EndCurrentOrQueuedJob(item.reservation.job, JobCondition.InterruptForced, true));
				}
			};
		}

		public ForcedFloatMenuOption(List<Pawn> pawns, string label, Action action, MenuOptionPriority priority, Action<Rect> mouseoverGuiAction, Thing revalidateClickTarget, float extraPartWidth, Func<Rect, bool> extraPartOnGUI, WorldObject revalidateWorldClickTarget, bool playSelectionSound, int orderInPriority)
			: base(label, ActionRunner(pawns, action), priority, mouseoverGuiAction, revalidateClickTarget, extraPartWidth, extraPartOnGUI, revalidateWorldClickTarget, playSelectionSound, orderInPriority)
		{
			// somehow necessary or else 'extraPartWidth' will be 0
			base.extraPartWidth = buttonSpace + buttonSize;
		}

		public bool RenderExtraPartOnGui(Rect drawRect)
		{
			var rect = drawRect;
			rect.xMin = rect.xMax - buttonSize;
			rect = rect.Rounded();
			var buttonRect = rect;
			var padding = Mathf.FloorToInt((rect.height - buttonSize) / 2);
			rect.y += padding;
			rect.height -= 2 * padding;

			var isOver = Mouse.IsOver(buttonRect);
			GUI.DrawTexture(rect, Forcing[isOver ? 1 : 0], ScaleMode.ScaleToFit);

			var selected = isOver && Input.GetMouseButtonDown(0);
			if (selected)
			{
				var success = ForceAction(true);
				if (success)
					return true;
			}
			return false;
			/* TODO enable later
			return Achtung.tutor?.HintWithRect("force-button", true, rect.ContractedBy(0, 20), (hintUsed) =>
			{
				var selected = Widgets.ButtonInvisible(rect);
				if (selected)
				{
					hintUsed();
					var success = ForceAction();
					if (success)
						return true;
				}
				return false;
			});
			*/
		}

		[SyncMethod] // multiplayer
		public static bool ForceActionSynced(Pawn forcePawn, WorkGiver_Scanner forceWorkgiver, int x, int z, bool useTracking)
		{
			var cell = new IntVec3(x, 0, z);

			var forcedWork = ForcedWork.Instance;
			forcedWork.Prepare(forcePawn);

			var workgiverDefs = ForcedWork.GetCombinedDefs(forceWorkgiver);
			foreach (var expandSearch in new[] { false, true })
				foreach (var workgiverDef in workgiverDefs)
				{
					var workgiver = workgiverDef.giverClass == null ? null : workgiverDef.Worker as WorkGiver_Scanner;
					if (workgiver == null)
						continue;

					var item = ForcedWork.HasJobItem(forcePawn, workgiver, cell, expandSearch);
					if (item == null)
						continue;

					Tools.CancelWorkOn(forcePawn, item);

					if (forcedWork.AddForcedJob(forcePawn, workgiverDefs, item))
					{
						var job = ForcedWork.GetJobItem(forcePawn, workgiver, item);
						var success = job != null && forcePawn.jobs.TryTakeOrderedJobPrioritizedWork(job, workgiver, cell);
						if (success == false)
							continue;
						else
							ForcedWork.Instance.Unprepare(forcePawn);
					}

					if (useTracking)
					{
						MouseTracker.StartDragging(forcePawn, cell, cellRadius =>
						{
							var forcedJob = ForcedWork.Instance.GetForcedJob(forcePawn);
							if (forcedJob != null)
								forcedJob.cellRadius = cellRadius;
						});
					}

					return true;
				}

			forcedWork.RemoveForcedJob(forcePawn);
			Messages.Message("CouldNotFindMoreForcedWork".Translate(forcePawn.Name.ToStringShort), MessageTypeDefOf.RejectInput);
			return false;
		}

		public virtual bool ForceAction(bool useTracking)
		{
			return ForceActionSynced(forcePawn, forceWorkgiver, forceCell.x, forceCell.z, useTracking);
		}
	}
}
