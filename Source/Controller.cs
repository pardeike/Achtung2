using RimWorld;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace AchtungMod;

public class Controller
{
	public List<Colonist> colonists;
	public Vector3 lineStart;
	public Vector3 lineEnd;
	public bool groupMovement;
	public bool groupedMoved;
	public Vector3 groupCenter;
	public Colonist centerOnColonist;
	public int groupRotation;
	public bool groupRotationWas45;
	public bool isDragging;
	public bool suppressMenu;
	public bool drawColonistPreviews;

	public static Controller instance;

	public static Controller GetInstance()
	{
		instance ??= new Controller();
		return instance;
	}

	public Controller()
	{
		colonists = [];
		lineStart = Vector3.zero;
		lineEnd = Vector3.zero;
		isDragging = false;
		suppressMenu = false;
		drawColonistPreviews = true;
	}

	public static void InstallDefs()
	{
		new List<JobDef>
			{
				new JobDriver_CleanRoom().MakeDef(),
				new JobDriver_FightFire().MakeDef(),
				JobDriver_TacticalApproach.MakeDef()
			}
		.DoIf(def => DefDatabase<JobDef>.GetNamedSilentFail(def.defName) == null, DefDatabase<JobDef>.Add);
	}

	bool ShowMenu(MultiActions actions, bool forceMenu, Action gotoAction)
	{
		if (actions == null)
			return true;
		var menuAdded = false;
		var optionTaken = false;
		if (actions.Count(false) > 0)
		{
			if (forceMenu == false)
			{
				var autoTakableOptions = actions.GetAutoTakeableActions();
				var first = autoTakableOptions.FirstOrDefault();
				if (first != null)
				{
					optionTaken = true;
					first.Chosen(true, null);
				}
			}
			if (optionTaken == false)
			{
				Find.WindowStack.Add(actions.GetWindow());
				menuAdded = true;
				suppressMenu = true;
			}
		}
		if (menuAdded)
		{
			Tools.SetCursor(AchtungCursor.Default);
			EndDragging();
		}
		if (optionTaken == false && menuAdded == false && actions.EveryoneHasGoto && gotoAction != null)
		{
			actions.allPawns.Do(pawn => Tools.SetDraftStatus(pawn, true));
			gotoAction();
			return true;
		}
		if (menuAdded == false && Achtung.Settings.positioningEnabled == false)
			return true;
		Event.current.Use();
		return false;
	}

	public bool MouseDown(Vector3 pos, int button, bool longPress)
	{
		var doPositioning = Achtung.Settings.positioningEnabled;
		var doForceMenu = Achtung.Settings.maxForcedItems > 0;
		if (doPositioning == false && doForceMenu == false)
			return true;
		var selector = Find.Selector;

		if (longPress == false)
			colonists = Tools.GetSelectedColonists();

		if (colonists.Count == 0)
			return true;

		if (isDragging && button == 0 && groupMovement == false)
		{
			Tools.DraftWithSound(colonists, true);
			EndDragging();
			return true;
		}

		if (button != 1)
			return true;

		var actions = doForceMenu ? new MultiActions(colonists, UI.MouseMapPosition()) : null;
		var achtungPressed = Tools.IsModKeyPressed(Achtung.Settings.achtungKey);
		var allDrafted = colonists.All(colonist => colonist.pawn.Drafted || achtungPressed);
		var mixedDrafted = !allDrafted && colonists.Any(colonist => colonist.pawn.Drafted);

		var forceMenu = Achtung.Settings.forceCommandMenuMode switch
		{
			CommandMenuMode.Auto => mixedDrafted == false && allDrafted == false,
			CommandMenuMode.PressForMenu => Tools.IsModKeyPressed(Achtung.Settings.forceCommandMenuKey),
			CommandMenuMode.PressForPosition => Tools.IsModKeyPressed(Achtung.Settings.forceCommandMenuKey) == false,
			CommandMenuMode.Delayed => longPress,
			_ => false
		};

		var map = Find.CurrentMap;
		var cell = IntVec3.FromVector3(pos);
		if (cell.InBounds(map) == false)
			return true;

		var pawnsUnderMouse = map.thingGrid.ThingsListAt(cell).OfType<Pawn>().ToList();

		var subjectClicked = pawnsUnderMouse
			.Where(pawn =>
				(PawnAttackGizmoUtility.CanOrderPlayerPawn(pawn) == false)
				|| (pawn.Drafted == false && longPress == false)
				|| (pawn.Drafted == true && longPress == true)
			).Any();
		var standableClicked = cell.Standable(map);

		if (subjectClicked && colonists.Count > 1 && achtungPressed == false && forceMenu == false)
		{
			var allHaveWeapons = colonists.All(colonist => FloatMenuUtility.GetRangedAttackAction(colonist.pawn, cell, out _) != null);
			if (allHaveWeapons)
				return true;
		}

		if (forceMenu || (subjectClicked && achtungPressed == false) || standableClicked == false)
		{
			if (actions == null)
				return true;
			return ShowMenu(actions, forceMenu, null);
		}

		centerOnColonist = pawnsUnderMouse.Where(pawn =>
			pawn.drafter != null
			&& pawn.IsPlayerControlled
			&& pawn.Downed == false
			&& (pawn.jobs?.IsCurrentJobPlayerInterruptible() ?? false)
		)
		.OrderBy(pawn => pawn.Drafted ? 0 : 1)
		.Select(pawn => new Colonist(pawn))
		.FirstOrDefault();

		if (centerOnColonist != null && (centerOnColonist.pawn.Drafted || achtungPressed))
		{
			if (colonists.Contains(centerOnColonist) == false)
				colonists.Add(centerOnColonist);
		}

		if (achtungPressed)
			Tools.DraftWithSound(colonists, true);

		var useFormation = doPositioning && (centerOnColonist != null || achtungPressed);
		void DoDrag() => StartDragging(pos, useFormation);

		// in multiplayer, drafting will update pawn.Drafted in the same tick, so we fake it
		if (allDrafted && doPositioning && longPress == false)
		{
			DoDrag();
			if (centerOnColonist == null)
				MouseDrag(pos, -1);
			return true;
		}

		return ShowMenu(actions, forceMenu, DoDrag);
	}

