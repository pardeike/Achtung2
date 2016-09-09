using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using UnityEngine;

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

		public virtual void StartJob(Pawn pawn, TargetInfo target)
		{
			// pawn.jobs.debugLog = true;

			Job job = new Job(MakeJobDef(), target);
			job.playerForced = true;
			pawn.jobs.StartJob(job, JobCondition.InterruptForced, null, true, true, null);
		}

		public virtual float Progress()
		{
			if (currentWorkCount <= 0f || totalWorkCount <= 0f) return 0f;
			return (totalWorkCount - currentWorkCount) / totalWorkCount;
		}

		public virtual void UpdateWorkLocations()
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

		public virtual void TickAction()
		{
			UpdateWorkLocations();

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
				Controller.SetDebugPositions((IEnumerable<ScoredPosition>)null);
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