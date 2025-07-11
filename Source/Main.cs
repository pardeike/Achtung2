using Brrainz;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Profile;

using static HarmonyLib.Code;

namespace AchtungMod;

[StaticConstructorOnStartup]
public static class AchtungLoader
{
	public static bool IsSameSpotInstalled;

	static AchtungLoader()
	{
		Controller.InstallDefs();

		Achtung.harmony = new Harmony("net.pardeike.rimworld.mods.achtung");
		Achtung.harmony.PatchAll();

		const string sameSpotId = "net.pardeike.rimworld.mod.samespot";
		IsSameSpotInstalled = Achtung.harmony.GetPatchedMethods()
			.Any(method => Harmony.GetPatchInfo(method)?.Transpilers.Any(transpiler => transpiler.owner == sameSpotId) ?? false);

		CrossPromotion.Install(76561197973010050);
		ModFeatures.Install<Achtung>();
	}
}

public class Achtung : Mod
{
	public static Harmony harmony = null;
	public static AchtungSettings Settings;
	public static string rootDir;

	public Achtung(ModContentPack content) : base(content)
	{
		rootDir = content.RootDir;
		Settings = GetSettings<AchtungSettings>();
	}

	public override void DoSettingsWindowContents(Rect inRect) => AchtungSettings.DoWindowContents(inRect);
	public override string SettingsCategory() => "Achtung!";
}

[HarmonyPatch(typeof(CameraDriver))]
[HarmonyPatch(nameof(CameraDriver.Update))]
static class Root_Play_Update_Patch
{
	public static bool isDragging = false;
	public static void Prefix(Vector3 ___rootPos, out Vector3 __state) => __state = ___rootPos;
	public static void Postfix(Vector3 ___rootPos, Vector3 __state) => isDragging = ___rootPos != __state;
}

[HarmonyPatch(typeof(LetterStack))]
[HarmonyPatch(nameof(LetterStack.LettersOnGUI))]
static class LetterStack_LettersOnGUI_Patch
{
	static void Prefix(ref float baseY)
	{
		if (DebugViewSettings.drawPawnDebug == false || Prefs.DevMode == false) return;

		const float rowHeight = 26f;
		const float spacing = 4f;
		var num = (float)UI.screenWidth - 200f;
		var rect = new Rect(num, baseY + 10f - rowHeight, 193f - 2 * (spacing + rowHeight), rowHeight);
		baseY -= rowHeight;
		Text.Anchor = TextAnchor.MiddleRight;
		Widgets.Label(rect, $"{Game_UpdatePlay_Patch.fps} (min {Game_UpdatePlay_Patch.minFps}) {Game_UpdatePlay_Patch.iterations}");
		rect.xMin = rect.xMax + spacing;
		rect.width = rowHeight;
		if (Widgets.ButtonText(rect, "+")) Game_UpdatePlay_Patch.minFps++;
		rect.x += rowHeight + spacing;
		if (Widgets.ButtonText(rect, "-")) Game_UpdatePlay_Patch.minFps--;
		Text.Anchor = TextAnchor.UpperLeft;
	}
}

[HarmonyPatch(typeof(Game))]
[HarmonyPatch(nameof(Game.UpdatePlay))]
static class Game_UpdatePlay_Patch
{
	static readonly IEnumerator it = Looper();

	public static int minFps = 30;
	public static int iterations = 0;
	static int prevFrames = 0;
	public static int fps = 0;
	public static int[] fpsSlots = new int[10];
	static int previousN = -1;

	static IEnumerator Looper()
	{
		while (true)
		{
			var jobs = ForcedWork.Instance.AllForcedJobs();
			var didYield = false;
			foreach (var job in jobs)
			{
				var map = job?.pawn?.Map;
				if (map != null)
				{
					if (job.cancelled == false)
					{
						var it = job.ExpandThingTargets(map);
						while (it.MoveNext())
						{
							yield return null;
							didYield = true;
						}
					}

					if (job.cancelled == false)
					{
						var it = job.ExpandCellTargets(map);
						while (it.MoveNext())
						{
							yield return null;
							didYield = true;
						}
					}

					if (job.cancelled == false)
					{
						var it = job.ContractTargets(map);
						while (it.MoveNext())
						{
							yield return null;
							didYield = true;
						}
					}
				}
			}
			if (didYield == false)
				yield return null;
		}
	}

	public static void Postfix()
	{
		if (Current.ProgramState != ProgramState.Playing) return;
		var camera = Find.CameraDriver;
		if (Root_Play_Update_Patch.isDragging) return;
		var s1 = (int)(camera.rootSize * 1000);
		var s2 = (int)(camera.desiredSize * 1000);
		if (Math.Abs(s1 - s2) > 1) return; // can be 59999/60000 when completely zoomed out

		if (ForcedWork.Instance.hasForcedJobs)
		{
			var n = Tools.EnvTicks() / 100 % 10;
			prevFrames++;
			if (previousN != n)
			{
				previousN = n;
				var old = fpsSlots[n];
				fpsSlots[n] = prevFrames;
				fps = fps - old + prevFrames;
				prevFrames = 0;
			}

			if (fps < minFps)
				iterations = 0;
			else if (iterations < 800)
				iterations++;
		}
		else
			iterations = 0;

		if (Scribe.mode == LoadSaveMode.Inactive)
			for (var i = 0; i < iterations; i++)
				_ = it.MoveNext();
	}
}

