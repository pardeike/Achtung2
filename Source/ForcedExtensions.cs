using RimWorld;
using System.Linq;
using Verse.AI;
using Verse;

namespace AchtungMod;

public static class ForcedExtensions
{
	[System.ThreadStatic]
	static Pawn permissionPawn;

	[System.ThreadStatic]
	static Job permissionJob;

	[System.ThreadStatic]
	static int permissionJobId;

	[System.ThreadStatic]
	static Thing directlyForcedThing;

	public readonly struct PermissionScope : System.IDisposable
	{
		readonly Pawn previousPawn;
		readonly Job previousJob;
		readonly int previousJobId;
		readonly Thing previousThing;
		readonly bool initialized;

		public PermissionScope(Pawn pawn, Job job = null, Thing directTarget = null)
		{
			previousPawn = permissionPawn;
			previousJob = permissionJob;
			previousJobId = permissionJobId;
			previousThing = directlyForcedThing;
			permissionPawn = pawn;
			permissionJob = job;
			permissionJobId = job?.loadID ?? -1;
			directlyForcedThing = directTarget;
			initialized = true;
		}

		public void Dispose()
		{
			if (initialized == false) return;
			permissionPawn = previousPawn;
			permissionJob = previousJob;
			permissionJobId = previousJobId;
			directlyForcedThing = previousThing;
		}
	}

	public static bool AppliesTo(Pawn pawn)
	{
		return pawn != null && pawn == permissionPawn
			&& (permissionJob == null || (pawn.CurJob == permissionJob && permissionJob.loadID == permissionJobId));
	}

	public static bool IsDirectTarget(Pawn pawn, Thing thing)
		=> AppliesTo(pawn) && thing != null && thing == directlyForcedThing;

	public static bool InAllowedArea(Pawn pawn, IntVec3 cell)
	{
		return cell.IsValid && cell.InBounds(pawn.Map)
			&& (Achtung.Settings.ignoreRestrictions || cell.InAllowedArea(pawn));
	}

	public static bool AllowedTarget(Pawn pawn, Thing thing, Thing directTarget = null)
	{
		return thing != null
			&& (thing == directTarget || Achtung.Settings.ignoreForbidden || thing.IsForbidden(pawn.Faction) == false)
			&& (thing.SpawnedParentOrMe == pawn || thing.MapHeld == null || InAllowedArea(pawn, thing.PositionHeld));
	}

	static bool AllowedJob(Pawn pawn, Job job, Thing directTarget)
		=> job != null && job.TargetThings().All(thing => AllowedTarget(pawn, thing, directTarget))
			&& job.TargetCells().All(cell => InAllowedArea(pawn, cell));

	static bool CanUse(Pawn pawn, WorkGiver_Scanner scanner)
	{
		var workType = scanner.def.workType;
		return scanner.MissingRequiredCapacity(pawn) == null
			&& pawn.WorkTagIsDisabled(scanner.def.workTags) == false
			&& (workType == null || (pawn.WorkTypeIsDisabled(workType) == false
				&& (Achtung.Settings.ignoreAssignments || pawn.workSettings?.GetPriority(workType) > 0)));
	}

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
		if (Achtung.Settings.ignoreForbidden == false && IsDirectTarget(permissionPawn, t) == false && t.IsForbidden(Faction.OfPlayer))
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
		return (Achtung.Settings.ignoreForbidden || IsDirectTarget(permissionPawn, t) || t.IsForbidden(Faction.OfPlayer) == false)
			&& t.GetSlotGroup() != null
			&& t.stackCount != t.def.stackLimit;
	}

	public static Job GetThingJob(this Thing thing, Pawn pawn, WorkGiver_Scanner scanner, bool ignoreReserve = false, Thing directTarget = null)
	{
		if (thing == null || thing.Spawned == false || CanUse(pawn, scanner) == false || AllowedTarget(pawn, thing, directTarget) == false) return null;
		using var scope = new PermissionScope(pawn, directTarget: directTarget);
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
		if (potentialWork == false || scanner.HasJobOnThing(pawn, thing, true) == false)
			return null;
		var job = scanner.JobOnThing(pawn, thing, true);
		if (AllowedJob(pawn, job, directTarget) == false || thing.IsForbidden(pawn))
			return null;
		var reachable = ignoreReserve
			? pawn.CanReach(thing, scanner.PathEndMode, Danger.Deadly)
			: pawn.CanReserveAndReach(thing, scanner.PathEndMode, Danger.Deadly);
		return reachable ? job : null;
	}

	public static Job GetCellJob(this IntVec3 cell, Pawn pawn, WorkGiver_Scanner workgiver, bool ignoreReserve = false, Thing directTarget = null)
	{
		if (CanUse(pawn, workgiver) == false || InAllowedArea(pawn, cell) == false) return null;
		using var scope = new PermissionScope(pawn, directTarget: directTarget);
		if (workgiver.PotentialWorkCellsGlobal(pawn).Contains(cell) == false
			|| workgiver.HasJobOnCell(pawn, cell, ignoreReserve) == false)
			return null;
		var job = workgiver.JobOnCell(pawn, cell);
		return AllowedJob(pawn, job, directTarget) && pawn.CanReach(cell, workgiver.PathEndMode, Danger.Deadly) ? job : null;
	}
}
