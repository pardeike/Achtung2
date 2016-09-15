using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace AchtungMod
{
	public class JobDriver_AutoCombat : JobDriver_Thoroughly
	{
		public enum AutoCombatState
		{
			Advancing,
			Moving,
			Shooting,
			Hitting,
			Ready
		}

		public enum AttackStyle
		{
			None,
			Melee,
			Ranged
		}

		public enum DoorCheese
		{
			None,
			Shooting,
			Hiding
		}

		public class EnemyInfo
		{
			public Pawn pawn;
			public bool hasRangedWeapon;
			public bool inRoom;

			public EnemyInfo(Pawn enemy)
			{
				pawn = enemy;
				Verb verb = enemy.TryGetAttackVerb(false);
				hasRangedWeapon = (verb != null && enemy.equipment != null && enemy.equipment.Primary != null && enemy.equipment.Primary.def.IsRangedWeapon && verb.IsStillUsableBy(enemy));
				Room room = enemy.GetRoom();
				inRoom = room != null && room.IsHuge == false;
			}
		}

		public static IEnumerable<MentalStateDef> badStates = new HashSet<MentalStateDef> {
				MentalStateDefOf.Berserk,
				MentalStateDefOf.Manhunter,
				MentalStateDefOf.ManhunterPermanent,
				MentalStateDefOf.PanicFlee,
				MentalStateDefOf.WanderPsychotic
			};
		public static JobDef autoCombatJobDef = new JobDriver_AutoCombat().MakeJobDef();

		// saved
		public AutoCombatState state = AutoCombatState.Moving;
		public AttackStyle attackStyle = AttackStyle.None;
		public Pawn target;

		// volatile
		public static bool debug = false;
		public Verb currentVerb = null;
		public Action<bool> pathEndCallback = b => { };
		public IntVec3 pathEndLocation = IntVec3.Invalid;
		public List<EnemyInfo> currentEnemies = new List<EnemyInfo>();
		public IEnumerable<Pawn> otherColonists = new List<Pawn>();
		public HashSet<Vector3> otherColonistPositions = new HashSet<Vector3>();
		public HashSet<ScoredPosition> scoredPositions = new HashSet<ScoredPosition>();
		public int tickCounter = 0;
		public DoorCheese doorCheese = DoorCheese.None;
		public IntVec3 beforeDoorPosition = IntVec3.Invalid;
		public IntVec3 doorCheesePosition = IntVec3.Invalid;
		public bool targetIsFleeing = false;

		public override string GetPrefix()
		{
			return "AutoCombat";
		}

		public override string GetLabel()
		{
			return (GetPrefix() + "Label").Translate(target == null ? "" : target.NameStringShort);
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.LookValue<AutoCombatState>(ref this.state, "state", AutoCombatState.Moving, false);
			Scribe_Values.LookValue<AttackStyle>(ref this.attackStyle, "attackStyle", AttackStyle.None, false);
			Scribe_References.LookReference<Pawn>(ref target, "target");
		}

		public bool ValidTarget(TargetInfo info)
		{
			if (info == null) return false;
			Thing t = info.Thing;
			if (t == null) return false;

			Pawn p = t as Pawn;
			if (p != null && p.Spawned == true && p.Destroyed == false && p.Downed == false && p.Dead == false) return true;

			return false;
		}

		public override IEnumerable<TargetInfo> CanStart(Pawn pawn, Vector3 clickPos)
		{
			base.CanStart(pawn, clickPos);
			if (pawn.IsColonistPlayerControlled == false) return null;
			if (pawn.story.WorkTagIsDisabled(WorkTags.Violent)) return null;

			return GenUI.TargetsAt(clickPos, TargetingParameters.ForAttackHostile(), true)
				.Where(ValidTarget)
				.Where(t =>
				{
					target = t.Thing as Pawn;
					UpdateColonistsAndEnemies();
					UpdateVerbAndWorkLocations();
					return currentVerb != null && attackStyle != AttackStyle.None;
				});
		}

		public static Dictionary<float, HashSet<IntVec3>> circles = null;
		public static IEnumerable<IntVec3> GetCircle(float radius)
		{
			if (circles == null) circles = new Dictionary<float, HashSet<IntVec3>>();
			HashSet<IntVec3> cells = circles.ContainsKey(radius) ? circles[radius] : null;
			if (cells == null)
			{
				cells = new HashSet<IntVec3>();
				IEnumerator<IntVec3> enumerator = GenRadial.RadialPatternInRadius(radius).GetEnumerator();
				while (enumerator.MoveNext())
				{
					IntVec3 v = enumerator.Current;
					cells.Add(v);
					cells.Add(new IntVec3(-v.x, 0, v.z));
					cells.Add(new IntVec3(-v.x, 0, -v.z));
					cells.Add(new IntVec3(v.x, 0, -v.z));
				}
				enumerator.Dispose();
				circles[radius] = cells;
			}
			return cells;
		}

		public HashSet<IntVec3> GetDangerArea(float range, bool useSafeDistance)
		{
			float aggressiveness = Settings.instance.aggressiveness;
			HashSet<IntVec3> result = new HashSet<IntVec3>();
			float safeRadius = GenMath.LerpDouble(0, 1, 3f, 0f, aggressiveness);

			Controller.projectiles.Do(p =>
			{
				Projectile pj = p.Key;
				ProjectileInfo pi = p.Value;
				if (targetIsFleeing == false) result.UnionWith(GetCircle(pj.def.projectile.explosionRadius + safeRadius).Select(vec => pi.targ.Cell + vec));
				XiaolinWu line = new XiaolinWu(pi.origin, false, pi.targ.Cell.ToVector3Shifted(), true, 1f);
				result.UnionWith(line.cells);
			});

			if (targetIsFleeing == false && useSafeDistance) currentEnemies.Do(e =>
			{
				float safeDistanceByType = e.hasRangedWeapon ? 1f : range * 0.75f;
				float minimumSafeDistance = e.inRoom ? GenMath.LerpDouble(0, 1, 3f, 1f, aggressiveness) : GenMath.LerpDouble(0, 1, safeDistanceByType, 4f, aggressiveness);
				if (minimumSafeDistance > 0f) result.UnionWith(GetCircle(minimumSafeDistance).Select(vec => e.pawn.Position + vec));
			});

			return result;
		}

		public bool CanShootTargetSafelyFrom(IntVec3 cell, HashSet<IntVec3> avoidDangerArea = null)
		{
			if (cell.InBounds() == false) return false;
			if (avoidDangerArea != null && avoidDangerArea.Contains(cell)) return false;
			if (Reachability.CanReach(pawn, cell, PathEndMode.OnCell, pawn.NormalMaxDanger()) == false) return false;
			if (currentVerb == null || currentVerb.CanHitTargetFrom(cell, target) == false) return false;

			float distance = GenGeo.MagnitudeHorizontalSquared(cell.ToVector3Shifted() - target.TrueCenter());
			Vector2 cellVec = new Vector2(cell.x, cell.z);
			Vector2 targetVec = new Vector2(target.TrueCenter().x, target.TrueCenter().z);
			return otherColonists
				.Where(p => GenGeo.MagnitudeHorizontalSquared(cell.ToVector3Shifted() - p.TrueCenter()) < distance)
				.Where(p =>
				{
					Vector2 colonistVec = new Vector2(p.TrueCenter().x, p.TrueCenter().z);
					float radius = p.GetRoom() != null && p.GetRoom().IsHuge == false ? 1f : 2f;
					return GenGeo.IntersectLineCircle(colonistVec, radius, cellVec, targetVec);
				})
				.Count() == 0;
		}

		public HashSet<ScoredPosition> PositionsForRangedAttack()
		{
			float aggressiveness = Settings.instance.aggressiveness;
			float range = GenMath.LerpDouble(0, 1, 0.98f, 0.5f, aggressiveness) * currentVerb.verbProps.range;
			if (targetIsFleeing) range = range * 0.75f;
			float rangeSquared = range * range;

			HashSet<IntVec3> danger = GetDangerArea(range, true);
			HashSet<IntVec3> occuppiedDoors = new HashSet<IntVec3>(otherColonists.Select(p => p.Position).Where(v => Find.ThingGrid.ThingAt<Building_Door>(v) != null));
			HashSet<ScoredPosition> result = new HashSet<ScoredPosition>(
				 GetCircle(range)
				.Where(v =>
				{
					IntVec3 cell = target.Position + v;
					return (targetIsFleeing || pawn.Position.DistanceToSquared(cell) <= rangeSquared) &&
						CanShootTargetSafelyFrom(cell, danger) && occuppiedDoors.Contains(cell) == false;
				})
				.Select(v => new ScoredPosition(target.Position + v)));

			if (result.Count() == 0 && targetIsFleeing == false)
			{
				danger = GetDangerArea(range, false);
				result = new HashSet<ScoredPosition>(
					GetCircle(range).Select(v => target.Position + v)
						.Where(cell => pawn.Position.DistanceToSquared(cell) <= rangeSquared && CanShootTargetSafelyFrom(cell, danger))
						.Select(cell => new ScoredPosition(cell))
				);
			}

			float pawnClosenessFactor = GenMath.LerpDouble(0, 1, 0f, 10f, aggressiveness);
			float targetDistanceFactor = target.InAggroMentalState ? GenMath.LerpDouble(0, 1, 1f, 0f, aggressiveness) : GenMath.LerpDouble(0, 1, 10f, 0f, aggressiveness);
			float hitChanceFactor = GenMath.LerpDouble(0, 1, 0.5f, 1.5f, aggressiveness);
			float coverFactor = (currentEnemies.Count() == 0 ? 0f : (10f * aggressiveness - 5f) / (float)currentEnemies.Count());

			if (targetIsFleeing == false)
			{
				Tools.ApplyScoreLerped(result, sp => target.Position.DistanceToSquared(sp.v), 0f, targetDistanceFactor);
				currentEnemies.Do(e => Tools.ApplyScore(result, sp => coverFactor * CoverUtility.CalculateOverallBlockChance(sp.v, e.pawn.Position)));
			}
			Tools.ApplyScoreLerped(result, sp => pawn.Position.DistanceToSquared(sp.v), pawnClosenessFactor, 0f);
			Tools.ApplyScore(result, sp => -1f * hitChanceFactor * CoverUtility.CalculateOverallBlockChance(target.Position, sp.v));

			return result;
		}

		public HashSet<ScoredPosition> PositionsForMeleeAttack()
		{
			HashSet<ScoredPosition> result = new HashSet<ScoredPosition>();

			HashSet<IntVec3> attackArea = new HashSet<IntVec3>();
			currentEnemies.Do(e => attackArea.UnionWith(GetCircle(7f).Select(v => e.pawn.Position + v)));
			currentEnemies.DoIf(e => (e.pawn != target), e => attackArea.ExceptWith(GetCircle(2f).Select(v => e.pawn.Position + v)));
			attackArea.ExceptWith(GetDangerArea(0f, false));

			result = new HashSet<ScoredPosition>(attackArea
				.Where(cell => CanShootTargetSafelyFrom(cell))
				.Select(v => new ScoredPosition(v)));

			float targetClosenessFactor = 20f;
			float enemyDistanceFactor = currentEnemies.Count() <= 1 ? 0f : 1f / (float)(currentEnemies.Count() - 1);
			float coverFactor = currentEnemies.Count() <= 1 ? 0f : 1f / (float)(currentEnemies.Count() - 1);

			Tools.ApplyScoreLerped(result, sp => 1f / Tools.PawnCellDistance(target, sp.v), 0f, targetClosenessFactor);
			currentEnemies.DoIf(e => (e.pawn != target), e => Tools.ApplyScoreLerped(result, sp => Tools.PawnCellDistance(e.pawn, sp.v), 0f, enemyDistanceFactor));
			currentEnemies.DoIf(e => (e.pawn != target), e => Tools.ApplyScore(result, sp => coverFactor * CoverUtility.CalculateOverallBlockChance(sp.v, e.pawn.Position)));

			return result;
		}

		public void UpdateColonistsAndEnemies()
		{
			currentEnemies = Find.MapPawns.AllPawnsSpawned
				.Where(p => p.Spawned == true && p.Destroyed == false && p.Downed == false && p.Dead == false)
				.Where(p => p.InAggroMentalState || p.HostileTo(pawn.Faction))
				.Select(p => new EnemyInfo(p)).ToList();

			otherColonists = Find.MapPawns.AllPawnsSpawned
				.Where(p => p != pawn && p.Spawned == true && p.Destroyed == false && p.Dead == false && p.Faction == pawn.Faction);

			targetIsFleeing = target != null && target.MentalStateDef != null && target.MentalStateDef.category == MentalStateCategory.Panic;
		}

		public override void UpdateVerbAndWorkLocations()
		{
			currentVerb = pawn.TryGetAttackVerb(false);
			if (currentVerb != null && currentVerb.IsStillUsableBy(pawn) && pawn.equipment.Primary != null && pawn.equipment.Primary.def.IsRangedWeapon)
			{
				scoredPositions = PositionsForRangedAttack();
				attackStyle = AttackStyle.Ranged;
				return;
			}

			currentVerb = pawn.meleeVerbs.TryGetMeleeVerb();
			if (currentVerb != null)
			{
				scoredPositions = PositionsForMeleeAttack();
				attackStyle = AttackStyle.Melee;
				return;
			}

			attackStyle = AttackStyle.None;
			scoredPositions = new HashSet<ScoredPosition>();
		}

		public Pawn FindNextTarget()
		{
			return Find.MapPawns.AllPawnsSpawned
				.Where(p => p.InAggroMentalState || p.HostileTo(pawn.Faction))
				.Where(p => p.Spawned == true && p.Destroyed == false && p.Downed == false && p.Dead == false)
				.OrderBy(p => (pawn.Faction == target.Faction ? 100000f : 0f) + p.Position.DistanceToSquared(pawn.Position))
				.FirstOrDefault();
		}

		public override void InitAction()
		{
			base.InitAction();
			target = TargetA.Thing as Pawn;
			workLocations = new HashSet<IntVec3>();
		}

		public override void Notify_PatherArrived()
		{
			base.Notify_PatherArrived();
			PathEnded(false);
		}

		public override void Notify_PatherFailed()
		{
			base.Notify_PatherFailed();
			PathEnded(true);
		}

		public bool TryShoot(Action callback)
		{
			currentVerb.castCompleteCallback?.Invoke();
			currentVerb.castCompleteCallback = delegate
			{
				currentVerb.castCompleteCallback = null;
				callback();
			};
			pawn.Drawer.rotator.FaceCell(target.Position);
			return currentVerb.TryStartCastOn(target, false, true);
		}

		public bool TryGoto(IntVec3 destination, Action<bool> callback)
		{
			bool canReserve = TryPathStart(destination);
			if (canReserve == false)
			{
				callback(false);
				return false;
			}
			pathEndCallback(false);
			pathEndCallback = callback;
			return true;
		}

		public bool TryPathStart(IntVec3 destination)
		{
			if (destination.IsValid && Find.Reservations.CanReserve(pawn, destination))
			{
				pathEndLocation = destination;
				Find.Reservations.Reserve(pawn, pathEndLocation);
				pawn.pather.StartPath(destination, PathEndMode.OnCell);
				return true;
			}
			return false;
		}

		public void PathEnded(bool failure)
		{
			if (pathEndLocation.IsValid)
			{
				if (Find.Reservations.ReservedBy(pathEndLocation, pawn))
					Find.Reservations.Release(pathEndLocation, pawn);
				pathEndCallback(failure);
				pathEndCallback = b => { };
			}
		}

		public override void TickAction()
		{
			CheckJobCancelling();
			UpdateColonistsAndEnemies();

			if (ValidTarget(target) == false)
			{
				target = FindNextTarget();

				if (ValidTarget(target) == false)
				{
					Controller.ClearDebugPositions();
					Find.PawnDestinationManager.UnreserveAllFor(pawn);
					EndJobWith(JobCondition.Succeeded);
					return;
				}

				UpdateColonistsAndEnemies();
			}

			if (pawn.Dead || pawn.Downed || pawn.HasAttachment(ThingDefOf.Fire) || badStates.Contains(pawn.MentalStateDef))
			{
				Controller.ClearDebugPositions();
				Find.PawnDestinationManager.UnreserveAllFor(pawn);
				EndJobWith(JobCondition.Incompletable);
				return;
			}

			if ((++tickCounter % 2) == 0)
			{
				UpdateVerbAndWorkLocations();
				if (currentVerb == null) return;

				if (Find.Selector.IsSelected(pawn)) Controller.AddDebugPositions(scoredPositions);
			}

			if (attackStyle == AttackStyle.Ranged)
			{
				// I haz cheez
				switch (doorCheese)
				{
					case DoorCheese.None:
						Building_Door door = Find.ThingGrid.ThingAt<Building_Door>(pawn.Position);
						if (door != null && beforeDoorPosition.IsValid && targetIsFleeing == false)
						{
							bool doorIsSafe = scoredPositions.Where(sp => sp.v == door.Position).FirstOrDefault() != null;
							if (doorIsSafe && CanShootTargetSafelyFrom(door.Position) && pawn.CanReserve(door.Position))
							{
								pawn.pather.StopDead();
								Action shootingEnded = delegate
								{
									Action<bool> backingEnded = success =>
									{
										if (doorCheesePosition.IsValid)
										{
											Find.Reservations.Release(doorCheesePosition, pawn);
											doorCheesePosition = IntVec3.Invalid;
										}
										doorCheese = DoorCheese.None;
									};

									pawn.pather.ResetToCurrentPosition();
									if (TryGoto(beforeDoorPosition, backingEnded))
									{
										doorCheese = DoorCheese.Hiding;
										state = AutoCombatState.Moving;
									}
									state = AutoCombatState.Ready;
								};

								if (TryShoot(shootingEnded))
								{
									if (doorCheesePosition.IsValid) Find.Reservations.Release(doorCheesePosition, pawn);

									doorCheesePosition = door.Position;
									Find.Reservations.Reserve(pawn, doorCheesePosition);
									doorCheese = DoorCheese.Shooting;
									state = AutoCombatState.Shooting;
									return;
								}
								state = AutoCombatState.Ready;
							}
							else
							{
								if (doorCheesePosition.IsValid)
								{
									Find.Reservations.Release(doorCheesePosition, pawn);
									doorCheesePosition = IntVec3.Invalid;
									state = AutoCombatState.Ready;
								}
							}
							break;
						}
						beforeDoorPosition = pawn.Position;
						break;

					case DoorCheese.Shooting:
						if (targetIsFleeing || scoredPositions.Where(sp => sp.v == doorCheesePosition).FirstOrDefault() == null)
						{
							Action<bool> backingEnded = success =>
							{
								if (doorCheesePosition.IsValid)
								{
									Find.Reservations.Release(doorCheesePosition, pawn);
									doorCheesePosition = IntVec3.Invalid;
								}
								doorCheese = DoorCheese.None;
							};

							if (TryGoto(beforeDoorPosition, backingEnded))
							{
								doorCheese = DoorCheese.Hiding;
								state = AutoCombatState.Moving;
							}
						}
						return;

					case DoorCheese.Hiding:
						if (pawn.Position != beforeDoorPosition) return;
						if (doorCheesePosition.IsValid)
						{
							Find.Reservations.Release(doorCheesePosition, pawn);
							doorCheesePosition = IntVec3.Invalid;
						}
						doorCheese = DoorCheese.None;
						break;
				}
			}

			// no cells, no best pos - we are too far away. let's get closer
			Func<ScoredPosition, bool> reserve = sp => false;
			if (attackStyle == AttackStyle.Ranged) reserve = sp => pawn.CanReserveAndReach(sp.v, PathEndMode.OnCell, DangerUtility.NormalMaxDanger(pawn));
			if (attackStyle == AttackStyle.Melee) reserve = sp => pawn.CanReserveAndReach(sp.v, PathEndMode.Touch, Danger.Deadly);
			ScoredPosition bestPos = scoredPositions.Where(reserve).OrderByDescending(sp => sp.score).FirstOrDefault();
			if (bestPos == null)
			{
				// no reserve, it's too far and during the journey lots of thing will happen
				pawn.pather.StartPath(target.Position, PathEndMode.Touch);
				state = AutoCombatState.Advancing;
				return;
			}

			// we are at the perfect shooting spot - let's shoot 'em down
			if (attackStyle == AttackStyle.Ranged && pawn.Position == bestPos.v)
			{
				if (state != AutoCombatState.Shooting)
				{
					if (currentVerb.CanHitTargetFrom(pawn.Position, target))
					{
						bool success = TryShoot(() => state = AutoCombatState.Ready);
						if (success)
						{
							state = AutoCombatState.Shooting;
							return;
						}
						else
							state = AutoCombatState.Ready;
					}
				}
				else
				{

				}
			}

			// if we're a brawler and we're close enough, hit 'em
			if (attackStyle == AttackStyle.Melee && GenAdj.AdjacentTo8WayOrInside(pawn.Position, target.Position))
			{
				Toils_Misc.ThrowColonistAttackingMote(TargetIndex.A);

				currentVerb.castCompleteCallback = delegate
				{
					state = AutoCombatState.Ready;
				};
				bool success = pawn.meleeVerbs.TryMeleeAttack(target, currentVerb, true);
				if (success) state = AutoCombatState.Hitting;
				return;
			}

			// otherwise: move
			if (pawn.pather.Destination != bestPos.v)
			{
				if (TryPathStart(bestPos.v))
				{
					state = AutoCombatState.Moving;
				}
				else
				{
					IntVec3 successPos = GenAdjFast.AdjacentCells8Way(bestPos.v)
						.OrderBy(v => (v - pawn.Position).LengthManhattan)
						.Where(v => TryPathStart(v)).FirstOrDefault();
					if (successPos != default(IntVec3))
					{
						state = AutoCombatState.Moving;
					}
				}
			}

			// nothing to do
		}

		public override string GetReport()
		{
			string s1 = target == null ? "" : " (" + target.NameStringShort + ")";
			string s2 = target == null ? "" : "at " + target.NameStringShort;
			string name = target == null ? "target" : target.NameStringShort;
			string status = (GetPrefix() + state).Translate(name);
			return (GetPrefix() + "Report").Translate(status);
		}

		protected override IEnumerable<Toil> MakeNewToils()
		{
			Toil toil = base.MakeNewToils().First();
			yield return toil;
		}
	}
}
