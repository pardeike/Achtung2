using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using RimWorld;
using System.Linq;

namespace AchtungMod
{
	public class ForcedConstructionWorkGiverDef : WorkGiverDef
	{
		public ForcedConstructionWorkGiverDef()
		{
			giverClass = typeof(ForcedConstructionWorkGiver);
		}
	}

	public class ForcedConstructionWorkGiver : WorkGiver_Scanner
	{
		static WorkGiverDef forcedConstructionWorkGiverDef = new ForcedConstructionWorkGiverDef();
		static ForcedConstructionWorkGiver _instance;
		public static ForcedConstructionWorkGiver Instance
		{
			get
			{
				if (_instance == null)
					_instance = new ForcedConstructionWorkGiver { def = forcedConstructionWorkGiverDef };
				return _instance;
			}
		}
	}

	public class ForcedConstruction /* : ForcedJob */
	{
		public static JobDef constructionJobDef = new JobDef();

		public static HashSet<Type> workGiverTypes = new HashSet<Type>()
		{
				typeof(WorkGiver_ConstructDeliverResourcesToBlueprints),
				typeof(WorkGiver_ConstructDeliverResourcesToFrames),
				typeof(WorkGiver_ConstructFinishFrames)
		};

		static IEnumerable<WorkGiver_Scanner> GetWorkGivers(Pawn pawn)
		{
			return DefDatabase<WorkTypeDef>.AllDefsListForReading
				.SelectMany(def => def.workGiversByPriority)
				.Select(workGiverDef => workGiverDef.Worker)
				.OfType<WorkGiver_Scanner>()
				.Where(workGiver => workGiverTypes.Contains(workGiver?.GetType()) && workGiver.def.directOrderable && !workGiver.ShouldSkip(pawn));
		}

		internal /*override*/ Type GetWorkGiverType() { return null; }
		internal /*override*/ WorkGiver_Scanner GetWorkGiver()
		{
			return ForcedConstructionWorkGiver.Instance;
		}

		internal /*override*/ int ThingScore(LocalTargetInfo thing, IntVec3 closeToCell)
		{
			var isPowerConduitBlueprint = (thing.Thing as Blueprint_Build)?.def.entityDefToBuild == ThingDefOf.PowerConduit;
			var isPowerConduitFrame = (thing.Thing as Frame)?.resourceContainer.ElementAtOrDefault(0)?.def == ThingDefOf.PowerConduit;
			var isNormalBlueprint = isPowerConduitBlueprint == false && (thing.Thing is Blueprint);
			return (isPowerConduitFrame ? -20000 : 0) + (isPowerConduitBlueprint ? -10000 : 0) + (isNormalBlueprint ? 10000 : 0) + thing.Cell.DistanceToSquared(closeToCell);
		}

		// construction needs to pick the right workgiver so we cannot use the base JobOnThing
		//
		internal /*override*/ bool HasJobOnThing(WorkGiver_Scanner unused, Pawn pawn, LocalTargetInfo thing)
		{
			foreach (var workGiver in GetWorkGivers(pawn))
			{
				if (thing.HasThing)
				{
					if (workGiver.HasJobOnThing(pawn, thing.Thing, false))
						return true;
				}
				else
				{
					if (workGiver.HasJobOnCell(pawn, thing.Cell))
						return true;
				}
			}
			return false;
		}

		// construction needs to pick the right workgiver so we cannot use the base JobOnThing
		//
		internal /*override*/ Job JobOnThing(WorkGiver_Scanner unused, Pawn pawn, LocalTargetInfo thing, bool forced)
		{
			foreach (var workGiver in GetWorkGivers(pawn))
			{
				if (thing.HasThing)
				{
					var job = workGiver.JobOnThing(pawn, thing.Thing, false);
					if (job != null) return job;
				}
				else
				{
					var job = workGiver.JobOnCell(pawn, thing.Cell);
					if (job != null) return job;
				}
			}
			return null;
			/*
			foreach (var workGiver in GetWorkGivers(pawn))
			{
				var disabled1 = pawn.story?.WorkTagIsDisabled(workGiver.def.workTags) ?? false;
				var disabled2 = pawn.story?.WorkTypeIsDisabled(workGiver.def.workType) ?? false;
				if (disabled1 == false && disabled2 == false)
					if (thing.Thing.IsForbidden(pawn) == false && pawn.CanReach(thing, workGiver.PathEndMode, Danger.Deadly, false, TraverseMode.ByPawn))
						return workGiver.JobOnThing(pawn, thing.Thing, forced);
			}
			*/
		}

		internal /*override*/ string MenuLabel(LocalTargetInfo thing)
		{
			return "ForcedConstruction".Translate();
		}
	}
}