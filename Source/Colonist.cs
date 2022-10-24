using RimWorld;
using UnityEngine;
using UnityEngine.UIElements;
using Verse;
using Verse.AI;
using static RimWorld.MechClusterSketch;
using static UnityEngine.GraphicsBuffer;

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
				Tools.OrderToSynced(pawn, bestCell.x, bestCell.z);
			}
		}
	}
}
