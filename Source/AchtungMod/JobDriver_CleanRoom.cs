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
		public class RoomInfo
		{
			public Room room;
			public bool valid;

			public RoomInfo(Room room, bool valid)
			{
				this.room = room;
				this.valid = valid;
			}
		}

		public override string GetPrefix()
		{
			return "CleanRoom";
		}

		public override EffecterDef GetWorkIcon()
		{
			return EffecterDefOf.Clean;
		}

		public override IEnumerable<LocalTargetInfo> CanStart(Pawn thePawn, Vector3 clickPos)
		{
			base.CanStart(thePawn, clickPos);
			var cleanDef = DefDatabase<WorkTypeDef>.GetNamed("Cleaning");
			if (thePawn.workSettings.GetPriority(cleanDef) == 0) return null;
			LocalTargetInfo cell = IntVec3.FromVector3(clickPos);
			var info = WorkInfoAt(thePawn, cell);
			return (info.valid && info.room != null) ? new List<LocalTargetInfo> { cell } : null;
		}

		// filth in room
		public IEnumerable<Thing> AllWorkInRoom(Room room, Pawn thePawn)
		{
			return room
				.ContainedAndAdjacentThings.OfType<Filth>().Cast<Thing>()
				.Where(f => f.Destroyed == false && thePawn.CanReach(f, PathEndMode.Touch, thePawn.NormalMaxDanger()) && thePawn.CanReserve(f, 1))
				.OrderBy(f => Math.Abs(f.Position.x - thePawn.Position.x) + Math.Abs(f.Position.z - thePawn.Position.z));
		}

		// room info
		public RoomInfo WorkInfoAt(Pawn targetPawn, LocalTargetInfo target)
		{
			var room = RegionAndRoomQuery.RoomAt(target.Cell, targetPawn.Map);
			if (room == null || room.IsHuge || room.Group.AnyRoomTouchesMapEdge) return new RoomInfo(room, false);
			if (AllWorkInRoom(room, targetPawn).Count() == 0) return new RoomInfo(room, false);
			return new RoomInfo(room, true);
		}

		public override LocalTargetInfo FindNextWorkItem()
		{
			var info = WorkInfoAt(pawn, TargetA);
			if (info.valid == false) return null;
			var items = AllWorkInRoom(info.room, pawn).ToList();
			currentWorkCount = items.Count;
			if (totalWorkCount < currentWorkCount) totalWorkCount = currentWorkCount;
			return items.FirstOrDefault();
		}

		public override bool DoWorkToItem()
		{
			subCounter++;
			if (subCounter > currentItem.Thing.def.filth.cleaningWorkToReduceThickness)
			{
				var filth = currentItem.Thing as Filth;
				filth?.ThinFilth();
				subCounter = 0f;
			}
			return currentItem.Thing.Destroyed;
		}

		public override string GetReport()
		{
			var info = WorkInfoAt(pawn, TargetA);
			var name = info.room == null ? "" : " " + info.room.Role.label;
			return (GetPrefix() + "Report").Translate(name, info.valid ? Math.Floor(Progress() * 100f) + "%" : "-");
		}

		public override bool TryMakePreToilReservations()
		{
			return true;
		}
	}
}