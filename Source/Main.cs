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
		MultiplayerSupport.Install();

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
	public static HashSet<Thing> usedThingsPerTick = [];

	public Achtung(ModContentPack content) : base(content)
	{
		rootDir = content.RootDir;
		Settings = GetSettings<AchtungSettings>();
		DynamicWorkTypes.Initialize();
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

[HarmonyPatch]
static class FloatMenuOptionProviders_Patch
{
	public static IEnumerable<MethodBase> TargetMethods()
	{
		// fully qualified because AchtungMod declares its own FloatMenuOptionProvider_CleanRoom
		yield return AccessTools.PropertyGetter(typeof(RimWorld.FloatMenuOptionProvider_CleanRoom), nameof(RimWorld.FloatMenuOptionProvider_CleanRoom.Undrafted));
		yield return AccessTools.PropertyGetter(typeof(FloatMenuOptionProvider_ExtinguishFires), nameof(FloatMenuOptionProvider_ExtinguishFires.Drafted));
		yield return AccessTools.PropertyGetter(typeof(FloatMenuOptionProvider_ExtinguishFires), nameof(FloatMenuOptionProvider_ExtinguishFires.Undrafted));
	}

	public static bool Prefix(MethodBase __originalMethod, ref bool __result)
	{
		if (Achtung.Settings.replaceCleanRoom && __originalMethod.DeclaringType == typeof(RimWorld.FloatMenuOptionProvider_CleanRoom))
		{
			__result = false;
			return false;
		}
		if (Achtung.Settings.replaceFightFire && __originalMethod.DeclaringType == typeof(FloatMenuOptionProvider_ExtinguishFires))
		{
			__result = false;
			return false;
		}
		return true;
	}
}

[HarmonyPatch(typeof(FloatMenuMakerMap))]
[HarmonyPatch(nameof(FloatMenuMakerMap.GetOptions))]
static class FloatMenuMakerMap_GetOptions_Patch
{
	public static void Postfix(List<Pawn> selectedPawns, List<FloatMenuOption> __result)
	{
		if (Achtung.Settings.CustomPositioningEnabled)
			return;
		if (Event.current?.type != EventType.MouseDown || Event.current.button != 1)
			return;
		if (__result.NullOrEmpty())
			return;
		if (selectedPawns.NullOrEmpty())
			return;
		if (selectedPawns.All(pawn => pawn != null && PawnAttackGizmoUtility.CanOrderPlayerPawn(pawn) && pawn.Drafted) == false)
			return;

		var gotoOption = __result.FirstOrDefault(Tools.IsGoHereOption);
		if (gotoOption == null || gotoOption.Disabled || gotoOption.action == null)
			return;

		var nonGotoOptions = __result.Where(option => Tools.IsGoHereOption(option) == false).ToList();
		if (nonGotoOptions.Count == 0 || nonGotoOptions.All(Tools.CanYieldToPlainGoto) == false)
			return;

		__result.RemoveAll(Tools.CanYieldToPlainGoto);
	}
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
	static IEnumerator iterator = Looper();
	const float maxForcedWorkFrameTimeMs = 8f;
	const int maxIterations = 800;
	const int multiplayerWorkIntervalTicks = 15;
	const int iterationBackoff = 8;
	const int fpsRecoveryBuffer = 3;
	const int contractIntervalTicks = 15;
	const int slowLaneFrameInterval = 6;

	public static int minFps = 30;
	public static int iterations = 0;
	static int prevFrames = 0;
	public static int fps = 0;
	public static int[] fpsSlots = new int[10];
	static int previousN = -1;
	static int slowLaneFrames = 0;
	static bool multiplayerMode = false;

	static void SetMode(bool useMultiplayer)
	{
		if (multiplayerMode == useMultiplayer)
			return;

		multiplayerMode = useMultiplayer;
		iterator = Looper();
		iterations = 0;
		prevFrames = 0;
		fps = 0;
		Array.Clear(fpsSlots, 0, fpsSlots.Length);
		previousN = -1;
		slowLaneFrames = 0;

		if (useMultiplayer)
			ForcedWork.Instance.ResetRuntimeStateForMultiplayer();
	}

	public static void MultiplayerTick()
	{
		var useMultiplayer = MultiplayerSupport.IsActive;
		SetMode(useMultiplayer);
		if (useMultiplayer == false || Find.TickManager.TicksGame % multiplayerWorkIntervalTicks != 0)
			return;

		foreach (var job in ForcedWork.Instance.AllForcedJobs())
		{
			var map = job?.pawn?.Map;
			if (map == null || job.cancelled)
				continue;

			Drain(job.ExpandThingTargets(map));
			if (job.cancelled == false)
				Drain(job.ExpandCellTargets(map));
			if (job.cancelled == false)
				Drain(job.ContractTargets(map));
		}
	}

	static void Drain(IEnumerator work)
	{
		while (work.MoveNext()) { }
	}

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
						var tick = Find.TickManager.TicksGame;
						var offset = job.pawn.thingIDNumber % contractIntervalTicks;
						if ((tick + offset) % contractIntervalTicks == 0)
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
			}
			if (didYield == false)
				yield return null;
		}
	}

	public static void Postfix()
	{
		if (Current.ProgramState != ProgramState.Playing) return;
		var useMultiplayer = MultiplayerSupport.IsActive;
		SetMode(useMultiplayer);
		if (useMultiplayer) return;
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
			{
				iterations = Math.Max(0, iterations - iterationBackoff);
				slowLaneFrames = 0;
			}
			else if (fps >= minFps + fpsRecoveryBuffer && iterations < maxIterations)
			{
				iterations++;
				slowLaneFrames = 0;
			}
		}
		else
		{
			iterations = 0;
			slowLaneFrames = 0;
		}

		if (Scribe.mode == LoadSaveMode.Inactive)
		{
			var frameIterations = iterations;
			if (ForcedWork.Instance.hasForcedJobs && frameIterations == 0 && fps >= minFps)
			{
				// When performance is only barely acceptable, keep progress visible without resuming full-speed expansion.
				if (++slowLaneFrames >= slowLaneFrameInterval)
				{
					frameIterations = 1;
					slowLaneFrames = 0;
				}
			}

			var frameDeadline = Time.realtimeSinceStartup + maxForcedWorkFrameTimeMs / 1000f;
			for (var i = 0; i < frameIterations; i++)
			{
				_ = iterator.MoveNext();
				if (Time.realtimeSinceStartup >= frameDeadline)
					break;
			}
		}
	}
}

