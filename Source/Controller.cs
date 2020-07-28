using Multiplayer.API;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace AchtungMod
{
	public class Controller
	{
		private enum Button
		{
			left = 0,
			right = 1
		}

		public List<Colonist> colonists;
		public Vector3 lineStart;
		public Vector3 lineEnd;
		public bool groupMovement;
		public Vector3 groupCenter;
		public int groupRotation;
		public bool groupRotationWas45;
		public bool isDragging;
		public bool drawColonistPreviews;

		public static Controller controller;
		public static Controller GetInstance()
		{
			if (controller == null)
				controller = new Controller();
			return controller;
		}

		public Controller()
		{
			colonists = new List<Colonist>();
			lineStart = Vector3.zero;
			lineEnd = Vector3.zero;
			isDragging = false;
			drawColonistPreviews = true;
		}

		public static void InstallDefs()
		{
			new List<JobDef>
			{
				new JobDriver_CleanRoom().MakeDef(),
				new JobDriver_FightFire().MakeDef(),
				new JobDriver_SowAll().MakeDef()
			}
			.DoIf(def => DefDatabase<JobDef>.GetNamedSilentFail(def.defName) == null, DefDatabase<JobDef>.Add);
		}

		public bool MouseDown(Vector3 pos)
		{
			colonists = Tools.GetSelectedColonists();

			if (colonists.Count == 0 || Achtung.Settings.positioningEnabled == false)
				return true;

			if (isDragging && Event.current.button == (int)Button.left && groupMovement == false)
			{
				Tools.DraftWithSound(colonists, true);
				EndDragging();
				return true;
			}

			if (Event.current.button != (int)Button.right)
				return true;

			var actions = new MultiActions(colonists, UI.MouseMapPosition());
			var achtungPressed = Tools.IsModKeyPressed(Achtung.Settings.achtungKey);
			var allDrafted = colonists.All(colonist => colonist.pawn.Drafted || achtungPressed);
			var mixedDrafted = !allDrafted && colonists.Any(colonist => colonist.pawn.Drafted);

			var forceMenu = false;
			if (Achtung.Settings.forceCommandMenuMode == CommandMenuMode.Auto && !mixedDrafted)
			{
				forceMenu = allDrafted == false;
			}
			else
			{
				forceMenu = Tools.IsModKeyPressed(Achtung.Settings.forceCommandMenuKey);
				if (Achtung.Settings.forceCommandMenuMode == CommandMenuMode.PressForPosition)
					forceMenu = !forceMenu;
			}

			var map = Find.CurrentMap;
			var cell = IntVec3.FromVector3(pos);

			var thingsClicked = map.thingGrid.ThingsListAt(cell);
			var pawnClicked = thingsClicked.OfType<Pawn>().Any();
			var standableClicked = cell.Standable(map);

			if (pawnClicked && colonists.Count > 1 && achtungPressed == false && forceMenu == false)
			{
				var allHaveWeapons = colonists.All(colonist =>
				{
					var rangedVerb = colonist.pawn.TryGetAttackVerb(null, false);
					return rangedVerb != null && rangedVerb.verbProps.range > 0;
				});
				if (allHaveWeapons)
					return true;
			}

			if (forceMenu || (pawnClicked && achtungPressed == false) || standableClicked == false)
			{
				if (actions.Count(false) > 0)
					Find.WindowStack.Add(actions.GetWindow());
				Event.current.Use();
				return false;
			}

			if (achtungPressed)
				Tools.DraftWithSound(colonists, true);

			// in multiplayer, drafting will update pawn.Drafted in the same tick, so we fake it
			if (allDrafted)
			{
				StartDragging(pos, achtungPressed);
				return true;
			}

			if (actions.Count(false) > 0)
				Find.WindowStack.Add(actions.GetWindow());
			Event.current.Use();
			return false;
		}

		private void StartDragging(Vector3 pos, bool asGroup)
		{
			groupMovement = asGroup;

			if (groupMovement)
			{
				groupCenter.x = colonists.Sum(colonist => colonist.startPosition.x) / colonists.Count;
				groupCenter.z = colonists.Sum(colonist => colonist.startPosition.z) / colonists.Count;
				groupRotation = 0;
				groupRotationWas45 = Tools.Has45DegreeOffset(colonists);
			}
			else
			{
				lineStart = pos;
				lineStart.y = Altitudes.AltitudeFor(AltitudeLayer.MetaOverlays);
			}

			colonists.Do(colonist => colonist.offsetFromCenter = colonist.startPosition - groupCenter);

			isDragging = true;
			Event.current.Use();
		}

		private void EndDragging()
		{
			groupMovement = false;
			if (isDragging)
			{
				colonists.Clear();
				Event.current.Use();
			}
			isDragging = false;
		}

		public void MouseDrag(Vector3 pos)
		{
			if (Event.current.button != (int)Button.right)
				return;

			if (isDragging == false)
				return;

			if (groupMovement)
			{
				colonists.Do(colonist => colonist.OrderTo(pos + Tools.RotateBy(colonist.offsetFromCenter, groupRotation, groupRotationWas45)));
				Event.current.Use();
				return;
			}

			lineEnd = pos;
			lineEnd.y = Altitudes.AltitudeFor(AltitudeLayer.MetaOverlays);
			var count = colonists.Count;
			var dragVector = lineEnd - lineStart;

			var delta = count > 1 ? dragVector / (count - 1) : Vector3.zero;
			var linePosition = count == 1 ? lineEnd : lineStart;
			Tools.OrderColonistsAlongLine(colonists, lineStart, lineEnd).Do(colonist =>
			{
				colonist.OrderTo(linePosition);
				linePosition += delta;
			});

			Event.current.Use();
		}

		public void MouseUp()
		{
			if (Event.current.button != (int)Button.right)
				return;

			EndDragging();
		}

		public void KeyDown(KeyCode key)
		{
			if (isDragging)
			{
				if (groupMovement == false && Tools.IsModKey(key, Achtung.Settings.achtungKey))
				{
					var undraftedColonists = colonists.Where(colonist => colonist.originalDraftStatus == false).ToList();
					if (undraftedColonists.Count > 0)
					{
						Tools.DraftWithSound(undraftedColonists, true);
						EndDragging();
						return;
					}
				}

				switch (key)
				{
					case KeyCode.Q:
						if (groupMovement)
						{
							groupRotation -= 45;

							var pos = UI.MouseMapPosition();
							colonists.Do(colonist => colonist.OrderTo(pos + Tools.RotateBy(colonist.offsetFromCenter, groupRotation, groupRotationWas45)));

							Event.current.Use();
						}
						break;

					case KeyCode.E:
						if (groupMovement)
						{
							groupRotation += 45;

							var pos = UI.MouseMapPosition();
							colonists.Do(colonist => colonist.OrderTo(pos + Tools.RotateBy(colonist.offsetFromCenter, groupRotation, groupRotationWas45)));

							Event.current.Use();
						}
						break;

					case KeyCode.Escape:
						isDragging = false;
						Tools.CancelDrafting(colonists);
						colonists.Clear();
						Event.current.Use();
						break;
				}
			}
		}

		[SyncMethod] // multiplayer
		static void StartWorkSynced(Type driverType, Pawn pawn, LocalTargetInfo target, LocalTargetInfo clickCell)
		{
			var driver = (JobDriver_Thoroughly)Activator.CreateInstance(driverType);
			driver.StartJob(pawn, target, clickCell);
		}

		private static void AddDoThoroughly(List<FloatMenuOption> options, Vector3 clickPos, Pawn pawn, Type driverType)
		{
			var driver = (JobDriver_Thoroughly)Activator.CreateInstance(driverType);
			var clickCell = new LocalTargetInfo(IntVec3.FromVector3(clickPos));
			var targets = driver.CanStart(pawn, clickCell);
			if (targets != null)
			{
				var existingJobs = driver.SameJobTypesOngoing();
				foreach (var target in targets)
				{
					var suffix = existingJobs.Count > 0 ? " " + "AlreadyDoing".Translate("" + (existingJobs.Count + 1)) : new TaggedString("");
					options.Add(new FloatMenuOption(driver.GetLabel() + suffix, () => StartWorkSynced(driverType, pawn, target, clickCell), MenuOptionPriority.Low));
				}
			}
		}

		public static IEnumerable<FloatMenuOption> AchtungChoicesAtFor(Vector3 clickPos, Pawn pawn)
		{
			var options = new List<FloatMenuOption>();

			AddDoThoroughly(options, clickPos, pawn, typeof(JobDriver_CleanRoom));
			AddDoThoroughly(options, clickPos, pawn, typeof(JobDriver_FightFire));
			AddDoThoroughly(options, clickPos, pawn, typeof(JobDriver_SowAll));

			return options;
		}

		private static void DrawForcedJobs()
		{
			var forcedWork = Find.World.GetComponent<ForcedWork>();
			var map = Find.CurrentMap;
			if (map == null || forcedWork == null)
				return;

			forcedWork.ForcedJobsForMap(map)
				.DoIf(forcedJob => forcedJob.pawn.Spawned && forcedJob.pawn.Map == map && Find.Selector.IsSelected(forcedJob.pawn), forcedJob =>
				{
					forcedJob.AllCells(true).Distinct()
						.Do(cell => Tools.DrawForceIcon(cell.ToVector3()));
				});
		}

		/*private void DrawReservations()
		{
			var reservationManager = Find.CurrentMap?.reservationManager;
			if (reservationManager == null)
				return;

			var selector = Find.Selector;
			var allReservations = Tools.Reservations(reservationManager);
			allReservations
				.DoIf(res => selector.IsSelected(res.Claimant), res => Tools.DebugPosition(res.Target.Cell.ToVector3(), res.Target.HasThing ? new Color(1f, 0f, 0f, 0.2f) : new Color(0f, 1f, 0f, 0.2f)));

			//Find.CurrentMap?.reservationManager?
			//	.AllReservedThings()?
			//	.Where(t => t != null)
			//	.Do(thing => Tools.DebugPosition(thing.Position.ToVector3(), new Color(1f, 0f, 0f, 0.2f)));

			//var forcedWork = Find.World.GetComponent<ForcedWork>();
			//forcedWork.GetForbiddenLocations()
			//	.Do(cell => Tools.DebugPosition(cell.ToVector3(), new Color(0f, 0f, 1f, 0.2f)));
		}*/

		public void HandleDrawing()
		{
			DrawForcedJobs();

			// for debugging reservations
			// DrawReservations();

			if (isDragging)
			{
				if (colonists.Count > 1 && groupMovement == false)
					Tools.DrawLineBetween(lineStart, lineEnd);

				colonists.Do(c =>
				{
					var pos = c.designation;
					if (pos == Vector3.zero)
						return;

					Tools.DrawMarker(pos);
					if (drawColonistPreviews)
					{
						c.pawn.Drawer.renderer.RenderPawnAt(pos);
						c.pawn.DrawExtraSelectionOverlays();
					}
				});
			}
		}

		public void HandleDrawingOnGUI()
		{
			colonists.DoIf(c => (c.designation != Vector3.zero), c =>
			{
				var labelPos = Tools.LabelDrawPosFor(c.designation, -0.6f);
				GenMapUI.DrawPawnLabel(c.pawn, labelPos, 1f, 9999f, null);
			});
		}

		public bool HandleEvents()
		{
			var pos = UI.MouseMapPosition();
			var runOriginal = true;
			switch (Event.current.type)
			{
				case EventType.MouseDown:
					runOriginal = MouseDown(pos);
					MouseDrag(pos);
					break;

				case EventType.MouseDrag:
					MouseDrag(pos);
					break;

				case EventType.MouseUp:
					MouseUp();
					break;

				case EventType.KeyDown:
					KeyDown(Event.current.keyCode);
					break;
			}
			return runOriginal;
		}
	}
}