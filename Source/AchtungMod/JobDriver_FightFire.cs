using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using System.Linq;
using UnityEngine;

namespace AchtungMod
{
	public class JobDriver_FightFire : JobDriver_Thoroughly
	{
		public override string GetPrefix()
		{
			return "FireFight";
		}

		public override IEnumerable<TargetInfo> CanStart(Pawn pawn, Vector3 clickPos)
		{
			base.CanStart(pawn, clickPos);
			if (pawn.workSettings.GetPriority(WorkTypeDefOf.Firefighter) == 0) return null;
			TargetInfo cell = IntVec3.FromVector3(clickPos);
			Thing item = Find.ThingGrid.ThingAt(cell.Cell, ThingDefOf.Fire);
			if (item == null) return null;
			bool canFight = item.Destroyed == false && pawn.CanReach(item, PathEndMode.Touch, pawn.NormalMaxDanger()) && pawn.CanReserve(item, 1);
			return canFight ? new List<TargetInfo> { cell } : null;
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