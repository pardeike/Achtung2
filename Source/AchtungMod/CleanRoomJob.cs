using Verse;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse.AI;

namespace AchtungMod
{
	// to get the ultimate cleaning experience, one has to double check after every cleaning job if the room
	// has become dirty again (by the one cleaning or by someone/something else). This job is recursively calling
	// itself to check for filth and if found schedules clean jobs for every cell and one extra job to check the
	// room again
	//
	public class CheckRoomJob : JobDriver
	{
		// the jobdef triggers the instantiation of the class
		//
		public static JobDef makeJobDef()
		{
			JobDef def = new JobDef();
			def.driverClass = typeof(CheckRoomJob);
			def.reportString = "Check and clean room if necessary";
			def.playerInterruptible = true; // set this to false and the cleaning will try hard to continue :)
			def.canCheckOverrideOnDamage = false;
			def.suspendable = false;
			def.alwaysShowWeapon = false;
			def.neverShowWeapon = false;
			def.casualInterruptible = false; // we want it done NOW
			return def;
		}

		// main entry point
		// 
		public static void checkRoomAndCleanIfNecessary(IntVec3 loc, Pawn colonist)
		{
			TargetInfo target = new TargetInfo(loc);
			Job checkJob = new Job(makeJobDef(), target);
			colonist.QueueJob(checkJob);
		}

		// checks for unreserved filth for the room at 'loc'
		//
		public static bool hasFilthToClean(Pawn colonist, IntVec3 loc)
		{
			Room room = RoomQuery.RoomAt(loc);
			if (room == null || room.IsHuge)
			{
				return false;
			}

			if (colonist.workSettings.GetPriority(WorkTypeDefOf.Cleaning) <= 0)
			{
				return false;
			}

			List<Filth> filth = room.AllContainedThings.OfType<Filth>().ToList();
			bool result = filth.Any(f => colonist.CanReserve(f, 1));
			return result;
		}

		// called by the job scheduler to return a number of work units
		//
		protected override IEnumerable<Toil> MakeNewToils()
		{
			Room room = RoomQuery.RoomAt(TargetA.Cell);
			if (room != null)
			{
				Filth filth = null;
				try
				{
					filth = room.AllContainedThings.OfType<Filth>().ToList().Find(
						 f => pawn.CanReach(f, PathEndMode.ClosestTouch, pawn.NormalMaxDanger())
							  && pawn.CanReserve(f, 1)
					);
				}
				catch (System.Exception)
				{
				}
				if (filth != null)
				{
					Toil reserveToil = Toils_Reserve.Reserve(TargetIndex.A);
					reserveToil.FailOn((Toil t) =>
					{
						return t.actor.CanReserve(filth) == false;
					});
					yield return reserveToil;

					Job job = new Job(JobDefOf.Clean, filth);
					job.playerForced = true;
					pawn.QueueJob(job);
					Toil cleanToil = Toils_JobTransforms.ExtractNextTargetFromQueue(TargetIndex.A);
					cleanToil.atomicWithPrevious = true;
					yield return cleanToil;

					Toil releaseToil = Toils_Reserve.Release(TargetIndex.A);
					releaseToil.atomicWithPrevious = true;
					yield return releaseToil;

					checkRoomAndCleanIfNecessary(TargetA.Cell, pawn);
					Toil checkToil = Toils_JobTransforms.ExtractNextTargetFromQueue(TargetIndex.A);
					checkToil.atomicWithPrevious = true;
					yield return checkToil;
				}
			}
			yield break;
		}
	}
}