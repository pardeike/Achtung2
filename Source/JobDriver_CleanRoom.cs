using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using System.Linq;
using UnityEngine;

namespace AchtungMod
{
	public class JobDriver_CleanRoom : JobDriver_Thoroughly
	{
		private IntVec3 initialClickPosition = IntVec3.Invalid;

		public override string GetPrefix()
		{
			return "CleanRoom";
		}

		public override EffecterDef GetWorkIcon()
		{
			return EffecterDefOf.Clean;
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref initialClickPosition, "roomLocation", IntVec3.Invalid, false);
		}

		public override IEnumerable<LocalTargetInfo> CanStart(Pawn thePawn, Vector3 clickPos)
		{
			base.CanStart(thePawn, clickPos);
			var cleanDef = DefDatabase<WorkTypeDef>.GetNamed("Cleaning");
			if (thePawn.workSettings.GetPriority(cleanDef) == 0)
				return null;

			initialClickPosition = IntVec3.FromVector3(clickPos);
			var filthToClean = AllFilth();
			if (filthToClean.Any())
				return new List<LocalTargetInfo> { filthToClean.First() };
			return null;
		}

		public override LocalTargetInfo FindNextWorkItem()
		{
			if (initialClickPosition.IsValid == false)
				initialClickPosition = workLocations.First();

			var filth = AllFilth();
			currentWorkCount = filth.Count();
			if (totalWorkCount < currentWorkCount) totalWorkCount = currentWorkCount;
			return filth.FirstOrDefault();
		}

		public override bool DoWorkToItem()
		{
			subCounter++;
			if (subCounter > currentItem.Thing.def.filth.cleaningWorkToReduceThickness)
			{
				var filth = currentItem.Thing as Filth;
				filth?.ThinFilth();
				if (filth?.Destroyed ?? false)
					pawn.records.Increment(RecordDefOf.MessesCleaned);
				subCounter = 0f;
			}
			return currentItem.Thing.Destroyed;
		}

		public override string GetReport()
		{
			var room = RoomAt(initialClickPosition);
			var name = room == null ? "" : " " + room.Role.label;
			return (GetPrefix() + "Report").Translate(name, room == null ? "-" : Math.Floor(Progress() * 100f) + "%");
		}

		public override bool TryMakePreToilReservations()
		{
			return true;
		}

		//

		private Filth FirstFilthAt(IntVec3 cell)
		{
			return pawn.Map.thingGrid.ThingAt<Filth>(cell);
		}

		private IEnumerable<Filth> AllFilth()
		{
			var room = RoomAt(initialClickPosition);
			if (room == null)
				return new List<Filth>();

			var filth = room
				.ContainedAndAdjacentThings.OfType<Filth>()
				.Where(f =>
					f.Destroyed == false
					&& pawn.CanReach(f, PathEndMode.Touch, pawn.NormalMaxDanger())
					&& pawn.CanReserve(f, 1)
				);

			var filthCount = filth.Count();
			if (filthCount > 0)
			{
				var center = filth.Aggregate(IntVec3.Zero, (prev, f) => prev + f.Position);
				center = new IntVec3(center.x / filthCount, 0, center.z / filthCount);
				filth = filth.OrderBy(f => (currentItem.Cell - f.Position).LengthHorizontalSquared - 2 * (center - f.Position).LengthHorizontalSquared);
			}

			return filth;
		}

		private Room RoomAt(IntVec3 cell)
		{
			if (cell.IsValid == false)
				return null;
			var theRoom = RegionAndRoomQuery.RoomAt(cell, pawn.Map);
			if (theRoom == null || theRoom.IsHuge || theRoom.Group.AnyRoomTouchesMapEdge)
				return null;
			return theRoom;
		}
	}
}