[HarmonyPatch(typeof(World))]
[HarmonyPatch(nameof(World.ExposeData))]
static class World_ExposeData_Patch
{
	[HarmonyPriority(Priority.VeryHigh)]
	public static void Prefix()
	{
		var rescuing = DefDatabase<WorkTypeDef>.GetNamedSilentFail(Tools.RescuingWorkTypeDef.defName);
		var doctorRescueWorkGiver = DefDatabase<WorkGiverDef>.GetNamed("DoctorRescue");
		if (rescuing == null && Achtung.Settings.rescueEnabled)
			Tools.savedWorkTypeDef = DynamicWorkTypes.AddWorkTypeDef(Tools.RescuingWorkTypeDef, WorkTypeDefOf.Doctor, doctorRescueWorkGiver);
	}
}

[HarmonyPatch(typeof(World))]
[HarmonyPatch(nameof(World.FinalizeInit))]
static class World_FinalizeInit_Patch
{
	public static void Prefix() => ForcedWork.Instance = null;
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

// Optional fast path. Custom thinkers keep their state and use ForcedWork's dictionary.
//
[HarmonyPatch(typeof(PawnComponentsUtility))]
[HarmonyPatch(nameof(PawnComponentsUtility.AddComponentsForSpawn))]
static class PawnComponentsUtility_AddComponentsForSpawn_Patch
{
	[HarmonyPriority(int.MinValue)]
	public static void Postfix(Pawn pawn)
	{
		if (PawnAttackGizmoUtility.CanOrderPlayerPawn(pawn) == false) return;

		if (pawn.thinker is Pawn_AchtungThinker existing)
		{
			existing.forcedJobs = ForcedWork.Instance.GetForcedJobsInstance(pawn);
			return;
		}
		if (pawn.thinker != null && pawn.thinker.GetType() != typeof(Pawn_Thinker))
			return;

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
		if (ForcedExtensions.AppliesTo(___pawn) == false && ForcedWork.Instance.IsPreparing(___pawn) == false) return;
		__result = ___pawn.WorkTypeIsDisabled(w) == false && ___pawn.workSettings.GetPriority(w) == 0;
	}
}
//
[HarmonyPatch(typeof(Alert_HunterHasShieldAndRangedWeapon))]
[HarmonyPatch(nameof(Alert_HunterHasShieldAndRangedWeapon.BadHunters), MethodType.Getter)]
static class Alert_HunterLacksRangedWeapon_HuntersWithoutRangedWeapon_Patch
{
	static bool WorkIsActive(Pawn_WorkSettings instance, WorkTypeDef w)
		=> instance.GetPriority(w) > 0; // "unpatch" it

