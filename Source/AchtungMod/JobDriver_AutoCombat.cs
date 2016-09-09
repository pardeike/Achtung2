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
		enum State
		{
			Advancing,
			Moving,
			Shooting,
			Ready
		}

		Pawn target;
		HashSet<ScoredPosition> scoredPositions = new HashSet<ScoredPosition>();
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
			Scribe_References.LookReference<Pawn>(ref target, "target");
		}

		public bool ValidTarget(TargetInfo info)
		{
			if (info == null) return false;
			Thing t = info.Thing;
			if (t == null) return false;



			Pawn p = t as Pawn;
			if (p != null && p.Downed == false && p.Dead == false && p.Destroyed == false && p.Position.InBounds()) return true;

			return false;
		}

		public override IEnumerable<TargetInfo> CanStart(Pawn pawn, Vector3 clickPos)
		{
			base.CanStart(pawn, clickPos);
			return GenUI.TargetsAt(clickPos, TargetingParameters.ForAttackHostile(), true).Where(ValidTarget);
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

		public override void UpdateWorkLocations()
		{
			// Future code example for real cost-pathing. For now, not used because the way it is
			// handled, executed and slow
			//
			// pawn.equipment.TryStartAttack
			// pawn.pather.StartPath(target, PathEndMode.OnCell);
			// pawn.Reserve(target);
			// pawn.ThreatDisabled();

			Verb verb = pawn.TryGetAttackVerb(false);
			scoredPositions = new HashSet<ScoredPosition>();
			if (verb != null)
			{
				float range = verb.verbProps.range;
				float rangeSquared = range * range;
				float minimumDistance = range / 3f;
				float minimumDistanceSquared = minimumDistance * minimumDistance;

				HashSet<IntVec3> aggroArea = new HashSet<IntVec3>();
				Find.MapPawns.AllPawnsSpawned
					.Where(p => p.InAggroMentalState || p.HostileTo(pawn.Faction))
					.ToList().ForEach(p => aggroArea.UnionWith(GetCircle(minimumDistance).Select(v => p.Position + v)));

				List<IntVec3> circleCells = GetCircle(range);
				scoredPositions = new HashSet<ScoredPosition>(circleCells
					.Where(v =>
					{
						IntVec3 cell = target.Position + v;
						return cell.InBounds() &&
							pawn.CanReserve(cell, 1) &&
							aggroArea.Contains(cell) == false &&
							pawn.Position.DistanceToSquared(cell) <= rangeSquared &&
							GenSight.LineOfSight(cell, target.Position, false);
					})
					.Select(v => new ScoredPosition(target.Position + v, 100f)));

				bool isAggro = target.InAggroMentalState || target.HostileTo(pawn.Faction);

				float pawnClosenessFactor = 5f;
				float targetClosenessFactor = isAggro ? 0.5f : 5f;
				float hitChanceFactor = 1f;
				float coverFactor = 10f;

				float minCosts = 999999f;
				float maxCosts = -999999f;
				scoredPositions.ToList().ForEach(sp =>
				{
					float costs = pawn.Position.DistanceToSquared(sp.v); // PathFinder.FindPath(pawn.Position, sp.v, pawn).TotalCost;
					minCosts = Math.Min(minCosts, costs);
					maxCosts = Math.Max(maxCosts, costs);
				});
				scoredPositions.ToList().ForEach(sp => sp.Add(GenMath.LerpDouble(minCosts, maxCosts, pawnClosenessFactor, 0f, pawn.Position.DistanceToSquared(sp.v))));

				minCosts = 999999f;
				maxCosts = -999999f;
				scoredPositions.ToList().ForEach(sp =>
				{
					float costs = target.Position.DistanceToSquared(sp.v); // PathFinder.FindPath(target.Position, sp.v, target).TotalCost;
					minCosts = Math.Min(minCosts, costs);
					maxCosts = Math.Max(maxCosts, costs);
				});
				scoredPositions.ToList().ForEach(sp => sp.Add(GenMath.LerpDouble(minCosts, maxCosts, 0f, targetClosenessFactor, target.Position.DistanceToSquared(sp.v))));

				scoredPositions.ToList().ForEach(sp => sp.Add(hitChanceFactor * -1f * CoverUtility.CalculateOverallBlockChance(target.Position, sp.v)));

				if (isAggro)
				{
					scoredPositions.ToList().ForEach(sp => sp.Add(coverFactor * CoverUtility.CalculateOverallBlockChance(sp.v, target.Position)));
				}

				if (Find.Selector.IsSelected(pawn)) Controller.SetDebugPositions(scoredPositions);
				if (Find.Selector.SelectedObjects.Count() == 0) Controller.SetDebugPositions(new List<ScoredPosition>());
			}
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
				EndJobWith(JobCondition.Succeeded);
				Controller.SetDebugPositions(new List<ScoredPosition>());
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
				Verb verb = pawn.TryGetAttackVerb(false);
				if (verb != null)
				{
					if (pawn.Position == bestPos.v)
					{
						if (state == State.Moving || state == State.Ready)
						{
							verb.castCompleteCallback = delegate
							{
								state = State.Ready;
							};
							bool success = verb.TryStartCastOn(TargetA, false, true);
							if (success) state = State.Shooting;
						}
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