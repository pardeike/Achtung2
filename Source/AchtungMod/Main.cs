using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using System.Reflection;
using System;
using Harmony;
using Harmony.ILCopying;

namespace AchtungMod
{
	[StaticConstructorOnStartup]
	static class Main
	{
		static Main()
		{
			Controller.getInstance().InstallJobDefs();

			var harmony = HarmonyInstance.Create("net.pardeike.rimworld.mods.achtung");
			harmony.PatchAll(Assembly.GetExecutingAssembly());
		}
	}

	// start of game/map
	//
	[HarmonyPatch(typeof(Root_Play))]
	[HarmonyPatch("Start")]
	static class Root_Play_Start_Patch
	{
		static void Postfix()
		{
			Settings.Load();
			Controller.getInstance().Initialize();
		}
	}

	// handle events early
	//
	[HarmonyPatch(typeof(MainTabsRoot))]
	[HarmonyPatch("HandleLowPriorityShortcuts")]
	static class MainTabsRoot_HandleLowPriorityShortcuts_Patch
	{
		static void Prefix()
		{
			Controller.getInstance().HandleEvents();
		}
	}

	// handle drawing
	//
	[HarmonyPatch(typeof(SelectionDrawer))]
	[HarmonyPatch("DrawSelectionOverlays")]
	static class SelectionDrawer_DrawSelectionOverlays_Patch
	{
		static void Prefix()
		{
			Controller.getInstance().HandleDrawing();
		}
	}

	// handle gui
	//
	[HarmonyPatch(typeof(ThingOverlays))]
	[HarmonyPatch("ThingOverlaysOnGUI")]
	static class ThingOverlays_ThingOverlaysOnGUI_Patch
	{
		static void Postfix()
		{
			Controller.getInstance().HandleDrawingOnGUI();
		}
	}

	// turn reservation error into warning inject
	//
	[HarmonyPatch(typeof(ReservationManager))]
	[HarmonyPatch("LogCouldNotReserveError")]
	static class ReservationManager_LogCouldNotReserveError_Patch
	{
		[HarmonyProcessors]
		static HarmonyProcessor ErrorToWarning(MethodBase original)
		{
			var fromMethod = AccessTools.Method(typeof(Log), "Error", new Type[] { typeof(string) });
			var toMethod = AccessTools.Method(typeof(Log), "Warning", new Type[] { typeof(string) });

			var processor = new HarmonyProcessor();
			processor.AddILProcessor(new MethodReplacer(fromMethod, toMethod));
			return processor;
		}
	}

	// custom context menu
	//
	[HarmonyPatch(typeof(FloatMenuMakerMap))]
	[HarmonyPatch("ChoicesAtFor")]
	static class FloatMenuMakerMap_ChoicesAtFor_Patch
	{
		static void Postfix(List<FloatMenuOption> __result, Vector3 clickPos, Pawn pawn)
		{
			__result.AddRange(Controller.getInstance().AchtungChoicesAtFor(clickPos, pawn));
		}
	}

	// track projectiles
	//
	[HarmonyPatch(typeof(Projectile))]
	[HarmonyPatch("Launch")]
	[HarmonyPatch(new Type[] { typeof(Thing), typeof(Vector3), typeof(LocalTargetInfo), typeof(Thing) })]
	static class Projectile_Launch_Patch
	{
		static void Prefix(Projectile __instance, Thing launcher, Vector3 origin, LocalTargetInfo targ, Thing equipment)
		{
			Controller.getInstance().AddProjectile(__instance, launcher, origin, targ, equipment);
		}
	}
}