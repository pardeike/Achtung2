using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace AchtungMod
{
	public class Colonist(Pawn pawn)
	{
		public Pawn pawn = pawn;
		public IntVec3 designation = IntVec3.Invalid;
		public IntVec3 lastOrder = IntVec3.Invalid;
		public Vector3 startPosition = pawn.DrawPos;
		public Vector3 offsetFromCenter = Vector3.zero;
		public bool originalDraftStatus = Tools.GetDraftingStatus(pawn);

		public IntVec3 UpdateOrderPos(Vector3 pos)
		{
			var cell = pos.ToIntVec3();

			if (AchtungLoader.IsSameSpotInstalled)
			{
				if (cell.Standable(pawn.Map) && ReachabilityUtility.CanReach(pawn, cell, PathEndMode.OnCell, Danger.Deadly))
				{
					designation = cell;
					return cell;
				}
			}

			var bestCell = IntVec3.Invalid;
			if (ModsConfig.BiotechActive && pawn.IsColonyMech && MechanitorUtility.InMechanitorCommandRange(pawn, cell) == false)
			{
				var overseer = pawn.GetOverseer();
				var map = overseer.MapHeld;
				if (map == pawn.MapHeld)
				{
					var mechanitor = overseer.mechanitor;
					foreach (var newPos in GenRadial.RadialCellsAround(cell, 20f, false))
						if (mechanitor.CanCommandTo(newPos))
							if (map.pawnDestinationReservationManager.CanReserve(newPos, pawn, true)
								&& newPos.Standable(map)
								&& pawn.CanReach(newPos, PathEndMode.OnCell, Danger.Deadly, false, false, TraverseMode.ByPawn)
							)
							{
								bestCell = newPos;
								break;
							}
				}
			}
			else
				bestCell = RCellFinder.BestOrderedGotoDestNear(cell, pawn);
			if (bestCell.InBounds(pawn.Map))
			{
				designation = bestCell;
				return bestCell;
			}
			return IntVec3.Invalid;
		}

		public void OrderTo(Vector3 pos)
		{
			var bestCell = UpdateOrderPos(pos);
			if (bestCell.IsValid && lastOrder.IsValid == false || lastOrder != bestCell)
			{
				lastOrder = bestCell;
				Tools.OrderTo(pawn, bestCell.x, bestCell.z);
			}
		}
	}
}