[HarmonyPatch(typeof(World))]
[HarmonyPatch(nameof(World.FinalizeInit))]
static class World_FinalizeInit_Patch
{
	public static void Prefix()
	{
		ForcedWork.Instance = null;

		var rescuing = DefDatabase<WorkTypeDef>.GetNamedSilentFail(Tools.RescuingWorkTypeDef.defName);
		var doctorRescueWorkGiver = DefDatabase<WorkGiverDef>.GetNamed("DoctorRescue");
		if (rescuing == null && Achtung.Settings.rescueEnabled)
			Tools.savedWorkTypeDef = DynamicWorkTypes.AddWorkTypeDef(Tools.RescuingWorkTypeDef, WorkTypeDefOf.Doctor, doctorRescueWorkGiver);
		Log.Message($"Achtung v{Performance.GetModVersionString()} Info: To make Achtung log some performance info, create an empty 'AchtungPerformance.txt' file in same directory as Player.log");
	}
}

[HarmonyPatch]
static class HugsLib_Quickstart_InitateSaveLoading_Patch
{
	const string name = "HugsLib.Quickstart.QuickstartController:InitateSaveLoading";
	public static bool Prepare(MethodBase _) => TargetMethod() != null;
	public static MethodBase TargetMethod() => AccessTools.Method(name);
	public static void Prefix() => Find.Maps?.ForEach(map => ForcedWork.Instance?.Cleanup(map));
}

[HarmonyPatch(typeof(MemoryUtility))]
[HarmonyPatch(nameof(MemoryUtility.ClearAllMapsAndWorld))]
static class MemoryUtility_ClearAllMapsAndWorld_Patch
{
	public static void Prefix() => Find.Maps?.ForEach(map => ForcedWork.Instance?.Cleanup(map));
}

[HarmonyPatch(typeof(Game))]
[HarmonyPatch(nameof(Game.DeinitAndRemoveMap))]
static class Game_DeinitAndRemoveMap_Patch
{
	public static void Prefix(Map map) => ForcedWork.Instance?.Cleanup(map);
}

// a way to store an extra property on a pawn
// we subclass Pawn_Thinker and hope for nobody doing the same thing
//
[HarmonyPatch(typeof(PawnComponentsUtility))]
[HarmonyPatch(nameof(PawnComponentsUtility.AddComponentsForSpawn))]
static class PawnComponentsUtility_AddComponentsForSpawn_Patch
{
	[HarmonyPriority(int.MinValue)]
	public static void Postfix(Pawn pawn)
	{
		if (pawn.IsColonist == false) return;

		var t = pawn.thinker.GetType();
		while (true)
		{
			var t2 = t.BaseType;
			if (t2 == null || t2 == typeof(object))
				break;
			t = t2;
		}
		if (t != typeof(Pawn_Thinker))
			Log.Error($"Achtung identified a potential mod conflict: The instance for pawn.thinker is of type {t} but should be {typeof(Pawn_Thinker)}. As a result Achtung performance is degraded");

		pawn.thinker = new Pawn_AchtungThinker(pawn)
		{
			forcedJobs = ForcedWork.Instance.GetForcedJobsInstance(pawn)
		};
	}
}

// build-in "Ignore Me Passing" functionality
//
[HarmonyPatch(typeof(GenConstruct))]
[HarmonyPatch(nameof(GenConstruct.BlocksConstruction))]
static class GenConstruct_BlocksConstruction_Patch
{
	public static bool Prefix(ref bool __result, Thing t)
	{
		if (t is Pawn)
		{
			__result = false;
			return false;
		}
		return true;
	}
}

[HarmonyPatch(typeof(DesignatorManager))]
[HarmonyPatch(nameof(DesignatorManager.DesignatorManagerUpdate))]
static class DesignatorManager_DesignatorManagerUpdate_Patch
{
	public static void Postfix() => MouseTracker.GetInstance().OnGUI();
}

// allow for disabled work types when option is on and we have a forced job
//
[HarmonyPatch(typeof(Pawn_WorkSettings))]
[HarmonyPatch(nameof(Pawn_WorkSettings.WorkIsActive))]
static class Pawn_WorkSettings_WorkIsActive_Patch
{
	public static void Postfix(Pawn ___pawn, WorkTypeDef w, ref bool __result)
	{
		if (__result == true) return;
		if (Achtung.Settings.ignoreAssignments == false) return;
		if (ForcedWork.Instance.HasForcedJob(___pawn) == false) return;
		__result = ___pawn.workSettings.GetPriority(w) == 0;
	}
}
/* DISABLED IN 1.6
[HarmonyPatch(typeof(Alert_HunterLacksRangedWeapon))]
[HarmonyPatch(nameof(Alert_HunterLacksRangedWeapon.HuntersWithoutRangedWeapon), MethodType.Getter)]
static class Alert_HunterLacksRangedWeapon_HuntersWithoutRangedWeapon_Patch
{
	static bool WorkIsActive(Pawn_WorkSettings instance, WorkTypeDef w)
	{
		return instance.GetPriority(w) > 0; // "unpatch" it
	}

	public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	{
		var fromMethod = SymbolExtensions.GetMethodInfo((Pawn_WorkSettings workSettings) => workSettings.WorkIsActive(null));
		var toMethod = SymbolExtensions.GetMethodInfo(() => WorkIsActive(null, null));
		return instructions.MethodReplacer(fromMethod, toMethod);
	}
}*/

