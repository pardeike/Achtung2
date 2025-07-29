using System;
using System.Linq;
using RimWorld;
using Verse;

namespace AchtungMod;

public class FloatMenuOptionProvider_TacticalApproach : FloatMenuOptionProvider
{
	public override bool Drafted => true;
	public override bool Undrafted => false;
	public override bool Multiselect => false;
	public override bool RequiresManipulation => true;

	public override FloatMenuOption GetSingleOption(FloatMenuContext context)
		=> new(JobDriver_TacticalApproach.GetLabel(), () => StartWork(context), MenuOptionPriority.Low);

	static void StartWork(FloatMenuContext context)
	{
		var pawn = context.FirstSelectedPawn;
		var target = context.ClickedPawns.FirstOrDefault();
		if (target != null)
		{
			var newJob = JobMaker.MakeJob(JobDriver_TacticalApproach.jobDef, target);
			newJob.playerForced = true;
			_ = pawn.jobs.TryTakeOrderedJob(newJob);
		}
	}
}