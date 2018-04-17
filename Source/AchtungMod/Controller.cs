using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
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
		public bool achtungPressed;
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

		public void InstallDefs()
		{
			new List<JobDef>
			{
				new JobDriver_CleanRoom().MakeDef(),
				new JobDriver_FightFire().MakeDef(),
				new JobDriver_SowAll().MakeDef()
			}
			.DoIf(def => DefDatabase<JobDef>.GetNamedSilentFail(def.defName) == null, DefDatabase<JobDef>.Add);
		}

		public void MouseDown(Vector3 pos)
		{
			colonists = Tools.GetSelectedColonists();
			if (colonists.Count == 0 || Achtung.Settings.positioningEnabled == false)
				return;

			if (isDragging && Event.current.button == (int)Button.left && groupMovement == false)
			{
				Tools.DraftWithSound(colonists, true);
				EndDragging();
				return;
			}

			if (Event.current.button != (int)Button.right)
				return;

			var actions = new MultiActions(colonists, UI.MouseMapPosition());
			achtungPressed = Tools.IsModKeyPressed(Achtung.Settings.achtungKey);

			var forceMenu = Tools.IsModKeyPressed(Achtung.Settings.forceCommandMenuKey);
			var thingsClicked = Find.VisibleMap.thingGrid.ThingsListAt(IntVec3.FromVector3(pos));
			var pawnClicked = thingsClicked.OfType<Pawn>().Any();
			var weaponClicked = thingsClicked.Any(thing => thing.def.IsWeapon);
			var medicineClicked = thingsClicked.Any(thing => thing.def.IsMedicine);
			var bedClicked = thingsClicked.Any(thing => thing.def.IsBed);
			var fireClicked = thingsClicked.Any(thing => thing.def == ThingDefOf.Fire);

			if (pawnClicked && colonists.Count > 1 && achtungPressed == false && forceMenu == false)
			{
				var allHaveWeapons = colonists.All(colonist =>
				{
					var rangedVerb = colonist.pawn.TryGetAttackVerb(false);
					return rangedVerb != null && rangedVerb.verbProps.range > 0;
				});
				if (allHaveWeapons)
					return;
			}

			if ((weaponClicked || medicineClicked || bedClicked || fireClicked) && achtungPressed == false)
			{
				if (actions.Count(false) > 0)
					Find.WindowStack.Add(actions.GetWindow());
				Event.current.Use();
				return;
			}

			if (forceMenu || (pawnClicked && achtungPressed == false))
			{
				if (actions.Count(false) > 0)
					Find.WindowStack.Add(actions.GetWindow());
				Event.current.Use();
				return;
			}

			if (achtungPressed)
				Tools.DraftWithSound(colonists, true);

			var allDrafted = colonists.All(colonist => colonist.pawn.Drafted);
			if (allDrafted)
			{
				StartDragging(pos, achtungPressed);
				return;
			}

			if (actions.Count(false) > 0)
				Find.WindowStack.Add(actions.GetWindow());
			Event.current.Use();
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

		private void AddDoThoroughly(List<FloatMenuOption> options, Vector3 clickPos, Pawn pawn, Type driverType)
		{
			var driver = (JobDriver_Thoroughly)Activator.CreateInstance(driverType);
			var targets = driver.CanStart(pawn, clickPos);
			if (targets != null)
			{
				var existingJobs = driver.SameJobTypesOngoing();
				targets.Do(target =>
				{
					var suffix = existingJobs.Count > 0 ? " " + ("AlreadyDoing".Translate(existingJobs.Count + 1)) : "";
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
			ForcedJob.AddToJobMenu(options, clickPos, pawn);

			return options;
		}

		public void HandleDrawing()
		{
			ForcedWork.SettingsForMap(Find.VisibleMap)
				.SelectMany(item => item.Value)
				.SelectMany(setting => setting?.Cells ?? new List<IntVec3>())
				.Distinct()
				.Do(cell => Tools.DrawForceIcon(cell.ToVector3()));

#if DEBUG
			Find.VisibleMap?.reservationManager?
				.AllReservedThings()?
				.Where(t => t != null)
				.Do(thing => Tools.DebugPosition(thing.Position.ToVector3(), new Color(1f, 0f, 0f, 0.2f)));
#endif

			if (isDragging)
			{
				if (colonists.Count > 1 && groupMovement == false)
					Tools.DrawLineBetween(lineStart, lineEnd, 1.0f);

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
					MouseUp();
					break;

				case EventType.KeyDown:
					KeyDown(Event.current.keyCode);
					break;
			}
		}
	}
}