// forced hauling outside of allowed area
//
[HarmonyPatch(typeof(HaulAIUtility))]
[HarmonyPatch(nameof(HaulAIUtility.PawnCanAutomaticallyHaulFast))]
static class HaulAIUtility_PawnCanAutomaticallyHaulFast_Patch
{
	public static bool Prefix(Pawn p, bool forced, ref bool __result)
	{
		if (p?.Map != null && p.RaceProps.Humanlike && Achtung.Settings.ignoreRestrictions)
		{
			var forcedWork = ForcedWork.Instance;
			if (forced || forcedWork.HasForcedJob(p))
			{
				__result = true;
				return false;
			}
		}
		return true;
	}
}

// replace toil conditions that call IsForbidden with a new condition
// that calls the original condition only every 60 ticks to improve tps
// with the risk of pawns in extreme cases using forbidden cells/items
//
[HarmonyPatch(typeof(Toil))]
[HarmonyPatch(nameof(Toil.AddEndCondition))]
static class Toil_AddEndCondition_Patch
{
	public static readonly MethodInfo mIsForbidden = SymbolExtensions.GetMethodInfo(() => ForbidUtility.IsForbidden(null, (Pawn)null));
	public static readonly Dictionary<MethodInfo, bool> hasForbiddenState = [];

	public static void Prefix(Toil __instance, ref Func<JobCondition> newEndCondition)
	{
		var method = newEndCondition?.Method;
		if (method == null) return;

		if (hasForbiddenState.TryGetValue(method, out var hasForbidden) == false)
		{
			hasForbidden = PatchProcessor.ReadMethodBody(method).Any(pair => pair.Value is MethodInfo method && method == mIsForbidden);
			hasForbiddenState.Add(method, hasForbidden);
		}
		if (hasForbidden)
		{
			var condition = newEndCondition;
			newEndCondition = () =>
			{
				if (__instance.actor?.IsHashIntervalTick(60) ?? true)
					return condition();
				return JobCondition.Ongoing;
			};
		}
	}
}

// replace toil conditions that call IsForbidden with a new condition
// that calls the original condition only every 60 ticks to improve tps
// with the risk of pawns in extreme cases using forbidden cells/items
//
[HarmonyPatch(typeof(Toil))]
[HarmonyPatch(nameof(Toil.AddFailCondition))]
static class Toil_AddFailCondition_Patch
{
	public static void Prefix(Toil __instance, ref Func<bool> newFailCondition)
	{
		var method = newFailCondition?.Method;
		if (method == null) return;

		if (Toil_AddEndCondition_Patch.hasForbiddenState.TryGetValue(method, out var hasForbidden) == false)
		{
			var mIsForbidden = Toil_AddEndCondition_Patch.mIsForbidden;
			hasForbidden = PatchProcessor.ReadMethodBody(method).Any(pair => pair.Value is MethodInfo method && method == mIsForbidden);
			Toil_AddEndCondition_Patch.hasForbiddenState.Add(method, hasForbidden);
		}
		if (hasForbidden)
		{
			var condition = newFailCondition;
			newFailCondition = () =>
			{
				if (__instance.actor?.IsHashIntervalTick(60) ?? true)
					return condition();
				return false;
			};
		}
	}
}

// allow for jobs outside allowed area
//
[HarmonyPatch(typeof(ForbidUtility))]
[HarmonyPatch(nameof(ForbidUtility.InAllowedArea))]
static class ForbidUtility_InAllowedArea_Patch
{
	public static void Postfix(/*IntVec3 c,*/ Pawn forPawn, ref bool __result)
	{
		var map = forPawn?.Map;
		if (map == null || __result || forPawn.RaceProps.Humanlike == false) return;

		var forcedWork = ForcedWork.Instance;
		if (forcedWork.hasForcedJobs == false && forcedWork.IsPreparing(forPawn) == false) return;

		if (forcedWork.HasForcedJob(forPawn))
		{
			if (Achtung.Settings.ignoreRestrictions)
				__result = true;
			return;
		}

		// for now, this takes way too much cpu so we disable it and hope for the best
		/*
		if (__result == true)
		{
			// ignore any forced work cells if colonist is not forced
			if (forcedWork.NonForcedShouldIgnore(map, c))
				__result = false;
		}
		*/
	}
}

