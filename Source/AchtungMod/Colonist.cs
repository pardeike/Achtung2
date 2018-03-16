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
		public bool originalDraftStatus;

		public Colonist(Pawn pawn)
		{
			this.pawn = pawn;
			startPosition = pawn.DrawPos;
			lastOrder = IntVec3.Invalid;
			designation = Vector3.zero;
			originalDraftStatus = Tools.GetDraftingStatus(pawn);
		}

		public IntVec3 UpdateOrderPos(Vector3 pos)
		{
			var bestCell = RCellFinder.BestOrderedGotoDestNear(pos.ToIntVec3(), pawn);
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

				var job = new Job(JobDefOf.Goto, bestCell);
				if (pawn.jobs.IsCurrentJobPlayerInterruptible())
				{
					pawn.jobs.TryTakeOrderedJob(job);
				}
			}
		}
	}
}