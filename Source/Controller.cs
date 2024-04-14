using RimWorld;
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
		public Pawn isAiming;
		public IAttackTarget aimAttackTarget, lastAimAttackTarget;
		public Colonist isMoving;
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
			isAiming = null;
			suppressMenu = false;
			drawColonistPreviews = true;
		}

		public static void InstallDefs()
		{
			new List<JobDef>
			{
				// new JobDriver_CleanRoom().MakeDef(),
				new JobDriver_FightFire().MakeDef(),
			}
			.DoIf(def => DefDatabase<JobDef>.GetNamedSilentFail(def.defName) == null, DefDatabase<JobDef>.Add);
		}

		bool ShowMenu(MultiActions actions, bool forceMenu, Map map, IntVec3 cell, Action gotoAction)
		{
			if (actions == null)
				return true;
			var menuAdded = false;
			var optionTaken = false;
			if (actions.Count(false) > 0)
			{
				if (cell.InBounds(map) && forceMenu == false)
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
				EndDragging();
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

			if (longPress == false)
				colonists = Tools.GetSelectedColonists();

			if (colonists.Count == 0)
				return true;

			if (isDragging && button == (int)Button.left && groupMovement == false)
			{
				Tools.DraftWithSound(colonists, true);
				EndDragging();
				return true;
			}

			if (isAiming != null && button == (int)Button.left)
			{
				isAiming = null;
				return true;
			}

			if (button != (int)Button.right)
				return true;

			var actions = doForceMenu ? new MultiActions(colonists, UI.MouseMapPosition()) : null;
			var achtungPressed = Tools.IsModKeyPressed(Achtung.Settings.achtungKey);
			var allDrafted = colonists.All(colonist => colonist.pawn.Drafted || colonist.pawn.IsColonyMechPlayerControlled || achtungPressed);
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

			var thingsClicked = map.thingGrid.ThingsListAt(cell);
			var subjectClicked = thingsClicked.OfType<Pawn>().Where(pawn => (pawn.IsColonist == false && pawn.IsColonyMech == false) || pawn.Drafted == false).Any();
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
				return ShowMenu(actions, forceMenu, map, cell, null);
			}

			if (achtungPressed)
				Tools.DraftWithSound(colonists, true);

			// in multiplayer, drafting will update pawn.Drafted in the same tick, so we fake it
			if (allDrafted && doPositioning && longPress == false)
			{
				StartDragging(pos, achtungPressed);
				return true;
			}

			return ShowMenu(actions, forceMenu, map, cell, () => StartDragging(pos, achtungPressed));
		}

		private void StartDragging(Vector3 pos, bool asGroup)
		{
			if (isAiming != null)
				return;

			var draftedColonists = colonists.Where(colonist => colonist.pawn.Drafted).ToList();

			groupMovement = asGroup;
			if (groupMovement)
			{
				groupCenter.x = draftedColonists.Sum(colonist => colonist.startPosition.x) / draftedColonists.Count;
				groupCenter.z = draftedColonists.Sum(colonist => colonist.startPosition.z) / draftedColonists.Count;
				groupRotation = 0;
				groupRotationWas45 = Tools.Has45DegreeOffset(draftedColonists);
			}
			else
				lineStart = pos.ToIntVec3().ToVector3Shifted();

			draftedColonists
				.Do(colonist => colonist.offsetFromCenter = colonist.startPosition - groupCenter);

			isDragging = true;
			Event.current.Use();
		}

		public void MouseDrag(Vector3 pos)
		{
			var draftedColonists = colonists.Where(colonist => colonist.pawn.Drafted).ToList();

			if (Event.current.button != (int)Button.right)
				return;

			if (isDragging == false)
				return;

			if (groupMovement)
			{
				draftedColonists.Do(colonist => colonist.OrderTo(pos + Tools.RotateBy(colonist.offsetFromCenter, groupRotation, groupRotationWas45)));
				Event.current.Use();
				return;
			}

			lineEnd = pos.ToIntVec3().ToVector3Shifted();
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

		public bool StartAiming(Vector3 pos)
		{
			if (isAiming != null)
				return false;

			var draggedDraftedColonist = Find.CurrentMap.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer)
				.FirstOrDefault(pawn =>
					pawn.Drafted && pawn.IsPlayerControlled && pawn.Downed == false &&
					pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) &&
					pawn.CurrentEffectiveVerb != null && pawn.CurrentEffectiveVerb.ApparelPreventsShooting() == false &&
					(pos - pawn.DrawPos).MagnitudeHorizontalSquared() < 1f
				);
			if (draggedDraftedColonist == null)
				return false;

			isAiming = draggedDraftedColonist;
			if (isAiming != null)
			{
				lastAimAttackTarget = null;
				Find.Selector.Select(isAiming);
				Tools.SetCursor(AchtungCursor.Attack);
				Event.current.Use();
				return true;
			}
			return false;
		}

		public void AimDrag(Vector3 pos)
		{
			if (isAiming == null)
				return;
			var verb = isAiming.CurrentEffectiveVerb;
			if (verb == null)
				return;

			isAiming.rotationTracker.Face(pos);

			var map = isAiming.Map;
			if (verb.IsMeleeAttack)
			{
				aimAttackTarget = AttackTargetFinder.FindBestReachableMeleeTarget(target => target.NotPlayer() && (pos - target.Thing.DrawPos).MagnitudeHorizontalSquared() < 1f, isAiming, 9999f, true, true);
				if (lastAimAttackTarget != aimAttackTarget)
				{
					lastAimAttackTarget = aimAttackTarget;
					if (lastAimAttackTarget is Thing thing)
					{
						isAiming.jobs.EndCurrentJob(JobCondition.InterruptForced);
						var job = JobMaker.MakeJob(JobDefOf.AttackMelee, thing);
						job.playerForced = true;
						isAiming.jobs.TryTakeOrderedJob(job, new JobTag?(JobTag.DraftedOrder), false);
						FleckMaker.Static(thing.DrawPos, map, FleckDefOf.FeedbackMelee, 1f);
					}
				}
			}
			else
			{
				var targets = map.attackTargetsCache.GetPotentialTargetsFor(isAiming)
					.Where(target => target.NotPlayer() && (pos - target.Thing.DrawPos).MagnitudeHorizontalSquared() < 1f)
					.ToList();
				aimAttackTarget = AttackTargetFinder.GetRandomShootingTargetByScore(targets, isAiming, verb);
				if (lastAimAttackTarget != aimAttackTarget)
				{
					lastAimAttackTarget = aimAttackTarget;
					if (lastAimAttackTarget is Thing thing)
					{
						isAiming.jobs.EndCurrentJob(JobCondition.InterruptForced);
						var job = JobMaker.MakeJob(JobDefOf.AttackStatic, thing);
						job.playerForced = true;
						isAiming.jobs.TryTakeOrderedJob(job, new JobTag?(JobTag.DraftedOrder), false);
						FleckMaker.Static(thing.DrawPos, map, FleckDefOf.FeedbackShoot, 1f);
					}
				}
			}
		}

		public bool StartMoving(Vector3 pos)
		{
			var draggedDraftedColonist = Find.CurrentMap.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer)
				.FirstOrDefault(pawn =>
					pawn.Drafted && pawn.IsPlayerControlled && pawn.Downed == false &&
					(pos - pawn.DrawPos).MagnitudeHorizontalSquared() < 1f
				);
			if (draggedDraftedColonist == null)
				return false;

			if (draggedDraftedColonist != null)
			{
				isMoving = new Colonist(draggedDraftedColonist);
				Find.Selector.Select(draggedDraftedColonist);
				Tools.SetCursor(AchtungCursor.Position);
				Event.current.Use();
				return true;
			}
			return false;
		}

		public void MoveDrag(Vector3 pos)
		{
			if (isMoving == null)
				return;

			isMoving.OrderTo(pos);
			if (isMoving.designation.IsValid)
				isMoving.pawn.rotationTracker.Face(isMoving.designation.ToVector3Shifted());
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

		public void MouseUp()
		{
			Tools.SetCursor(AchtungCursor.Default);

			if (isMoving != null)
			{
				isMoving = null;
				Event.current.Use();
				return;
			}

			if (isAiming != null)
			{
				isAiming = null;
				Event.current.Use();
				return;
			}

			if (Event.current.button == (int)Button.right)
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

		static void StartWork(Type driverType, Pawn pawn, LocalTargetInfo target, LocalTargetInfo clickCell)
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
					options.Add(new FloatMenuOption(driver.GetLabel() + suffix, () => StartWork(driverType, pawn, target, clickCell), MenuOptionPriority.Low));
				}
			}
		}

		public static IEnumerable<FloatMenuOption> AchtungChoicesAtFor(Vector3 clickPos, Pawn pawn)
		{
			var options = new List<FloatMenuOption>();
			//AddDoThoroughly(options, clickPos, pawn, typeof(JobDriver_CleanRoom));
			AddDoThoroughly(options, clickPos, pawn, typeof(JobDriver_FightFire));
			return options;
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

		static readonly Color thingColor = Color.red.ToTransparent(0.2f);
		static readonly Color cellColor = Color.green.ToTransparent(0.2f);
		public void DebugDrawReservations()
		{
			var reservationManager = Find.CurrentMap?.reservationManager;
			if (reservationManager == null)
				return;

			var selector = Find.Selector;
			reservationManager.ReservationsReadOnly
				.DoIf(res => selector.IsSelected(res.Claimant), res => Tools.DebugPosition(res.Target.Cell.ToVector3(), res.Target.HasThing ? thingColor : cellColor));
		}

		public void HandleDrawing()
		{
			if (Achtung.Settings.workMarkers != WorkMarkers.Off)
				DrawForcedJobs();

			// for debugging reservations
			// DebugDrawReservations();

			if (isAiming != null)
			{
				var verb = isAiming.CurrentEffectiveVerb;
				if (verb != null)
				{
					if (verb.IsMeleeAttack)
					{
						var pos = aimAttackTarget?.Thing?.DrawPos ?? UI.MouseMapPosition();
						GenDraw.DrawLineBetween(isAiming.DrawPos, pos, AltitudeLayer.Item.AltitudeFor(), GenDraw.LineMatRed, 0.2f);
					}
					else
					{
						var thing = aimAttackTarget?.Thing;
						if (thing != null)
							GenDraw.DrawLineBetween(isAiming.DrawPos, thing.DrawPos, AltitudeLayer.Item.AltitudeFor(), GenDraw.LineMatRed, 0.2f);
						else
						{
							var cell = UI.MouseCell();
							if (GenSight.LineOfSight(isAiming.Position, cell, isAiming.Map))
								GenDraw.DrawLineBetween(isAiming.DrawPos, UI.MouseMapPosition(), AltitudeLayer.Item.AltitudeFor(), GenDraw.LineMatRed, 0.2f);
						}
					}
				}
			}

			if (isMoving != null)
			{
				var pos = isMoving.designation;
				if (pos == IntVec3.Invalid)
					return;

				var vec = pos.ToVector3Shifted();
				Tools.DrawMarker(vec);
				if (drawColonistPreviews)
				{
					isMoving.pawn.Drawer.renderer.RenderPawnAt(vec);
					isMoving.pawn.DrawExtraSelectionOverlays();
				}
			}

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
			if (isMoving != null && isMoving.designation.IsValid)
			{
				var labelPos = Tools.LabelDrawPosFor(isMoving.designation, -0.6f);
				GenMapUI.DrawPawnLabel(isMoving.pawn, labelPos, 1f, 9999f, null);
			}

			colonists.DoIf(c => c.designation.IsValid, c =>
			{
				var labelPos = Tools.LabelDrawPosFor(c.designation, -0.6f);
				GenMapUI.DrawPawnLabel(c.pawn, labelPos, 1f, 9999f, null);
			});
		}

		static int longPressThreshold = -1;
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
				&& longPressThreshold > -1
				&& Event.current.rawType == EventType.Layout
				&& Tools.EnvTicks() > longPressThreshold
				&& suppressMenu == false)
			{
				longPressThreshold = -1;
				return MouseDown(pos, (int)Button.right, true);
			}

			var button = Event.current.button;
			switch (Event.current.rawType)
			{
				case EventType.MouseDown:
					if (button == (int)Button.right)
					{
						if (StartAiming(pos))
							break;
						suppressMenu = false;
					}
					else
					{
						if (StartMoving(pos))
							break;
					}
					longPressThreshold = button == (int)Button.right ? Tools.EnvTicks() + Achtung.Settings.menuDelay : -1;
					runOriginal = MouseDown(pos, button, false);
					MoveDrag(pos);
					AimDrag(pos);
					MouseDrag(pos);
					break;

				case EventType.MouseDrag:
					MoveDrag(pos);
					AimDrag(pos);
					MouseDrag(pos);
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
}
