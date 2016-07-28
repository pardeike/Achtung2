using Verse;
using RimWorld;
using System.Reflection;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace AchtungMod
{
	public static class Patcher
	{
		// this method is bascially a copy of RimWorlds "SelectorOnGUI" except for the
		// first call to the private method "HandleWorldClicks" which we call via reflections
		//
		public static void SelectorOnGUI_Original()
		{
			MethodInfo handleWorldClicksMethod = Find.Selector.GetType().GetMethod("HandleWorldClicks", BindingFlags.NonPublic | BindingFlags.Instance);
			handleWorldClicksMethod.Invoke(Find.Selector, null);

			if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape && Find.Selector.SelectedObjects.Count > 0)
			{
				Find.Selector.ClearSelection();
				Event.current.Use();
			}
			if (Find.Selector.NumSelected > 0 && Find.MainTabsRoot.OpenTab == null)
			{
				Find.MainTabsRoot.SetCurrentTab(MainTabDefOf.Inspect, false);
			}
		}

		// IMPORTANT: keep this method short and simple or else the patcher will fail weirdly
		//
		public static void SelectorOnGUI()
		{
			// we act on right-click only
			//
			bool isRightClick = Event.current.button == 1;
			if (isRightClick)
			{
				EventType type = Event.current.type;
				Vector3 where = Gen.ScreenToWorldPoint(Input.mousePosition);

				// our main method
				bool eventHandled = Worker.RightClickHandler(type, where);
				if (eventHandled)
				{
					return;
				}
			}

			SelectorOnGUI_Original();
		}
	}
}