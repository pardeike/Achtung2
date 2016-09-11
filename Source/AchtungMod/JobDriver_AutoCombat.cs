using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace AchtungMod
{
	class JobDriver_AutoCombat : JobDriver_Thoroughly
	{
		enum AutoCombatState
		{
			Advancing,
			Moving,
			Shooting,
			Hitting,
			Ready
		}

		enum AttackStyle
		{
			None,
			Melee,
			Ranged
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
		AutoCombatState state = AutoCombatState.Moving;
		AttackStyle attackStyle = AttackStyle.None;
		Pawn target;
		int tickCounter = 0;

		// volatile
		Verb currentVerb = null;
		IEnumerable<Pawn> currentEnemies = new List<Pawn>();
		HashSet<ScoredPosition> scoredPositions = new HashSet<ScoredPosition>();

		public override string GetPrefix()
		{
			return "AutoCombat";
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.LookValue<AutoCombatState>(ref this.state, "state", AutoCombatState.Moving, false);
			Scribe_Values.LookValue<AttackStyle>(ref this.attackStyle, "attackStyle", AttackStyle.None, false);
			Scribe_References.LookReference<Pawn>(ref target, "target");
			Scribe_Values.LookValue<int>(ref this.tickCounter, "tickCounter", 0, false);
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
					UpdateEnemies();
					UpdateWorkLocations();
					return currentVerb != null && (attackStyle == AttackStyle.Ranged || scoredPositions.Count() > 0);
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

		public HashSet<IntVec3> GetDangerArea(float minimumSafeDistance, bool onlyProjectiles)
		{
			HashSet<IntVec3> result = new HashSet<IntVec3>();
			Controller.projectiles.Do(p =>
			{
				Projectile pj = p.Key;
				ProjectileInfo pi = p.Value;
				if (onlyProjectiles == false) result.UnionWith(GetCircle(pj.def.projectile.explosionRadius + 0.5f).Select(vec => pi.targ.Cell + vec));
				result.UnionWith(Tools.CellsBetween(pi.origin.ToIntVec3(), pi.targ.Cell));
			});
			if (minimumSafeDistance > 0f && onlyProjectiles == false) currentEnemies.Do(p => result.UnionWith(GetCircle(minimumSafeDistance).Select(vec => p.Position + vec)));
			return result;
		}

		public HashSet<ScoredPosition> PositionsForRangedAttack()
		{
			bool targetIsFleeing = target.MentalStateDef != null && target.MentalStateDef.category == MentalStateCategory.Panic;

			float range = currentVerb.verbProps.range;
			if (targetIsFleeing) range = range * 0.75f;
			float rangeSquared = range * range;
			float minimumDistance = target.GetRoom() == null ? range / 4f : 3f;

			HashSet<IntVec3> danger = GetDangerArea(minimumDistance, targetIsFleeing);
			HashSet<ScoredPosition> result = new HashSet<ScoredPosition>(
				 GetCircle(range)
				.Where(v =>
				{
					IntVec3 cell = target.Position + v;
					if (targetIsFleeing)
					{
						return cell.InBounds() &&
						danger.Contains(cell) == false &&
						GenSight.LineOfSight(cell, target.Position, false);
					}
					return cell.InBounds() &&
						danger.Contains(cell) == false &&
						pawn.Position.DistanceToSquared(cell) <= rangeSquared &&
						GenSight.LineOfSight(cell, target.Position, false);
				})
				.Select(v => new ScoredPosition(target.Position + v)));

			if (result.Count() == 0 && targetIsFleeing == false)
			{
				danger = GetDangerArea(0f, targetIsFleeing);
				result = new HashSet<ScoredPosition>(
				 GetCircle(range)
				.Where(v =>
				{
					IntVec3 cell = target.Position + v;
					return cell.InBounds() &&
						(targetIsFleeing || danger.Contains(cell) == false) &&
						pawn.Position.DistanceToSquared(cell) <= rangeSquared &&
						GenSight.LineOfSight(cell, target.Position, false);
				})
				.Select(v => new ScoredPosition(target.Position + v)));
			}

			float pawnClosenessFactor = 5f;
			float targetClosenessFactor = target.InAggroMentalState ? 0.5f : 5f;
			float hitChanceFactor = 1f;
			float coverFactor = currentEnemies.Count() == 0 ? 0f : 10f / (float)currentEnemies.Count();

			if (targetIsFleeing == false)
			{
				Tools.ApplyScoreLerped(result, sp => target.Position.DistanceToSquared(sp.v), 0f, targetClosenessFactor);
				currentEnemies.Do(e => Tools.ApplyScore(result, sp => coverFactor * CoverUtility.CalculateOverallBlockChance(sp.v, e.Position)));
			}
			Tools.ApplyScoreLerped(result, sp => pawn.Position.DistanceToSquared(sp.v), pawnClosenessFactor, 0f);
			Tools.ApplyScore(result, sp => hitChanceFactor * -1f * CoverUtility.CalculateOverallBlockChance(target.Position, sp.v));

			return result;
		}

		public HashSet<ScoredPosition> PositionsForMeleeAttack()
		{
			HashSet<ScoredPosition> result = new HashSet<ScoredPosition>();

			bool targetIsFleeing = target.MentalStateDef != null && target.MentalStateDef.category == MentalStateCategory.Panic;
			HashSet<IntVec3> attackArea = new HashSet<IntVec3>();
			currentEnemies.Do(e => attackArea.UnionWith(GetCircle(7f).Select(v => e.Position + v)));
			currentEnemies.DoIf(e => (e != target), e => attackArea.ExceptWith(GetCircle(2f).Select(v => e.Position + v)));
			HashSet<IntVec3> danger = GetDangerArea(0f, targetIsFleeing);
			int n = attackArea.Count();
			attackArea.ExceptWith(danger);

			result = new HashSet<ScoredPosition>(attackArea
				.Where(cell =>
					cell.InBounds() &&
					GenSight.LineOfSight(cell, target.Position, false))
				.Select(v => new ScoredPosition(v)));

			float targetClosenessFactor = 20f;
			float enemyDistanceFactor = currentEnemies.Count() <= 1 ? 0f : 1f / (float)(currentEnemies.Count() - 1);
			float coverFactor = currentEnemies.Count() <= 1 ? 0f : 1f / (float)(currentEnemies.Count() - 1);

			Tools.ApplyScoreLerped(result, sp => 1f / Tools.PawnCellDistance(target, sp.v), 0f, targetClosenessFactor);
			currentEnemies.DoIf(e => (e != target), e => Tools.ApplyScoreLerped(result, sp => Tools.PawnCellDistance(e, sp.v), 0f, enemyDistanceFactor));
			currentEnemies.DoIf(e => (e != target), e => Tools.ApplyScore(result, sp => coverFactor * CoverUtility.CalculateOverallBlockChance(sp.v, e.Position)));

			return result;
		}

		public void UpdateEnemies()
		{
			currentEnemies = Find.MapPawns.AllPawnsSpawned
				.Where(p => p.InAggroMentalState || p.HostileTo(pawn.Faction))
				.Where(p => p.Spawned == true && p.Destroyed == false && p.Downed == false && p.Dead == false);
		}

		public override void UpdateWorkLocations()
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

		public override void TickAction()
		{
			CheckJobCancelling();
			UpdateEnemies();

			if (ValidTarget(target) == false)
			{
				target = FindNextTarget();
				if (ValidTarget(target) == false)
				{
					EndJobWith(JobCondition.Succeeded);
					return;
				}
			}

			if (pawn.Dead || pawn.Downed || pawn.HasAttachment(ThingDefOf.Fire) || badStates.Contains(pawn.MentalStateDef))
			{
				EndJobWith(JobCondition.Incompletable);
				return;
			}

			if (--tickCounter < 0)
			{
				tickCounter = GenTicks.TicksPerRealSecond / 6;
				UpdateWorkLocations();
			}

			Func<ScoredPosition, bool> reserve = sp => false;
			if (attackStyle == AttackStyle.Ranged) reserve = sp => pawn.CanReserveAndReach(sp.v, PathEndMode.OnCell, DangerUtility.NormalMaxDanger(pawn));
			if (attackStyle == AttackStyle.Melee) reserve = sp => pawn.CanReserveAndReach(sp.v, PathEndMode.Touch, Danger.Deadly);
			if (Find.Selector.IsSelected(pawn)) Controller.AddDebugPositions(scoredPositions);

			ScoredPosition bestPos = scoredPositions.Where(reserve).OrderByDescending(sp => sp.score).FirstOrDefault();
			if (bestPos != null)
			{
				if (currentVerb != null)
				{
					if (attackStyle == AttackStyle.Ranged && pawn.Position == bestPos.v)
					{
						if (state == AutoCombatState.Moving || state == AutoCombatState.Ready)
						{
							currentVerb.castCompleteCallback = delegate
							{
								state = AutoCombatState.Ready;
							};
							bool success = currentVerb.TryStartCastOn(target, false, true);
							if (success) state = AutoCombatState.Shooting;
						}
					}
					else if (attackStyle == AttackStyle.Melee && GenAdj.AdjacentTo8WayOrInside(pawn.Position, target.Position))
					{
						Toils_Misc.ThrowColonistAttackingMote(TargetIndex.A);
						bool success = pawn.meleeVerbs.TryMeleeAttack(target, currentVerb, true);
						if (success) state = AutoCombatState.Hitting;
					}
					else
					{
						Find.Reservations.Reserve(pawn, bestPos.v);
						pawn.pather.StartPath(bestPos.v, PathEndMode.OnCell);
						state = AutoCombatState.Moving;
					}
				}
			}
			else
			{
				if (state != AutoCombatState.Advancing)
				{
					Find.Reservations.Reserve(pawn, target.Position);
					pawn.pather.StartPath(target.Position, PathEndMode.Touch);
					state = AutoCombatState.Advancing;
				}
			}
		}

		public override string GetReport()
		{
			String s1 = target == null ? "" : " (" + target.NameStringShort + ")";
			String s2 = target == null ? "" : "at " + target.NameStringShort;
			String name = target == null ? "target" : target.NameStringShort;
			String status = (GetPrefix() + state).Translate(name);
			return (GetPrefix() + "Report").Translate(status);
		}

		protected override IEnumerable<Toil> MakeNewToils()
		{
			Toil toil = base.MakeNewToils().First();
			yield return toil;
		}
	}
}
