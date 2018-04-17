using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace AchtungMod
{
	public class JobFunctions
	{
		public ForcedJob forcedJob;
		public Func<WorkGiver_Scanner, Pawn, LocalTargetInfo, bool> hasJobOnThingFunc;
		public Func<WorkGiver_Scanner, Pawn, LocalTargetInfo, bool, Job> jobOnThingFunc;
		public Func<WorkGiver_Scanner, Pawn, IntVec3, bool> hasJobOnCellFunc;
		public Func<WorkGiver_Scanner, Pawn, IntVec3, bool, Job> jobOnCellFunc;
		public Func<LocalTargetInfo, IntVec3, int> thingScoreFunc;
		public Func<LocalTargetInfo, string> menuLabelFunc;

		public JobFunctions(Type type)
		{
			forcedJob = (ForcedJob)Activator.CreateInstance(type);
			hasJobOnThingFunc = (workGiver, pawn, thing) => forcedJob.HasJobOnThing(workGiver, pawn, thing);
			jobOnThingFunc = (workGiver, pawn, thing, forced) => forcedJob.JobOnThing(workGiver, pawn, thing, forced);
			hasJobOnCellFunc = (workGiver, pawn, cell) => forcedJob.HasJobOnCell(workGiver, pawn, cell);
			jobOnCellFunc = (workGiver, pawn, cell, forced) => forcedJob.JobOnCell(workGiver, pawn, cell, forced);
			thingScoreFunc = (thing, closeToCell) => forcedJob.ThingScore(thing, closeToCell);
			menuLabelFunc = (thing) => forcedJob.MenuLabel(thing);
		}

		public WorkGiver_Scanner WorkGiver => forcedJob.GetWorkGiver();
	}
}