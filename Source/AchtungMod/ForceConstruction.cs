using Verse;
using RimWorld;
using Verse.AI;
using Verse.AI.Group;
using System.Collections.Generic;

namespace AchtungMod
{
	public class ThoroughlyLord : Lord
	{
		public ThoroughlyLord()
		{
			loadID = Find.UniqueIDsManager.GetNextLordID();
			extraForbiddenThings = new List<Thing>();
		}
	}

	public class WorkGiver_ConstructFinishFramesAll : WorkGiver_ConstructFinishFrames
	{
		public WorkGiverDef MakeDef()
		{
			WorkGiverDef original;
			var def = Tools.MakeWorkGiverDef(this, out original);
			if (def == null) return null;
			def.defName = original.defName + "All";
			def.description = original.description + " (All)";
			def.label = original.label + " ALl";
			return def;
		}

		public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
		{
			var job = base.JobOnThing(pawn, t, forced);
			if (job != null)
			{
				job.lord = new ThoroughlyLord();
				job.lord.extraForbiddenThings.Add(t);
				job.lord.extraForbiddenThings = Tools.UpdateCells(job.lord.extraForbiddenThings, pawn, t.Position);
			}
			return job;
		}

		public override Danger MaxPathDanger(Pawn pawn)
		{
			return Danger.Deadly;
		}

		public override float GetPriority(Pawn pawn, TargetInfo t)
		{
			return 100000f;
		}
	}

	public class WorkGiver_ConstructDeliverResourcesToBlueprintsAll : WorkGiver_ConstructDeliverResourcesToBlueprints
	{
		public WorkGiverDef MakeDef()
		{
			WorkGiverDef original;
			var def = Tools.MakeWorkGiverDef(this, out original);
			if (def == null) return null;
			def.defName = original.defName + "All";
			def.description = original.description + " (All)";
			def.label = original.label + " ALl";
			return def;
		}

		public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
		{
			var job = base.JobOnThing(pawn, t, forced);
			if (job != null)
			{
				job.lord = new ThoroughlyLord();
				job.lord.extraForbiddenThings.Add(t);
				job.lord.extraForbiddenThings = Tools.UpdateCells(job.lord.extraForbiddenThings, pawn, t.Position);
			}
			return job;
		}

		public override Danger MaxPathDanger(Pawn pawn)
		{
			return Danger.Deadly;
		}

		public override float GetPriority(Pawn pawn, TargetInfo t)
		{
			return 100000f;
		}
	}

	public class WorkGiver_ConstructDeliverResourcesToFramesAll : WorkGiver_ConstructDeliverResourcesToFrames
	{
		public WorkGiverDef MakeDef()
		{
			WorkGiverDef original;
			var def = Tools.MakeWorkGiverDef(this, out original);
			if (def == null) return null;
			def.defName = original.defName + "All";
			def.description = original.description + " (All)";
			def.label = original.label + " ALl";
			return def;
		}

		public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
		{
			var job = base.JobOnThing(pawn, t, forced);
			if (job != null)
			{
				job.lord = new ThoroughlyLord();
				job.lord.extraForbiddenThings.Add(t);
				job.lord.extraForbiddenThings = Tools.UpdateCells(job.lord.extraForbiddenThings, pawn, t.Position);
			}
			return job;
		}

		public override Danger MaxPathDanger(Pawn pawn)
		{
			return Danger.Deadly;
		}

		public override float GetPriority(Pawn pawn, TargetInfo t)
		{
			return 100000f;
		}
	}
}