// forced repair outside of allowed area
//
[HarmonyPatch(typeof(WorkGiver_Repair))]
[HarmonyPatch(nameof(WorkGiver_Repair.HasJobOnThing))]
static class WorkGiver_Repair_HasJobOnThing_Patch
{
	public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	{
		var instr = instructions.ToList();

		var f_NotInHomeAreaTrans = AccessTools.Field(typeof(WorkGiver_FixBrokenDownBuilding), nameof(WorkGiver_FixBrokenDownBuilding.NotInHomeAreaTrans));
		if (f_NotInHomeAreaTrans == null)
			throw new Exception("Cannot find method WorkGiver_FixBrokenDownBuilding.NotInHomeAreaTrans");
		var i = instr.FindIndex(inst => inst.LoadsField(f_NotInHomeAreaTrans));
		if (i > 0)
		{
			object label = null;
			for (var j = i - 1; j >= 0; j--)
				if (instr[j].opcode == OpCodes.Brtrue || instr[j].opcode == OpCodes.Brtrue_S)
				{
					label = instr[j].operand;
					break;
				}
			if (label != null)
			{
				instr.Insert(i++, Ldarg_3);
				instr.Insert(i++, Brtrue[label]);
			}
			else
				Log.Error("Cannot find Brfalse before NotInHomeAreaTrans");
		}
		else
			Log.Error("Cannot find ldsfld RimWorld.WorkGiver_FixBrokenDownBuilding::NotInHomeAreaTrans");

		foreach (var inst in instr)
			yield return inst;
	}
}

[HarmonyPatch(typeof(BeautyDrawer))]
[HarmonyPatch(nameof(BeautyDrawer.ShouldShow))]
static class BeautyDrawer_ShouldShow_Patch
{
	public static void Postfix(ref bool __result)
	{
		if (__result == false) return;
		if (Find.Selector.SelectedPawns.Count(pawn => pawn.IsColonist && pawn.Drafted) > 1)
			__result = false;
	}
}
//
[HarmonyPatch(typeof(CellInspectorDrawer))]
[HarmonyPatch(nameof(CellInspectorDrawer.ShouldShow))]
static class CellInspectorDrawer_ShouldShow_Patch
{
	public static void Postfix(ref bool __result)
	{
		if (__result == false) return;
		if (Find.Selector.SelectedPawns.Count(pawn => pawn.IsColonist && pawn.Drafted) > 1)
			__result = false;
	}
}

// allow multiple colonists by reserving the exit path from a build place
/*
	* ### ENABLE AGAIN FOR SMART BUILDING
	*
[HarmonyPatch(typeof(JobDriver_ConstructFinishFrame))]
[HarmonyPatch(nameof(JobDriver_ConstructFinishFrame.TryMakePreToilReservations))]
static class JobDriver_ConstructFinishFrame_TryMakePreToilReservations_Patch
{
	static void Postfix(JobDriver_ConstructFinishFrame __instance, ref bool __result)
	{
		if (__result == false) return;

		var pawn = __instance.pawn;
		var forcedWork = ForcedWork.Instance;
		if (forcedWork.HasForcedJob(pawn) == false) return;

		var job = __instance.job;
		var buildCell = job.targetA;
		var map = __instance.pawn.Map;
		var pathGrid = map.pathGrid;
		// var reserationManager = map.reservationManager;

		void FloodFillReserve(IntVec3 pos, IntVec3 prev, int depth)
		{
			//var success = reserationManager.Reserve(pawn, job, pos, 1, -1, null, false);
			forcedWork.AddForbiddenLocation(pawn, pos);
			var cells = GenAdj.CardinalDirections
				.Select(v => v + pos)
				.Where(cell => cell != prev && pathGrid.Walkable(cell));
			if (cells.Count() == 1)
			{
				var cell = cells.First();
				FloodFillReserve(cell, pos, depth + 1);
			}
		}
		FloodFillReserve(buildCell.Cell, IntVec3.Invalid, 0);
	}
}

[HarmonyPatch(typeof(ReservationManager))]
[HarmonyPatch(nameof(ReservationManager.ReleaseClaimedBy))]
static class ReservationManager_ReleaseClaimedBy_Patch
{
	static void Postfix(Pawn claimant)
	{
		var forcedWork = ForcedWork.Instance;
		if (forcedWork.HasForcedJob(claimant) == false) return;
		forcedWork.RemoveForbiddenLocations(claimant);
	}
}

[HarmonyPatch(typeof(ReservationManager))]
[HarmonyPatch(nameof(ReservationManager.ReleaseAllClaimedBy))]
static class ReservationManager_ReleaseAllClaimedBy_Patch
{
	static void Postfix(Pawn claimant)
	{
		var forcedWork = ForcedWork.Instance;
		if (forcedWork.HasForcedJob(claimant) == false) return;
		forcedWork.RemoveForbiddenLocations(claimant);
	}
}*/

