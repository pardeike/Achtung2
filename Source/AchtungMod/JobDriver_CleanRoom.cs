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

		public override IEnumerable<TargetInfo> CanStart(Pawn pawn, Vector3 clickPos)
		{
			base.CanStart(pawn, clickPos);
			if (pawn.workSettings.GetPriority(WorkTypeDefOf.Cleaning) == 0) return null;
			TargetInfo cell = IntVec3.FromVector3(clickPos);
			RoomInfo info = WorkInfoAt(pawn, cell);
			return (info.valid && info.room != null) ? new List<TargetInfo> { cell } : null;
		}

		// filth in room
		public IEnumerable<Thing> AllWorkInRoom(Room room, Pawn pawn)
		{
			return room
				.AllContainedThings.OfType<Filth>().Cast<Thing>()
				.Where(f => f.Destroyed == false && pawn.CanReach(f, PathEndMode.Touch, pawn.NormalMaxDanger()) && pawn.CanReserve(f, 1))
				.OrderBy(f => Math.Abs(f.Position.x - pawn.Position.x) + Math.Abs(f.Position.z - pawn.Position.z));
		}

		// room info
		public RoomInfo WorkInfoAt(Pawn pawn, TargetInfo target)
		{
			Room room = RoomQuery.RoomAt(target.Cell);
			if (room == null || room.IsHuge) return new RoomInfo(room, false);
			if (AllWorkInRoom(room, pawn).Count() == 0) return new RoomInfo(room, false);
			return new RoomInfo(room, true);
		}

		public override TargetInfo FindNextWorkItem()
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
			toil.WithEffect("Clean", TargetIndex.A);
			yield return toil;
		}
	}
}