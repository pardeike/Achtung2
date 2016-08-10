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
		public static List<Pawn> selectedAndReadyPawns()
		{
			List<Pawn> allPawns = Find.Selector.SelectedObjects.OfType<Pawn>().ToList();
			return allPawns.FindAll(pawn =>
				 pawn.drafter != null
				 && pawn.IsColonistPlayerControlled
				 && pawn.drafter.CanTakeOrderedJob()
				 && pawn.Downed == false
			);
		}

		// sets the draft status of a colonists and returns the old status
		//
		public static bool SetDraftStatus(Pawn pawn, bool drafted)
		{
			if (pawn.drafter == null)
			{
				pawn.drafter = new Pawn_DraftController(pawn);
			}
			bool previousStatus = pawn.drafter.Drafted;
			if (pawn.drafter.Drafted != drafted)
			{
				pawn.drafter.Drafted = drafted;
			}
			return previousStatus;
		}

		// auto draft unless shift is pressed
		//
		public static void AutoDraft(Pawn pawn)
		{
			bool shiftPressed = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
			if (!shiftPressed)
			{
				SetDraftStatus(pawn, true);
			}
		}

		// OrderToCell drafts pawns if they are not and orders them to a specific location
		// if you don't want drafting you can disable it temporarily by holding shift in
		// which case the colonists run to the clicked location just to give it up as soom
		// as they reach it
		//
		public static void OrderToCell(Pawn pawn, IntVec3 cell)
		{
			AutoDraft(pawn);
			Job job = new Job(JobDefOf.Goto, cell);
			job.playerForced = true;
			pawn.drafter.TakeOrderedJob(job);
		}

		// makes a colonist forget anything ongoing and planned
		//
		public static void clearJobs(Pawn pawn)
		{
			if (pawn.jobQueue == null)
			{
				pawn.jobQueue = new Queue<Job>();
			}
			pawn.jobQueue.Clear();

			if (pawn.jobs != null)
			{
				pawn.jobs.StopAll();
			}
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

		// get the options for a colonist that would be displayed in original code
		//
		private static List<FloatMenuOption> GetMenuOptions(Pawn colonist, bool forceDrafting)
		{
			bool oldDraftStatus = false;
			if (forceDrafting)
			{
				oldDraftStatus = SetDraftStatus(colonist, true);
			}
			List<FloatMenuOption> options = FloatMenuMakerMap.ChoicesAtFor(Gen.MouseMapPosVector3(), colonist);
			if (forceDrafting)
			{
				SetDraftStatus(colonist, oldDraftStatus);
			}
			return options;
		}

		// build a single menu that contains the sum of all colonist choices
		// returns if menu is not empty
		//
		public static bool BuildCommonCommandMenu(List<Pawn> colonistsSelected, Pawn clickedPawn)
		{
			OrderedDictionary<string, OrderedDictionary<Pawn, Action>> allActions = new OrderedDictionary<string, OrderedDictionary<Pawn, Action>>();
			foreach (Pawn colonist in colonistsSelected)
			{
				// get possible commandos by temporarily draft colonist
				List<FloatMenuOption> options = GetMenuOptions(colonist, clickedPawn != null);
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

			// build common menu
			List<FloatMenuOption> list = new List<FloatMenuOption>();
			foreach (string commonLabel in allActions.Keys)
			{
				string suffix = (colonistsSelected.Count > 1) ? " (" + allActions[commonLabel].Keys.Count + ")" : "";
				string title = commonLabel + suffix;
				FloatMenuOption option = new FloatMenuOption(title, delegate
				{
					foreach (Pawn actor in allActions[commonLabel].Keys)
					{
						AutoDraft(actor);
						Action colonistAction = allActions[commonLabel][actor];
						colonistAction();
					}
				}, MenuOptionPriority.High, null, null);
				list.Add(option);
			}
			if (list.Count == 0)
			{
				return false;
			}
			FloatMenu menu = clickedPawn != null ? new FloatMenu(list, clickedPawn.NameStringShort, false) : new FloatMenu(list);
			Find.WindowStack.Add(menu);
			return true;
		}

		// add filth cleaning command if necessary
		//
		public static void AddCleanFilthCommand(Pawn colonist, IntVec3 loc, List<FloatMenuOption> commands)
		{
			if (CheckRoomJob.hasFilthToClean(colonist, loc))
			{
				commands.Add(new FloatMenuOption("CleanThisRoom".Translate(), delegate
				{
					clearJobs(colonist);
					CheckRoomJob.checkRoomAndCleanIfNecessary(loc, colonist);
				}, MenuOptionPriority.High, null, null));
			}
		}

		/*
		public static void ForceGenerateRoof(Room room)
		{
			 FieldInfo queuedGenerateRoomsInfo = typeof(AutoBuildRoofZoneSetter).GetField("queuedGenerateRooms", BindingFlags.Static | BindingFlags.NonPublic);
			 List<Room> rooms = queuedGenerateRoomsInfo.GetValue(null) as List<Room>;
			 if (rooms.Contains(room))
			 {
				  rooms.Remove(room);
			 }
			 rooms.Insert(0, room);
			 queuedGenerateRoomsInfo.SetValue(null, rooms);
			 AutoBuildRoofZoneSetter.ResolveQueuedGenerateRoofs();
		}*/

		// add build roff command if necessary
		//
		public static void AddHandleRoofCommand(Pawn colonist, Room room, bool isBuildingRoof, List<FloatMenuOption> commands)
		{
			// we duplicate AutoBuildRoofZoneSetter.cs:65 precheck here
			if (room.Dereferenced == false && room.TouchesMapEdge == false && room.RegionCount <= 26 && room.CellCount <= 320)
			{
				RoofGrid roofGrid = Find.RoofGrid;
				IEnumerable<IntVec3> cellsToRoof = room.Cells.Where(cell => roofGrid.Roofed(cell) == false);
				IEnumerable<IntVec3> cellsWithRoof = room.Cells.Where(cell => roofGrid.Roofed(cell) == true);
				bool workToDo = (isBuildingRoof == true && cellsToRoof.Count() > 0) || (isBuildingRoof == false && cellsWithRoof.Count() > 0);
				if (workToDo)
				{
					string label = isBuildingRoof ? "RoofThisRoom" : "UnroofThisRoom";
					commands.Add(new FloatMenuOption(label.Translate(), delegate
					{
						if (isBuildingRoof)
						{
							clearJobs(colonist);
							cellsWithRoof.ToList().ForEach(cell =>
								  {
									  Job job = new Job(JobDefOf.BuildRoof, new TargetInfo(cell));
									  colonist.QueueJob(job);
								  });
						}
						else
						{
							clearJobs(colonist);
							cellsWithRoof.ToList().ForEach(cell =>
								  {
									  Job job = new Job(JobDefOf.RemoveRoof, new TargetInfo(cell));
									  colonist.QueueJob(job);
								  });
						}
					}, MenuOptionPriority.High, null, null));
				}
			}
		}

		// main routine: handle mouse events and command your colonists!
		// returns true if event was completely handled and thus prevents
		// original code from being called
		//
		public static bool RightClickHandler(EventType type, Vector3 where)
		{
			if (type == EventType.MouseDown)
			{
				List<Pawn> colonistsSelected = selectedAndReadyPawns();
				if (colonistsSelected.Count == 0)
				{
					return false;
				}

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
					bool success = BuildCommonCommandMenu(colonistsSelected, clickedPawn);
					if (success)
					{
						Event.current.Use();
					}
					return success;
				}

				// with only one colonist selected we choose to stay out of the way
				// however, pressing the alt-key activates this mod anyway
				//
				if (colonistsSelected.Count == 1)
				{
					bool altPressed = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
					if (altPressed)
					{
						lastCells = colonistsSelected.Select(p => IntVec3.Invalid).ToArray<IntVec3>();
						dragStart = where;
						isDragging = true;
					}
					else
					{
						Pawn colonist = colonistsSelected.ElementAt(0);
						if (colonist.Drafted == false)
						{
							Room room = RoomQuery.RoomAt(where.ToIntVec3());
							if (room != null && room.IsHuge == false)
							{
								// build room menu
								List<FloatMenuOption> commands = new List<FloatMenuOption>();

								// add our own commands first
								AddCleanFilthCommand(colonist, where.ToIntVec3(), commands);
								// AddHandleRoofCommand(colonist, room, true, commands);   -- does not work yet
								// AddHandleRoofCommand(colonist, room, false, commands);  -- does not work yet

								// add original commands back
								commands.AddRange(GetMenuOptions(colonist, false));
								if (commands.Count > 0)
								{
									Find.WindowStack.Add(new FloatMenu(commands));
									room.DrawFieldEdges();
									return true;
								}
							}
						}
					}
				}
				if (colonistsSelected.Count > 1)
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
				List<Pawn> colonistsSelected = selectedAndReadyPawns();
				int colonistsCount = colonistsSelected.Count;
				if (colonistsCount > 0 && colonistsCount == lastCells.Count())
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
						if (cell != null)
						{
							// TODO: use a permanent cell maker instead of these
							MoteThrower.ThrowStatic(cell, ThingDefOf.Mote_FeedbackGoto);

							if (lastCell.IsValid == false || cell != lastCell)
							{
								lastCells[i] = cell;
								OrderToCell(pawn, cell);
							}
						}

						linePosition += delta;
						i++;
					}
					return true;
				}
			}

			return false;
		}
	}
}