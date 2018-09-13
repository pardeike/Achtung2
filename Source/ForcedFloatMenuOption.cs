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

		public ForcedMultiFloatMenuOption(string label) : base(label, null, MenuOptionPriority.Default, null, null, 0f, null, null) { }

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
		public static readonly Texture2D L_Normal = ContentFinder<Texture2D>.Get("ForceButton0L", true);
		public static readonly Texture2D M_Normal = ContentFinder<Texture2D>.Get("ForceButton0M", true);
		public static readonly Texture2D R_Normal = ContentFinder<Texture2D>.Get("ForceButton0R", true);
		public static readonly Texture2D L_Selected = ContentFinder<Texture2D>.Get("ForceButton1L", true);
		public static readonly Texture2D M_Selected = ContentFinder<Texture2D>.Get("ForceButton1M", true);
		public static readonly Texture2D R_Selected = ContentFinder<Texture2D>.Get("ForceButton1R", true);

		public Pawn forcePawn;
		public IntVec3 forceCell;
		public WorkGiver_Scanner forceWorkgiver;

		public static string ForcedLabelText = Translator.CanTranslate("ForceMenuButtonText") ? "ForceMenuButtonText".Translate() : " ! ";
		public static float buttonSpace = 10f;
		public static float _buttonWidth = 0f;
		public static float ButtonWidth
		{
			get
			{
				if (_buttonWidth == 0f)
				{
					Text.Font = GameFont.Tiny;
					var size = Text.CalcSize(ForcedLabelText);
					const float padding = 11f;
					_buttonWidth = size.x + 2f * padding;
				}
				return _buttonWidth;
			}
		}

		public static FloatMenuOption CreateForcedMenuItem(string label, Action action, MenuOptionPriority priority, Action mouseoverGuiAction, Thing revalidateClickTarget, float extraPartWidth, Func<Rect, bool> extraPartOnGUI, WorldObject revalidateWorldClickTarget, Pawn pawn, IntVec3 clickCell, WorkGiver_Scanner workgiver)
		{
			if (action == null)
				return new FloatMenuOption(label, action, priority, mouseoverGuiAction, revalidateClickTarget, extraPartWidth, extraPartOnGUI, revalidateWorldClickTarget);

			var option = new ForcedFloatMenuOption(label, action, priority, mouseoverGuiAction, revalidateClickTarget, extraPartWidth, extraPartOnGUI, revalidateWorldClickTarget) { };
			option.forcePawn = pawn;
			option.forceCell = clickCell;
			option.forceWorkgiver = workgiver;
			option.extraPartOnGUI = extraPartRect => option.RenderExtraPartOnGui(extraPartRect);

			return option;
		}

		public ForcedFloatMenuOption(string label, Action action, MenuOptionPriority priority, Action mouseoverGuiAction, Thing revalidateClickTarget, float extraPartWidth, Func<Rect, bool> extraPartOnGUI, WorldObject revalidateWorldClickTarget)
			: base(label, action, priority, mouseoverGuiAction, revalidateClickTarget, extraPartWidth, extraPartOnGUI, revalidateWorldClickTarget)
		{
			// somehow necessary or else 'extraPartWidth' will be 0
			base.extraPartWidth = buttonSpace + ButtonWidth;
		}

		public bool RenderExtraPartOnGui(Rect drawRect)
		{
			var rect = drawRect;
			rect.xMin += buttonSpace;
			rect = new Rect(Mathf.Round(rect.x), Mathf.Round(rect.y), Mathf.Round(rect.width), Mathf.Round(rect.height));

			var highlight = Mouse.IsOver(rect);
			var b_left = highlight ? L_Selected : L_Normal;
			var b_middle = highlight ? M_Selected : M_Normal;
			var b_right = highlight ? R_Selected : R_Normal;

			var yPlus = Mathf.Round((rect.height - b_left.height / 2f) / 2f);
			var buttonRect = rect;
			buttonRect.y += yPlus;
			buttonRect.height -= 2f * yPlus;

			buttonRect.width = Mathf.Round(b_left.width / 2f);
			GUI.DrawTexture(buttonRect, b_left);
			buttonRect.x += buttonRect.width;

			buttonRect.width = rect.width - Mathf.Round(b_left.width / 2f) - Mathf.Round(b_right.width / 2f);
			GUI.DrawTexture(buttonRect, b_middle, ScaleMode.StretchToFill);
			buttonRect.x += buttonRect.width;

			buttonRect.width = Mathf.Round(b_right.width / 2f);
			GUI.DrawTexture(buttonRect, b_right);

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

				Tools.CancelWorkOn(forcePawn, item);

				if (forcedWork.AddForcedJob(forcePawn, workgiverDefs, item))
				{
					var job = forcedWork.GetJobItem(forcePawn, workgiver, item);
					var success = job == null ? false : forcePawn.jobs.TryTakeOrderedJobPrioritizedWork(job, workgiver, forceCell);
					if (success == false)
					{
						forcedWork.Prepare(forcePawn);
						continue;
					}
				}
				return true;
			}

			forcedWork.RemoveForcedJob(forcePawn);
			Messages.Message(forcePawn.Name.ToStringShort + " could not find more forced work. The remaining work is most likely reserved or not accessible.", MessageTypeDefOf.RejectInput);
			return false;
		}
	}
}