[HarmonyPatch(typeof(ReservationManager))]
[HarmonyPatch(nameof(ReservationManager.RespectsReservationsOf))]
[HarmonyPatch([typeof(Pawn), typeof(Pawn)])]
static class ReservationManager_RespectsReservationsOf_Patch
{
	public static bool Prefix(Pawn newClaimant, Pawn oldClaimant, ref bool __result)
	{
		var forcedWork = ForcedWork.Instance;
		if (forcedWork.HasForcedJob(newClaimant) && forcedWork.HasForcedJob(oldClaimant))
		{
			__result = false;
			return false;
		}
		return true;
	}
}

[HarmonyPatch(typeof(ReservationManager))]
[HarmonyPatch(nameof(ReservationManager.Reserve))]
static class ReservationManager_Reserve_Patch
{
	public static bool CanReserve(ReservationManager reservationManager, Pawn claimant, LocalTargetInfo target, int maxPawns, int stackCount, ReservationLayerDef layer, bool ignoreOtherReservations)
	{
		if (ignoreOtherReservations)
		{
			var forcedWork = ForcedWork.Instance;
			if (forcedWork.HasForcedJob(claimant))
					return false;
		}
		return reservationManager.CanReserve(claimant, target, maxPawns, stackCount, layer, ignoreOtherReservations);
	}

	public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	{
		var fromMethod = AccessTools.Method(typeof(ReservationManager), nameof(ReservationManager.CanReserve));
		var toMethod = AccessTools.Method(typeof(ReservationManager_Reserve_Patch), nameof(CanReserve));
		return instructions.MethodReplacer(fromMethod, toMethod);
	}
}

[HarmonyPatch]
static class Toils_Construct_MakeSolidThingFromBlueprintIfNecessary_Patch
{
	public static IEnumerable<MethodBase> TargetMethods()
	{
		return typeof(Toils_Construct).GetNestedTypes(AccessTools.all)
			.SelectMany(t => AccessTools.GetDeclaredMethods(t))
			.Where(m => m.Name.Contains("<MakeSolidThingFromBlueprintIfNecessary>"));
	}

	static bool IsForced(Pawn pawn) => ForcedWork.Instance.HasForcedJob(pawn);

	static void EnqueueWhenForced(Pawn pawn, Blueprint blueprint, Thing thing)
	{
		var forcedJob = ForcedWork.Instance.GetForcedJob(pawn);
		forcedJob?.Replace(blueprint, thing);
	}

	public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	{
		var m_TryReplaceWithSolidThing = AccessTools.Method(typeof(Blueprint), nameof(Blueprint.TryReplaceWithSolidThing));
		var matcher = new CodeMatcher(instructions)
			.MatchEndForward(
				new CodeMatch(code => code.opcode == OpCodes.Ldloca || code.opcode == OpCodes.Ldloca_S, name: "thingVar"),
				new CodeMatch(code => code.opcode == OpCodes.Ldloca || code.opcode == OpCodes.Ldloca_S),
				new CodeMatch(operand: m_TryReplaceWithSolidThing),
				new CodeMatch(code => code.Branches(out var _)),
				new CodeMatch()
			);

		_ = matcher
			.InsertAndAdvance(
				Ldloc_0,
				Ldloc_2,
				Ldloc[matcher.NamedMatch("thingVar").operand],
				CodeInstruction.Call(() => EnqueueWhenForced(default, default, default))
			)
			.MatchStartForward(new CodeMatch(code => code.operand is MethodInfo method && method.Name == "Reserve"))
			.MatchStartBackwards(new CodeMatch(name: "branch"), Ldloc_0);

		var branch = matcher.NamedMatch("branch");
		if (branch.Branches(out _) == false)
		{
			Log.Error("Cannot find branch before ldloc.0...Reserve");
			return instructions;
		}

		return matcher.Advance(1).InsertAndAdvance(
			Ldloc_0,
			CodeInstruction.Call(() => IsForced(default)),
			Brtrue[branch.operand]
		).InstructionEnumeration();
	}
}

// ignore forbidden for forced jobs
//
[HarmonyPatch(typeof(ForbidUtility))]
[HarmonyPatch(nameof(ForbidUtility.IsForbidden))]
[HarmonyPatch([typeof(Thing), typeof(Pawn)])]
static class ForbidUtility_IsForbidden_Patch
{
	public static bool Prepare() => Achtung.Settings.ignoreForbidden;

	public static void FixPatch()
	{
		var method = SymbolExtensions.GetMethodInfo(() => ForbidUtility.IsForbidden(null, (Pawn)null));
		var hasPatch = Harmony.GetPatchInfo(method)?.Owners.Contains(Achtung.harmony.Id) ?? false;
		if (Prepare() != hasPatch)
		{
			if (hasPatch)
				Achtung.harmony.Unpatch(method, HarmonyPatchType.All, Achtung.harmony.Id);
			else
				_ = new PatchClassProcessor(Achtung.harmony, typeof(ForbidUtility_IsForbidden_Patch)).Patch();
		}
	}

	public static bool Prefix(Thing t, Pawn pawn, ref bool __result)
	{
		if (pawn?.Map != null && pawn.RaceProps.Humanlike)
		{
			var forcedWork = ForcedWork.Instance;
			// t.IsForbidden() is necessary or else we allow a lot more jobs as a side effect
			if (forcedWork.HasForcedJob(pawn) && t.IsForbidden(pawn.Faction))
			{
				__result = false;
				return false;
			}
		}
		return true;
	}
}

