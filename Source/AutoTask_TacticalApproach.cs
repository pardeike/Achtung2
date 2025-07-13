using RimWorld;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Verse;
using Verse.AI;

namespace AchtungMod;

public class AutoTask_TacticalApproach(Pawn pawn, Pawn enemy) : IAutoTask
{
	const int jobDeltaTicks = 5;

	public enum State
	{
		Waiting,
		Positioning,
		Retreating,
		Attacking
	}

	public Pawn pawn = pawn;
	public Pawn enemy = enemy;
	public IntVec3 attackPos = IntVec3.Invalid;
	public State state = State.Waiting;

	readonly List<Action> logs = [];
	void Log(string msg)
	{
		lock (logs)
			logs.Add(() => Verse.Log.Warning($"{pawn.LabelShortCap} [{state}] {msg}"));
	}

	Thread thread;
	Action nextCommand = null;
	public bool Stopping { get; set; }

	public void ExposeData()
	{
		Scribe_References.Look(ref pawn, "pawn");
		Scribe_References.Look(ref enemy, "enemy");
		Scribe_Values.Look(ref attackPos, "attackPos", IntVec3.Invalid);
		Scribe_Values.Look(ref state, "state", State.Waiting);
	}

	void SetState(State newState)
	{
		if (state == newState) return;
		Log($"{state} -> {newState}");
		state = newState;
	}

	public void Tick()
	{
		lock (logs)
		{
			logs.Do(log => log());
			logs.Clear();
		}

		lock (this)
		{
			nextCommand?.Invoke();
			nextCommand = null;
		}
	}

	void PerformNext(Action action)
	{
		lock (this)
			nextCommand = action;
	}

	bool EnemyFinished()
	{
		if (enemy.Downed) return true;
		if (enemy.Dead) return true;
		if (enemy.Destroyed) return true;
		return false;
	}

	public void Goto(IntVec3 gotoLoc, State state)
	{
		PerformNext(delegate
		{
			if (FloatMenuOptionProvider_DraftedMove.PawnCanGoto(pawn, gotoLoc))
			{
				Log($"is {state.ToString().ToLower()} to position {gotoLoc}");
				FloatMenuOptionProvider_DraftedMove.PawnGotoAction(gotoLoc, pawn, gotoLoc);
				SetState(state);
			}
		});
	}

	public void Attack()
	{
		var action = FloatMenuOptionProvider_DraftedAttack.GetAttackAction(pawn, enemy, out _, out var _);
		if (action != null)
		{
			PerformNext(delegate
			{
				Log($"is attacking {enemy}");
				nextCommand = action;
				SetState(State.Attacking);
			});
		}
		else
		{
			attackPos = CanFindAttackPosition(pawn, enemy);
			Log($"could not attack {enemy}, going to {attackPos}");
			Goto(attackPos, State.Positioning);
		}
	}

	public void Start(Action onComplete)
	{
		thread = new Thread(() =>
		{
			Log($"started tactical attack on {enemy}");
			while (Stopping == false && Step())
				Thread.Sleep(jobDeltaTicks);
			Log($"ended tactical attack on {enemy}");
			onComplete();

		})
		{ IsBackground = true, Name = "Tactical Attack " + pawn.LabelShortCap };
		thread.Start();
	}

	bool Step()
	{
		if (Find.TickManager.Paused || nextCommand != null)
			return true;

		if (EnemyFinished())
		{
			Log($"terminated {enemy}");
			return false;
		}

		if (pawn.pather.AtDestinationPosition() && state != State.Attacking)
		{
			Log($"reached position");
			SetState(State.Waiting);
		}

		if (TooClose() && state != State.Retreating && NeedsSafePosition(out var coverPos))
		{
			Goto(coverPos, State.Retreating);
			return true;
		}

		if (TooFar() && state != State.Positioning)
		{
			attackPos = CanFindAttackPosition(pawn, enemy);
			Goto(attackPos, State.Positioning);
			return true;
		}

		var isShooting = state == State.Attacking && pawn.stances.curStance.StanceBusy;
		if (isShooting == false && state != State.Positioning && state != State.Retreating)
			Attack();

		return true;
	}

	float GetSafeDistanceToEnemy()
		=> enemy.TryGetAttackVerb(pawn, false, false).EffectiveRange + 1;

	bool NeedsSafePosition(out IntVec3 coverPos)
	{
		var enemyPos = enemy.Position;

		if (enemy.equipment.PrimaryEq.PrimaryVerb.CanHitTarget(pawn) == false)
		{
			coverPos = IntVec3.Invalid;
			return false;
		}

		var safeDistance = GetSafeDistanceToEnemy();
		var distanceSquared = (int)(safeDistance * safeDistance) + 1;
		var maxRadius = (int)safeDistance + 10;
		return RCellFinder.TryFindRandomCellNearWith(pawn.Position, cell => cell.DistanceToSquared(enemyPos) > distanceSquared, pawn.Map, out coverPos, 5, maxRadius);
	}

	bool TooClose()
	{
		var distance = pawn.Position.DistanceTo(enemy.Position);
		var safeDistance = GetSafeDistanceToEnemy();
		return distance < safeDistance;
	}

	bool TooFar()
	{
		var enemyPos = enemy.Position;
		var distance = pawn.Position.DistanceTo(enemyPos);
		var safeDistance = enemy.TryGetAttackVerb(pawn, false, false).EffectiveRange;
		var minDistance = pawn.TryGetAttackVerb(enemy, false, false).EffectiveRange;
		if (distance > minDistance && distance >= safeDistance)
		{
			if (attackPos.IsValid)
				distance = attackPos.DistanceTo(enemyPos);
			if (distance >= minDistance)
				return true;
		}
		return false;
	}

	static IntVec3 CanFindAttackPosition(Pawn pawn, Pawn target)
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
			: Mathf.Max(castPositionRequest.verb.EffectiveRange, 1.42f);
		if (CastPositionFinder.TryFindCastPosition(castPositionRequest, out var result))
			return result;
		return IntVec3.Invalid;
	}
}