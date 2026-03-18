using Brrainz;
using RimWorld;
using System;
using UnityEngine;
using Verse;

namespace AchtungMod;

public class SettingsToggles : Window
{
	const float ScrollbarWidth = 24f;
	const float ToggleGap = 6f;
	const float DescriptionIndent = 34f;
	const float MeasureHeight = 10000f;

	public override Vector2 InitialSize => new(520f, 520f);
	Vector2 scrollPosition = Vector2.zero;

	public SettingsToggles()
	{
		doCloseButton = true;
		doCloseX = true;
		closeOnClickedOutside = false;
		absorbInputAroundWindow = true;
		draggable = true;
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
		var outerRect = inRect.TopPartPixels(inRect.height - FooterRowHeight);
		var innerWidth = inRect.width - ScrollbarWidth;
		var innerHeight = Mathf.Max(CalculateContentHeight(innerWidth), outerRect.height);
		scrollPosition.y = Mathf.Clamp(scrollPosition.y, 0f, Mathf.Max(0f, innerHeight - outerRect.height));
		var innerRect = new Rect(0f, 0f, innerWidth, innerHeight);

		Widgets.BeginScrollView(outerRect, ref scrollPosition, innerRect, true);
		var list = new Listing_Standard
		{
			ColumnWidth = innerRect.width,
			maxOneColumn = true
		};
		list.Begin(innerRect);
		DrawToggles(list, applyChanges: true);
		GUI.color = Color.white;
		Text.Font = GameFont.Small;
		list.End();
		Widgets.EndScrollView();
	}

	float CalculateContentHeight(float width)
	{
		var measureRect = new Rect(0f, 0f, width, MeasureHeight);
		var list = new Listing_Standard
		{
			ColumnWidth = width,
			maxOneColumn = true
		};

		list.Begin(measureRect);
		DrawToggles(list, applyChanges: false);
		var height = list.CurHeight;
		list.End();

		return height;
	}

	void DrawToggles(Listing_Standard list, bool applyChanges)
	{
		foreach (var toggle in toggles)
		{
			var value = toggle.getter();
			if (applyChanges)
			{
				list.CheckboxEnhanced(toggle.label, ref value, null, toggle.action);
				toggle.setter(value);
			}
			else
				MeasureCheckboxEnhanced(list, toggle.label);

			list.Gap(ToggleGap);
		}
	}

	static void MeasureCheckboxEnhanced(Listing_Standard list, string name)
	{
		Text.Font = GameFont.Small;
		_ = list.GetRect(Text.CalcHeight((name + "Title").Translate(), list.ColumnWidth));
		list.Gap(list.verticalSpacing);

		Text.Font = GameFont.Tiny;
		list.ColumnWidth -= DescriptionIndent;
		_ = list.GetRect(Text.CalcHeight((name + "Explained").Translate(), list.ColumnWidth));
		list.Gap(list.verticalSpacing);
		list.ColumnWidth += DescriptionIndent;

		Text.Font = GameFont.Small;
		list.Gap(ToggleGap);
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
