using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using UnityEngine;
using RimWorld;

namespace AchtungMod
{
	public abstract class JobDriver_Thoroughly : JobDriver
	{
		public HashSet<IntVec3> workLocations = null;
		public TargetInfo currentItem = null;
		public bool isMoving = false;
		public float subCounter = 0;
		public float currentWorkCount = -1f;
		public float totalWorkCount = -1f;

		public virtual string GetPrefix()
		{
			return "DoThoroughly";
		}

		public virtual string GetLabel()
		{
			return (GetPrefix() + "Label").Translate();
		}

		public virtual JobDef MakeJobDef()
		{
			JobDef def = new JobDef();
			def.driverClass = GetType();
			def.collideWithPawns = false;
			def.defName = GetPrefix();
			def.label = GetLabel();
			def.reportString = (GetPrefix() + "InfoText").Translate();
			def.description = (GetPrefix() + "Description").Translate();
			def.playerInterruptible = true;
			def.canCheckOverrideOnDamage = true;
			def.suspendable = true;
			def.alwaysShowWeapon = false;
			def.neverShowWeapon = true;
			def.casualInterruptible = true;
			return def;
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Collections.LookHashSet<IntVec3>(ref this.workLocations, "workLocations");
			Scribe_Values.LookValue<bool>(ref this.isMoving, "isMoving", false, false);
			Scribe_Values.LookValue<float>(ref this.subCounter, "subCounter", 0, false);
			Scribe_Values.LookValue<float>(ref this.currentWorkCount, "currentWorkCount", -1f, false);
			Scribe_Values.LookValue<float>(ref this.totalWorkCount, "totalWorkCount", -1f, false);
		}

		public virtual IEnumerable<TargetInfo> CanStart(Pawn pawn, Vector3 clickPos)
		{
			this.pawn = pawn;
			return null;
		}

		public List<Job> SameJobTypesOngoing()
		{
			List<Job> jobs = new List<Job>();
			if (pawn.jobs == null) return jobs;
			JobQueue queue = pawn.jobs.jobQueue;
			if (queue == null) return jobs;
			for (int i = -1; i < queue.Count; i++)
			{
				Job job = i == -1 && pawn.CurJob != null ? pawn.CurJob : queue[i];
				if (job.def.driverClass.IsInstanceOfType(this)) jobs.Add(job);
			}
			return jobs;
		}

		public virtual void StartJob(Pawn pawn, TargetInfo target)
		{
			Job job = new Job(MakeJobDef(), target);
			job.playerForced = true;
			pawn.jobs.StartJob(job, JobCondition.InterruptForced, null, true, true, null);
		}

		public virtual float Progress()
		{
			if (currentWorkCount <= 0f || totalWorkCount <= 0f) return 0f;
			return (totalWorkCount - currentWorkCount) / totalWorkCount;
		}

		public virtual void UpdateVerbAndWorkLocations()
		{
		}

		public virtual TargetInfo FindNextWorkItem()
		{
			return null;
		}

		public override void Notify_PatherArrived()
		{
			isMoving = false;
		}

		public virtual void InitAction()
		{
			workLocations = new HashSet<IntVec3>() { TargetA.Cell };
			currentItem = null;
			isMoving = false;
			subCounter = 0;
			currentWorkCount = -1f;
			totalWorkCount = -1f;
		}

		public virtual bool DoWorkToItem()
		{
			return true;
		}

		public virtual bool CurrentItemInvalid()
		{
			return
				currentItem == null ||
				(currentItem.Thing != null && currentItem.Thing.Destroyed) ||
				currentItem.Cell.IsValid == false ||
				(currentItem.Cell.x == 0 && currentItem.Cell.z == 0);
		}

		public Func<bool> GetPawnBreakLevel()
		{
			MentalBreaker mb = pawn.mindState.mentalBreaker;
			switch (Settings.instance.breakLevel)
			{
				case BreakLevel.Minor:
					return () => mb.BreakMinorIsImminent;
				case BreakLevel.Major:
					return () => mb.BreakMajorIsImminent;
				case BreakLevel.AlmostExtreme:
					return () => mb.BreakExtremeIsApproaching;
				case BreakLevel.Extreme:
					return () => mb.BreakExtremeIsImminent;
			}
			return () => false;
		}

		public Func<bool> GetPawnHealthLevel()
		{
			Pawn_HealthTracker ht = pawn.health;
			switch (Settings.instance.healthLevel)
			{
				case HealthLevel.ShouldBeTendedNow:
					return () => ht.ShouldBeTendedNow || ht.ShouldDoSurgeryNow;
				case HealthLevel.PrefersMedicalRest:
					return () => ht.PrefersMedicalRest;
				case HealthLevel.NeedsMedicalRest:
					return () => ht.NeedsMedicalRest;
				case HealthLevel.InPainShock:
					return () => ht.InPainShock;
			}
			return () => false;
		}

		public virtual void CheckJobCancelling()
		{
			if (pawn.Dead || pawn.Downed || pawn.HasAttachment(ThingDefOf.Fire))
			{
				Find.PawnDestinationManager.UnreserveAllFor(pawn);
				EndJobWith(JobCondition.Incompletable);
				return;
			}

			if (GetPawnBreakLevel()())
			{
				Find.PawnDestinationManager.UnreserveAllFor(pawn);
				EndJobWith(JobCondition.Incompletable);
				string jobName = (GetPrefix() + "Label").Translate();
				string label = "JobInterruptedLabel".Translate(jobName);
				Find.LetterStack.ReceiveLetter(new Letter(label, "JobInterruptedBreakdown".Translate(pawn.NameStringShort), LetterType.BadNonUrgent, pawn));
				return;
			}

			if (GetPawnHealthLevel()())
			{
				Find.PawnDestinationManager.UnreserveAllFor(pawn);
				EndJobWith(JobCondition.Incompletable);
				string jobName = (GetPrefix() + "Label").Translate();
				Find.LetterStack.ReceiveLetter(new Letter("JobInterruptedLabel".Translate(jobName), "JobInterruptedBadHealth".Translate(pawn.NameStringShort), LetterType.BadNonUrgent, pawn));
				return;
			}
		}

		public virtual void TickAction()
		{
			CheckJobCancelling();
			UpdateVerbAndWorkLocations();

			if (CurrentItemInvalid())
			{
				currentItem = FindNextWorkItem();
				if (CurrentItemInvalid() == false)
				{
					Find.Reservations.Reserve(pawn, currentItem);
					pawn.CurJob.SetTarget(TargetIndex.A, currentItem);
				}
			}
			if (CurrentItemInvalid())
			{
				EndJobWith(JobCondition.Succeeded);
				return;
			}

			if (pawn.Position.AdjacentTo8WayOrInside(currentItem))
			{
				bool itemCompleted = DoWorkToItem();
				if (itemCompleted) currentItem = null;
			}
			else if (!isMoving)
			{
				pawn.pather.StartPath(currentItem, PathEndMode.Touch);
				isMoving = true;
			}
		}

		public override string GetReport()
		{
			return (GetPrefix() + "Report").Translate(Math.Floor(Progress() * 100f) + "%");
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