// teleporting does not end current job with error condition or causes the pawn
// to loose the active job and stand still without new assignments
//
// Side effect: instant teleportation (which looks arguable better than vanilla)
//
[HarmonyPatch(typeof(Pawn_PathFollower))]
[HarmonyPatch(nameof(Pawn_PathFollower.TryRecoverFromUnwalkablePosition))]
static class Pawn_PathFollower_TryRecoverFromUnwalkablePosition_Patch
{
	public static void My_Notify_Teleported(Pawn pawn, bool _1, bool _2) => pawn.Drawer.tweener.ResetTweenedPosToRoot();

	public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	{
		var from = AccessTools.Method(typeof(Pawn), nameof(Pawn.Notify_Teleported));
		var to = SymbolExtensions.GetMethodInfo(() => My_Notify_Teleported(default, default, default));
		return Transpilers.MethodReplacer(instructions, from, to);
	}
}

// for forced jobs, do not find work "on the way" to the work cell
//
[HarmonyPatch(typeof(WorkGiver_ConstructDeliverResources))]
[HarmonyPatch(nameof(WorkGiver_ConstructDeliverResources.FindNearbyNeeders))]
static class WorkGiver_ConstructDeliverResources_FindNearbyNeeders_Patch
{
	public static IEnumerable<Thing> RadialDistinctThingsAround_Patch(IntVec3 center, Map map, float radius, bool useCenter, Pawn pawn)
	{
		var forcedWork = ForcedWork.Instance;
		var forcedJob = forcedWork.GetForcedJob(pawn);
		if (forcedJob != null && forcedJob.isThingJob)
		{
			foreach (var thing1 in forcedJob.GetUnsortedTargets())
				yield return thing1;
		}
		else
		{
			foreach (var thing2 in GenRadial.RadialDistinctThingsAround(center, map, radius, useCenter))
				yield return thing2;
		}
	}

	public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	{
		var m_RadialDistinctThingsAround = AccessTools.Method(typeof(GenRadial), nameof(GenRadial.RadialDistinctThingsAround));
		var m_RadialDistinctThingsAround_Patch = SymbolExtensions.GetMethodInfo(() => RadialDistinctThingsAround_Patch(default, default, default, default, default));

		var found = 0;
		var list = instructions.ToList();
		var count = list.Count;
		var idx = 0;
		while (idx < count)
		{
			if (list[idx].Calls(m_RadialDistinctThingsAround))
			{
				list[idx].opcode = OpCodes.Call;
				list[idx].operand = m_RadialDistinctThingsAround_Patch;

				// add extra 'pawn' before CALL (extra last argument on our method)
				list.Insert(idx, Ldarg_1);
				idx++;
				count++;
				found++;
			}
			idx++;
		}
		if (found != 2)
			Log.Error("Cannot find both calls to RadialDistinctThingsAround in WorkGiver_ConstructDeliverResources.FindNearbyNeeders");

		foreach (var instruction in list)
			yield return instruction;
	}
}

// patch in our menu options
//
[HarmonyPatch(typeof(FloatMenuOptionProvider_WorkGivers))]
[HarmonyPatch(nameof(FloatMenuOptionProvider_WorkGivers.ScannerShouldSkip))]
static class FloatMenuOptionProvider_WorkGivers_ScannerShouldSkip_Patch
{
	public static bool Prefix(WorkGiver_Scanner scanner, Thing t, ref bool __result)
	{
		if (Achtung.Settings.ignoreForbidden
			&& scanner is WorkGiver_Haul
			&& t?.def != null
			&& t.def.alwaysHaulable
			&& t.def.EverHaulable)
		{
			__result = false;
			return false;
		}

		return true;
	}
}
//
[HarmonyPatch(typeof(FloatMenuOptionProvider_WorkGivers))]
[HarmonyPatch(nameof(FloatMenuOptionProvider_WorkGivers.GetWorkGiverOption))]
static class FloatMenuOptionProvider_WorkGivers_GetWorkGiverOption_Patch
{
	[HarmonyPriority(1000000)]
	public static void Prefix(Pawn pawn, out ForcedWork __state)
	{
		__state = null;
		if (pawn?.Map != null)
		{
			__state = ForcedWork.Instance;
			__state.Prepare(pawn);
		}
	}

	public static void Postfix(Pawn pawn, ForcedWork __state) => __state?.Unprepare(pawn);

	public static int AchtungGetPriority(Pawn_WorkSettings workSettings, WorkTypeDef w)
	{
		if (Achtung.Settings.ignoreAssignments == false)
			return workSettings.GetPriority(w);
		return workSettings.pawn.WorkTypeIsDisabled(w) ? 0 : 1;
	}

	public static FloatMenuOption DecorateForcedTask(FloatMenuOption option, Pawn pawn, LocalTargetInfo target, string reservedText, ReservationLayerDef layer, WorkGiver_Scanner workgiver)
	{
		var forcedOption = ForcedFloatMenuOption.CreateForcedMenuItem(option, pawn, target, workgiver);
		return FloatMenuUtility.DecoratePrioritizedTask(forcedOption, pawn, target, reservedText, layer);
	}

