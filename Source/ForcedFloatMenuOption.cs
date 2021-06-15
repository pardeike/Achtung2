using Multiplayer.API;
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

		public ForcedMultiFloatMenuOption(string label) : base(label, null, MenuOptionPriority.Default, null, null, 0f, null, null, false, 0) { }

		public override bool ForceAction()
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

		public static TaggedString ForcedLabelText = Translator.CanTranslate("ForceMenuButtonText") ? "ForceMenuButtonText".Translate() : new TaggedString(" ! ");
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

		public static FloatMenuOption CreateForcedMenuItem(string label, Action action, MenuOptionPriority priority, Action<Rect> mouseoverGuiAction, Thing revalidateClickTarget, float extraPartWidth, Func<Rect, bool> extraPartOnGUI, WorldObject revalidateWorldClickTarget, bool playSelectionSound, int orderInPriority, Pawn pawn, Vector3 clickPos, WorkGiver_Scanner workgiver)
		{
			if (action == null)
				return new FloatMenuOption(label, action, priority, mouseoverGuiAction, revalidateClickTarget, extraPartWidth, extraPartOnGUI, revalidateWorldClickTarget, playSelectionSound, orderInPriority);

			var option = new ForcedFloatMenuOption(label, action, priority, mouseoverGuiAction, revalidateClickTarget, extraPartWidth, extraPartOnGUI, revalidateWorldClickTarget, playSelectionSound, orderInPriority) { };
			option.forcePawn = pawn;
			option.forceCell = IntVec3.FromVector3(clickPos);
			option.forceWorkgiver = workgiver;
			option.extraPartOnGUI = extraPartRect => option.RenderExtraPartOnGui(extraPartRect);

			return option;
		}

		public ForcedFloatMenuOption(string label, Action action, MenuOptionPriority priority, Action<Rect> mouseoverGuiAction, Thing revalidateClickTarget, float extraPartWidth, Func<Rect, bool> extraPartOnGUI, WorldObject revalidateWorldClickTarget, bool playSelectionSound, int orderInPriority)
			: base(label, action, priority, mouseoverGuiAction, revalidateClickTarget, extraPartWidth, extraPartOnGUI, revalidateWorldClickTarget, playSelectionSound, orderInPriority)
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

		[SyncMethod] // multiplayer
		public static bool ForceActionSynced(Pawn forcePawn, WorkGiver_Scanner forceWorkgiver, int x, int z)
		{
			var cell = new IntVec3(x, 0, z);

			var forcedWork = ForcedWork.Instance;
			forcedWork.Prepare(forcePawn);

			var workgiverDefs = ForcedWork.GetCombinedDefs(forceWorkgiver);
			foreach (var workgiverDef in workgiverDefs)
			{
				var workgiver = workgiverDef.giverClass == null ? null : workgiverDef.Worker as WorkGiver_Scanner;
				if (workgiver == null) continue;

				var item = ForcedWork.HasJobItem(forcePawn, workgiver, cell);
				if (item == null) continue;

				Tools.CancelWorkOn(forcePawn, item);

				if (forcedWork.AddForcedJob(forcePawn, workgiverDefs, item))
				{
					var job = ForcedWork.GetJobItem(forcePawn, workgiver, item);
					var success = job != null && forcePawn.jobs.TryTakeOrderedJobPrioritizedWork(job, workgiver, cell);
					if (success == false)
					{
						forcedWork.Prepare(forcePawn); // TODO: should this be Unprepare() ?
						continue;
					}
				}
				return true;
			}

			forcedWork.RemoveForcedJob(forcePawn);
			Messages.Message(forcePawn.Name.ToStringShort + " could not find more forced work. The remaining work is most likely reserved or not accessible.", MessageTypeDefOf.RejectInput);
			return false;
		}

		public virtual bool ForceAction()
		{
			return ForceActionSynced(forcePawn, forceWorkgiver, forceCell.x, forceCell.z);
		}
	}
}
