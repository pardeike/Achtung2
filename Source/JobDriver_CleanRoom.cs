using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace AchtungMod
{
	public class JobDriver_CleanRoom : JobDriver_Thoroughly
	{
		Room room;

		public override string GetPrefix()
		{
			return "CleanRoom";
		}

		public override EffecterDef GetWorkIcon()
		{
			return EffecterDefOf.Clean;
		}

		public override IEnumerable<LocalTargetInfo> CanStart(Pawn thePawn, LocalTargetInfo clickCell)
		{
			_ = base.CanStart(thePawn, clickCell);
			var cleanDef = DefDatabase<WorkTypeDef>.GetNamed("Cleaning");
			if (thePawn.workSettings == null)
				return null;
			if (Achtung.Settings.ignoreAssignments == false && thePawn.workSettings.GetPriority(cleanDef) == 0)
				return null;

			if (AllFilth(clickCell.Cell).Any())
				return new List<LocalTargetInfo> { clickCell };
			return null;
		}

		public override LocalTargetInfo FindNextWorkItem()
		{
			var filth = AllFilth();
			currentWorkCount = filth.Count();
			if (totalWorkCount < currentWorkCount) totalWorkCount = currentWorkCount;
			var pos = pawn.Position;
			return filth.OrderBy(f => (pos - f.Position).LengthHorizontalSquared).FirstOrDefault();
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
			var name = room == null ? "" : " " + room.Role.label;
			return (GetPrefix() + "Report").Translate(name, room == null ? "-" : Math.Floor(Progress() * 100f) + "%");
		}

		public override bool TryMakePreToilReservations(bool errorOnFailed)
		{
			return true;
		}

		//

		private IEnumerable<Filth> AllFilth(IntVec3? useCell = null)
		{
			room = ValidateRoom(room ?? RoomAt(useCell ?? TargetLocA));
			room = room ?? ValidateRoom(RoomAt(useCell ?? TargetLocA));
			if (room == null) return new List<Filth>();

			var pathGrid = pawn.Map.pathGrid;
			if (pathGrid == null)
				return new List<Filth>();

			return room
				.ContainedAndAdjacentThings.OfType<Filth>()
				.Where(f =>
				{
					if (f == null || f.Destroyed) return false;
					if (pathGrid.Walkable(f.Position) == false) return false;
					var fRoom = f.GetRoom();
					if (fRoom == null) return false;
					if (fRoom != room && fRoom.IsDoorway == false) return false;
					if (pawn.CanReach(f, PathEndMode.Touch, pawn.NormalMaxDanger()) == false) return false;
					if (pawn.CanReserve(f, 1) == false) return false;
					return true;
				});
		}

		private Room ValidateRoom(Room room)
		{
			if (room == null) return null;
			if (room.Dereferenced) return null;
			if (room.IsHuge) return null;
			if (room.RegionCount == 0) return null;
			if (room.Group.AnyRoomTouchesMapEdge) return null;
			return room;
		}

		private Room RoomAt(IntVec3 cell)
		{
			if (cell.IsValid == false) return null;
			return RegionAndRoomQuery.RoomAt(cell, pawn.Map);
		}
	}
}