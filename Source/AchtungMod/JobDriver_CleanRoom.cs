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

		public override IEnumerable<LocalTargetInfo> CanStart(Pawn pawn, Vector3 clickPos)
		{
			base.CanStart(pawn, clickPos);
			var cleanDef = DefDatabase<WorkTypeDef>.GetNamed("Cleaning");
			if (pawn.workSettings.GetPriority(cleanDef) == 0) return null;
			LocalTargetInfo cell = IntVec3.FromVector3(clickPos);
			RoomInfo info = WorkInfoAt(pawn, cell);
			return (info.valid && info.room != null) ? new List<LocalTargetInfo> { cell } : null;
		}

		// filth in room
		public IEnumerable<Thing> AllWorkInRoom(Room room, Pawn pawn)
		{
			return room
				.ContainedAndAdjacentThings.OfType<Filth>().Cast<Thing>()
				.Where(f => f.Destroyed == false && pawn.CanReach(f, PathEndMode.Touch, pawn.NormalMaxDanger()) && pawn.CanReserve(f, 1))
				.OrderBy(f => Math.Abs(f.Position.x - pawn.Position.x) + Math.Abs(f.Position.z - pawn.Position.z));
		}

		// room info
		public RoomInfo WorkInfoAt(Pawn pawn, LocalTargetInfo target)
		{
			Room room = RegionAndRoomQuery.RoomAt(target.Cell, pawn.Map);
			if (room == null || room.IsHuge) return new RoomInfo(room, false);
			if (AllWorkInRoom(room, pawn).Count() == 0) return new RoomInfo(room, false);
			return new RoomInfo(room, true);
		}

		public override LocalTargetInfo FindNextWorkItem()
		{
			RoomInfo info = WorkInfoAt(pawn, TargetA);
			if (info.valid == false) return null;
			IEnumerable<Thing> items = AllWorkInRoom(info.room, pawn);
			currentWorkCount = items.Count();
			if (totalWorkCount < currentWorkCount) totalWorkCount = currentWorkCount;
			return items.FirstOrDefault();
		}

		public override bool DoWorkToItem()
		{
			subCounter++;
			if (subCounter > currentItem.Thing.def.filth.cleaningWorkToReduceThickness)
			{
				Filth filth = currentItem.Thing as Filth;
				if (filth != null) filth.ThinFilth();
				subCounter = 0f;
			}
			return currentItem.Thing.Destroyed;
		}

		public override string GetReport()
		{
			RoomInfo info = WorkInfoAt(pawn, TargetA);
			string name = info.room == null ? "" : " " + info.room.Role.label;
			return (GetPrefix() + "Report").Translate(new object[] { name, info.valid ? Math.Floor(Progress() * 100f) + "%" : "-" });
		}

		protected override IEnumerable<Toil> MakeNewToils()
		{
			Toil toil = base.MakeNewToils().First();
			toil.WithEffect(EffecterDefOf.Clean, TargetIndex.A);

			yield return toil;
		}

		public override bool TryMakePreToilReservations()
		{
			return true;
		}
	}
}