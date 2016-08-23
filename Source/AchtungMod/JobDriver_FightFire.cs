using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using System.Linq;

namespace AchtungMod
{
	public class FireInfo
	{
		public Fire fire;
		public bool valid;
		public string error;

		public FireInfo(Fire fire, bool valid, string error)
		{
			this.fire = fire;
			this.valid = valid;
			this.error = error;
		}
	}

	public class JobDriver_FightFire : JobDriver
	{
		HashSet<IntVec3> fireLocations = null;
		Fire currentFire = null;
		bool isMoving = false;
		float currentFireCount = -1f;
		float totalFireCount = -1f;

		public static JobDef makeJobDef()
		{
			JobDef def = new JobDef();
			def.driverClass = typeof(JobDriver_FightFire);
			def.collideWithPawns = false;
			def.defName = "FightFire";
			def.label = "FightThisFire".Translate();
			def.reportString = "ExtinguishInfoText".Translate();
			def.description = "ExtinguishDescription".Translate();
			def.playerInterruptible = true;
			def.canCheckOverrideOnDamage = true;
			def.suspendable = true;
			def.alwaysShowWeapon = false;
			def.neverShowWeapon = true;
			def.casualInterruptible = true;
			return def;
		}

		public static bool CanStart(Pawn pawn, IntVec3 loc)
		{
			Fire fire = Find.ThingGrid.ThingAt(loc, ThingDefOf.Fire) as Fire;
			if (fire == null) return false;
			return fire.Destroyed == false && pawn.CanReach(fire, PathEndMode.Touch, pawn.NormalMaxDanger()) && pawn.CanReserve(fire, 1);
		}

		public static void StartJob(Pawn pawn, IntVec3 loc)
		{
			pawn.jobs.debugLog = true;

			Job job = new Job(makeJobDef(), new TargetInfo(loc));
			job.playerForced = true;
			pawn.jobs.StartJob(job, JobCondition.InterruptForced, null, true, true, null);
		}

		public float Progress()
		{
			if (currentFireCount <= 0f || totalFireCount <= 0f) return 0f;
			return (totalFireCount - currentFireCount) / totalFireCount;
		}

		public void UpdateFireLocations()
		{
			List<IntVec3> locations = fireLocations.ToList();
			locations.ForEach(pos =>
			{
				if (Find.ThingGrid.CellContains(pos, ThingDefOf.Fire) == false) fireLocations.Remove(pos);
				GenAdj.CellsAdjacent8Way(new TargetInfo(pos))
					.Where(loc => fireLocations.Contains(loc) == false)
					.Where(loc => Find.ThingGrid.CellContains(loc, ThingDefOf.Fire))
					.ToList().ForEach(loc => fireLocations.Add(loc));
			});
			currentFireCount = locations.Count();
			if (totalFireCount < currentFireCount) totalFireCount = currentFireCount;
		}

		public Fire FindNextFireToExtinguish()
		{
			return fireLocations
				.OrderBy(loc => Math.Abs(loc.x - pawn.Position.x) + Math.Abs(loc.z - pawn.Position.z))
				.Select(loc => Find.ThingGrid.ThingAt(loc, ThingDefOf.Fire) as Fire)
				.Where(f => f.Destroyed == false && pawn.CanReach(f, PathEndMode.Touch, pawn.NormalMaxDanger()) && pawn.CanReserve(f, 1))
				.FirstOrDefault();
		}

		public override void Notify_PatherArrived()
		{
			isMoving = false;
		}

		public void InitAction()
		{
			fireLocations = new HashSet<IntVec3>() { TargetA.Cell };
			currentFire = null;
			isMoving = false;
			currentFireCount = -1f;
			totalFireCount = -1f;
		}

		public void TickAction()
		{
			UpdateFireLocations();

			if (currentFire == null || currentFire.Destroyed)
			{
				currentFire = FindNextFireToExtinguish();
				if (currentFire != null)
				{
					Find.Reservations.Reserve(pawn, currentFire);
					pawn.CurJob.SetTarget(TargetIndex.A, new TargetInfo(currentFire));
				}
			}
			if (currentFire == null)
			{
				ReadyForNextToil();
				return;
			}

			if (pawn.Position.AdjacentTo8WayOrInside(currentFire))
			{
				pawn.natives.TryBeatFire(currentFire);
				if (currentFire.Destroyed)
				{
					pawn.records.Increment(RecordDefOf.FiresExtinguished);
					currentFire = null;
				}
			}
			else if (!isMoving)
			{
				pawn.pather.StartPath(new TargetInfo(currentFire), PathEndMode.Touch);
				isMoving = true;
			}
		}

		public override string GetReport()
		{
			return "FightingFireReport".Translate(Math.Floor(Progress() * 100f) + "%");
		}

		protected override IEnumerable<Toil> MakeNewToils()
		{
			Toil toil = new Toil();
			toil.initAction = new Action(InitAction);
			toil.tickAction = new Action(TickAction);
			toil.WithProgressBar(TargetIndex.A, Progress, true, -0.5f);
			toil.defaultCompleteMode = ToilCompleteMode.Never;
			yield return toil;
		}
	}
}