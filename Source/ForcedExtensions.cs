using RimWorld;
using System.Linq;
using Verse.AI;
using Verse;

namespace AchtungMod;

public static class ForcedExtensions
{
	public static bool Ignorable(this WorkGiver_Scanner workgiver)
	{
		return (
			   workgiver is WorkGiver_Haul
			|| workgiver is WorkGiver_Repair
			|| workgiver is WorkGiver_ConstructAffectFloor
			|| workgiver is WorkGiver_ConstructDeliverResources
			|| workgiver is WorkGiver_ConstructFinishFrames
			|| workgiver is WorkGiver_Flick
			|| workgiver is WorkGiver_Miner
			|| workgiver is WorkGiver_Refuel
			|| workgiver is WorkGiver_RemoveRoof
			|| workgiver is WorkGiver_Strip
			|| workgiver is WorkGiver_TakeToBed
			|| workgiver is WorkGiver_RemoveBuilding
		);
	}

	// fix for forbidden state in cached handlers
	//
	public static bool ShouldBeHaulable(Thing t)
	{
		// vanilla code but added 'Achtung.Settings.ignoreForbidden == false &&'
		if (Achtung.Settings.ignoreForbidden == false && t.IsForbidden(Faction.OfPlayer))
			return false;

		if (t.def.alwaysHaulable == false)
		{
			if (t.def.EverHaulable == false)
				return false;

			// vanilla code but added 'Achtung.Settings.ignoreForbidden == false &&'
			if (Achtung.Settings.ignoreForbidden == false
				&& t.Map.designationManager.DesignationOn(t, DesignationDefOf.Haul) == null
				&& t.IsInAnyStorage() == false
			)
			return false;
		}

		return t.IsInValidBestStorage() == false;
	}
	//
	public static bool ShouldBeMergeable(Thing t)
	{
		// vanilla code but added 'Achtung.Settings.ignoreForbidden ||'
		return (Achtung.Settings.ignoreForbidden || t.IsForbidden(Faction.OfPlayer) == false)
			&& t.GetSlotGroup() != null
			&& t.stackCount != t.def.stackLimit;
	}

	public static Job GetThingJob(this Thing thing, Pawn pawn, WorkGiver_Scanner scanner, bool ignoreReserve = false)
	{
		if (thing == null || thing.Spawned == false) return null;
		var request = scanner.PotentialWorkThingRequest;
		var potentialWork = request.IsUndefined == false && request.Accepts(thing);
		if (potentialWork == false)
		{
			var workThingsGlobal = scanner.PotentialWorkThingsGlobal(pawn);
			if (request.IsUndefined == false)
				workThingsGlobal ??= pawn.Map.listerThings.ThingsMatching(request);
			if (workThingsGlobal != null && workThingsGlobal.Contains(thing))
				potentialWork = true;
		}
		if (potentialWork == false && scanner is WorkGiver_Haul)
			potentialWork = ShouldBeHaulable(thing);
		else if (potentialWork == false && scanner is WorkGiver_Merge)
			potentialWork = ShouldBeMergeable(thing);
		if (potentialWork)
			if (scanner.MissingRequiredCapacity(pawn) == null)
				if (scanner.HasJobOnThing(pawn, thing, true))
				{
					var job = scanner.JobOnThing(pawn, thing, true);
					if (job != null)
					{
						var ignorable = scanner.Ignorable();
						if (Achtung.Settings.ignoreForbidden && ignorable || thing.IsForbidden(pawn) == false)
							if (Achtung.Settings.ignoreRestrictions && ignorable || thing.Position.InAllowedArea(pawn))
							{
								if (
									(ignoreReserve == false && pawn.CanReserveAndReach(thing, scanner.PathEndMode, Danger.Deadly))
									||
									(ignoreReserve && pawn.CanReach(thing, scanner.PathEndMode, Danger.Deadly))
								)
									return job;
							}
					}
				}
		return null;
	}

	public static Job GetCellJob(this IntVec3 cell, Pawn pawn, WorkGiver_Scanner workgiver, bool ignoreReserve = false)
	{
		if (cell.IsValid == false) return null;
		if (workgiver.PotentialWorkCellsGlobal(pawn).Contains(cell))
			if (workgiver.MissingRequiredCapacity(pawn) == null)
				if (workgiver.HasJobOnCell(pawn, cell, ignoreReserve))
				{
					var job = workgiver.JobOnCell(pawn, cell);
					if (pawn.CanReach(cell, workgiver.PathEndMode, Danger.Deadly))
						return job;
				}
		return null;
	}
}