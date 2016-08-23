using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace AchtungMod
{
	// start of game inject
	//
	public class MapIniterUtility_Patch : MonoBehaviour
	{
		internal static void MapIniterUtility_FinalizeMapInit()
		{
			MapIniterUtility.FinalizeMapInit();
			var game = new GameObject();
			game.AddComponent<MapIniterUtility_Patch>();
		}

		void Start()
		{
			Controller.getInstance(); // force creation
			Destroy(gameObject);
		}
	}

	// handle events early inject
	//
	public class MainTabsRoot_Patch : MainTabsRoot
	{
		internal void MainTabsRoot_HandleLowPriorityShortcuts()
		{
			Controller.getInstance().HandleEvents();
			HandleLowPriorityShortcuts();
		}
	}

	// handle drawing inject
	//
	public static class SelectionDrawer_Patch
	{
		internal static void SelectionDrawer_DrawSelectionOverlays()
		{
			Controller.getInstance().HandleDrawing();
			SelectionDrawer.DrawSelectionOverlays();
		}
	}

	// handle gui inject
	//
	public class ThingOverlays_Patch : ThingOverlays
	{
		public void ThingOverlays_ThingOverlaysOnGUI()
		{
			ThingOverlaysOnGUI();
			Controller.getInstance().HandleDrawingOnGUI();
		}
	}

	// turn reservation error into warning inject
	//
	public class ReservationManager_Patch
	{
		internal void ReservationManager_LogCouldNotReserveError(Pawn claimant, TargetInfo target, int maxPawns)
		{
			Job curJob = claimant.CurJob;
			string str = "null";
			int curToilIndex = -1;
			if (curJob != null)
			{
				str = curJob.ToString();
				if (claimant.jobs.curDriver != null)
				{
					curToilIndex = claimant.jobs.curDriver.CurToilIndex;
				}
			}

			Pawn pawn = Find.Reservations.FirstReserverOf(target, claimant.Faction, true);
			string str2 = "null";
			int num2 = -1;
			if (pawn != null)
			{
				Job job2 = pawn.CurJob;
				if (job2 != null)
				{
					str2 = job2.ToString();
					if (pawn.jobs.curDriver != null)
					{
						num2 = pawn.jobs.curDriver.CurToilIndex;
					}
				}
			}

			Log.Warning(string.Concat(new object[] {
					 "Could not reserve ", target, " for ", claimant.NameStringShort, " doing job ", str, "(curToil=", curToilIndex, ") for maxPawns ", maxPawns
				}));
			Log.Warning(string.Concat(new object[] {
					 "Existing reserver: ", pawn.NameStringShort, " doing job ", str2, "(curToil=", num2, ")"
				}));

		}
	}

	public static class FloatMenuMakerMap_Patch
	{
		public static List<FloatMenuOption> FloatMenuMakerMap_ChoicesAtFor(Vector3 clickPos, Pawn pawn)
		{
			List<FloatMenuOption> options = FloatMenuMakerMap.ChoicesAtFor(clickPos, pawn);
			options.AddRange(Controller.getInstance().ChoicesAtFor(clickPos, pawn));
			return options;
		}
	}

	[StaticConstructorOnStartup]
	static class Main
	{
		static Main()
		{
			var injector = new HookInjector();
			injector.Inject(typeof(MapIniterUtility), "FinalizeMapInit", typeof(MapIniterUtility_Patch));
			injector.Inject(typeof(MainTabsRoot), "HandleLowPriorityShortcuts", typeof(MainTabsRoot_Patch));
			injector.Inject(typeof(SelectionDrawer), "DrawSelectionOverlays", typeof(SelectionDrawer_Patch));
			injector.Inject(typeof(ThingOverlays), "ThingOverlaysOnGUI", typeof(ThingOverlays_Patch));
			injector.Inject(typeof(ReservationManager), "LogCouldNotReserveError", typeof(ReservationManager_Patch));
			injector.Inject(typeof(FloatMenuMakerMap), "ChoicesAtFor", typeof(FloatMenuMakerMap_Patch));
		}
	}
}