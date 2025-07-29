using RimWorld;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace AchtungMod;

public class JobDriver_TacticalApproach : JobDriver
{
	public static string GetPrefix() => "TacticalApproach";
	public static string GetLabel() => (GetPrefix() + "Label").Translate();

	public static JobDef jobDef;
	public static JobDef MakeDef()
	{
		jobDef = new JobDef
		{
			driverClass = typeof(JobDriver_TacticalApproach),
			collideWithPawns = false,
			defName = GetPrefix(),
			label = GetLabel(),
			reportString = $"{GetPrefix()}InfoText".Translate(),
			description = $"{GetPrefix()}Description".Translate(),
			playerInterruptible = true,
			checkOverrideOnDamage = CheckJobOverrideOnDamageMode.Always,
			suspendable = true,
			alwaysShowWeapon = true,
			neverShowWeapon = false,
			casualInterruptible = true
		};
		return jobDef;
	}

	static readonly IntVec3[] neighborOffsets =
	[
		new IntVec3(0, 0, 1),
		new IntVec3(1, 0, 0),
		new IntVec3(0, 0, -1),
		new IntVec3(-1, 0, 0)
	];

	public int movingDirection = 0;
	public IntVec3 lastPosition = IntVec3.Invalid;
	public IntVec3 lastEnemyPosition = IntVec3.Invalid;
	public HashSet<IntVec3> cachedCells = [];
	public HashSet<IntVec3> usedAttackPositions = [];

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_Values.Look(ref movingDirection, "movingDirection", 0);
		if (Scribe.mode == LoadSaveMode.PostLoadInit)
		{
			movingDirection = 0;
			cachedCells ??= [];
			cachedCells.Clear();
			usedAttackPositions ??= [];
			usedAttackPositions.Clear();
			lastPosition = IntVec3.Invalid;
			lastEnemyPosition = IntVec3.Invalid;
		}
	}

	public Pawn Enemy => TargetThingA as Pawn;

	public override string GetReport()
	{
		if (Enemy != null)
			return JobUtility.GetResolvedJobReport(job.def.reportString, Enemy);
		return base.GetReport();
	}

	public override bool TryMakePreToilReservations(bool errorOnFailed)
		=> pawn.Reserve(Enemy, job, 1, -1, null, errorOnFailed, false);

	private bool EnemyFinished()
	{
		var enemy = Enemy;
		if (enemy == null) return true;
		if (enemy.Downed) return true;
		if (enemy.Dead) return true;
		if (enemy.Destroyed) return true;
		return false;
	}

	public HashSet<IntVec3> UpdateCells()
	{
		var pos = pawn.Position;
		var aim = Enemy.Position;
		if (lastPosition == pos && lastEnemyPosition == aim) return cachedCells;
		if (lastEnemyPosition != aim) usedAttackPositions.Clear();
		lastPosition = pos;
		lastEnemyPosition = aim;
		var radius = Enemy.CurrentEffectiveVerb.EffectiveRange;
		var optimalRangeSquared = pawn.CurrentEffectiveVerb.EffectiveRange * 0.8f * (pawn.CurrentEffectiveVerb.verbProps.range * 0.8f);
		var map = pawn.Map;
		var cells = Visibility.GetShootingCells(pawn, Enemy);
		var interestingCells = cells.Where(cell =>
			neighborOffsets.Count(o => cells.Contains(cell + o)) < 3
			&& cell.CanBeSeenOver(map)
			&& GenSight.LineOfSight(aim, cell, map, true) == false
		);
		Controller.dangerGrids[pawn] = [.. cells.Except(interestingCells)];
		cachedCells = interestingCells.Any() ? [.. interestingCells] : cells;
		return cachedCells;
	}

	private void StopShooting()
	{
		var stance_Warmup = pawn.stances.curStance as Stance_Warmup;
		if (stance_Warmup != null)
			pawn.stances.CancelBusyStanceSoft();
		if (pawn.CurJob != null && pawn.CurJob.def == JobDefOf.AttackStatic)
			pawn.jobs.EndCurrentJob(JobCondition.Incompletable, true, true);
	}

	private void MoveAway()
	{
		var radius = Enemy.CurrentEffectiveVerb.EffectiveRange;
		var optimalRangeSquared = pawn.CurrentEffectiveVerb.EffectiveRange * 0.8f * (pawn.CurrentEffectiveVerb.verbProps.range * 0.8f);
		var pos = pawn.Position;
		var map = pawn.Map;
		var cells = Visibility.GetShootingCells(pawn, Enemy);
		var traverseParms = TraverseParms.For(pawn);
		var coverPos = GenRadial.RadialCellsAround(pos, radius, false)
			.First(cell => cells.Contains(cell) == false && usedAttackPositions.Contains(cell) == false && map.reachability.CanReach(pos, cell, PathEndMode.OnCell, traverseParms));

		if (coverPos.IsValid == false)
		{
			Log.Warning($"-> {pawn} cannot find cover to retreat to, staying put");
			movingDirection = 0;
			return;
		}

		_ = usedAttackPositions.Add(coverPos);

		StopShooting();
		pawn.pather.StopDead();

		Log.Warning($"-> {pawn} is retreating to {coverPos}");
		job.SetTarget(TargetIndex.B, coverPos);
		movingDirection = -1;
		pawn.pather.StartPath(coverPos, PathEndMode.OnCell);
	}

	private void MoveCloser()
	{
		var attackPos = CanFindAttackPosition();
		if (attackPos.IsValid == false)
		{
			Log.Warning($"-> {pawn} cannot find an attack position");
			movingDirection = 0;
			return;
		}

		StopShooting();
		pawn.pather.StopDead();

		Log.Warning($"-> {pawn} is moving to attack position {attackPos}");
		job.SetTarget(TargetIndex.B, attackPos);
		movingDirection = 1;
		pawn.pather.StartPath(attackPos, PathEndMode.OnCell);
	}

	public override IEnumerable<Toil> MakeNewToils()
	{
		yield return new Toil()
		{
			initAction = () =>
			{
				movingDirection = 0;
				lastPosition = IntVec3.Invalid;
				lastEnemyPosition = IntVec3.Invalid;
				cachedCells = [];
				usedAttackPositions = [];
				Log.Warning($"### {pawn} tactical attack started");
			},
			finishActions = [
				() =>
				{
					Log.Warning($"### {pawn} tactical attack done");
					Controller.dangerGrids[pawn]?.Clear();
					_ = Controller.dangerGrids.Remove(pawn);
				}
			],
			tickAction = () =>
			{
				//var sw = Stopwatch.StartNew();
				//try
				//{
				if (EnemyFinished())
				{
					Log.Warning($"-> {pawn} enemy finished, ending job");
					EndJobWith(JobCondition.Succeeded);
					return;
				}

				var verb = pawn.CurrentEffectiveVerb;
				if (verb == null || verb.IsStillUsableBy(pawn) == false)
				{
					Log.Warning($"-> {pawn} attack verb is not usable, ending job");
					EndJobWith(JobCondition.Incompletable);
					return;
				}

				_ = UpdateCells();
				var isMoving = pawn.pather.Moving;
				var busyStance = pawn.stances.curStance as Stance_Busy;
				var isAttacking = busyStance != null && busyStance.focusTarg == TargetA;

				var inDanger = UnderAttack() || TooClose();
				if ((movingDirection != -1 || isMoving == false) && inDanger)
				{
					MoveAway();
					return;
				}
				else if ((movingDirection != 1 || isMoving == false) && TooFar())
				{
					MoveCloser();
					return;
				}

				var canHit = verb.CanHitTarget(Enemy);
				var s = $"move={isMoving} attack={isAttacking} danger={inDanger} stance={pawn.stances.curStance} hit={canHit} dir={movingDirection}";
				if (jobDef.reportString != s)
					jobDef.reportString = s;

				if (inDanger) // safety
					return;

				if (canHit && isAttacking == false)
				{
					if (isMoving)
					{
						pawn.pather.StopDead();
						movingDirection = 0;
					}

					if (verb.TryStartCastOn(Enemy))
						Log.Warning($"-> {pawn} start attack {Enemy} at {Enemy.Position}");
					else
						Log.Warning($"-> {pawn} cannot hit target, waiting");
				}
				else if (canHit == false)
				{
					if (isMoving == false)
						MoveCloser();
				}
				//}
				//finally
				//{
				//	var microseconds = sw.ElapsedTicks * 1000000f / Stopwatch.Frequency;
				//	sw.Stop();
				//	if (microseconds > 1000)
				//		Log.Warning($"### {pawn} tactical attack tick took {microseconds / 1000f} ms");
				//}
			},
			defaultCompleteMode = ToilCompleteMode.Never
		};
	}

	public float GetSafeDistanceToEnemy()
		=> Enemy?.TryGetAttackVerb(pawn, false, false).EffectiveRange * 0.95f ?? 5f;

	private bool UnderAttack()
	{
		var stance = Enemy.stances.curStance as Stance_Warmup;
		return stance != null && stance.focusTarg == pawn;
	}

	public bool TooClose() => IsInDanger(pawn.Position);

	public bool TooFar()
	{
		var enemy = Enemy;
		var enemyPos = enemy.Position;
		var distance = pawn.Position.DistanceTo(enemyPos);
		var safeDistance = enemy.TryGetAttackVerb(pawn, false, false).EffectiveRange * 0.95f;
		var minDistance = pawn.TryGetAttackVerb(Enemy, false, false).EffectiveRange * 0.95f;
		if (distance > minDistance && distance >= safeDistance)
		{
			var attackPos = TargetB.Cell;
			if (attackPos.IsValid)
				distance = attackPos.DistanceTo(enemyPos);
			if (distance > minDistance)
				return true;
		}
		return false;
	}

	public bool IsInDanger(IntVec3 cell)
	{
		if (Controller.dangerGrids.TryGetValue(pawn, out var dangerCells) == false)
			return false;
		return dangerCells.Contains(cell);
	}

	public IntVec3 CanFindAttackPosition()
	{
		var traverseParms = TraverseParms.For(pawn);
		var bestCell = IntVec3.Invalid;
		var bestScore = float.MinValue;
		var map = Map;
		var pos = pawn.Position;
		var aim = Enemy.Position;
		var optimalRangeSquared = pawn.CurrentEffectiveVerb.EffectiveRange * 0.8f * (pawn.CurrentEffectiveVerb.verbProps.range * 0.8f);
		UpdateCells()
			.DoIf(
				cell =>
				{
					if (cell.WalkableBy(map, pawn) == false) return false;
					if (pos != cell && cell.InAllowedArea(pawn) == false) return false;
					if (map.reachability.CanReach(pos, cell, PathEndMode.OnCell, traverseParms) == false) return false;
					return true;
				},
				cell =>
				{
					const float BaseWeight = 0.3f;
					const float CoverWeightFactor = 0.55f;
					const float DecayFactor = 0.967f;

					var weight = BaseWeight + CoverUtility.CalculateOverallBlockChance(cell, aim, map) * CoverWeightFactor;

					var distance = (pos - cell).LengthHorizontal;
					weight *= Mathf.Pow(DecayFactor, distance);

					float cellDistSq = (cell - aim).LengthHorizontalSquared;
					var rangeRatio = Mathf.Abs(cellDistSq - optimalRangeSquared) / optimalRangeSquared;
					var rangeFactor = 0.7f + 0.3f * (1f - rangeRatio);
					if (cellDistSq < 25f)
						rangeFactor *= 0.5f;

					var score = weight * rangeFactor;
					if (score > bestScore)
					{
						bestScore = score;
						bestCell = cell;
					}
				}
			);
		return bestCell;
	}
}