	public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	{
		var fromMethod = SymbolExtensions.GetMethodInfo((Pawn_WorkSettings workSettings) => workSettings.WorkIsActive(null));
		var toMethod = SymbolExtensions.GetMethodInfo(() => WorkIsActive(null, null));
		return instructions.MethodReplacer(fromMethod, toMethod);
	}
}

// allow for jobs outside allowed area
//
[HarmonyPatch(typeof(Pawn_PlayerSettings))]
[HarmonyPatch(nameof(Pawn_PlayerSettings.RespectsAllowedArea), MethodType.Getter)]
static class Pawn_PlayerSettings_RespectsAllowedArea_Patch
{
	public static void Postfix(Pawn ___pawn, ref bool __result)
	{
		if (__result == false) return;
		if (Achtung.Settings.ignoreRestrictions == false) return;
		var forcedWork = ForcedWork.Instance;
		if (ForcedExtensions.AppliesTo(___pawn) == false && forcedWork.IsPreparing(___pawn) == false) return;
		__result = false;
	}
}

// forced repair as a scanner and outside of allowed area
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
		if (Achtung.Settings.CustomPositioningEnabled == false) return;
		if (Find.Selector.SelectedPawns.Count(pawn => PawnAttackGizmoUtility.CanOrderPlayerPawn(pawn) && pawn.Drafted) > 1)
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
		if (Achtung.Settings.CustomPositioningEnabled == false) return;
		if (Find.Selector.SelectedPawns.Count(pawn => PawnAttackGizmoUtility.CanOrderPlayerPawn(pawn) && pawn.Drafted) > 1)
			__result = false;
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
		var fakeThing = (Thing)null;
		var fakeBool = false;
		var m_TryReplaceWithSolidThing = SymbolExtensions.GetMethodInfo((Blueprint bp) => bp.TryReplaceWithSolidThing(default, out fakeThing, out fakeBool));
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

// Keep vanilla forbidden checks, including areas and ritual reservations, while
// removing the drafted/mech bypass only during Achtung-generated work.
//
[HarmonyPatch(typeof(ForbidUtility), nameof(ForbidUtility.CaresAboutForbidden))]
static class ForbidUtility_CaresAboutForbidden_Patch
{
	public static void Postfix(Pawn pawn, ref bool __result)
	{
		if (ForcedExtensions.AppliesTo(pawn))
			__result = true;
	}
}

// Toil init can also run from path arrival, outside the regular/interval ticks.
// Restore all scopes on exceptions, without affecting menu/direct vanilla orders.
//
[HarmonyPatch]
static class JobDriver_ForcedPermissions_Patch
{
	public static IEnumerable<MethodBase> TargetMethods()
	{
		yield return AccessTools.Method(typeof(JobDriver), nameof(JobDriver.DriverTick));
		yield return AccessTools.Method(typeof(JobDriver), nameof(JobDriver.DriverTickInterval));
		yield return AccessTools.Method(typeof(JobDriver), "TryActuallyStartNextToil");
	}

