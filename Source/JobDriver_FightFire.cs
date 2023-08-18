using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace AchtungMod
{
	public class JobDriver_FightFire : JobDriver_Thoroughly
	{
		public override string GetPrefix() => "FireFight";

		public override IEnumerable<LocalTargetInfo> CanStart(Pawn thePawn, LocalTargetInfo clickCell)
		{
			_ = base.CanStart(thePawn, clickCell);
			if (thePawn.workSettings == null || thePawn.WorkTypeIsDisabled(WorkTypeDefOf.Firefighter))
				return null;
			if (Achtung.Settings.ignoreAssignments == false && thePawn.workSettings.GetPriority(WorkTypeDefOf.Firefighter) == 0)
				return null;
			var item = thePawn.Map.thingGrid.ThingAt(clickCell.cellInt, ThingDefOf.Fire);
			if (item == null)
				return null;
			var canFight = item.Destroyed == false && thePawn.CanReach(item, PathEndMode.Touch, thePawn.NormalMaxDanger()) && thePawn.CanReserve(item, 1);
			return canFight ? new List<LocalTargetInfo> { clickCell } : null;
		}

		public override void UpdateVerbAndWorkLocations()
		{
			workLocations.ToList().Do(pos =>
			{
				if (pawn.Map.thingGrid.CellContains(pos, ThingDefOf.Fire) == false)
					_ = workLocations.Remove(pos);
				GenAdj.CellsAdjacent8Way(new LocalTargetInfo(pos).ToTargetInfo(pawn.Map))
					.Where(loc => workLocations.Contains(loc) == false)
					.Where(loc => pawn.Map.thingGrid.CellContains(loc, ThingDefOf.Fire))
					.Do(loc => workLocations.Add(loc));
			});
			currentWorkCount = workLocations.Count();
			if (totalWorkCount < currentWorkCount)
				totalWorkCount = currentWorkCount;
		}

		public override LocalTargetInfo FindNextWorkItem()
		{
			return workLocations
				.OrderBy(loc => Math.Abs(loc.x - pawn.Position.x) + Math.Abs(loc.z - pawn.Position.z))
				.Select(loc => pawn.Map.thingGrid.ThingAt(loc, ThingDefOf.Fire) as Fire)
				.FirstOrDefault(f => f != null && f.Destroyed == false && pawn.CanReach(f, PathEndMode.Touch, pawn.NormalMaxDanger()) && pawn.CanReserve(f, 1));
		}

		public override bool DoWorkToItem()
		{
			if (pawn.natives.TryBeatFire(currentItem.thingInt as Fire))
				if (currentItem.thingInt.Destroyed)
				{
					pawn.records.Increment(RecordDefOf.FiresExtinguished);
					return true;
				}
			return false;
		}

		public override bool TryMakePreToilReservations(bool errorOnFailed)
		{
			return true;
		}
	}
}
