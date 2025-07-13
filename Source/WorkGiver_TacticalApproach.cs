using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace AchtungMod;

/*
public class WorkGiver_TacticalApproach : WorkGiver_Scanner
{
	const float maxSearchDistance = 60f;
	public static JobDef jobDef;

	public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
	{
		var things = pawn.Map.mapPawns.AllPawnsSpawned
			.Where(p =>
				p.Position.InHorDistOf(pawn.Position, maxSearchDistance)
				&& GenHostility.IsActiveThreatTo(p, Faction.OfPlayer, true, false)
			);
		Log.Warning($"PotentialWorkThingsGlobal found {things.Count()} targets for pawn {pawn.Name} at {pawn.Position}.");
		return things;
	}

	public override PathEndMode PathEndMode => PathEndMode.OnCell;

	public override Danger MaxPathDanger(Pawn pawn) => Danger.Deadly;

	public override bool ShouldSkip(Pawn pawn, bool forced)
	{
		var skip = CanUseTacticalWeapon(pawn) == false;
		Log.Warning($"Skip pawn {pawn.Name} at {pawn.Position} ? {(skip ? "yes" : "no")}");
		return skip;
	}

	public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced)
	{
		bool Check()
		{
			if (t is not Pawn pawn2) return false;
			if (pawn2.Downed) return false;
			if (pawn.CanReserve(t, 1, -1, null, forced)) return false;
			if (CanFindAttackPosition(pawn, pawn2).IsValid == false) return false;
			return true;
		}
		var hasJob = Check();
		Log.Warning($"HasJobOnThing for pawn {pawn.Name} at {pawn.Position} on target {t.LabelShortCap} ? {(hasJob ? "yes" : "no")}");
		return hasJob;
	}

	public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
	{
		if (t is not Pawn target) return null;
		var position = CanFindAttackPosition(pawn, target);
		if (position == IntVec3.Invalid)
		{
			Log.Warning($"No attack position found for pawn {pawn.Name} at {pawn.Position} on target {t.LabelShortCap}");
			return null;
		}

		var job = JobMaker.MakeJob(jobDef, t, position);
		Log.Warning($"JobOnThing for pawn {pawn.Name} at {pawn.Position} on target {t.LabelShortCap} from {position} : {job}");
		return job;
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

	private static bool ShieldBlocksRange(Pawn pawn)
	{
		var wornApparel = pawn.apparel.WornApparel;
		for (var i = 0; i < wornApparel.Count; i++)
			if (wornApparel[i].def.IsShieldThatBlocksRanged)
				return true;
		return false;
	}

	private static bool CanUseTacticalWeapon(Pawn pawn)
	{
		var equipment = pawn.equipment;
		var primaryDef = equipment.Primary?.def;
		if (primaryDef?.IsRangedWeapon == false) return false;
		if (equipment.PrimaryEq.PrimaryVerb.HarmsHealth() == false) return false;
		if (primaryDef.IsWeaponUsingProjectiles && ShieldBlocksRange(pawn)) return false;
		return true;
	}
}
*/