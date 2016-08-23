using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace AchtungMod
{
	public class Colonist
	{
		private static FieldInfo draftHandlerField = typeof(DraftStateHandler).GetField("draftedInt", BindingFlags.NonPublic | BindingFlags.Instance);

		public Pawn pawn;
		public Vector3 designation;
		public IntVec3 lastOrder;
		public Vector3 startPosition;
		public bool originalDraftStatus;

		public Colonist(Pawn pawn, bool forceDraft)
		{
			this.pawn = pawn;
			startPosition = pawn.DrawPos;
			lastOrder = IntVec3.Invalid;
			designation = Vector3.zero;
			originalDraftStatus = Tools.GetDraftingStatus(pawn);

			if (forceDraft) Tools.SetDraftStatus(pawn, true);
		}

		public IntVec3 UpdateOrderPos(Vector3 pos)
		{
			IntVec3 bestCell = Pawn_DraftController.BestGotoDestNear(pos.ToIntVec3(), pawn);
			if (bestCell != null && bestCell.InBounds())
			{
				designation = bestCell.ToVector3Shifted();
				designation.y = Altitudes.AltitudeFor(AltitudeLayer.Pawn);
				return bestCell;
			}
			return IntVec3.Invalid;
		}

		public void OrderTo(Vector3 pos)
		{
			IntVec3 bestCell = UpdateOrderPos(pos);
			if (bestCell.IsValid && lastOrder.IsValid == false || lastOrder != bestCell)
			{
				lastOrder = bestCell;

				Job job = new Job(JobDefOf.Goto, bestCell);
				if (pawn.drafter.CanTakeOrderedJob())
				{
					pawn.drafter.TakeOrderedJob(job);
				}
			}
		}
	}
}