	public static void Prefix(JobDriver __instance, out ForcedExtensions.PermissionScope __state)
	{
		var pawn = __instance.pawn;
		var job = __instance.job;
		var forcedJob = pawn?.Map != null ? ForcedWork.Instance.GetForcedJob(pawn) : null;
		var forced = forcedJob != null && job != null && pawn.CurJob == job && forcedJob.activeJobId == job.loadID;
		__state = new ForcedExtensions.PermissionScope(forced ? pawn : null, job, forcedJob?.explicitTarget);
	}

	public static void Finalizer(ForcedExtensions.PermissionScope __state) => __state.Dispose();
}

// Ignore only the faction's forbidden flag, not every reason IsForbidden can
// return true. Direct vanilla orders outside the scope retain vanilla behavior.
//
[HarmonyPatch(typeof(ForbidUtility))]
[HarmonyPatch(nameof(ForbidUtility.IsForbidden))]
[HarmonyPatch([typeof(Thing), typeof(Pawn)])]
static class ForbidUtility_IsForbidden_Patch
{
	public static bool Prepare() => true;

	public static void FixPatch()
	{
		var method = SymbolExtensions.GetMethodInfo(() => ForbidUtility.IsForbidden(null, (Pawn)null));
		var hasPatch = Harmony.GetPatchInfo(method)?.Owners.Contains(Achtung.harmony.Id) ?? false;
		if (hasPatch == false)
			_ = new PatchClassProcessor(Achtung.harmony, typeof(ForbidUtility_IsForbidden_Patch)).Patch();
	}

	public static bool IsForbiddenToFaction(Thing thing, Faction faction, Pawn pawn)
	{
		if (ForcedExtensions.IsDirectTarget(pawn, thing))
			return false;
		if (Achtung.Settings.ignoreForbidden && pawn?.Map != null
			&& (ForcedExtensions.AppliesTo(pawn) || ForcedWork.Instance.IsPreparing(pawn)))
			return false;
		return thing.IsForbidden(faction);
	}

	public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	{
		var original = SymbolExtensions.GetMethodInfo(() => ForbidUtility.IsForbidden(null, (Faction)null));
		var replacement = SymbolExtensions.GetMethodInfo(() => IsForbiddenToFaction(default, default, default));
		foreach (var instruction in instructions)
		{
			if (instruction.Calls(original))
			{
				yield return new CodeInstruction(OpCodes.Ldarg_1).MoveLabelsFrom(instruction);
				instruction.operand = replacement;
			}
			yield return instruction;
		}
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
		var from = SymbolExtensions.GetMethodInfo((Pawn pawn) => pawn.Notify_Teleported(default, default));
		var to = SymbolExtensions.GetMethodInfo(() => My_Notify_Teleported(default, default, default));
		return Transpilers.MethodReplacer(instructions, from, to);
	}
}

// for each tick, clear used things so that we don't reuse them for new jobs
//
[HarmonyPatch(typeof(TickManager))]
[HarmonyPatch(nameof(TickManager.DoSingleTick))]
static class TickManager_DoSingleTick_Patch
{
	public static void Prefix()
	{
		MultiplayerSupport.ApplySharedSettings();
		Achtung.usedThingsPerTick = [];
	}
	public static void Postfix() => Game_UpdatePlay_Patch.MultiplayerTick();
}

// patch in our menu options
//
[HarmonyPatch(typeof(FloatMenuOptionProvider_WorkGivers))]
[HarmonyPatch(nameof(FloatMenuOptionProvider_WorkGivers.ScannerShouldSkip))]
static class FloatMenuOptionProvider_WorkGivers_ScannerShouldSkip_Patch
{
	public static bool Prefix(WorkGiver_Scanner scanner, Thing t, ref bool __result)
	{
		if (Achtung.Settings.ForceCommandsEnabled
			&& Achtung.Settings.ignoreForbidden
			&& scanner is WorkGiver_Haul
			&& t?.def != null
			&& t.def.alwaysHaulable
			&& t.def.EverHaulable
			&& t.Map.reservationManager.IsReserved(t) == false)
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
		if (Achtung.Settings.ForceCommandsEnabled && pawn?.Map != null)
		{
			var forcedWork = ForcedWork.Instance;
			if (forcedWork.IsPreparing(pawn) == false)
			{
				__state = forcedWork;
				__state.Prepare(pawn);
			}
		}
	}

