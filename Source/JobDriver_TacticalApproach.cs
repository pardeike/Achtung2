using RimWorld;
using System;
using System.Collections.Generic;
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

	public enum State
	{
		Undefined = 0,
		TooFar = 1,
		TooClose = 2,
		Moving = 3,
		Attack = 4
	}

	public State state = State.Undefined;

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_Values.Look(ref state, "state", State.Undefined);
	}

	public Pawn Enemy => (Pawn)job.GetTarget(TargetIndex.A).Thing;

	public override string GetReport()
	{
		if (Enemy != null)
			return JobUtility.GetResolvedJobReport(job.def.reportString, Enemy);
		return base.GetReport();
	}

	public override bool TryMakePreToilReservations(bool errorOnFailed)
		=> pawn.Reserve(Enemy, job, 1, -1, null, errorOnFailed, false);

	private void SetState(State newState)
	{
		Log.Warning($"{pawn} state changed from {state} to {newState}");
		state = newState;
	}

	private bool EnemyFinished()
	{
		var enemy = Enemy;
		if (enemy == null) return true;
		if (enemy.Downed) return true;
		if (enemy.Dead) return true;
		if (enemy.Destroyed) return true;
		return false;
	}

	public override IEnumerable<Toil> MakeNewToils()
	{
		_ = this.FailOn(delegate
		{
			var done = EnemyFinished();
			if (done)
				Log.Warning($"### {pawn} tactical attack done");
			return done;
		});

		yield return Toils_Combat.TrySetJobToUseAttackVerb(TargetIndex.A);

		var checkAttackPos = Toils_General
			.Do(delegate
			{
				Log.Warning($"### {pawn} checking position");

				if (TooClose() && state != State.TooClose)
				{
					var enemyPos = Enemy.Position;
					var safeDistance = GetSafeDistanceToEnemy();
					var distanceSquared = (int)(safeDistance * safeDistance) + 1;
					var maxRadius = (int)safeDistance + 5;
					if (RCellFinder.TryFindRandomCellNearWith(pawn.Position, cell => cell.DistanceToSquared(enemyPos) >= distanceSquared, pawn.Map, out var coverPos, 5, maxRadius))
					{
						Log.Warning($"-> {pawn} is retreating to {coverPos}");
						job.SetTarget(TargetIndex.B, coverPos);
						SetState(State.TooClose);
					}
					else
						Log.Warning($"-> {pawn} cannot find cover to retreat to, staying put");
				}
				if (TooFar() && state != State.TooFar)
				{
					var attackPos = CanFindAttackPosition(pawn, Enemy);
					Log.Warning($"-> {pawn} is moving to attack position {attackPos}");
					job.SetTarget(TargetIndex.B, attackPos);
					SetState(State.TooFar);
				}
			});
		yield return checkAttackPos;

		/*yield return Toils_Combat
			.GotoCastPosition(TargetIndex.A, TargetIndex.B, true)
			.AddInitAction(() => Log.Warning($"-> {pawn} going to cast positon {TargetB.Cell}"))
			.JumpIf(() => TooClose() && state != State.TooClose, checkAttackPos)
			.JumpIf(() => TooFar() && state != State.TooFar, checkAttackPos);*/

		yield return Toils_Goto
			.Goto(TargetIndex.B, PathEndMode.OnCell)
			.AddInitAction(delegate
			{
				Log.Warning($"-> {pawn} going to cast positon {TargetB.Cell}");
				SetState(State.Moving);
			})
			.JumpIf(delegate
			{
				if (TooClose() && state != State.TooClose) return true;
				if (TooFar() && state != State.TooFar) return true;
				Log.Message("...");
				return false;
			}, checkAttackPos);

		var checkTargetToil = Toils_Jump.JumpIfTargetNotHittable(TargetIndex.A, checkAttackPos);
		yield return checkTargetToil;

		yield return Toils_Combat
			.CastVerb(TargetIndex.A, TargetIndex.B, true)
			.AddInitAction(delegate
			{
				Log.Warning($"-> {pawn} is attacking enemy {Enemy} at {Enemy.Position}");
				SetState(State.Attack);
			})
			.JumpIf(delegate
			{
				if (TooClose() && state != State.TooClose) return true;
				if (TooFar() && state != State.TooFar) return true;
				Log.Message("...");
				return false;
			}, checkAttackPos);

		yield return Toils_Jump
			.Jump(checkTargetToil)
			.AddInitAction(() => Log.Warning($"{pawn} finished attacking {Enemy} at {Enemy.Position}"));
	}

	public float GetSafeDistanceToEnemy()
		=> Enemy?.TryGetAttackVerb(pawn, false, false).EffectiveRange * 0.95f ?? 5f;

	public bool TooClose()
	{
		var distance = pawn.Position.DistanceTo(Enemy.Position);
		var safeDistance = GetSafeDistanceToEnemy();
		return distance < safeDistance;
	}

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

	public static IntVec3 CanFindAttackPosition(Pawn pawn, Pawn target)
	{
		var castPositionRequest = new CastPositionRequest
		{
			caster = pawn,
			target = target,
			verb = pawn.TryGetAttackVerb(target, false, false),
			wantCoverFromTarget = true
		};
		castPositionRequest.maxRangeFromTarget = target.Downed
			? Mathf.Min(castPositionRequest.verb.EffectiveRange, target.RaceProps.executionRange)
			: Mathf.Max(castPositionRequest.verb.EffectiveRange * 0.95f, 1.42f);
		if (CastPositionFinder.TryFindCastPosition(castPositionRequest, out var result))
			return result;
		return IntVec3.Invalid;
	}
}