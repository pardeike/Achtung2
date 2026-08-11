using System;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace AchtungMod;

public class Colonist(Pawn pawn)
{
	public Pawn pawn = pawn;
	public IntVec3 designation = IntVec3.Invalid;
	public IntVec3 lastOrder = pawn.Position;
	public Vector3 startPosition = pawn.DrawPos;
	public Vector3 offsetFromCenter = Vector3.zero;
	public bool originalDraftStatus = Tools.GetDraftingStatus(pawn);
	public bool draftRequestedForPositioning;

	public bool DraftedForPositioning => pawn.Drafted || draftRequestedForPositioning;

	public IntVec3 UpdateOrderPos(Vector3 pos)
		=> UpdateOrderPos(pos, null);

	public IntVec3 UpdateOrderPos(Vector3 pos, Predicate<IntVec3> cellValidator = null)
	{
		var cell = pos.ToIntVec3();
		var map = pawn.Map;

		if (AchtungLoader.IsSameSpotInstalled)
		{
			if (cell.Standable(map)
				&& (cellValidator?.Invoke(cell) ?? true)
				&& ReachabilityUtility.CanReach(pawn, cell, PathEndMode.OnCell, Danger.Deadly))
			{
				designation = cell;
				return cell;
			}
		}

		if (Tools.TryGetStandableMoveAnchor(cell, map, out var moveAnchor))
			cell = moveAnchor;

		var bestCell = IntVec3.Invalid;
		if (ModsConfig.BiotechActive && pawn.IsColonyMech && MechanitorUtility.InMechanitorCommandRange(pawn, cell) == false)
		{
			var overseer = pawn.GetOverseer();
			var overseerMap = overseer.MapHeld;
			if (overseerMap == pawn.MapHeld)
			{
				var mechanitor = overseer.mechanitor;
				foreach (var newPos in GenRadial.RadialCellsAround(cell, 20f, false))
					if (mechanitor.CanCommandTo(newPos))
						if ((cellValidator?.Invoke(newPos) ?? true)
							&& overseerMap.pawnDestinationReservationManager.CanReserve(newPos, pawn, true)
							&& newPos.Standable(overseerMap)
							&& pawn.CanReach(newPos, PathEndMode.OnCell, Danger.Deadly, false, false, TraverseMode.ByPawn)
						)
						{
							bestCell = newPos;
							break;
						}
			}
		}
		else
			bestCell = RCellFinder.BestOrderedGotoDestNear(cell, pawn, cellValidator);
		if (bestCell.InBounds(map))
		{
			designation = bestCell;
			return bestCell;
		}
		return IntVec3.Invalid;
	}

	public void OrderTo(Vector3 pos)
	{
		var bestCell = UpdateOrderPos(pos);
		if (bestCell.IsValid && bestCell != lastOrder)
		{
			lastOrder = bestCell;
			Tools.OrderTo(pawn, bestCell.x, bestCell.z);
		}
	}

	// implement equals based on pawn
	public override bool Equals(object obj)
	{
		if (obj is Colonist other)
			return pawn == other.pawn;
		return false;
	}
	public override int GetHashCode() => pawn.GetHashCode();
}
