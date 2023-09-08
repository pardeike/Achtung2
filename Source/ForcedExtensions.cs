using RimWorld;
using System.Linq;
using Verse.AI;
using Verse;

namespace AchtungMod
{
	public static class ForcedExtensions
	{
		public static bool Ignorable(this WorkGiver_Scanner workgiver)
		{
			return (false
				|| (workgiver as WorkGiver_Haul) != null
				|| (workgiver as WorkGiver_Repair) != null
				|| (workgiver as WorkGiver_ConstructAffectFloor) != null
				|| (workgiver as WorkGiver_ConstructDeliverResources) != null
				|| (workgiver as WorkGiver_ConstructFinishFrames) != null
				|| (workgiver as WorkGiver_Flick) != null
				|| (workgiver as WorkGiver_Miner) != null
				|| (workgiver as WorkGiver_Refuel) != null
				|| (workgiver as WorkGiver_RemoveRoof) != null
				|| (workgiver as WorkGiver_Strip) != null
				|| (workgiver as WorkGiver_TakeToBed) != null
				|| (workgiver as WorkGiver_RemoveBuilding) != null
			);
		}

		// fix for forbidden state in cached handlers
		//
		public static bool ShouldBeHaulable(Thing t)
		{
			// vanilla code but added 'Achtung.Settings.ignoreForbidden == false &&'
			if (Achtung.Settings.ignoreForbidden == false && t.IsForbidden(Faction.OfPlayer))
				return false;

			if (!t.def.alwaysHaulable)
			{
				if (!t.def.EverHaulable)
					return false;
				// vanilla code but added 'Achtung.Settings.ignoreForbidden == false &&'
				if (Achtung.Settings.ignoreForbidden == false && t.Map.designationManager.DesignationOn(t, DesignationDefOf.Haul) == null && !t.IsInAnyStorage())
					return false;
			}
			return !t.IsInValidBestStorage();
		}
		//
		public static bool ShouldBeMergeable(Thing t)
		{
			// vanilla code but added 'Achtung.Settings.ignoreForbidden ||'
			return (Achtung.Settings.ignoreForbidden || !t.IsForbidden(Faction.OfPlayer)) && t.GetSlotGroup() != null && t.stackCount != t.def.stackLimit;
		}

		public static Job GetThingJob(this Thing thing, Pawn pawn, WorkGiver_Scanner scanner, bool ignoreReserve = false)
		{
			if (thing == null || thing.Spawned == false)
				return null;
			var potentialWork = scanner.PotentialWorkThingRequest.Accepts(thing);
			if (potentialWork == false)
			{
				var workThingsGlobal = scanner.PotentialWorkThingsGlobal(pawn);
				workThingsGlobal ??= pawn.Map.listerThings.ThingsMatching(scanner.PotentialWorkThingRequest);
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
			if (cell.IsValid == false)
				return null;
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
}
