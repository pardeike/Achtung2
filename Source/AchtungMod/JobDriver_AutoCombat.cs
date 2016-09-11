using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI;

namespace AchtungMod
{
	class JobDriver_AutoCombat : JobDriver_Thoroughly
	{
		enum State
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

		Pawn target;
		Verb currentVerb = null;
		HashSet<ScoredPosition> scoredPositions = new HashSet<ScoredPosition>();
		AttackStyle attackStyle = AttackStyle.None;
		State state = State.Moving;
		int tickCounter = 0;

		public override string GetPrefix()
		{
			return "AutoCombat";
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.LookValue<State>(ref this.state, "state", State.Moving, false);
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
					UpdateWorkLocations();
					return currentVerb != null && (attackStyle == AttackStyle.Ranged || scoredPositions.Count() > 0);
				});
		}

		public static Dictionary<float, HashSet<IntVec3>> circles = null;
		public static List<IntVec3> GetCircle(float radius)
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
			return cells.ToList();
		}

		public void ApplyCosts(HashSet<ScoredPosition> result, Func<ScoredPosition, float> func)
		{
			result
				.Select(sp => new KeyValuePair<ScoredPosition, float>(sp, func(sp)))
				.ToList().ForEach(kv => kv.Key.Add(kv.Value));
		}

		public void ApplyCosts(HashSet<ScoredPosition> result, Func<ScoredPosition, float> func, float factorMin, float factorMax)
		{
			IEnumerable<KeyValuePair<ScoredPosition, float>> costs = result.Select(sp => new KeyValuePair<ScoredPosition, float>(sp, func(sp)));
			if (costs.Count() > 0)
			{
				float minCosts = costs.Min(kv => kv.Value);
				float maxCosts = costs.Max(kv => kv.Value);
				costs.ToList().ForEach(kv => kv.Key.Add(GenMath.LerpDouble(minCosts, maxCosts, factorMin, factorMax, kv.Value)));
			}
		}

		public HashSet<IntVec3> GetAggroArea(List<Pawn> enemies, float minimumSafeDistance = 0f)
		{
			HashSet<IntVec3> result = new HashSet<IntVec3>();
			enemies.ForEach(e =>
			{
				if (e.equipment != null && e.equipment.PrimaryEq != null)
				{
					Verb enemyVerb = e.equipment.PrimaryEq.PrimaryVerb;
					if (enemyVerb != null && enemyVerb.verbProps != null && enemyVerb.verbProps.projectileDef != null)
					{
						List<Projectile> projectiles = Find.ListerThings.ThingsOfDef(enemyVerb.verbProps.projectileDef).Cast<Projectile>().ToList();
						projectiles.ForEach(p => result.UnionWith(GetCircle(p.def.projectile.explosionRadius + 1f).Select(v => p.Position + v)));
					}
				}
				if (minimumSafeDistance > 0f) result.UnionWith(GetCircle(minimumSafeDistance).Select(v => e.Position + v));

				/*
				 * A first draft to mark shooting lines as aggro
				 * 
				Find.MapPawns.PawnsInFaction(pawn.Faction).Where(p => p != pawn).ToList()
					.ForEach(p =>
					{
						Verb v = p.TryGetAttackVerb(false);
						if (v != null && v.IsStillUsableBy(pawn) && pawn.equipment.Primary != null && pawn.equipment.Primary.def.IsRangedWeapon)
						{
							FieldInfo field = typeof(Verb).GetField("currentTarget", BindingFlags.NonPublic | BindingFlags.Instance);
							TargetInfo pt = (TargetInfo)field.GetValue(v);
							ShootLine line;
							v.TryFindShootLineFromTo(p.Position, pt, out line);
							Vector3 src = line.Source.ToVector3();
							Vector3 dst = line.Dest.ToVector3();
							for (int x = line.Source.x; x <= line.Dest.x; x += Math.Sign(line.Dest.x - line.Source.x))
							{
								int z = (int)(src.z + (dst.z - src.z) * (float)(x - line.Source.x) / (line.Dest.x - line.Source.x));
								result.Add(new IntVec3(x, 0, z));
							}
							for (int z = line.Source.z; z <= line.Dest.z; z += Math.Sign(line.Dest.z - line.Source.z))
							{
								int x = (int)(src.x + (dst.x - src.x) * (float)(z - line.Source.z) / (line.Dest.z - line.Source.z));
								result.Add(new IntVec3(x, 0, z));
							}
						}
					});
				*/
			});
			return result;
		}

		public HashSet<ScoredPosition> PositionsForRangedAttack(List<Pawn> enemies, Verb verb)
		{
			bool targetIsFleeing = target.MentalStateDef != null && target.MentalStateDef.category == MentalStateCategory.Panic;

			float range = verb.verbProps.range;
			if (targetIsFleeing) range = range * 0.75f;
			float rangeSquared = range * range;
			float minimumDistance = target.GetRoom() == null ? range / 4f : 3f;

			HashSet<IntVec3> aggroArea = GetAggroArea(enemies, minimumDistance);
			HashSet<ScoredPosition> result = new HashSet<ScoredPosition>(
				 GetCircle(range)
				.Where(v =>
				{
					IntVec3 cell = target.Position + v;
					return cell.InBounds() &&
						(targetIsFleeing || aggroArea.Contains(cell) == false) &&
						pawn.Position.DistanceToSquared(cell) <= rangeSquared &&
						pawn.CanReserveAndReach(cell, PathEndMode.OnCell, DangerUtility.NormalMaxDanger(pawn)) &&
						GenSight.LineOfSight(cell, target.Position, false);
				})
				.Select(v => new ScoredPosition(target.Position + v)));

			if (result.Count() == 0)
			{
				aggroArea = GetAggroArea(enemies);
				result = new HashSet<ScoredPosition>(
				 GetCircle(range)
				.Where(v =>
				{
					IntVec3 cell = target.Position + v;
					return cell.InBounds() &&
						aggroArea.Contains(cell) == false &&
						pawn.Position.DistanceToSquared(cell) <= rangeSquared &&
						pawn.CanReserveAndReach(cell, PathEndMode.OnCell, DangerUtility.NormalMaxDanger(pawn)) &&
						GenSight.LineOfSight(cell, target.Position, false);
				})
				.Select(v => new ScoredPosition(target.Position + v)));
			}

			float pawnClosenessFactor = 5f;
			float targetClosenessFactor = target.InAggroMentalState ? 0.5f : 5f;
			float hitChanceFactor = 1f;
			float coverFactor = enemies.Count() == 0 ? 0f : 10f / (float)enemies.Count();

			if (targetIsFleeing == false)
			{
				ApplyCosts(result, sp => pawn.Position.DistanceToSquared(sp.v), pawnClosenessFactor, 0f);
				ApplyCosts(result, sp => target.Position.DistanceToSquared(sp.v), 0f, targetClosenessFactor);
				ApplyCosts(result, sp => hitChanceFactor * -1f * CoverUtility.CalculateOverallBlockChance(target.Position, sp.v));
				enemies.ForEach(e => ApplyCosts(result, sp => coverFactor * CoverUtility.CalculateOverallBlockChance(sp.v, e.Position)));
			}

			if (Find.Selector.IsSelected(pawn)) Controller.AddDebugPositions(result);

			return result;
		}

		public HashSet<ScoredPosition> PositionsForMeleeAttack(List<Pawn> enemies, Verb verb)
		{
			HashSet<IntVec3> aggroArea = GetAggroArea(enemies);
			HashSet<ScoredPosition> result = new HashSet<ScoredPosition>();

			HashSet<IntVec3> attackArea = new HashSet<IntVec3>();
			enemies.ForEach(e => attackArea.UnionWith(GetCircle(5f).Select(v => e.Position + v)));
			attackArea.ExceptWith(GetAggroArea(enemies));
			enemies.ForEach(e => { if (e != target) attackArea.ExceptWith(GetCircle(2f).Select(v => e.Position + v)); });

			result = new HashSet<ScoredPosition>(attackArea
				.Where(cell =>
					cell.InBounds() &&
					pawn.CanReserveAndReach(cell, PathEndMode.OnCell, Danger.Deadly) &&
					GenSight.LineOfSight(cell, target.Position, false))
				.Select(v => new ScoredPosition(v)));

			float targetClosenessFactor = 10f;
			float coverFactor = enemies.Count() == 0 ? 0f : 0.5f / (float)enemies.Count();

			ApplyCosts(result, sp => target.Position.DistanceToSquared(sp.v), targetClosenessFactor, 0f);
			enemies.ForEach(e => { if (e != target) ApplyCosts(result, sp => coverFactor * CoverUtility.CalculateOverallBlockChance(sp.v, e.Position)); });

			if (Find.Selector.IsSelected(pawn)) Controller.AddDebugPositions(result);

			return result;
		}

		public override void UpdateWorkLocations()
		{
			List<Pawn> enemies = Find.MapPawns.AllPawnsSpawned
				.Where(p => p.InAggroMentalState || p.HostileTo(pawn.Faction))
				.Where(p => p.Spawned == true && p.Destroyed == false && p.Downed == false && p.Dead == false)
				.ToList();

			currentVerb = pawn.TryGetAttackVerb(false);
			if (currentVerb != null && currentVerb.IsStillUsableBy(pawn) && pawn.equipment.Primary != null && pawn.equipment.Primary.def.IsRangedWeapon)
			{
				scoredPositions = PositionsForRangedAttack(enemies, currentVerb);
				attackStyle = AttackStyle.Ranged;
				return;
			}

			currentVerb = pawn.meleeVerbs.TryGetMeleeVerb();
			if (currentVerb != null)
			{
				scoredPositions = PositionsForMeleeAttack(enemies, currentVerb);
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
			if (ValidTarget(target) == false)
			{
				target = FindNextTarget();
				if (ValidTarget(target) == false)
				{
					EndJobWith(JobCondition.Succeeded);
					return;
				}
			}

			List<MentalStateDef> badStates = new List<MentalStateDef> {
				MentalStateDefOf.Berserk,
				MentalStateDefOf.Manhunter,
				MentalStateDefOf.ManhunterPermanent,
				MentalStateDefOf.PanicFlee,
				MentalStateDefOf.WanderPsychotic
			};
			if (pawn.Dead || pawn.Downed || pawn.HasAttachment(ThingDefOf.Fire) || badStates.Contains(pawn.MentalStateDef))
			{
				EndJobWith(JobCondition.Incompletable);
				return;
			}

			if (--tickCounter < 0)
			{
				tickCounter = GenTicks.TicksPerRealSecond / 10;
				UpdateWorkLocations();
			}

			ScoredPosition bestPos = scoredPositions.OrderByDescending(sp => sp.score).FirstOrDefault();
			if (bestPos != null)
			{
				if (currentVerb != null)
				{
					if (attackStyle == AttackStyle.Ranged && pawn.Position == bestPos.v)
					{
						if (state == State.Moving || state == State.Ready)
						{
							currentVerb.castCompleteCallback = delegate
							{
								state = State.Ready;
							};
							bool success = currentVerb.TryStartCastOn(target, false, true);
							if (success) state = State.Shooting;
						}
					}
					else if (attackStyle == AttackStyle.Melee && GenAdj.AdjacentTo8WayOrInside(pawn.Position, target.Position))
					{
						Toils_Misc.ThrowColonistAttackingMote(TargetIndex.A);
						bool success = pawn.meleeVerbs.TryMeleeAttack(target, currentVerb, true);
						if (success) state = State.Hitting;
					}
					else
					{
						Find.Reservations.Reserve(pawn, bestPos.v);
						pawn.pather.StartPath(bestPos.v, PathEndMode.OnCell);
						state = State.Moving;
					}
				}
			}
			else
			{
				if (state != State.Advancing)
				{
					Find.Reservations.Reserve(pawn, target.Position);
					pawn.pather.StartPath(target.Position, PathEndMode.OnCell);
					state = State.Advancing;
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