	public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	{
		var m_GetPriority = SymbolExtensions.GetMethodInfo((Pawn_WorkSettings pws, WorkTypeDef wtd) => pws.GetPriority(wtd));
		var m_AchtungGetPriority = SymbolExtensions.GetMethodInfo(() => AchtungGetPriority(default, default));

		var m_DecoratePrioritizedTask = SymbolExtensions.GetMethodInfo(() => FloatMenuUtility.DecoratePrioritizedTask(default, default, default, default, default));
		var m_DecorateForcedTask = SymbolExtensions.GetMethodInfo(() => DecorateForcedTask(default, default, default, default, default, default));

		var list = Transpilers.MethodReplacer(instructions, m_GetPriority, m_AchtungGetPriority).ToList();
		for (var i = 0; i < list.Count(); i++)
		{
			var instruction = list[i];
			if (instruction.Calls(m_GetPriority))
				instruction.operand = m_AchtungGetPriority;
			else if (instruction.Calls(m_DecoratePrioritizedTask))
			{
				list.Insert(i, Ldloc_1);
				instruction.operand = m_DecorateForcedTask;
			}
		}
		return list;
	}
}
//
[HarmonyPatch(typeof(Pawn_JobTracker))]
[HarmonyPatch(nameof(Pawn_JobTracker.EndCurrentJob))]
static class Pawn_JobTracker_EndCurrentJob_Patch
{
	[HarmonyPriority(int.MaxValue)]
	static void Prefix() => Performance.EndCurrentJob_Start();

	[HarmonyPriority(int.MinValue)]
	static void Postfix() => Performance.EndCurrentJob_Stop();

	public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	{
		var m_CleanupCurrentJob = AccessTools.Method(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.CleanupCurrentJob));
		var m_ContinueJob = AccessTools.Method(typeof(ForcedJob), nameof(ForcedJob.ContinueJob));
		var f_pawn = AccessTools.Field(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.pawn));
		var f_curJob = AccessTools.Field(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.curJob));

		if (m_CleanupCurrentJob == null)
			throw new Exception("Cannot find method Pawn_JobTracker.CleanupCurrentJob");
		if (f_pawn == null)
			throw new Exception("Cannot find field Pawn_JobTracker.pawn");

		var instrList = instructions.ToList();
		for (var i = 0; i < instrList.Count; i++)
		{
			var instruction = instrList[i];
			yield return instruction;

			if (instruction.OperandIs(m_CleanupCurrentJob) == false)
				continue;

			var jump = -1;
			var unexpected = false;
			var expected = new[] { OpCodes.Nop, OpCodes.Ldarg_2, OpCodes.Ldloc_S, OpCodes.Stloc_S };
			for (var j = i + 1; j < i + 6; j++)
			{
				var opcode = instrList[j].opcode;
				if (opcode == OpCodes.Brfalse || opcode == OpCodes.Brfalse_S)
				{
					jump = j;
					break;
				}
				else if (expected.Contains(opcode) == false)
				{
					unexpected = true;
					Log.Error($"Unexpected opcode {opcode} while transpiling Pawn_JobTracker.EndCurrentJob");
					break;
				}
			}
			if (unexpected)
				continue;
			if (jump == -1)
			{
				Log.Error("Could not find Brfalse after CleanupCurrentJob while transpiling Pawn_JobTracker.EndCurrentJob");
				continue;
			}
			var endLabel = instrList[jump].operand;

			for (var j = i; j < jump; j++)
				yield return instrList[++i];

			yield return Ldarg_0;
			yield return Ldarg_0;
			yield return Ldfld[f_curJob];
			yield return Ldarg_0;
			yield return Ldfld[f_pawn];
			yield return Ldarg_1;
			yield return Call[m_ContinueJob];

			yield return Brtrue[endLabel];
		}
	}
}

// make sure fake drafting is off when undrafted
//
[HarmonyPatch(typeof(Pawn_DraftController))]
[HarmonyPatch(nameof(Pawn_DraftController.Drafted), MethodType.Setter)]
static class Pawn_DraftController_Drafted_Patch
{
	public static void Postfix(Pawn_DraftController __instance)
	{
		if (__instance.draftedInt == false && __instance.pawn != null)
		{
			var forcedWork = ForcedWork.Instance;
			forcedWork.Unprepare(__instance.pawn);
		}
	}
}

// release forced work when pawn disappears
//
[HarmonyPatch(typeof(Pawn))]
[HarmonyPatch(nameof(Pawn.DeSpawn))]
static class Pawn_DeSpawn_Patch
{
	public static void Postfix(Pawn __instance)
	{
		var forcedWork = ForcedWork.Instance;
		forcedWork.Remove(__instance);
	}
}