	private void StartDragging(Vector3 pos, bool asGroup)
	{
		var draftedColonists = colonists.Where(colonist => colonist.pawn.Drafted).ToList();

		groupMovement = asGroup;
		if (groupMovement)
		{
			if (centerOnColonist != null)
			{
				groupCenter = centerOnColonist.pawn.Position.ToVector3Shifted();
			}
			else
			{
				groupCenter.x = draftedColonists.Sum(colonist => colonist.startPosition.x) / draftedColonists.Count;
				groupCenter.z = draftedColonists.Sum(colonist => colonist.startPosition.z) / draftedColonists.Count;
			}
			groupRotation = 0;
			groupRotationWas45 = Tools.Has45DegreeOffset(draftedColonists);
		}
		else
		{
			lineStart = pos;
			lineStart.y = Altitudes.AltitudeFor(AltitudeLayer.MetaOverlays);
		}

		draftedColonists
			.Do(colonist => colonist.offsetFromCenter = colonist.startPosition - groupCenter);

		isDragging = true;
		Event.current.Use();
		Tools.SetCursor(AchtungCursor.Position);
	}

	private void EndDragging()
	{
		groupMovement = false;
		groupedMoved = false;
		centerOnColonist = null;
		if (isDragging)
		{
			colonists.Clear();
			Event.current.Use();
		}
		isDragging = false;
	}

	public void MouseDrag(Vector3 pos, int dragCount)
	{
		if (dragCount == 0 && centerOnColonist != null)
		{
			var selector = Find.Selector;
			if (selector.IsSelected(centerOnColonist.pawn) == false)
			{
				if (Event.current.shift == false)
				{
					selector.ClearSelection();
					colonists = [];
				}
				selector.Select(centerOnColonist.pawn);
				if (colonists.Contains(centerOnColonist) == false)
					colonists.Add(centerOnColonist);
			}
		}

		var draftedColonists = colonists.Where(colonist => colonist.pawn.Drafted).ToList();

		if (Event.current.button != 1)
			return;

		if (isDragging == false)
			return;

		if (groupMovement)
		{
			draftedColonists.Do(colonist => colonist.OrderTo(pos + Tools.RotateBy(colonist.offsetFromCenter, groupRotation, groupRotationWas45)));
			groupedMoved = true;
			Event.current.Use();
			return;
		}

		lineEnd = pos;
		lineEnd.y = Altitudes.AltitudeFor(AltitudeLayer.MetaOverlays);
		var count = draftedColonists.Count;
		var dragVector = lineEnd - lineStart;
		suppressMenu |= dragVector.MagnitudeHorizontalSquared() > 0.5f;

		var delta = count > 1 ? dragVector / (count - 1) : Vector3.zero;
		var linePosition = count == 1 ? lineEnd : lineStart;
		Tools.OrderColonistsAlongLine(draftedColonists, lineStart, lineEnd).Do(colonist =>
		{
			colonist.OrderTo(linePosition);
			linePosition += delta;
		});

		Event.current.Use();
	}

