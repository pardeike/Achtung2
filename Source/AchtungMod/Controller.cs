using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace AchtungMod
{
	public class Controller
	{
		public IEnumerable<Colonist> colonists;
		public Vector3 lineStart;
		public Vector3 lineEnd;
		public bool isDragging;
		public bool relativeMovement;
		public bool drawColonistPreviews;
		public static Dictionary<Projectile, ProjectileInfo> projectiles = new Dictionary<Projectile, ProjectileInfo>();

		public static HashSet<ScoredPosition> debugPositions = new HashSet<ScoredPosition>();
		public static bool debugPositionNeedsClear = true;
		public static void AddDebugPositions(IEnumerable<ScoredPosition> pos)
		{
			if (Settings.instance.debugPositions == false) return;
			if (debugPositionNeedsClear) debugPositions = new HashSet<ScoredPosition>();
			debugPositionNeedsClear = false;
			if (pos != null) debugPositions.UnionWith(pos);
		}
		public static void AddDebugPositions(IEnumerable<IntVec3> vecs)
		{
			if (Settings.instance.debugPositions == false) return;
			if (debugPositionNeedsClear) debugPositions = new HashSet<ScoredPosition>();
			debugPositionNeedsClear = false;
			if (vecs != null) debugPositions.UnionWith(vecs.Select(v => new ScoredPosition(v)));
		}
		public static void ClearDebugPositions()
		{
			if (Settings.instance.debugPositions == false) return;
			if (debugPositionNeedsClear) debugPositions = new HashSet<ScoredPosition>();
			debugPositionNeedsClear = false;
		}

		public static Controller controller = null;
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

		public void Initialize()
		{
			Messages.Message("AchtungOptions".Translate(), MessageSound.Benefit);
			string state = Settings.instance.modActive ? "" : "AchtungOff".Translate();
			Messages.Message("AchtungVersion".Translate(Tools.Version, state), MessageSound.Silent);
		}

		public void InstallJobDefs()
		{
			new List<JobDef> {
					 new JobDriver_CleanRoom().MakeJobDef(),
					 new JobDriver_FightFire().MakeJobDef(),
					 new JobDriver_SowAll().MakeJobDef(),
					 new JobDriver_AutoCombat().MakeJobDef()
				}
			.DoIf(def => DefDatabase<JobDef>.GetNamedSilentFail(def.defName) == null, def => DefDatabase<JobDef>.Add(def));
		}

		public IEnumerable<Colonist> GetSelectedColonists(bool forceDraft)
		{
			List<Colonist> temp = new List<Colonist>();
			foreach (Pawn tempPawn in Tools.UserSelectedAndReadyPawns()) temp.Add(new Colonist(tempPawn, forceDraft));
			return temp;
		}

		public IEnumerable<Colonist> GetAllColonists(bool forceDraft)
		{
			List<Colonist> temp = new List<Colonist>();

			IEnumerable<Pawn> pawns = Find.VisibleMap.listerThings.ThingsInGroup(ThingRequestGroup.Pawn)
				.Cast<Pawn>()
				.Where(pawn =>
					pawn.Faction == Faction.OfPlayer
					&& pawn.drafter != null
					&& pawn.IsColonistPlayerControlled
					&& pawn.Downed == false
					&& pawn.jobs.IsCurrentJobPlayerInterruptible()
				);

			foreach (Pawn tempPawn in pawns) temp.Add(new Colonist(tempPawn, forceDraft));
			return temp;
		}

		public void MouseDown(Vector3 pos)
		{
			if (Event.current.button == 1)
			{
				Vector3 where = UI.MouseMapPosition();

				relativeMovement = Tools.IsModKeyPressed(Settings.instance.relativeMovementKey);

				bool forceDraft = Tools.IsModKeyPressed(Settings.instance.forceDraftKey);
				colonists = GetSelectedColonists(forceDraft);
				if (colonists.Count() > 0)
				{
					bool ignoreMenu = Tools.IsModKeyPressed(Settings.instance.ignoreMenuKey);
					if (colonists.Count() == 1 && ignoreMenu == Settings.instance.reverseMenuKey)
					{
						IEnumerable<FloatMenuOption> choices = FloatMenuMakerMap.ChoicesAtFor(where, colonists.First().pawn);
						if (choices.Count() > 0)
						{
							// don't overwrite existing floating menu
							return;
						}
					}

					// build multi menu from existing commands
					MultiActions actions = new MultiActions(colonists, where);

					// present combined menu to the user
					if (actions.Count() > 0 && ignoreMenu == Settings.instance.reverseMenuKey)
					{
						Find.WindowStack.Add(actions.GetWindow());
						Event.current.Use();
						return;
					}

					if (colonists.Count() == 1 && Tools.GetDraftingStatus(colonists.First().pawn) == false)
					{
						// don't drag if neither standard menu nor multi menu have any choices and it's a single colonist
						return;
					}

					// start dragging
					lineStart = pos;
					lineStart.y = Altitudes.AltitudeFor(AltitudeLayer.MetaOverlays);

					isDragging = true;
					Event.current.Use();
				}
			}
		}

		public void MouseDrag(Vector3 pos)
		{
			if (isDragging == true)
			{
				lineEnd = pos;
				lineEnd.y = Altitudes.AltitudeFor(AltitudeLayer.MetaOverlays);
				int count = colonists.Count();
				Vector3 dragVector = lineEnd - lineStart;

				if (relativeMovement)
				{
					colonists.Do(colonist =>
					{
						Vector3 delta = lineEnd - lineStart;
						colonist.OrderTo(colonist.startPosition + delta);
					});
				}
				else
				{
					Vector3 delta = count > 1 ? dragVector / (float)(count - 1) : Vector3.zero;
					Vector3 linePosition = count == 1 ? lineEnd : lineStart;
					colonists.Do(colonist =>
					{
						colonist.OrderTo(linePosition);
						linePosition += delta;
					});
				}

				Event.current.Use();
			}
		}

		/*
		public bool TryCopyElement(Vector3 where)
		{
			BuildableDef buildDef = null;
			ThingDef thingDef = null;

			Thing thing = Find.VisibleMap.thingGrid.ThingAt<Building>(where.ToIntVec3());
			if (thing != null)
			{
				buildDef = thing.def;
				thingDef = thing.Stuff;
			}
			else
			{
				Blueprint_Build blueprint = Find.VisibleMap.thingGrid.ThingAt<Blueprint_Build>(where.ToIntVec3());
				if (blueprint != null)
				{
					thing = blueprint;
					buildDef = blueprint.def.entityDefToBuild;
					thingDef = blueprint.stuffToUse;
				}
			}

			if (thing != null && buildDef != null)
			{
				Designator_Build designator = DefDatabase<DesignationCategoryDef>.AllDefs
					.SelectMany(catdef => catdef.ResolvedAllowedDesignators)
					.Where(d => d is Designator_Build && (d as Designator_Build).PlacingDef == buildDef)
					.Cast<Designator_Build>()
					.FirstOrDefault();
				if (designator != null)
				{
					Find.Selector.ClearSelection();
					Find.Selector.Select(thing, true, true);

					SoundDefOf.SelectDesignator.PlayOneShotOnCamera();
					designator.SetStuffDef(thingDef);
					Find.DesignatorManager.Select(designator);

					Find.MainTabsRoot.SetCurrentTab(MainTabDefOf.Inspect, true);

					FieldInfo placingRot = designator.GetType().GetField("placingRot", BindingFlags.Instance | BindingFlags.NonPublic);
					if (placingRot != null) placingRot.SetValue(designator, thing.Rotation);

					return true;
				}
			}

			return false;
		}
		*/

		public void AddDoThoroughly(List<FloatMenuOption> options, Vector3 clickPos, Pawn pawn, Type driverType)
		{
			JobDriver_Thoroughly driver = (JobDriver_Thoroughly)Activator.CreateInstance(driverType);
			IEnumerable<LocalTargetInfo> targets = driver.CanStart(pawn, clickPos);
			if (targets != null)
			{
				List<Job> existingJobs = driver.SameJobTypesOngoing();
				targets.Do(target =>
				{
					Action action = delegate
					{
						driver.StartJob(pawn, target);
					};
					string suffix = existingJobs.Count() > 0 ? " " + ("AlreadyDoing".Translate(existingJobs.Count() + 1)) : "";
					options.Add(new FloatMenuOption(driver.GetLabel() + suffix, action, MenuOptionPriority.Low));
				});
			}
		}

		public IEnumerable<FloatMenuOption> AchtungChoicesAtFor(Vector3 clickPos, Pawn pawn)
		{
			List<FloatMenuOption> options = new List<FloatMenuOption>();
			if (Settings.instance.modActive == false) return options;

			bool ignoreMenu = Tools.IsModKeyPressed(Settings.instance.ignoreMenuKey);
			if (ignoreMenu == Settings.instance.reverseMenuKey)
			{
				AddDoThoroughly(options, clickPos, pawn, typeof(JobDriver_CleanRoom));
				AddDoThoroughly(options, clickPos, pawn, typeof(JobDriver_FightFire));
				AddDoThoroughly(options, clickPos, pawn, typeof(JobDriver_SowAll));
				AddDoThoroughly(options, clickPos, pawn, typeof(JobDriver_AutoCombat));
			}

			return options;
		}

		public void MouseUp(Vector3 pos)
		{
			if (isDragging == true)
			{
				colonists.Clear();
				Event.current.Use();
			}
			isDragging = false;
		}

		public void KeyDown(KeyCode key)
		{
			if (isDragging == true)
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
			if (Settings.instance.modActive == false) return;

			if (Settings.instance.debugPositions)
			{
				// if (Find.Selector.SelectedObjects.Count() == 0) debugPositions = new HashSet<ScoredPosition>();
				if (debugPositions.Count() > 0)
				{
					float min = debugPositions.Min(sp => sp.score);
					float max = debugPositions.Max(sp => sp.score);
					debugPositions.Do(sp => Tools.DebugPosition(sp.v.ToVector3(), new Color(1f, 0f, 0f, GenMath.LerpDouble(min, max, 0.1f, 0.8f, sp.score))));
					debugPositionNeedsClear = true;
				}
			}

			if (isDragging)
			{
				if (colonists.Count() > 1) Tools.DrawLineBetween(lineStart, lineEnd, 1.0f);

				colonists.DoIf(c => (c.designation != Vector3.zero), c =>
				{
					Tools.DrawMarker(c.designation);
					if (drawColonistPreviews)
					{
						c.pawn.Drawer.renderer.RenderPawnAt(c.designation);
						c.pawn.DrawExtraSelectionOverlays();
					}
				});
			}

			UpdateProjectiles();
		}

		public void HandleDrawingOnGUI()
		{
			if (Settings.instance.modActive == false) return;

			colonists.DoIf(c => (c.designation != Vector3.zero), c =>
			{
				Vector2 labelPos = Tools.LabelDrawPosFor(c.designation, -0.6f);
				GenMapUI.DrawPawnLabel(c.pawn, labelPos, 1f, 9999f, null);
			});
		}

		public void HandleEvents()
		{
			bool shiftKey = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
			if (shiftKey && Input.GetKeyDown(KeyCode.Return))
			{
				Event.current.Use();
				if (Find.WindowStack.IsOpen(typeof(PreferenceDialog)) == false)
				{
					Find.WindowStack.Add(new PreferenceDialog());
				}
			}

			if (Settings.instance.modActive == false) return;

			Vector3 pos = UI.MouseMapPosition();
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

		public void AddProjectile(Projectile projectile, Thing launcher, Vector3 origin, LocalTargetInfo targ, Thing equipment)
		{
			Bullet bullet = projectile as Bullet;
			Projectile_Explosive explosive = projectile as Projectile_Explosive;
			if (bullet != null || explosive != null)
			{
				projectiles.Add(projectile, new ProjectileInfo(launcher, origin, targ, equipment));
			}
		}

		public void UpdateProjectiles()
		{
			HashSet<Projectile> activeProjectiles = new HashSet<Projectile>();

			Find.VisibleMap.mapPawns.AllPawnsSpawned
				.Where(p => p.Spawned == true && p.Destroyed == false && p.Downed == false && p.Dead == false)
				.DoIf(p => p.equipment != null && p.equipment.Primary != null, p =>
				{
					ThingDef def = p.equipment.PrimaryEq.PrimaryVerb.verbProps.projectileDef;
					if (def != null) activeProjectiles.UnionWith(
						Find.VisibleMap.listerThings
							.ThingsOfDef(def)
							.Select(t => t as Projectile)
							.Where(t => t != null)
					);
				});

			Dictionary<Projectile, ProjectileInfo> remaining = new Dictionary<Projectile, ProjectileInfo>();
			projectiles.Keys.DoIf(p => activeProjectiles.Contains(p), p => remaining.Add(p, projectiles[p]));
			projectiles = remaining;
		}
	}
}