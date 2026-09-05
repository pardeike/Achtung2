using HarmonyLib;
using RimBridgeServer.Sdk;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace AchtungMod;

public sealed partial class AchtungBridgeTools
{
	[Tool("achtung/test_permission_contract", Description = "No-tick production permission checks for a construction target: drafted/undrafted and forbidden OFF/ON scanner jobs, both dependency queues, explicit-target exemption, and nested-scope exception cleanup. Does not issue jobs. Leaves Ignore forbidden OFF.")]
	public static object TestPermissionContract(int x, int z, string pawnId = null)
	{
		var pawn = FindPawn(pawnId);
		if (pawn?.Map == null || pawn.drafter == null || MultiplayerSupport.IsActive)
			return new { success = false, error = "Select a spawned draftable pawn outside multiplayer." };
		var cell = new IntVec3(x, 0, z);
		if (cell.InBounds(pawn.Map) == false)
			return new { success = false, error = "Target cell is outside the map." };
		var target = cell.GetThingList(pawn.Map).FirstOrDefault(thing => thing is Blueprint || thing is Frame);
		var scanner = DefDatabase<WorkGiverDef>.AllDefsListForReading.Select(def => def.Worker)
			.OfType<WorkGiver_Scanner>().FirstOrDefault(giver => target is Frame
				? giver is WorkGiver_ConstructDeliverResourcesToFrames
				: giver is WorkGiver_ConstructDeliverResourcesToBlueprints);
		if (target == null || scanner == null)
			return new { success = false, error = "A blueprint/frame and its resource-delivery workgiver are required." };

		var rows = new List<object>();
		var assertions = new Dictionary<string, bool>();
		var previousDrafted = pawn.drafter.draftedInt;
		var previousJob = pawn.CurJob;
		var forbiddenWood = ThingMaker.MakeThing(ThingDefOf.WoodLog);
		forbiddenWood.SetForbidden(true, false);
		var allowedJob = AccessTools.Method(typeof(ForcedExtensions), "AllowedJob");
		bool Validate(Job job, Thing direct = null) => (bool)allowedJob.Invoke(null, [pawn, job, direct]);
		try
		{
			foreach (var drafted in new[] { false, true })
				foreach (var ignoreForbidden in new[] { false, true })
				{
					pawn.drafter.draftedInt = drafted;
					Achtung.Settings.ignoreForbidden = ignoreForbidden;
					var job = target.GetThingJob(pawn, scanner, directTarget: target);
					var things = job.TargetThings().ToArray();
					var forbiddenDependencies = things.Where(thing => thing != target && thing.IsForbidden(pawn.Faction)).ToArray();
					rows.Add(new
					{
						drafted, ignoreForbidden, job = job?.def?.defName,
						queueACount = job?.targetQueueA?.Count ?? 0,
						queueBCount = job?.targetQueueB?.Count ?? 0,
						targets = things.Select(thing => new { id = thing.ThingID, def = thing.def.defName, position = thing.PositionHeld.ToString(), forbidden = thing.IsForbidden(pawn.Faction) }).ToArray()
					});
					assertions[$"scanner_has_job_drafted_{drafted}_ignore_{ignoreForbidden}"] = job != null;
					if (!ignoreForbidden)
					{
						assertions[$"no_forbidden_dependencies_drafted_{drafted}"] = job != null && forbiddenDependencies.Length == 0;
						assertions[$"permitted_material_selected_drafted_{drafted}"] = things.Any(thing => thing.def == ThingDefOf.WoodLog && !thing.IsForbidden(pawn.Faction));
					}
				}

			Achtung.Settings.ignoreForbidden = false;
			var synthetic = new Job { targetA = ThingMaker.MakeThing(ThingDefOf.WoodLog), targetQueueA = [forbiddenWood] };
			assertions["forbidden_queue_A_rejected"] = !Validate(synthetic);
			synthetic.targetQueueA = null;
			synthetic.targetQueueB = [forbiddenWood];
			assertions["forbidden_queue_B_rejected"] = !Validate(synthetic);
			assertions["explicit_forbidden_target_permitted"] = Validate(synthetic, forbiddenWood);
			Achtung.Settings.ignoreForbidden = true;
			assertions["forbidden_queue_override_permitted"] = Validate(synthetic);
			Achtung.Settings.ignoreForbidden = false;
			using (new ForcedExtensions.PermissionScope(pawn))
			{
				try
				{
					using var nested = new ForcedExtensions.PermissionScope(null);
					assertions["nested_scope_suspends_outer"] = !ForcedExtensions.AppliesTo(pawn);
					throw new InvalidOperationException("Expected permission-scope test exception.");
				}
				catch (InvalidOperationException)
				{
					assertions["exception_restores_outer_scope"] = ForcedExtensions.AppliesTo(pawn);
				}
			}
			assertions["scope_does_not_leak"] = !ForcedExtensions.AppliesTo(pawn);
			assertions["current_job_unchanged"] = pawn.CurJob == previousJob;
			return new { success = assertions.Values.All(value => value), pawnId = pawn.ThingID, targetId = target.ThingID, assertions, variants = rows, ticksAdvanced = 0, evidence = "Production scanner and permission checks, not job execution." };
		}
		finally
		{
			pawn.drafter.draftedInt = previousDrafted;
			Achtung.Settings.ignoreForbidden = false;
		}
	}
}
