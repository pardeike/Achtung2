using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using System.Linq;

namespace AchtungMod
{
	public class RoomInfo
	{
		public Room room;
		public bool valid;
		public string error;

		public RoomInfo(Room room, bool valid, string error)
		{
			this.room = room;
			this.valid = valid;
			this.error = error;
		}
	}

	public class JobDriver_CleanRoom : JobDriver
	{
		Filth currentFilth = null;
		bool isMoving = false;
		float cleaningCounter = 0;
		float currentFilthCount = -1f;
		float totalFilthCount = -1f;

		private static Func<SoundDef> soundFunc;

		public static JobDef makeJobDef()
		{
			JobDef def = new JobDef();
			def.driverClass = typeof(JobDriver_CleanRoom);
			def.collideWithPawns = false;
			def.defName = "CleanRoom";
			def.label = "CleanThisRoom".Translate();
			def.reportString = "CleanInfoText".Translate();
			def.description = "CleanDescription".Translate();
			def.playerInterruptible = true;
			def.canCheckOverrideOnDamage = true;
			def.suspendable = true;
			def.alwaysShowWeapon = false;
			def.neverShowWeapon = true;
			def.casualInterruptible = true;
			return def;
		}

		public static RoomInfo CanStart(Pawn pawn, IntVec3 loc)
		{
			return CleanableRoomAt(pawn, loc);
		}

		public static void StartJob(Pawn pawn, IntVec3 loc)
		{
			// pawn.jobs.debugLog = true;

			Job job = new Job(makeJobDef(), new TargetInfo(loc));
			job.playerForced = true;
			pawn.jobs.StartJob(job, JobCondition.InterruptForced, null, true, true, null);
		}

		private float Progress()
		{
			if (currentFilthCount <= 0f || totalFilthCount <= 0f) return 0f;
			return (totalFilthCount - currentFilthCount) / totalFilthCount;
		}

		public static IEnumerable<Filth> AllFilthInRoom(Room room, Pawn pawn)
		{
			return room
				.AllContainedThings.OfType<Filth>()
				.Where(f => f.Destroyed == false && pawn.CanReach(f, PathEndMode.Touch, pawn.NormalMaxDanger()) && pawn.CanReserve(f, 1))
				.OrderBy(f => Math.Abs(f.Position.x - pawn.Position.x) + Math.Abs(f.Position.z - pawn.Position.z));
		}

		public static RoomInfo CleanableRoomAt(Pawn pawn, IntVec3 loc)
		{
			Room room = RoomQuery.RoomAt(loc);
			if (room == null) return new RoomInfo(null, false, "NotARoom".Translate());
			if (room.IsHuge) return new RoomInfo(room, false, "RoomTooLarge".Translate());
			if (AllFilthInRoom(room, pawn).Count() == 0) return new RoomInfo(room, false, "RoomHasNoFilth".Translate());
			return new RoomInfo(room, true, "");
		}

		private Filth FindNextFilthToClean()
		{
			RoomInfo info = CleanableRoomAt(pawn, TargetA.Cell);
			if (info.valid == false) return null;
			IEnumerable<Filth> roomFilth = AllFilthInRoom(info.room, pawn);
			currentFilthCount = roomFilth.Count();
			if (totalFilthCount < currentFilthCount) totalFilthCount = currentFilthCount;
			return roomFilth.FirstOrDefault();
		}

		public override void Notify_PatherArrived()
		{
			isMoving = false;
		}

		private void InitAction()
		{
			currentFilth = null;
			isMoving = false;
			cleaningCounter = 0;
			currentFilthCount = -1f;
			totalFilthCount = -1f;
		}

		private void TickAction()
		{
			if (currentFilth == null || currentFilth.Destroyed)
			{
				currentFilth = FindNextFilthToClean();
				if (currentFilth != null)
				{
					Find.Reservations.Reserve(pawn, currentFilth);
					pawn.CurJob.SetTarget(TargetIndex.A, new TargetInfo(currentFilth));
				}
			}
			if (currentFilth == null)
			{
				ReadyForNextToil();
				return;
			}

			if (pawn.Position.AdjacentTo8WayOrInside(currentFilth))
			{
				cleaningCounter++;
				if (cleaningCounter > currentFilth.def.filth.cleaningWorkToReduceThickness)
				{
					currentFilth.ThinFilth();
					cleaningCounter = 0f;
				}
				if (currentFilth.Destroyed) currentFilth = null;
			}
			else if (!isMoving)
			{
				pawn.pather.StartPath(new TargetInfo(currentFilth), PathEndMode.Touch);
				isMoving = true;
			}
		}

		public override string GetReport()
		{
			RoomInfo info = CleanableRoomAt(pawn, TargetA.Cell);
			string name = info.room != null ? info.room.Role.label : "room";
			return "CleaningReport".Translate(name, info.valid ? Math.Floor(Progress() * 100f) + "%" : info.error);
		}

		protected override IEnumerable<Toil> MakeNewToils()
		{
			Toil toil = new Toil();
			toil.initAction = new Action(InitAction);
			toil.tickAction = new Action(TickAction);
			toil.WithEffect("Clean", TargetIndex.A);
			toil.WithProgressBar(TargetIndex.A, Progress, true, -0.5f);
			if (soundFunc == null) soundFunc = new Func<SoundDef>(() => { return SoundDefOf.Interact_CleanFilth; });
			toil.WithSustainer(soundFunc);
			toil.defaultCompleteMode = ToilCompleteMode.Never;
			yield return toil;
		}
	}
}