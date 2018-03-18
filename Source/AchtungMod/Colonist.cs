using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace AchtungMod
{
	public class Colonist
	{
		public Pawn pawn;
		public Vector3 designation;
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
			designation = Vector3.zero;
			originalDraftStatus = Tools.GetDraftingStatus(pawn);
		}

		public IntVec3 UpdateOrderPos(Vector3 pos)
		{
			var cell = pos.ToIntVec3();
			if (cell.Standable(pawn.Map) && pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly, false, TraverseMode.ByPawn))
			{
				designation = cell.ToVector3Shifted();
				designation.y = Altitudes.AltitudeFor(AltitudeLayer.Pawn);
				return cell;
			}

			var bestCell = RCellFinder.BestOrderedGotoDestNear(cell, pawn);
			if (bestCell.InBounds(pawn.Map))
			{
				designation = bestCell.ToVector3Shifted();
				designation.y = Altitudes.AltitudeFor(AltitudeLayer.Pawn);
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

				var job = new Job(JobDefOf.Goto, bestCell)
				{
					playerForced = true,
					collideWithPawns = false,
					locomotionUrgency = LocomotionUrgency.Sprint
				};
				if (pawn.jobs.IsCurrentJobPlayerInterruptible())
					pawn.jobs.TryTakeOrderedJob(job);
			}
		}
	}
}