	public void MouseUp()
	{
		Tools.SetCursor(AchtungCursor.Default);

		if (Event.current.button != 1)
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

			var draftedColonists = colonists.Where(colonist => colonist.pawn.Drafted).ToList();

			switch (key)
			{
				case KeyCode.Q:
					if (groupMovement)
					{
						groupRotation -= 45;

						var pos = UI.MouseMapPosition();
						draftedColonists.Do(colonist => colonist.OrderTo(pos + Tools.RotateBy(colonist.offsetFromCenter, groupRotation, groupRotationWas45)));

						Event.current.Use();
					}
					break;

				case KeyCode.E:
					if (groupMovement)
					{
						groupRotation += 45;

						var pos = UI.MouseMapPosition();
						draftedColonists.Do(colonist => colonist.OrderTo(pos + Tools.RotateBy(colonist.offsetFromCenter, groupRotation, groupRotationWas45)));

						Event.current.Use();
					}
					break;

				case KeyCode.Escape:
					isDragging = false;
					suppressMenu = false;
					Tools.CancelDrafting(colonists);
					colonists.Clear();
					Event.current.Use();
					break;
			}
		}
	}

	private static void DrawForcedJobs()
	{
		var forcedWork = ForcedWork.Instance;
		var map = Find.CurrentMap;
		if (map == null || forcedWork == null)
			return;

		var currentViewRect = Find.CameraDriver.CurrentViewRect;
		var forcedJobs = forcedWork.ForcedJobsForMap(map);
		forcedJobs?.DoIf(forcedJob => forcedJob.pawn.Spawned && forcedJob.pawn.Map == map && Find.Selector.IsSelected(forcedJob.pawn), forcedJob =>
		{
			forcedJob.AllCells(true).Distinct()
				.DoIf(cell => currentViewRect.Contains(cell), cell => Tools.DrawForceIcon(cell.x, cell.y));
		});
	}

	public void HandleDrawing()
	{
		if (Achtung.Settings.workMarkers != WorkMarkers.Off)
			DrawForcedJobs();

		if (isDragging)
		{
			if (colonists.Count > 1 && groupMovement == false)
				GenDraw.DrawLineBetween(lineStart, lineEnd, AltitudeLayer.Item.AltitudeFor(), GenDraw.LineMatRed, 0.5f);

			colonists.Do(c =>
			{
				var pos = c.designation;
				if (pos == IntVec3.Invalid)
					return;

				var vec = pos.ToVector3Shifted();
				Tools.DrawMarker(vec);
				if (drawColonistPreviews)
				{
					c.pawn.Drawer.renderer.RenderPawnAt(vec);
					c.pawn.DrawExtraSelectionOverlays();
				}
			});
		}
	}

	public void HandleDrawingOnGUI()
	{
		colonists.DoIf(c => c.designation.IsValid, c =>
		{
			var labelPos = Tools.LabelDrawPosFor(c.designation, -0.6f);
			GenMapUI.DrawPawnLabel(c.pawn, labelPos, 1f, 9999f, null);
		});
	}

	public void HandleEarlyRightClicks()
	{
		var selector = Find.Selector;
		var achtungPressed = Tools.IsModKeyPressed(Achtung.Settings.achtungKey);

		var hasSelectedColonists = selector.SelectedObjects.OfType<Pawn>()
			.Where(pawn =>
				pawn.drafter != null
				&& pawn.IsPlayerControlled
				&& pawn.Downed == false
				&& (pawn.jobs?.IsCurrentJobPlayerInterruptible() ?? false)
			)
			.Any();
		if (hasSelectedColonists && achtungPressed == false)
			return;

		var colonistsClicked = GenUI.ThingsUnderMouse(UI.MouseMapPosition(), 0.8f, TargetingParameters.ForPawns(), null)
			.OfType<Pawn>()
			.Where(pawn => PawnAttackGizmoUtility.CanOrderPlayerPawn(pawn))
			.ToList();
		if (colonistsClicked.Count == 1)
		{
			var colonist = colonistsClicked[0];
			if (colonist.Drafted || achtungPressed)
				selector.Select(colonist);
		}
	}

	static int longPressThreshold = -1;
	static int dragCount = 0;
	public bool HandleEvents()
	{
		if (Find.WindowStack.IsOpen<FloatMenu>())
		{
			isDragging = false;
			return true;
		}

		var pos = UI.MouseMapPosition();
		var runOriginal = true;
		if (Achtung.Settings.forceCommandMenuMode == CommandMenuMode.Delayed
			&& groupedMoved == false
			&& longPressThreshold > -1
			&& Event.current.rawType == EventType.Layout
			&& Tools.EnvTicks() > longPressThreshold
			&& suppressMenu == false)
		{
			longPressThreshold = -1;
			return MouseDown(pos, 1, true);
		}
		switch (Event.current.rawType)
		{
			case EventType.MouseDown:
				if (Event.current.button == 1)
					suppressMenu = false;
				longPressThreshold = Event.current.button == 1 ? Tools.EnvTicks() + Achtung.Settings.menuDelay : -1;
				runOriginal = MouseDown(pos, Event.current.button, false);
				dragCount = 0;
				break;

			case EventType.MouseDrag:
				MouseDrag(pos, dragCount++);
				break;

			case EventType.MouseUp:
				longPressThreshold = -1;
				MouseUp();
				break;

			case EventType.KeyDown:
				KeyDown(Event.current.keyCode);
				break;
		}
		return runOriginal;
	}
}
