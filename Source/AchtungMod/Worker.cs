using Verse;
using RimWorld;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Verse.AI;
using System.Reflection;
using System;
using mattmc3.Common.Collections.Generic;

namespace AchtungMod
{
	public static class Worker
	{
		public static bool isDragging = false;
		public static Vector3 dragStart;
		public static IntVec3[] lastCells;

		// make sure we deal with simple colonists that can take orders and can be drafted
		//
		public static List<Pawn> selectedPawns()
		{
			List<Pawn> allPawns = Find.Selector.SelectedObjects.OfType<Pawn>().ToList();
			return allPawns.FindAll(pawn =>
				pawn.drafter != null
				&& pawn.IsColonistPlayerControlled
				&& pawn.drafter.CanTakeOrderedJob()
				&& pawn.Downed == false
			);
		}

		// OrderToCell drafts pawns if they are not and orders them to a specific location
		// if you don't want drafting you can disable it temporarily by holding shift in
		// which case the colonists run to the clicked location just to give it up as soom
		// as they reach it
		//
		public static void OrderToCell(Pawn pawn, IntVec3 cell)
		{
			bool shiftPressed = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
			if (!shiftPressed)
			{
				if (pawn.drafter == null)
				{
					pawn.drafter = new Pawn_DraftController(pawn);
				}
				if (pawn.drafter.Drafted == false)
				{
					pawn.drafter.Drafted = true;
				}
			}

			Job job = new Job(JobDefOf.Goto, cell);
			job.playerForced = true;
			pawn.drafter.TakeOrderedJob(job);
		}

		// find first pawn clicked by using 'SelectableObjectsUnderMouse' from original Selector code
		//
		public static Pawn PawnUnderMouse()
		{
			MethodInfo selectableObjectsUnderMouseMethod = Find.Selector.GetType().GetMethod("SelectableObjectsUnderMouse", BindingFlags.NonPublic | BindingFlags.Instance);
			IEnumerable<object> result = selectableObjectsUnderMouseMethod.Invoke(Find.Selector, null) as IEnumerable<object>;
			List<Pawn> pawns = result.ToList().FindAll(o => o is Pawn).Cast<Pawn>().ToList();
			return pawns.Count == 0 ? null : pawns.First();
		}

		// build a single menu that contains the sum of all colonist choices
		//
		public static void BuildCommonCommandMenu(List<Pawn> colonistsSelected, Pawn clickedPawn)
		{
			OrderedDictionary<string, OrderedDictionary<Pawn, Action>> allActions = new OrderedDictionary<string, OrderedDictionary<Pawn, Action>>();
			foreach (Pawn colonist in colonistsSelected)
			{
				List<FloatMenuOption> options = FloatMenuMakerMap.ChoicesAtFor(Gen.MouseMapPosVector3(), colonist);
				foreach (FloatMenuOption option in options)
				{
					if (option.Disabled == false)
					{
						string name = option.Label;
						if (allActions.ContainsKey(name) == false)
						{
							allActions.Add(name, new OrderedDictionary<Pawn, Action>());
						}
						allActions[name].Add(colonist, option.action);
					}
				}
			}

			List<FloatMenuOption> list = new List<FloatMenuOption>();
			foreach (string commonLabel in allActions.Keys)
			{
				string title = commonLabel + " (" + allActions[commonLabel].Keys.Count + ")";
				FloatMenuOption option = new FloatMenuOption(title, delegate
				{
					foreach (Pawn actor in allActions[commonLabel].Keys)
					{
						Action colonistAction = allActions[commonLabel][actor];
						colonistAction();
					}
				}, MenuOptionPriority.High, null, null);
				list.Add(option);
			}
			Find.WindowStack.Add(new FloatMenu(list, clickedPawn.NameStringShort, false));
		}

		// main routine: handle mouse events and command your colonists!
		//
		public static void RightClickHandler(EventType type, Vector3 where)
		{
			if (type == EventType.MouseDown)
			{
				List<Pawn> colonistsSelected = selectedPawns();

				// if the user clicked on a pawn we don't go there
				// instead we try to build a "common" menu containing the sum of all possible
				// commands for all selected colonists and present it
				//
				// the menu then contains actions that will execute the corresponding action
				// for every colonist that had that menu too (compared by menu label name) 
				//
				Pawn clickedPawn = PawnUnderMouse();
				if (clickedPawn != null)
				{
					BuildCommonCommandMenu(colonistsSelected, clickedPawn);
					Event.current.Use();
					return;
				}

				// with only one colonist selected we choose to stay out of the way
				// however, pressing the alt-key activates this mod anyway
				//
				bool altPressed = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
				if (colonistsSelected.Count > 1 || (colonistsSelected.Count == 1 && altPressed))
				{
					lastCells = colonistsSelected.Select(p => IntVec3.Invalid).ToArray<IntVec3>();
					dragStart = where;

					isDragging = true;
				}
			}

			if (type == EventType.MouseUp && isDragging == true)
			{
				isDragging = false;
			}

			// while dragging, space out all selected colonists and order them to the positions
			//
			if ((type == EventType.MouseDown || type == EventType.MouseDrag) && isDragging == true)
			{
				List<Pawn> colonistsSelected = selectedPawns();
				int colonistsCount = colonistsSelected.Count;
				if (colonistsCount > 0)
				{
					Vector3 dragEnd = where;
					Vector3 line = dragEnd - dragStart;
					Vector3 delta = colonistsCount > 1 ? line / (float)(colonistsCount - 1) : Vector3.zero;
					Vector3 linePosition = colonistsCount == 1 ? dragEnd : dragStart;

					int i = 0;
					foreach (Pawn pawn in colonistsSelected)
					{
						IntVec3 lastCell = lastCells[i];

						IntVec3 optimalCell = linePosition.ToIntVec3();
						IntVec3 cell = Pawn_DraftController.BestGotoDestNear(optimalCell, pawn);
						// TODO: use a permanent cell maker instead of these
						MoteThrower.ThrowStatic(cell, ThingDefOf.Mote_FeedbackGoto);

						if (lastCell.IsValid == false || cell != lastCell)
						{
							lastCells[i] = cell;
							OrderToCell(pawn, cell);
						}

						linePosition += delta;
						i++;
					}
				}
			}
		}
	}
}