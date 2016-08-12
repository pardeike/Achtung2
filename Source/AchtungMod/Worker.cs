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
		public static Thing[] markers;

		// make sure we deal with simple colonists that can take orders and can be drafted
		//
		public static List<Pawn> UserSelectedAndReadyPawns()
		{
			List<Pawn> allPawns = Find.Selector.SelectedObjects.OfType<Pawn>().ToList();
			return allPawns.FindAll(pawn =>
				  pawn.drafter != null
				  && pawn.IsColonistPlayerControlled
				  && pawn.drafter.CanTakeOrderedJob()
				  && pawn.Downed == false
			);
		}

		// get draft status of a colonist
		//
		public static bool GetDraftingStatus(Pawn pawn)
		{
			if (pawn.drafter == null)
			{
				pawn.drafter = new Pawn_DraftController(pawn);
			}
			return pawn.drafter.Drafted;
		}

		// sets the draft status of a colonists and returns the old status
		//
		public static bool SetDraftStatus(Pawn pawn, bool drafted, bool fake = false)
		{
			bool previousStatus = GetDraftingStatus(pawn);
			if (pawn.drafter.Drafted != drafted)
			{
				if (fake) // we don't use the indirect method because it has lots of side effects
				{
					DraftStateHandler draftHandler = pawn.drafter.draftStateHandler;
					FieldInfo draftHandlerField = typeof(DraftStateHandler).GetField("draftedInt", BindingFlags.NonPublic | BindingFlags.Instance);
					if (draftHandlerField == null)
					{
						Log.Error("No field 'draftedInt' in DraftStateHandler");
					}
					else
					{
						draftHandlerField.SetValue(draftHandler, drafted);
					}
				}
				else
				{
					pawn.drafter.Drafted = drafted;
				}
			}
			return previousStatus;
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
				SetDraftStatus(pawn, true);
			}

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
		public static List<Pawn> PawnsUnderMouse()
		{
			MethodInfo selectableObjectsUnderMouseMethod = Find.Selector.GetType().GetMethod("SelectableObjectsUnderMouse", BindingFlags.NonPublic | BindingFlags.Instance);
			IEnumerable<object> result = selectableObjectsUnderMouseMethod.Invoke(Find.Selector, null) as IEnumerable<object>;
			return result.ToList().FindAll(o => o is Pawn).Cast<Pawn>().ToList();
		}

		// a simple wrapper for private methods in FloatMenuMakerMap
		//
		public static void FloatMenuMakerMapWrapper(string methodName, Vector3 clickPos, Pawn pawn, List<FloatMenuOption> list)
		{
			MethodInfo info = typeof(FloatMenuMakerMap).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
			if (info == null)
			{
				Log.Error("Method " + methodName + " not found in FloatMenuMakerMap");
				return;
			}
			info.Invoke(null, new object[] { clickPos, pawn, list });
		}

		// this is basically a copy of FloatMenuMakerMap.ChoicesAtFor(Vector3 clickPos, Pawn pawn)
		// but with more suble logic to avoid duplicates and side effects
		//
		public static List<FloatMenuOption> GetMenuOptionsForMode(Pawn pawn, ActionMode mode)
		{
			Vector3 clickPos = Gen.MouseMapPosVector3();
			IntVec3 intVec = IntVec3.FromVector3(clickPos);
			List<FloatMenuOption> list = new List<FloatMenuOption>();
			if (intVec.InBounds() && pawn.IsColonistPlayerControlled && pawn.drafter.CanTakeOrderedJob())
			{
				FloatMenuMakerMap.making = true;
				try
				{
					switch (mode)
					{
						case ActionMode.Drafted:
							bool undoDraft = false;
							if (pawn.drafter.Drafted == false)
							{
								SetDraftStatus(pawn, true, true);
								undoDraft = true;
							}
							FloatMenuMakerMapWrapper("AddDraftedOrders", clickPos, pawn, list);
							if (undoDraft)
							{
								SetDraftStatus(pawn, false, true);
							}
							break;
						case ActionMode.Undrafted:
							bool undoUndraft = false;
							if (pawn.drafter.Drafted == true)
							{
								SetDraftStatus(pawn, false, true);
								undoUndraft = true;
							}
							FloatMenuMakerMapWrapper("AddUndraftedOrders", clickPos, pawn, list);
							if (undoUndraft)
							{
								SetDraftStatus(pawn, true, true);
							}
							break;
						case ActionMode.Other:
							if (pawn.RaceProps.Humanlike)
							{
								FloatMenuMakerMapWrapper("AddHumanlikeOrders", clickPos, pawn, list);
							}
							list.AddRange(pawn.GetExtraFloatMenuOptionsFor(intVec));
							break;
					}
				}
				finally
				{
					FloatMenuMakerMap.making = false;
				}
			}
			return list;
		}

		// build a single menu that contains the sum of all colonist choices
		// returns if menu is not empty
		//
		public static List<FloatMenuOption> BuildCommonCommandMenu(List<Pawn> colonistsSelected, IntVec3 where)
		{
			// existing commands, polled by faking draft/undraft modes
			// plus human and other mods commands
			//
			OrderedDictionary<string, MultiPawnAction> allActions = new OrderedDictionary<string, MultiPawnAction>();
			List<ActionMode> stati = new List<ActionMode>() { ActionMode.Drafted, ActionMode.Undrafted, ActionMode.Other };
			foreach (ActionMode mode in stati)
			{
				foreach (Pawn colonist in colonistsSelected)
				{
					List<FloatMenuOption> optionsByMode = GetMenuOptionsForMode(colonist, mode);
					optionsByMode.ForEach(option => MultiPawnAction.AddToActions(allActions, colonist, mode, option));
				}
			}

			// our own commands
			foreach (Pawn colonist in colonistsSelected)
			{
				FloatMenuOption cleanOption = CleanFilthCommand(colonist, where);
				if (cleanOption != null)
				{
					MultiPawnAction.AddToActions(allActions, colonist, ActionMode.Undrafted, cleanOption);
				}
			}

			// build common menu
			List<FloatMenuOption> list = new List<FloatMenuOption>();
			foreach (string commonLabel in allActions.Keys)
			{
				ICollection<Pawn> colonists = allActions[commonLabel].Colonists();

				int count = colonists.Count;
				string countInfo = count != 1 ? (count + "x") : colonists.First().NameStringShort;
				string suffix = (colonistsSelected.Count > 1) ? " (" + countInfo + ")" : "";

				MenuOptionPriority priority = allActions[commonLabel].GetPriority();
				string title = commonLabel + suffix;
				FloatMenuOption option = new FloatMenuOption(title, delegate
				{
					foreach (Pawn actor in allActions[commonLabel].Colonists())
					{
						Action colonistAction = allActions[commonLabel].GetAction(actor);
						colonistAction();
					}
				}, priority);
				option.Disabled = allActions[commonLabel].IsDisabled();
				list.Add(option);
			}
			return list;
		}

		// add filth cleaning command if necessary
		//
		public static FloatMenuOption CleanFilthCommand(Pawn colonist, IntVec3 loc)
		{
			if (CheckRoomJob.hasFilthToClean(colonist, loc) == false)
			{
				return null;
			}
			return new FloatMenuOption("CleanThisRoom".Translate(), delegate
			{
				clearJobs(colonist);
				CheckRoomJob.checkRoomAndCleanIfNecessary(loc, colonist);
			},
			MenuOptionPriority.Low);
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
		}
		
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
		*/

		// main routine: handle mouse events and command your colonists!
		// returns true if event was completely handled and thus prevents
		// original code from being called
		//
		public static bool RightClickHandler(EventType type, Vector3 where)
		{
			if (type == EventType.MouseDown)
			{
				List<Pawn> colonistsSelected = UserSelectedAndReadyPawns();
				if (colonistsSelected.Count == 0)
				{
					// no colonist selected, exit
					return false;
				}

				// activate drag function only for more than one selected colonist and only if
				// the context menu is empty. Start by generating all possible commands for the
				// current click location and then adding our own to it. To force a menu even
				// when multiple colonists are selected, simply press the alt key
				//
				List<FloatMenuOption> commands = BuildCommonCommandMenu(colonistsSelected, where.ToIntVec3());
				bool altPressed = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
				List<Pawn> pawnsClicked = PawnsUnderMouse();
				if ((colonistsSelected.Count == 1 || (colonistsSelected.Count > 1 && altPressed) || pawnsClicked.Count > 0) && commands.Count > 0)
				{
					// present the new combined menu to the user
					Find.WindowStack.Add(new FloatMenu(commands));
					return true;
				}
				else
				{
					// start dragging positions
					lastCells = colonistsSelected.Select(p => IntVec3.Invalid).ToArray<IntVec3>();
					markers = new Thing[colonistsSelected.Count];
					for (int i = 0; i < colonistsSelected.Count; i++)
					{
						markers[i] = null;
					}
					dragStart = where;
					isDragging = true;
				}
			}

			if (type == EventType.MouseUp && isDragging == true)
			{
				markers.ToList().FindAll(m => m != null && m.Destroyed == false).ForEach(m => m.Destroy(DestroyMode.Vanish));
				markers = new Thing[0]; // save memory

				isDragging = false;
			}

			// while dragging, space out all selected colonists and order them to the positions
			//
			if ((type == EventType.MouseDown || type == EventType.MouseDrag) && isDragging == true)
			{
				List<Pawn> colonistsSelected = UserSelectedAndReadyPawns();
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
							if (lastCell.IsValid == false || cell != lastCell)
							{
								if (markers[i] != null)
								{
									if (markers[i].Destroyed == false)
									{
										markers[i].Destroy(DestroyMode.Cancel);
									}
									markers[i] = null;
								}

								lastCells[i] = cell;

								Thing marker = ThingMaker.MakeThing(ThingDef.Named("AchtungMarker"));
								if (GenPlace.TryPlaceThing(marker, cell, ThingPlaceMode.Direct))
								{
									markers[i] = marker;
								}

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