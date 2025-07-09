using System;
using RimWorld;
using Verse;

namespace AchtungMod
{
	public class FloatMenuOptionProvider_FightFire : FloatMenuOptionProvider
	{
		public override bool Drafted => true;
		public override bool Undrafted => true;
		public override bool Multiselect => true;
		public override bool RequiresManipulation => true;

		public override bool SelectedPawnValid(Pawn pawn, FloatMenuContext context)
		{
			var driver = Activator.CreateInstance<JobDriver_FightFire>();
			return driver.CanStart(pawn, context.ClickedCell) != null;
		}

		public override FloatMenuOption GetSingleOption(FloatMenuContext context)
		{
			var driver = (JobDriver_Thoroughly)Activator.CreateInstance(typeof(JobDriver_FightFire));
			var existingJobs = driver.SameJobTypesOngoing();
			var suffix = existingJobs.Count > 0 ? " " + "AlreadyDoing".Translate("" + (existingJobs.Count + 1)) : new TaggedString("");
			return new FloatMenuOption(driver.GetLabel() + suffix, () => StartWork(context, driver), MenuOptionPriority.Low);
		}

		static void StartWork(FloatMenuContext context, JobDriver_Thoroughly driver)
		{
			foreach (var pawn in context.ValidSelectedPawns)
				driver.StartJob(pawn, context.ClickedCell, context.ClickedCell);
		}
	}
}