	public static void Finalizer(Pawn pawn, ForcedWork __state) => __state?.Unprepare(pawn);

	public static int AchtungGetPriority(Pawn_WorkSettings workSettings, WorkTypeDef w)
	{
		if (Achtung.Settings.ForceCommandsEnabled == false || Achtung.Settings.ignoreAssignments == false || workSettings.pawn == null)
			return workSettings.GetPriority(w);
		return workSettings.pawn.WorkTypeIsDisabled(w) ? 0 : 1;
	}

	public static FloatMenuOption DecorateForcedTask(FloatMenuOption option, Pawn pawn, LocalTargetInfo target, string reservedText, ReservationLayerDef layer, WorkGiver_Scanner workgiver)
	{
		if (Achtung.Settings.ForceCommandsEnabled == false)
			return FloatMenuUtility.DecoratePrioritizedTask(option, pawn, target, reservedText, layer);

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
[HarmonyPatch(typeof(PriorityWork))]
[HarmonyPatch(nameof(PriorityWork.ClearPrioritizedWorkAndJobQueue))]
static class PriorityWork_ClearPrioritizedWorkAndJobQueue_Patch
{
	public static void Postfix(Pawn ___pawn)
		=> ForcedWork.Instance.Remove(___pawn);
}
//
[HarmonyPatch(typeof(Pawn_JobTracker))]
[HarmonyPatch(nameof(Pawn_JobTracker.EndCurrentJob))]
static class Pawn_JobTracker_EndCurrentJob_Patch
{
	public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	{
		var m_CleanupCurrentJob = SymbolExtensions.GetMethodInfo((Pawn_JobTracker pjt) => pjt.CleanupCurrentJob(default, default, default, default, default));
		var m_ContinueJob = SymbolExtensions.GetMethodInfo(() => ForcedJob.ContinueJob(default, default));
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
			yield return Ldfld[f_pawn];
			yield return Ldarg_1;
			yield return Call[m_ContinueJob];

			yield return Brtrue[endLabel];
		}
	}
}

// fix enter portal code so it no longer uses static state
[HarmonyPatch(typeof(FloatMenuOptionProvider_EnterMapPortal), nameof(FloatMenuOptionProvider_EnterMapPortal.GetSingleOptionFor))]
public static class FloatMenuOptionProvider_EnterMapPortal_GetSingleOptionFor_Patch
{
	public static bool Prefix(ref FloatMenuOption __result, Thing clickedThing, FloatMenuContext context)
	{
		var portal = clickedThing as MapPortal;
		if (portal == null)
		{
			__result = null;
			return false;
		}
		string text;
		if (!portal.IsEnterable(out text))
		{
			__result = new FloatMenuOption("CannotEnterPortal".Translate(portal.Label) + ": " + text, null, MenuOptionPriority.Default, null, null, 0f, null, null, true, 0);
			return false;
		}
		if (!context.IsMultiselect)
		{
			var acceptanceReport = FloatMenuOptionProvider_EnterMapPortal.CanEnterPortal(context.FirstSelectedPawn, portal);
			if (!acceptanceReport.Accepted)
			{
				__result = new FloatMenuOption("CannotEnterPortal".Translate(portal.Label) + ": " + acceptanceReport.Reason, null, MenuOptionPriority.Default, null, null, 0f, null, null, true, 0);
				return false;
			}
		}
		var enteringPawns = context.ValidSelectedPawns
			.Where(pawn => FloatMenuOptionProvider_EnterMapPortal.CanEnterPortal(pawn, portal))
			.ToList();
		if (enteringPawns.NullOrEmpty())
		{
			__result = null;
			return false;
		}
		__result = new FloatMenuOption(portal.EnterString, delegate
		{
			if (MultiplayerSupport.IsActive)
			{
				MultiplayerSupport.EnterPortal(enteringPawns, portal);
				return;
			}

			foreach (var pawn in enteringPawns)
			{
				var job = JobMaker.MakeJob(JobDefOf.EnterPortal, portal);
				job.playerForced = true;
				_ = pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc, false);
			}
		},
		MenuOptionPriority.High, null, null, 0f, null, null, true, 0);
		return false;
	}
}

// when botching a construction, add the frame to the forced job
[HarmonyPatch(typeof(Frame))]
[HarmonyPatch(nameof(Frame.FailConstruction))]
static class Frame_FailConstruction_Patch
{
	static Thing SpawnAndForce(Thing newThing, IntVec3 loc, Map map, Rot4 rot, WipeMode wipeMode, bool respawningAfterLoad, bool forbidLeavings, Pawn pawn)
	{
		var blueprint = GenSpawn.Spawn(newThing, loc, map, rot, wipeMode, respawningAfterLoad, forbidLeavings);
		var forcedJob = ForcedWork.Instance.GetForcedJob(pawn);
		forcedJob?.Add(blueprint);
		return blueprint;
	}

