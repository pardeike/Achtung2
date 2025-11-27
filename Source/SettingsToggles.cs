using Brrainz;
using RimWorld;
using System;
using UnityEngine;
using Verse;

namespace AchtungMod;

public class SettingsToggles : Window
{
	public override Vector2 InitialSize => new(520f, 480f);
	Vector2 scrollPosition = Vector2.zero;

	public SettingsToggles()
	{
		doCloseButton = true;
		doCloseX = true;
		closeOnClickedOutside = false;
		absorbInputAroundWindow = true;
	}

	record Toggle
	{
		public string label;
		public Action<bool> setter;
		public Func<bool> getter;
		public Action<bool> action;

		public Toggle(string label, Action<bool> setter, Func<bool> getter, Action<bool> action = null)
		{
			this.label = label;
			this.setter = setter;
			this.getter = getter;
			this.action = action;
		}
	}

	readonly Toggle[] toggles = [
		new Toggle("PositioningEnabled", b => Achtung.Settings.positioningEnabled = b, () => Achtung.Settings.positioningEnabled),
		new Toggle("BuildingSmart", b => Achtung.Settings.buildingSmart = b, () => Achtung.Settings.buildingSmart),
		new Toggle("RescueEnabled", b => Achtung.Settings.rescueEnabled = b, () => Achtung.Settings.rescueEnabled, ToggleRescue),
		new Toggle("ShowForceEndLetter", b => Achtung.Settings.forcedEndedLetter = b, () => Achtung.Settings.forcedEndedLetter),
		new Toggle("ReplaceCleanRoom", b => Achtung.Settings.replaceCleanRoom = b, () => Achtung.Settings.replaceCleanRoom),
		new Toggle("ReplaceFightFire", b => Achtung.Settings.replaceFightFire = b, () => Achtung.Settings.replaceFightFire),
	];

	public override void DoWindowContents(Rect inRect)
	{
		var innerRect = new Rect(0f, 0f, inRect.width - 24, toggles.Length * 60f);
		var outerRect = inRect.TopPartPixels(inRect.height - FooterRowHeight);
		Widgets.BeginScrollView(outerRect, ref scrollPosition, innerRect, true);
		var list = new Listing_Standard { ColumnWidth = innerRect.width };
		list.Begin(innerRect);
		foreach (var toggle in toggles)
		{
			var value = toggle.getter();
			list.CheckboxEnhanced(toggle.label, ref value, null, toggle.action);
			toggle.setter(value);
			list.Gap(6);
		}
		GUI.color = Color.white;
		list.End();
		Widgets.EndScrollView();
	}

	static void ToggleRescue(bool state)
	{
		var hasRescuing = DefDatabase<WorkTypeDef>.GetNamedSilentFail(Tools.RescuingWorkTypeDef.defName) != null;
		var doctorRescueWorkGiver = DefDatabase<WorkGiverDef>.GetNamed("DoctorRescue");
		if (hasRescuing != state)
		{
			if (state)
				Tools.savedWorkTypeDef = DynamicWorkTypes.AddWorkTypeDef(Tools.RescuingWorkTypeDef, WorkTypeDefOf.Doctor, doctorRescueWorkGiver);
			else
				DynamicWorkTypes.RemoveWorkTypeDef(Tools.RescuingWorkTypeDef, Tools.savedWorkTypeDef, doctorRescueWorkGiver);
		}
	}
}