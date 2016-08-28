using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using System.Linq;

namespace AchtungMod
{
	public class JobDriver_FightFire : JobDriver_Thoroughly
	{
		public override string GetPrefix()
		{
			return "FireFight";
		}

		public override bool CanStart(Pawn pawn, IntVec3 loc)
		{
			base.CanStart(pawn, loc);
			if (pawn.workSettings.GetPriority(WorkTypeDefOf.Firefighter) == 0) return false;
			Thing item = Find.ThingGrid.ThingAt(loc, ThingDefOf.Fire);
			if (item == null) return false;
			return item.Destroyed == false && pawn.CanReach(item, PathEndMode.Touch, pawn.NormalMaxDanger()) && pawn.CanReserve(item, 1);
		}

		public override void UpdateWorkLocations()
		{
			List<IntVec3> locations = workLocations.ToList();
			locations.ForEach(pos =>
			{
				if (Find.ThingGrid.CellContains(pos, ThingDefOf.Fire) == false) workLocations.Remove(pos);
				GenAdj.CellsAdjacent8Way(new TargetInfo(pos))
					.Where(loc => workLocations.Contains(loc) == false)
					.Where(loc => Find.ThingGrid.CellContains(loc, ThingDefOf.Fire))
					.ToList().ForEach(loc => workLocations.Add(loc));
			});
			currentWorkCount = locations.Count();
			if (totalWorkCount < currentWorkCount) totalWorkCount = currentWorkCount;
		}

		public override TargetInfo FindNextWorkItem()
		{
			Controller.SetDebugPositions(workLocations);
			return workLocations
				.OrderBy(loc => Math.Abs(loc.x - pawn.Position.x) + Math.Abs(loc.z - pawn.Position.z))
				.Select(loc => Find.ThingGrid.ThingAt(loc, ThingDefOf.Fire) as Fire)
				.Where(f => f.Destroyed == false && pawn.CanReach(f, PathEndMode.Touch, pawn.NormalMaxDanger()) && pawn.CanReserve(f, 1))
				.FirstOrDefault();
		}

		public override bool DoWorkToItem()
		{
			pawn.natives.TryBeatFire(currentItem.Thing as Fire);
			if (currentItem.Thing.Destroyed)
			{
				pawn.records.Increment(RecordDefOf.FiresExtinguished);
				return true;
			}
			return false;
		}
	}
}