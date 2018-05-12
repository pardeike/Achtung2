using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace AchtungMod
{
	public class ForcedMultiFloatMenuOption : ForcedFloatMenuOption
	{
		public List<ForcedFloatMenuOption> options;

		public override bool ForceAction()
		{
			var forcedWork = Find.World.GetComponent<ForcedWork>();
			var sharedCells = new List<IntVec3>();

			var result = false;
			foreach (var option in options)
			{
				if (sharedCells.Count > 0)
				{
					foreach (var cell in sharedCells)
					{
						option.forceCell = cell;
						if (option.ForceAction())
							break;
					}
				}
				else
				{
					if (option.ForceAction())
					{
						var jobItem = forcedWork.GetForcedJobs(option.forcePawn).FirstOrDefault();
						if (jobItem != null)
						{
							sharedCells = jobItem.targets.Select(target => target.item.Cell).OrderBy(cell => forceCell.DistanceToSquared(cell)).ToList();
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
		public static readonly Texture2D L_Normal = ContentFinder<Texture2D>.Get("ForceButton-L0", true);
		public static readonly Texture2D M_Normal = ContentFinder<Texture2D>.Get("ForceButton-M0", true);
		public static readonly Texture2D R_Normal = ContentFinder<Texture2D>.Get("ForceButton-R0", true);
		public static readonly Texture2D L_Selected = ContentFinder<Texture2D>.Get("ForceButton-L1", true);
		public static readonly Texture2D M_Selected = ContentFinder<Texture2D>.Get("ForceButton-M1", true);
		public static readonly Texture2D R_Selected = ContentFinder<Texture2D>.Get("ForceButton-R1", true);

		public Pawn forcePawn;
		public IntVec3 forceCell;
		public WorkGiver_Scanner forceWorkgiver;

		public static string ForcedLabelText => Translator.CanTranslate("ForceMenuButtonText") ? "ForceMenuButtonText".Translate() : " ! ";
		public static float beforeButtonSpace = 10f;
		public static float padding = 11f;

		public static FloatMenuOption CreateForcedMenuItem(string label, Action action, MenuOptionPriority priority, Action mouseoverGuiAction, Thing revalidateClickTarget, float extraPartWidth, Func<Rect, bool> extraPartOnGUI, WorldObject revalidateWorldClickTarget, Pawn pawn, Vector3 clickPos, WorkGiver_Scanner workgiver)
		{
			if (action == null)
				return new FloatMenuOption(label, action, priority, mouseoverGuiAction, revalidateClickTarget, extraPartWidth, extraPartOnGUI, revalidateWorldClickTarget);

			Text.Font = GameFont.Tiny;
			var size = Text.CalcSize(ForcedLabelText);
			var buttonWidth = size.x + 2f * padding;

			var option = new ForcedFloatMenuOption(label, action, priority, mouseoverGuiAction, revalidateClickTarget, beforeButtonSpace + buttonWidth, null, revalidateWorldClickTarget)
			{
				forcePawn = pawn,
				forceCell = IntVec3.FromVector3(clickPos),
				forceWorkgiver = workgiver,
				extraPartOnGUI = null // needs to be set below
			};
			option.extraPartOnGUI = drawRect => option.RenderExtraPartOnGui(drawRect);
			return option;
		}

		public ForcedFloatMenuOption()
		{
		}

		public ForcedFloatMenuOption(string label, Action action, MenuOptionPriority priority, Action mouseoverGuiAction, Thing revalidateClickTarget, float extraPartWidth, Func<Rect, bool> extraPartOnGUI, WorldObject revalidateWorldClickTarget)
			: base(label, action, priority, mouseoverGuiAction, revalidateClickTarget, extraPartWidth, extraPartOnGUI, revalidateWorldClickTarget) { }

		public bool RenderExtraPartOnGui(Rect drawRect)
		{
			var rect = drawRect;
			rect.xMin += beforeButtonSpace;

			var highlight = Mouse.IsOver(rect);
			var b_left = highlight ? L_Selected : L_Normal;
			var b_middle = highlight ? M_Selected : M_Normal;
			var b_right = highlight ? R_Selected : R_Normal;

			var r = rect;
			r.height = b_middle.height;
			r.y += (rect.height - r.height) / 2f;

			r.width = b_left.width;
			GUI.DrawTexture(r, b_left);
			r.x += r.width;

			r.width = rect.width - b_left.width - b_right.width;
			GUI.DrawTexture(r, b_middle, ScaleMode.StretchToFill);
			r.x += r.width;

			r.width = b_right.width;
			GUI.DrawTexture(r, b_right);

			Text.Font = GameFont.Tiny;
			GUI.color = Color.black;
			Text.Anchor = TextAnchor.MiddleCenter;
			rect.y++;
			Widgets.Label(rect, ForcedLabelText);
			Text.Anchor = TextAnchor.UpperLeft;

			var selected = Widgets.ButtonInvisible(rect);
			if (selected)
			{
				var success = ForceAction();
				if (success)
					return true;
			}
			return false;
		}

		public virtual bool ForceAction()
		{
			var forcedWork = Find.World.GetComponent<ForcedWork>();
			forcedWork.Prepare(forcePawn);

			var workgiverDefs = forcedWork.GetCombinedDefs(forceWorkgiver);
			foreach (var workgiverDef in workgiverDefs)
			{
				var workgiver = workgiverDef.Worker as WorkGiver_Scanner;
				if (workgiver == null) continue;

				var item = forcedWork.HasJobItem(forcePawn, workgiver, forceCell);
				if (item == null) continue;

				if (forcedWork.AddForcedJob(forcePawn, workgiverDefs, item))
				{
					var job = forcedWork.GetJobItem(forcePawn, workgiver, item);
					var success = job == null ? false : forcePawn.jobs.TryTakeOrderedJobPrioritizedWork(job, workgiver, forceCell);
					if (success == false)
					{
						Log.Error("Cannot execute job with " + forcePawn.NameStringShort + " at " + forceCell + " with " + workgiverDef + " even if HasJobItem returned " + item);
						forcedWork.Prepare(forcePawn);
						continue;
					}
				}
				return true;
			}

			forcedWork.Unprepare(forcePawn);
			Log.Error("Cannot find job for " + forcePawn.NameStringShort + " at " + forceCell + " with " + string.Join(",", workgiverDefs.Select(def => def.ToString()).ToArray()));
			return false;
		}
	}
}