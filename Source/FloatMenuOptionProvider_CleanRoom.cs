using System;
using System.Linq;
using RimWorld;
using Verse;

namespace AchtungMod;

public class FloatMenuOptionProvider_CleanRoom : FloatMenuOptionProvider
{
	public override bool Drafted => true;
	public override bool Undrafted => true;
	public override bool Multiselect => true;
	public override bool RequiresManipulation => true;

	public override bool SelectedPawnValid(Pawn pawn, FloatMenuContext context)
	{
		var driver = Activator.CreateInstance<JobDriver_CleanRoom>();
		return driver.CanStart(pawn, context.ClickedCell) != null;
	}

	public override FloatMenuOption GetSingleOption(FloatMenuContext context)
	{
		if (Achtung.Settings.replaceCleanRoom == false) return null;
		var driver = Activator.CreateInstance<JobDriver_CleanRoom>();
		var existingJobs = context.ValidSelectedPawns.Select(p => driver.SameJobTypesOngoing(p).Count).DefaultIfEmpty().Max();
		var suffix = existingJobs > 0 ? " " + "AlreadyDoing".Translate("" + (existingJobs + 1)) : new TaggedString("");
		return new SupplementalFloatMenuOption(driver.GetLabel() + suffix, () => StartWork(context, driver), MenuOptionPriority.Low);
	}

	static void StartWork(FloatMenuContext context, JobDriver_Thoroughly driver)
	{
		if (MultiplayerSupport.IsActive)
		{
			MultiplayerSupport.StartThoroughWork([.. context.ValidSelectedPawns], context.ClickedCell, ThoroughWorkType.CleanRoom);
			return;
		}

		foreach (var pawn in context.ValidSelectedPawns)
			driver.StartJob(pawn, context.ClickedCell, context.ClickedCell);
	}
}
