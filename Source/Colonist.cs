using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace AchtungMod
{
	public class Colonist
	{
		public Pawn pawn;
		public IntVec3 designation;
		public IntVec3 lastOrder;
		public Vector3 startPosition;
		public Vector3 offsetFromCenter;
		public bool originalDraftStatus;

		public Colonist(Pawn pawn)
		{
			this.pawn = pawn;
			startPosition = pawn.DrawPos;
			lastOrder = IntVec3.Invalid;
			offsetFromCenter = Vector3.zero;
			designation = IntVec3.Invalid;
			originalDraftStatus = Tools.GetDraftingStatus(pawn);
		}

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

			var bestCell = RCellFinder.BestOrderedGotoDestNear(cell, pawn);
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
				Tools.OrderToSynced(pawn, bestCell.x, bestCell.z);
			}
		}
	}
}