// add colonist widget buttons
//
[HarmonyPatch(typeof(PriorityWork))]
[HarmonyPatch(nameof(PriorityWork.GetGizmos))]
[StaticConstructorOnStartup]
static class PriorityWork_GetGizmos_Patch
{
	public static readonly Texture2D ForceRadiusExpand = ContentFinder<Texture2D>.Get("ForceRadiusExpand", true);
	public static readonly Texture2D ForceRadiusShrink = ContentFinder<Texture2D>.Get("ForceRadiusShrink", true);
	public static readonly Texture2D ForceRadiusShrinkOff = ContentFinder<Texture2D>.Get("ForceRadiusShrinkOff", true);
	public static readonly Texture2D BuildingSmart = ContentFinder<Texture2D>.Get("BuildingSmart", true);
	public static readonly Texture2D BuildingSmartOff = ContentFinder<Texture2D>.Get("BuildingSmartOff", true);

	public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> gizmos, Pawn ___pawn)
	{
		var gizmoList = gizmos.ToList();
		foreach (var gizmo in gizmos)
			yield return gizmo;

		var forcedWork = ForcedWork.Instance;
		var forcedJob = forcedWork.GetForcedJob(___pawn);
		if (forcedJob == null)
			yield break;

		var smart = forcedJob.buildSmart;
		yield return new Command_Action
		{
			defaultLabel = "BuildingSmart".Translate(),
			defaultDesc = "BuildingSmartDesc".Translate(),
			icon = smart ? BuildingSmart : BuildingSmartOff,
			activateSound = smart ? SoundDefOf.Designate_PlanRemove : SoundDefOf.Designate_PlanAdd,
			action = () =>
			{
				var forcedWork = ForcedWork.Instance;
				var forcedJob = forcedWork.GetForcedJob(___pawn);
				forcedJob.ToggleSmartBuilding();
			}
		};
	}
}

// ignore think tree when building uninterrupted
//
[HarmonyPatch(typeof(Pawn_JobTracker))]
[HarmonyPatch(nameof(Pawn_JobTracker.ShouldStartJobFromThinkTree))]
static class Pawn_JobTracker_ShouldStartJobFromThinkTree_Patch
{
	public static void Postfix(Pawn ___pawn, ref bool __result)
	{
		if (__result == false || ___pawn?.Map == null) return;

		var forcedWork = ForcedWork.Instance;
		if (forcedWork.HasForcedJob(___pawn) == false) return;

		var forcedJob = forcedWork.GetForcedJob(___pawn);
		if (forcedJob == null) return;

		var workGiver = ___pawn.CurJob?.workGiverDef;
		if (workGiver == null) return;

		__result = forcedJob.workgiverDefs.Contains(workGiver) == false;
	}
}

// handle events early
//
[HarmonyPatch(typeof(Selector))]
[HarmonyPatch(nameof(Selector.SelectorOnGUI))]
static class Selector_SelectorOnGUI_Patch
{
	public static bool Prefix() => Controller.GetInstance().HandleEvents();
}

// handle drawing
//
[HarmonyPatch(typeof(SelectionDrawer))]
[HarmonyPatch(nameof(SelectionDrawer.DrawSelectionOverlays))]
static class SelectionDrawer_DrawSelectionOverlays_Patch
{
	public static void Postfix()
	{
		if (WorldRendererUtility.WorldRendered == false)
			Controller.GetInstance().HandleDrawing();
	}
}

// handle gui
//
[HarmonyPatch(typeof(ThingOverlays))]
[HarmonyPatch(nameof(ThingOverlays.ThingOverlaysOnGUI))]
static class ThingOverlays_ThingOverlaysOnGUI_Patch
{
	public static void Postfix()
	{
		if (WorldRendererUtility.WorldRendered == false)
			Controller.GetInstance().HandleDrawingOnGUI();
	}
}

// turn some errors into warnings
//
[HarmonyPatch]
static class Errors_To_Warnings_Patch
{
	public static IEnumerable<MethodBase> TargetMethods()
	{
		yield return AccessTools.Method(typeof(ReservationManager), nameof(ReservationManager.LogCouldNotReserveError));
		yield return AccessTools.Method(typeof(JobUtility), nameof(JobUtility.TryStartErrorRecoverJob));
	}

	public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	{
		var fromMethod = SymbolExtensions.GetMethodInfo(() => Log.Error(default));
		var toMethod = SymbolExtensions.GetMethodInfo(() => Log.Warning(default));
		return instructions.MethodReplacer(fromMethod, toMethod);
	}
}

// pawn inspector panel
//
[HarmonyPatch(typeof(Pawn))]
[HarmonyPatch(nameof(Pawn.GetInspectString))]
static class Pawn_GetInspectString_Patch
{
	static readonly Dictionary<Pawn, string> cache = [];

	public static void Postfix(Pawn __instance, ref string __result)
	{
		if (cache.TryGetValue(__instance, out var text) && DateTime.Now.Ticks % 8 != 0)
		{
			__result = text;
			return;
		}

		var forcedWork = ForcedWork.Instance;
		if (forcedWork.HasForcedJob(__instance))
			__result = __result + "\n" + "ForcedCommandState".Translate();
		cache[__instance] = __result;
	}
}