	public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	{
		var m_Spawn = SymbolExtensions.GetMethodInfo(() => GenSpawn.Spawn(null, default, null, Rot4.Invalid, default, false, false));
		var m_SpawnAndForce = SymbolExtensions.GetMethodInfo(() => SpawnAndForce(default, default, default, default, default, default, default, default));
		return new CodeMatcher(instructions).MatchStartForward(Call[m_Spawn])
			.ThrowIfNotMatch("Cannot find GenSpawn.Spawn in Frame.FailConstruction")
			.InsertAndAdvance(Ldarg_1)
			.SetInstruction(Call[m_SpawnAndForce])
			.InstructionEnumeration();
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
		=> ForcedWork.Instance.Remove(__instance);
}

// add colonist widget buttons
//
[HarmonyPatch(typeof(PriorityWork))]
[HarmonyPatch(nameof(PriorityWork.GetGizmos))]
[StaticConstructorOnStartup]
static class PriorityWork_GetGizmos_Patch
{
	public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> gizmos, Pawn ___pawn)
	{
		foreach (var gizmo in gizmos)
			yield return gizmo;

		var forcedWork = ForcedWork.Instance;
		var forcedJob = forcedWork.GetForcedJob(___pawn);
		if (forcedJob == null)
			yield break;
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

// handle early events
//
[HarmonyPatch(typeof(Selector))]
[HarmonyPatch(nameof(Selector.SelectorOnGUI_BeforeMainTabs))]
static class Selector_SelectorOnGUI_BeforeMainTabs_Patch
{
	public static void Prefix()
	{
		var evt = Event.current;
		if (evt.type != EventType.MouseDown) return;
		if (evt.button != 1) return;
		if (evt.shift) return;

		Controller.GetInstance().HandleEarlyRightClicks();
	}
}

// handle map events
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
		=> Controller.GetInstance().HandleDrawing();
}

// handle gui
//
[HarmonyPatch(typeof(ThingOverlays))]
[HarmonyPatch(nameof(ThingOverlays.ThingOverlaysOnGUI))]
static class ThingOverlays_ThingOverlaysOnGUI_Patch
{
	public static void Postfix()
		=> Controller.GetInstance().HandleDrawingOnGUI();
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
