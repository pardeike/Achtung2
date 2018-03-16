using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace AchtungMod
{
	public class Controller
	{
		public List<Colonist> colonists;
		public Vector3 lineStart;
		public Vector3 lineEnd;
		public bool isDragging;
		public bool relativeMovement;
		public bool drawColonistPreviews;

		public static Controller controller;
		public static Controller getInstance()
		{
			if (controller == null) controller = new Controller();
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

		public void InstallJobDefs()
		{
			new List<JobDef> {
					 new JobDriver_CleanRoom().MakeJobDef(),
					 new JobDriver_FightFire().MakeJobDef(),
					new JobDriver_SowAll().MakeJobDef()
				}
			.DoIf(def => DefDatabase<JobDef>.GetNamedSilentFail(def.defName) == null, def => DefDatabase<JobDef>.Add(def));
		}

		public void MouseDown(Vector3 pos)
		{
			if (Event.current.button != 1) // right button
				return;

			colonists = Tools.GetSelectedColonists();
			if (colonists.Count() == 0)
				return;

			relativeMovement = Tools.IsModKeyPressed(Achtung.Settings.relativeMovementKey);
			var forceMenu = Tools.IsModKeyPressed(Achtung.Settings.forceCommandMenuKey);

			var clickLoc = UI.MouseMapPosition();

			if (colonists.Count() == 1)
			{
				if (relativeMovement)
				{
					StartDragging(pos);
					return;
				}

				var selectedPawn = colonists[0].pawn;
				if (selectedPawn.Drafted == false && Achtung.Settings.forceDraft)
				{
					if (Tools.HasNonEmptyFloatMenu(clickLoc, colonists[0].pawn) == false)
					{
						Tools.SetDraftStatus(colonists[0].pawn, true);
						if (Tools.HasNonEmptyFloatMenu(clickLoc, colonists[0].pawn) == false)
							Tools.SetDraftStatus(colonists[0].pawn, false);
					}
				}

				return;
			}

			if (Achtung.Settings.forceDraft)
				colonists.DoIf(colonist => colonist.pawn.Drafted == false, colonist => Tools.SetDraftStatus(colonist.pawn, true));

			var actions = new MultiActions(colonists, clickLoc);
			if (actions.Count() > 0 && (forceMenu || Tools.PawnsUnderMouse()))
			{
				// present combined menu to the user
				Find.WindowStack.Add(actions.GetWindow());
				Event.current.Use();
				return;
			}

			StartDragging(pos);
		}

		private void StartDragging(Vector3 pos)
		{
			lineStart = pos;
			lineStart.y = Altitudes.AltitudeFor(AltitudeLayer.MetaOverlays);

			isDragging = true;
			Event.current.Use();
		}

		public void MouseDrag(Vector3 pos)
		{
			if (Event.current.button != 1) // right button
				return;

			if (isDragging)
			{
				lineEnd = pos;
				lineEnd.y = Altitudes.AltitudeFor(AltitudeLayer.MetaOverlays);
				var count = colonists.Count();
				var dragVector = lineEnd - lineStart;

				if (relativeMovement)
				{
					colonists.Do(colonist =>
					{
						var delta = lineEnd - lineStart;
						colonist.OrderTo(colonist.startPosition + delta);
					});
				}
				else
				{
					var delta = count > 1 ? dragVector / (count - 1) : Vector3.zero;
					var linePosition = count == 1 ? lineEnd : lineStart;
					colonists.Do(colonist =>
					{
						colonist.OrderTo(linePosition);
						linePosition += delta;
					});
				}

				Event.current.Use();
			}
		}

		public void AddDoThoroughly(List<FloatMenuOption> options, Vector3 clickPos, Pawn pawn, Type driverType)
		{
			var driver = (JobDriver_Thoroughly)Activator.CreateInstance(driverType);
			var targets = driver.CanStart(pawn, clickPos);
			if (targets != null)
			{
				var existingJobs = driver.SameJobTypesOngoing();
				targets.Do(target =>
				{
					var suffix = existingJobs.Count() > 0 ? " " + ("AlreadyDoing".Translate(existingJobs.Count() + 1)) : "";
					options.Add(new FloatMenuOption(driver.GetLabel() + suffix, () => driver.StartJob(pawn, target), MenuOptionPriority.Low));
				});
			}
		}

		public IEnumerable<FloatMenuOption> AchtungChoicesAtFor(Vector3 clickPos, Pawn pawn)
		{
			var options = new List<FloatMenuOption>();
			AddDoThoroughly(options, clickPos, pawn, typeof(JobDriver_CleanRoom));
			AddDoThoroughly(options, clickPos, pawn, typeof(JobDriver_FightFire));
			AddDoThoroughly(options, clickPos, pawn, typeof(JobDriver_SowAll));
			return options;
		}

		public void MouseUp(Vector3 pos)
		{
			if (Event.current.button != 1) // right button
				return;

			if (isDragging)
			{
				colonists.Clear();
				Event.current.Use();
			}
			isDragging = false;
		}

		public void KeyDown(KeyCode key)
		{
			if (isDragging)
			{
				if (key == KeyCode.Escape)
				{
					isDragging = false;

					colonists.Do(colonist =>
					{
						Tools.SetDraftStatus(colonist.pawn, colonist.originalDraftStatus);
						colonist.pawn.mindState.priorityWork.Clear();
						if (colonist.pawn.jobs.curJob != null && colonist.pawn.jobs.IsCurrentJobPlayerInterruptible())
						{
							colonist.pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
						}
					});

					colonists.Clear();
					Event.current.Use();
				}
			}
		}

		public void HandleDrawing()
		{
			if (isDragging)
			{
				if (colonists.Count() > 1) Tools.DrawLineBetween(lineStart, lineEnd, 1.0f);

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

					if (Tools.IsModKeyPressed(Achtung.Settings.showWeaponRangesKey))
					{
						var verb = c.pawn.equipment?.PrimaryEq?.PrimaryVerb;
						if (verb != null && verb.verbProps.MeleeRange == false)
						{
							var range = verb.verbProps.range;
							if (range < 90f)
								GenDraw.DrawRadiusRing(pos.ToIntVec3(), range);
						}
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

		public void HandleEvents()
		{
			var pos = UI.MouseMapPosition();
			switch (Event.current.type)
			{
				case EventType.mouseDown:
					MouseDown(pos);
					MouseDrag(pos);
					break;
				case EventType.MouseDrag:
					MouseDrag(pos);
					break;
				case EventType.mouseUp:
					MouseUp(pos);
					break;
				case EventType.KeyDown:
					KeyDown(Event.current.keyCode);
					break;
				default:
					break;
			}
		}
	}
}