using BrrainzTools;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using Verse;
using Verse.AI;

namespace AchtungMod
{
	[StaticConstructorOnStartup]
	public static class AchtungLoader
	{
		public static bool IsSameSpotInstalled;

		static AchtungLoader()
		{
			Controller.InstallDefs();

			var harmony = new Harmony("net.pardeike.rimworld.mods.achtung");
			harmony.PatchAll();

			const string sameSpotId = "net.pardeike.rimworld.mod.samespot";
			IsSameSpotInstalled = harmony.GetPatchedMethods()
				.Any(method => Harmony.GetPatchInfo(method).Transpilers.Any(transpiler => transpiler.owner == sameSpotId));

			// multiplayer
			//
			if (MP.enabled)
				MP.RegisterAll();
		}
	}

	public class Achtung : Mod
	{
		public static AchtungSettings Settings;

		public Achtung(ModContentPack content) : base(content)
		{
			Settings = GetSettings<AchtungSettings>();
		}

		public override void DoSettingsWindowContents(Rect inRect)
		{
			AchtungSettings.DoWindowContents(inRect);
		}

		public override string SettingsCategory()
		{
			return "Achtung!";
		}
	}

	[HarmonyPatch(typeof(World))]
	[HarmonyPatch(nameof(World.FinalizeInit))]
	static class World_FinalizeInit_Patch
	{
		public static void Prefix()
		{
			ForcedWork.Instance = null;
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

	// forced hauling outside of allowed area
	//
	[HarmonyPatch(typeof(HaulAIUtility))]
	[HarmonyPatch(nameof(HaulAIUtility.PawnCanAutomaticallyHaulFast))]
	static class HaulAIUtility_PawnCanAutomaticallyHaulFast_Patch
	{
		public static bool Prefix(Pawn p, bool forced, ref bool __result)
		{
			if (p?.Map != null && Achtung.Settings.ignoreRestrictions)
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

	// allow for jobs outside allowed area
	//
	[HarmonyPatch(typeof(ForbidUtility))]
	[HarmonyPatch(nameof(ForbidUtility.InAllowedArea))]
	static class ForbidUtility_InAllowedArea_Patch
	{
		public static void Postfix(IntVec3 c, Pawn forPawn, ref bool __result)
		{
			var map = forPawn?.Map;
			if (map == null)
				return;

			var forcedWork = ForcedWork.Instance;
			if (forcedWork.HasForcedJob(forPawn))
			{
				if (Achtung.Settings.ignoreRestrictions)
				{
					__result = true;
					return;
				}

				return;
			}

			if (__result == true)
			{
				// ignore forced work cells if colonist is not forced
				if (forcedWork.IsForbiddenCell(map, c))
					__result = false;
			}
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
			if (f_NotInHomeAreaTrans == null) throw new Exception("Cannot find method WorkGiver_FixBrokenDownBuilding.NotInHomeAreaTrans");
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
					instr.Insert(i++, new CodeInstruction(OpCodes.Ldarg_3));
					instr.Insert(i++, new CodeInstruction(OpCodes.Brtrue, label));
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
			if (__result == false)
				return;

			var pawn = __instance.pawn;
			var forcedWork = ForcedWork.Instance;
			if (forcedWork.HasForcedJob(pawn) == false)
				return;

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
			if (forcedWork.HasForcedJob(claimant) == false)
				return;

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
			if (forcedWork.HasForcedJob(claimant) == false)
				return;

			forcedWork.RemoveForbiddenLocations(claimant);
		}
	}*/

	[HarmonyPatch(typeof(ReservationManager))]
	[HarmonyPatch(nameof(ReservationManager.RespectsReservationsOf))]
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
			var toMethod = AccessTools.Method(typeof(ReservationManager_Reserve_Patch), nameof(ReservationManager_Reserve_Patch.CanReserve));
			return instructions.MethodReplacer(fromMethod, toMethod);
		}
	}

	// ignore forbidden for forced jobs
	//
	[HarmonyPatch(typeof(ForbidUtility))]
	[HarmonyPatch(nameof(ForbidUtility.IsForbidden))]
	[HarmonyPatch(new Type[] { typeof(Thing), typeof(Pawn) })]
	static class ForbidUtility_IsForbidden_Patch
	{
		public static bool Prefix(Pawn pawn, ref bool __result)
		{
			if (pawn?.Map != null && Achtung.Settings.ignoreForbidden)
			{
				var forcedWork = ForcedWork.Instance;
				if (forcedWork.HasForcedJob(pawn))
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
		public static void My_Notify_Teleported(Pawn pawn, bool _1, bool _2)
		{
			pawn.Drawer.tweener.ResetTweenedPosToRoot();
		}

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
					list.Insert(idx, new CodeInstruction(OpCodes.Ldarg_1));
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
	[HarmonyPatch(typeof(FloatMenuMakerMap))]
	[HarmonyPatch(nameof(FloatMenuMakerMap.AddJobGiverWorkOrders))]
	static class FloatMenuMakerMap_AddJobGiverWorkOrders_Patch
	{
		public static void Prefix(Pawn pawn, out ForcedWork __state)
		{
			__state = ForcedWork.Instance;
			if (pawn?.Map != null)
				__state.Prepare(pawn);
		}

		public static void Postfix(Pawn pawn, ForcedWork __state)
		{
			if (pawn?.Map != null)
				__state.Unprepare(pawn);
		}

		public static int GetPriority(Pawn pawn, WorkTypeDef w)
		{
			if (Achtung.Settings.ignoreAssignments)
				return pawn.WorkTypeIsDisabled(w) ? 0 : 1;
			return pawn.workSettings.GetPriority(w);
		}

		static bool IgnoreForbiddenHauling(WorkGiver_Scanner workgiver, Thing thing)
		{
			if (workgiver is WorkGiver_Haul && thing?.def != null && thing.def.alwaysHaulable == false && thing.def.EverHaulable == false) return false;
			if (Achtung.Settings.ignoreForbidden == false) return false;
			return workgiver.Ignorable();
		}

		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
		{
			var m_GetPriority = AccessTools.Method(typeof(Pawn_WorkSettings), nameof(Pawn_WorkSettings.GetPriority));
			var floatMenuOptionConstructorArgs = new[] { typeof(string), typeof(Action), typeof(MenuOptionPriority), typeof(Action<Rect>), typeof(Thing), typeof(float), typeof(Func<Rect, bool>), typeof(WorldObject), typeof(bool), typeof(int) };
			var c_FloatMenuOption = AccessTools.Constructor(typeof(FloatMenuOption), floatMenuOptionConstructorArgs);
			var m_CreateForcedMenuItem = SymbolExtensions.GetMethodInfo(() => ForcedFloatMenuOption.CreateForcedMenuItem(default, default, default, default, default, default, default, default, default, default, default, default, default));
			var m_Accepts = SymbolExtensions.GetMethodInfo(() => new ThingRequest().Accepts(default));
			var m_IgnoreForbiddenHauling = SymbolExtensions.GetMethodInfo(() => IgnoreForbiddenHauling(default, default));
			if (c_FloatMenuOption == null)
				Log.Error($"Cannot find constructor for FloatMenuOption() with argument types {floatMenuOptionConstructorArgs.Join(t => t.Name)}");

			var list = instructions.ToList();

			var idx = list.FindIndex(instr => instr.Calls(m_Accepts));
			if (idx < 0 || idx >= list.Count)
				Log.Error("Cannot find ThingRequest.Accepts in RimWorld.FloatMenuMakerMap::AddJobGiverWorkOrders");
			else
			{
				var loadThingVar = list[idx - 1];
				var jump = list[idx + 1];
				if (jump.Branches(out var label) == false)
					Log.Error("Cannot find branch after ThingRequest.Accepts in RimWorld.FloatMenuMakerMap::AddJobGiverWorkOrders");
				else
					list.InsertRange(idx + 2, new[]
					{
						list[idx + 2], // local variable 'WorkGiver_Scanner'
						loadThingVar.Clone(), // clicked thing
						new CodeInstruction(OpCodes.Call, m_IgnoreForbiddenHauling),
						new CodeInstruction(OpCodes.Brtrue, label)
					});
			}

			var foundCount = 0;
			while (true)
			{
				idx = list.FindIndex(instr => instr.Calls(m_GetPriority));
				if (idx < 2 || idx >= list.Count)
					break;
				foundCount++;
				list[idx - 2].opcode = OpCodes.Nop;
				list[idx].opcode = OpCodes.Call;
				list[idx].operand = SymbolExtensions.GetMethodInfo(() => GetPriority(null, WorkTypeDefOf.Doctor));
			}
			if (foundCount != 2)
				Log.Error("Cannot find 2x Pawn_WorkSettings.GetPriority in RimWorld.FloatMenuMakerMap::AddJobGiverWorkOrders");

			foundCount = 0;
			Enumerable.Range(0, list.Count)
				.DoIf(i => list[i].opcode == OpCodes.Isinst && (Type)list[i].operand == typeof(WorkGiver_Scanner), i =>
				{
					idx = i + 1;
					if (list[idx].opcode == OpCodes.Stloc_S)
					{
						var localVar = list[idx].operand;

						idx = list.FindIndex(idx, code => code.opcode == OpCodes.Newobj && (ConstructorInfo)code.operand == c_FloatMenuOption);
						if (idx < 0)
							Log.Error("Cannot find 'Isinst WorkGiver_Scanner' in RimWorld.FloatMenuMakerMap::AddJobGiverWorkOrders");
						else
						{
							list[idx].opcode = OpCodes.Call;
							list[idx].operand = m_CreateForcedMenuItem;
							list.InsertRange(idx, new CodeInstruction[]
							{
								new CodeInstruction(OpCodes.Ldarg_1),
								new CodeInstruction(OpCodes.Ldarg_0),
								new CodeInstruction(OpCodes.Ldloc_S, localVar)
							});

							foundCount++;
						}
					}
				});
			if (foundCount != 2)
				Log.Error("Cannot find 2x 'Isinst WorkGiver_Scanner', 'Stloc_S n' -> 'Newobj FloatMenuOption()' in RimWorld.FloatMenuMakerMap::AddJobGiverWorkOrders");

			foreach (var instruction in list)
				yield return instruction;
		}
	}
	//
	[HarmonyPatch(typeof(Pawn_JobTracker))]
	[HarmonyPatch(nameof(Pawn_JobTracker.EndCurrentJob))]
	static class Pawn_JobTracker_EndCurrentJob_Patch
	{
		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var m_CleanupCurrentJob = AccessTools.Method(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.CleanupCurrentJob));
			var m_ContinueJob = AccessTools.Method(typeof(ForcedJob), nameof(ForcedJob.ContinueJob));
			var f_pawn = AccessTools.Field(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.pawn));
			var f_curJob = AccessTools.Field(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.curJob));

			if (m_CleanupCurrentJob == null) throw new Exception("Cannot find method Pawn_JobTracker.CleanupCurrentJob");
			if (f_pawn == null) throw new Exception("Cannot find field Pawn_JobTracker.pawn");

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

				yield return new CodeInstruction(OpCodes.Ldarg_0);
				yield return new CodeInstruction(OpCodes.Ldarg_0);
				yield return new CodeInstruction(OpCodes.Ldfld, f_curJob);
				yield return new CodeInstruction(OpCodes.Ldarg_0);
				yield return new CodeInstruction(OpCodes.Ldfld, f_pawn);
				yield return new CodeInstruction(OpCodes.Ldarg_1);
				yield return new CodeInstruction(OpCodes.Call, m_ContinueJob);

				yield return new CodeInstruction(OpCodes.Brtrue, endLabel);
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

	// add force radius buttons
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

		[SyncMethod] // multiplayer
		public static void ChangeCellRadiusSynced(Pawn pawn, int delta)
		{
			var forcedWork = ForcedWork.Instance;
			var forcedJob = forcedWork.GetForcedJob(pawn);
			if (forcedJob != null && forcedJob.cellRadius + delta >= 0)
				forcedJob.ChangeCellRadius(delta);
		}

		[SyncMethod] // multiplayer
		public static void ToggleSmartBuildingSynced(Pawn pawn)
		{
			var forcedWork = ForcedWork.Instance;
			var forcedJob = forcedWork.GetForcedJob(pawn);
			forcedJob.ToggleSmartBuilding();
		}

		public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> gizmos, Pawn ___pawn)
		{
			var gizmoList = gizmos.ToList();
			foreach (var gizmo in gizmos)
				yield return gizmo;

			var forcedWork = ForcedWork.Instance;
			var forcedJob = forcedWork.GetForcedJob(___pawn);
			if (forcedJob == null)
				yield break;

			var radius = forcedJob.cellRadius;
			var smart = forcedJob.buildSmart;

			yield return new Command_Action
			{
				defaultLabel = "IncreaseForceRadius".Translate(),
				defaultDesc = "IncreaseForceRadiusDesc".Translate(radius),
				icon = ForceRadiusExpand,
				activateSound = SoundDefOf.Designate_ZoneAdd,
				action = delegate { ChangeCellRadiusSynced(___pawn, 1); }
			};

			yield return new Command_Action
			{
				defaultLabel = "DecreaseForceRadius".Translate(),
				defaultDesc = "DecreaseForceRadiusDesc".Translate(radius),
				icon = radius > 0 ? ForceRadiusShrink : ForceRadiusShrinkOff,
				activateSound = radius > 0 ? SoundDefOf.Designate_ZoneAdd : SoundDefOf.Designate_Failed,
				action = delegate { ChangeCellRadiusSynced(___pawn, -1); }
			};

			yield return new Command_Action
			{
				defaultLabel = "BuildingSmart".Translate(),
				defaultDesc = "BuildingSmartDesc".Translate(),
				icon = smart ? BuildingSmart : BuildingSmartOff,
				activateSound = smart ? SoundDefOf.Designate_PlanRemove : SoundDefOf.Designate_PlanAdd,
				action = delegate { ToggleSmartBuildingSynced(___pawn); }
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
			if (__result == false || ___pawn?.Map == null)
				return;

			var forcedWork = ForcedWork.Instance;
			if (forcedWork.HasForcedJob(___pawn) == false)
				return;

			var forcedJob = forcedWork.GetForcedJob(___pawn);
			if (forcedJob == null)
				return;

			var workGiver = ___pawn.CurJob?.workGiverDef;
			if (workGiver == null)
				return;

			__result = forcedJob.workgiverDefs.Contains(workGiver) == false;
		}
	}

	// handle events early
	//
	[HarmonyPatch(typeof(Selector))]
	[HarmonyPatch(nameof(Selector.HandleMapClicks))]
	static class Selector_HandleMapClicks_Patch
	{
		public static bool Prefix()
		{
			return Controller.GetInstance().HandleEvents();
		}
	}

	// handle drawing
	//
	[HarmonyPatch(typeof(SelectionDrawer))]
	[HarmonyPatch(nameof(SelectionDrawer.DrawSelectionOverlays))]
	static class SelectionDrawer_DrawSelectionOverlays_Patch
	{
		public static void Postfix()
		{
			if (WorldRendererUtility.WorldRenderedNow == false)
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
			if (WorldRendererUtility.WorldRenderedNow == false)
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
			var fromMethod = SymbolExtensions.GetMethodInfo(() => Log.Error(""));
			var toMethod = SymbolExtensions.GetMethodInfo(() => Log.Warning(""));
			return instructions.MethodReplacer(fromMethod, toMethod);
		}
	}

	// pawn inspector panel
	//
	[HarmonyPatch(typeof(Pawn))]
	[HarmonyPatch(nameof(Pawn.GetInspectString))]
	static class Pawn_GetInspectString_Patch
	{
		public static void Postfix(Pawn __instance, ref string __result)
		{
			var forcedWork = ForcedWork.Instance;
			if (forcedWork.HasForcedJob(__instance))
				__result = __result + "\n" + "ForcedCommandState".Translate();
		}
	}

	// custom context menu
	//
	[HarmonyPatch(typeof(FloatMenuMakerMap))]
	[HarmonyPatch(nameof(FloatMenuMakerMap.ChoicesAtFor))]
	[StaticConstructorOnStartup]
	static class FloatMenuMakerMap_ChoicesAtFor_Postfix
	{
		public static void Postfix(List<FloatMenuOption> __result, Vector3 clickPos, Pawn pawn)
		{
			if (pawn?.Map != null && pawn.Drafted == false)
				if (WorldRendererUtility.WorldRenderedNow == false)
					__result.AddRange(Controller.AchtungChoicesAtFor(clickPos, pawn));
		}
	}
	//
	[HarmonyPatch(typeof(FloatMenuMakerMap))]
	[HarmonyPatch(nameof(FloatMenuMakerMap.ChoicesAtFor))]
	[StaticConstructorOnStartup]
	static class FloatMenuMakerMap_ChoicesAtFor_Finalizer
	{
		static readonly Texture2D AttentionIcon = ContentFinder<Texture2D>.Get("AttentionIcon", true);

		public static bool Prepare(MethodBase _)
		{
			return AccessTools.TypeByName("VisualExceptions.HarmonyMain") == null;
		}

		public static Exception Finalizer(Exception __exception, ref List<FloatMenuOption> __result)
		{
			if (__exception != null)
			{
				var handler = new ExceptionAnalyser(__exception);

				var mods = handler.GetInvolvedMods(new[] { "brrainz.achtung" }).Select(info => info.metaData.GetWorkshopName() ?? info.metaData.Name).Distinct();
				var possibleMods = mods.Any() ? $". Possible mods in order of likeliness: {mods.Join()}" : "";
				__result ??= new List<FloatMenuOption>();
				__result.Add(new FloatMenuOption($"Achtung found that some other mod(s) caused the following error: {__exception.GetType().Name}{possibleMods}. Select to copy annotated stacktrace to clipboard", () =>
				{
					var te = new TextEditor { text = handler.GetStacktrace() };
					te.SelectAll();
					te.Copy();
				}, AttentionIcon, Color.yellow, MenuOptionPriority.VeryLow));
			}
			return null;
		}
	}
}
