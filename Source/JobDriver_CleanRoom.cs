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
		}

		public override IEnumerable<LocalTargetInfo> CanStart(Pawn thePawn, LocalTargetInfo clickCell)
		{
			_ = base.CanStart(thePawn, clickCell);
			var cleanDef = DefDatabase<WorkTypeDef>.GetNamed("Cleaning");
			if (thePawn.workSettings == null || thePawn.workSettings.GetPriority(cleanDef) == 0)
				return null;

			var filthToClean = AllFilth(clickCell);
			if (filthToClean.Any())
				return new List<LocalTargetInfo> { clickCell };
			return null;
		}

		public override LocalTargetInfo FindNextWorkItem()
		{
			var filth = AllFilth(TargetB);
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
			var room = RoomAt(TargetA);
			var name = room == null ? "" : " " + room.Role.label;
			return (GetPrefix() + "Report").Translate(name, room == null ? "-" : Math.Floor(Progress() * 100f) + "%");
		}

		public override bool TryMakePreToilReservations(bool errorOnFailed)
		{
			return true;
		}

		//

		private IEnumerable<Filth> AllFilth(LocalTargetInfo roomLocation)
		{
			var room = RoomAt(roomLocation);
			if (room == null || room.IsHuge)
				return new List<Filth>();

			var pathGrid = pawn.Map.pathGrid;
			if (pathGrid == null)
				return new List<Filth>();

			var filth = room
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

			var filthCount = filth.Count();
			if (filthCount > 0)
			{
				var center = filth.Aggregate(IntVec3.Zero, (prev, f) => prev + f.Position);
				center = new IntVec3(center.x / filthCount, 0, center.z / filthCount);
				filth = filth.OrderBy(f => (currentItem.Cell - f.Position).LengthHorizontalSquared - 2 * (center - f.Position).LengthHorizontalSquared);
			}

			return filth;
		}

		private Room RoomAt(LocalTargetInfo target)
		{
			var cell = target.Cell;
			if (cell.IsValid == false)
				return null;
			var theRoom = RegionAndRoomQuery.RoomAt(cell, pawn.Map);
			if (theRoom == null || theRoom.IsHuge || theRoom.Group.AnyRoomTouchesMapEdge)
				return null;
			return theRoom;
		}
	}
}