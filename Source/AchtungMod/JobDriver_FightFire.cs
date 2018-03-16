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

		public override IEnumerable<LocalTargetInfo> CanStart(Pawn pawn, Vector3 clickPos)
		{
			base.CanStart(pawn, clickPos);
			if (pawn.workSettings.GetPriority(WorkTypeDefOf.Firefighter) == 0) return null;
			LocalTargetInfo cell = IntVec3.FromVector3(clickPos);
			var item = pawn.Map.thingGrid.ThingAt(cell.Cell, ThingDefOf.Fire);
			if (item == null) return null;
			var canFight = item.Destroyed == false && pawn.CanReach(item, PathEndMode.Touch, pawn.NormalMaxDanger()) && pawn.CanReserve(item, 1);
			return canFight ? new List<LocalTargetInfo> { cell } : null;
		}

		public override void UpdateVerbAndWorkLocations()
		{
			workLocations.ToList().Do(pos =>
			{
				if (pawn.Map.thingGrid.CellContains(pos, ThingDefOf.Fire) == false) workLocations.Remove(pos);
				GenAdj.CellsAdjacent8Way(new LocalTargetInfo(pos).ToTargetInfo(pawn.Map))
					.Where(loc => workLocations.Contains(loc) == false)
					.Where(loc => pawn.Map.thingGrid.CellContains(loc, ThingDefOf.Fire))
					.Do(loc => workLocations.Add(loc));
			});
			currentWorkCount = workLocations.Count();
			if (totalWorkCount < currentWorkCount) totalWorkCount = currentWorkCount;
		}

		public override LocalTargetInfo FindNextWorkItem()
		{
			return workLocations
				.OrderBy(loc => Math.Abs(loc.x - pawn.Position.x) + Math.Abs(loc.z - pawn.Position.z))
				.Select(loc => pawn.Map.thingGrid.ThingAt(loc, ThingDefOf.Fire) as Fire)
				.FirstOrDefault(f => f.Destroyed == false && pawn.CanReach(f, PathEndMode.Touch, pawn.NormalMaxDanger()) && pawn.CanReserve(f, 1));
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

		public override bool TryMakePreToilReservations()
		{
			return true;
		}
	}
}