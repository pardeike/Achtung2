using Brrainz;
using RimWorld;
using System;
using UnityEngine;
using Verse;

namespace AchtungMod;

public enum AchtungModKey
{
	None,
	Alt,
	Ctrl,
	Shift,
	Meta
}

public enum CommandMenuMode
{
	Auto,
	PressForMenu,
	PressForPosition,
	Delayed
}

public enum DraftedColonistDraggingMode
{
	Off,
	Unselected,
	Always
}

public enum BreakLevel
{
	None,
	Minor,
	Major,
	AlmostExtreme,
	Extreme
}

public enum HealthLevel
{
	None,
	ShouldBeTendedNow,
	PrefersMedicalRest,
	NeedsMedicalRest,
	InPainShock
}

public enum WorkMarkers
{
	Animated,
	Static,
	Off
}

public class AchtungSettings : ModSettings
{
	const float ColumnGap = 18f;
	const float HeaderHeight = 32f;
	const float HeaderGap = 8f;
	const float ScrollbarWidth = 24f;
	const float CloseButtonTopPadding = 12f;
	const float ControlGap = 10f;
	const float SubheadTopGap = ControlGap * 2f;
	const float SubheadBottomGap = 8f;
	const float ColumnBottomPadding = 20f;
	const float DialogFooterClearance = 40f;
	const float ColumnScrollFooterPadding = ColumnBottomPadding + DialogFooterClearance;
	const float SubheadBarWidthFactor = 0.60f;
	const float SubheadBarHorizontalPadding = 6f;
	const float SubheadBarVerticalPadding = 0f;

	struct ColumnMeasureCache
	{
		public float width;
		public float height;
	}

	static Vector2 positioningScrollPosition = Vector2.zero;
	static Vector2 forcingScrollPosition = Vector2.zero;
	static ColumnMeasureCache positioningMeasureCache;
	static ColumnMeasureCache forcingMeasureCache;

	public bool positioningEnabled = true;
	public bool rescueEnabled = true;
	public AchtungModKey achtungKey = AchtungModKey.Alt;
	public CommandMenuMode forceCommandMenuMode = CommandMenuMode.Delayed;
	public AchtungModKey forceCommandMenuKey = AchtungModKey.Ctrl;
	public DraftedColonistDraggingMode draftedColonistDraggingMode = DraftedColonistDraggingMode.Always;
	public BreakLevel breakLevel = BreakLevel.AlmostExtreme;
	public HealthLevel healthLevel = HealthLevel.InPainShock;
	public bool ignoreForbidden = false;
	public bool ignoreRestrictions = false;
	public bool ignoreAssignments = false;
	public WorkMarkers workMarkers = WorkMarkers.Animated;
	public bool buildingSmart = true;
	public bool keepDraftedAndUndraftedCommandsSeparate = false;
	public int maxForcedItems = 64;
	public int menuDelay = 250;
	public bool forcedEndedLetter = true;
	public bool replaceCleanRoom = true;
	public bool replaceFightFire = true;

	public static readonly int UnlimitedForcedItems = 2000;

	public bool CustomPositioningEnabled => positioningEnabled;
	public bool ForceCommandsEnabled => maxForcedItems > 0;

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_Values.Look(ref positioningEnabled, "positioningEnabled", true, true);
		Scribe_Values.Look(ref rescueEnabled, "rescueEnabled", true, true);
		Scribe_Values.Look(ref achtungKey, "achtungKey", AchtungModKey.Alt, true);
		Scribe_Values.Look(ref forceCommandMenuMode, "forceCommandMenuMode", CommandMenuMode.Auto, true);
		Scribe_Values.Look(ref forceCommandMenuKey, "forceCommandMenuKey", AchtungModKey.Ctrl, true);
		Scribe_Values.Look(ref draftedColonistDraggingMode, "draftedColonistDraggingMode", DraftedColonistDraggingMode.Always, true);
		Scribe_Values.Look(ref breakLevel, "BreakLevel", BreakLevel.AlmostExtreme, true);
		Scribe_Values.Look(ref healthLevel, "HealthLevel", HealthLevel.InPainShock, true);
		Scribe_Values.Look(ref ignoreForbidden, "ignoreForbidden", false, true);
		Scribe_Values.Look(ref ignoreRestrictions, "ignoreRestrictions", false, true);
		Scribe_Values.Look(ref ignoreAssignments, "ignoreAssignments", false, true);
		Scribe_Values.Look(ref workMarkers, "workMarkers", WorkMarkers.Animated, true);
		Scribe_Values.Look(ref buildingSmart, "buildingSmart", false, true);
		Scribe_Values.Look(ref keepDraftedAndUndraftedCommandsSeparate, "keepDraftedAndUndraftedCommandsSeparate", false, true);
		Scribe_Values.Look(ref maxForcedItems, "maxForcedItems", 64, true);
		Scribe_Values.Look(ref menuDelay, "menuDelay", 250, true);
		Scribe_Values.Look(ref forcedEndedLetter, "forcedEndedLetter", true, true);
		Scribe_Values.Look(ref replaceCleanRoom, "replaceCleanRoom", true, true);
		Scribe_Values.Look(ref replaceFightFire, "replaceFightFire", true, true);

		if (Scribe.mode == LoadSaveMode.PostLoadInit && Achtung.harmony != null)
			ForbidUtility_IsForbidden_Patch.FixPatch();
	}

	public static void DoWindowContents(Rect canvas)
	{
		var helpRect = canvas;
		helpRect.height = Text.LineHeight + 2;
		helpRect.x -= 17;
		helpRect.y -= 33 + 4;
		helpRect.xMin = helpRect.xMax - "AchtungTutorialButton".Translate().GetWidthCached() - 42;
		if (Widgets.ButtonText(helpRect, "AchtungTutorialButton".Translate()))
			ModFeatures.ShowAgain<Achtung>(true);

		var contentRect = canvas;
		contentRect.yMin += 4f;
		contentRect.yMax -= CloseButtonTopPadding;
		var columnWidth = (contentRect.width - ColumnGap) / 2f;
		var positioningRect = new Rect(contentRect.x, contentRect.y, columnWidth, contentRect.height);
		var forcingRect = new Rect(positioningRect.xMax + ColumnGap, contentRect.y, columnWidth, contentRect.height);

		DrawColumn(positioningRect, "PositioningSettingsHeader", ref positioningScrollPosition, ref positioningMeasureCache, DrawPositioningSettings);
		DrawColumn(forcingRect, "ForcingSettingsHeader", ref forcingScrollPosition, ref forcingMeasureCache, DrawForcingSettings);
	}

	static void DrawColumn(Rect rect, string titleKey, ref Vector2 scrollPosition, ref ColumnMeasureCache measureCache, Action<Listing_Standard> drawSettings)
	{
		DrawColumnHeader(rect.TopPartPixels(HeaderHeight), titleKey);

		var scrollRect = rect;
		scrollRect.yMin += HeaderHeight + HeaderGap;
		var viewWidth = scrollRect.width - ScrollbarWidth;
		if (Mathf.Approximately(measureCache.width, viewWidth) == false)
		{
			measureCache.width = viewWidth;
			measureCache.height = 0f;
		}

		var viewHeight = Mathf.Max(scrollRect.height, measureCache.height);
		scrollPosition.y = Mathf.Clamp(scrollPosition.y, 0f, Mathf.Max(0f, viewHeight - scrollRect.height));
		var viewRect = new Rect(0f, 0f, viewWidth, viewHeight);
		Widgets.BeginScrollView(scrollRect, ref scrollPosition, viewRect, true);
		var drawnHeight = DrawColumnList(viewRect, viewWidth, drawSettings);
		Widgets.EndScrollView();
		measureCache.height = Mathf.Max(scrollRect.height, drawnHeight);
	}

	static float DrawColumnList(Rect rect, float columnWidth, Action<Listing_Standard> drawSettings)
	{
		var list = new Listing_Standard
		{
			ColumnWidth = columnWidth,
			maxOneColumn = true
		};
		list.Begin(rect);
		DrawColumnContent(list, drawSettings);
		var height = list.CurHeight;
		GUI.color = Color.white;
		Text.Font = GameFont.Small;
		list.End();
		return height;
	}

	static void DrawColumnHeader(Rect rect, string titleKey)
	{
		var savedFont = Text.Font;
		var savedAnchor = Text.Anchor;
		Text.Font = GameFont.Medium;
		Text.Anchor = TextAnchor.UpperLeft;
		GUI.color = Color.white;
		Widgets.Label(rect, titleKey.Translate());
		Widgets.DrawLineHorizontal(rect.x, rect.yMax - 4f, rect.width);
		Text.Font = savedFont;
		Text.Anchor = savedAnchor;
	}

	static void DrawColumnContent(Listing_Standard list, Action<Listing_Standard> drawSettings)
	{
		drawSettings(list);
		list.Gap(ColumnScrollFooterPadding);
	}

	static void DrawSettingGap(Listing_Standard list) => list.Gap(ControlGap);

	static void DrawSubheading(Listing_Standard list, string name, bool addTopGap = true)
	{
		if (addTopGap)
			list.Gap(SubheadTopGap);

		var savedFont = Text.Font;
		var savedAnchor = Text.Anchor;
		var savedColor = GUI.color;
		Text.Font = GameFont.Small;

		var title = (name + "Title").Translate().ToString();
		var rowRect = list.GetRect(Text.LineHeight + SubheadBarVerticalPadding * 2f);
		var titleWidth = Text.CalcSize(title).x + SubheadBarHorizontalPadding * 2f;
		var barWidth = Mathf.Min(list.ColumnWidth, Mathf.Max(list.ColumnWidth * SubheadBarWidthFactor, titleWidth));
		var barRect = new Rect(rowRect.x, rowRect.y, barWidth, rowRect.height);
		var labelRect = new Rect(barRect.x + SubheadBarHorizontalPadding, barRect.y, barRect.width - SubheadBarHorizontalPadding * 2f, barRect.height);

		Widgets.DrawBoxSolid(barRect, Color.white);
		GUI.color = new Color(0.08f, 0.08f, 0.08f);
		Text.Anchor = TextAnchor.MiddleLeft;
		Widgets.Label(labelRect, title);
		Text.Anchor = savedAnchor;
		Text.Font = savedFont;
		GUI.color = savedColor;
		list.Gap(SubheadBottomGap);
	}

	static void DrawPositioningSettings(Listing_Standard list)
	{
		DrawSubheading(list, "PositioningRightClick", false);
		DrawCheckboxSetting(list, "PositioningEnabled", ref Achtung.Settings.positioningEnabled, null);
		if (Achtung.Settings.positioningEnabled)
		{
			DrawSettingGap(list);
			DrawValueSetting(list, "ForceCommandMenuMode", true, ref Achtung.Settings.forceCommandMenuMode);
			DrawSettingGap(list);
			DrawCheckboxSetting(list, "KeepDraftedAndUndraftedCommandsSeparate", ref Achtung.Settings.keepDraftedAndUndraftedCommandsSeparate, null);
			DrawPositioningModeOptions(list);

			DrawSubheading(list, "PositioningDragging");
			DrawValueSetting(list, "DraftedColonistDraggingMode", true, ref Achtung.Settings.draftedColonistDraggingMode);
			DrawSettingGap(list);
			DrawValueSetting(list, "AchtungModifier", false, ref Achtung.Settings.achtungKey);
		}
	}

	static void DrawForcingSettings(Listing_Standard list)
	{
		DrawSubheading(list, "ForcingCommands", false);
		DrawSliderSetting(list, "MaxForcedItems", ref Achtung.Settings.maxForcedItems, 0, UnlimitedForcedItems, ForcedItemsString);
		DrawSettingGap(list);
		DrawCheckboxSetting(list, "ReplaceCleanRoom", ref Achtung.Settings.replaceCleanRoom, null);
		DrawSettingGap(list);
		DrawCheckboxSetting(list, "ReplaceFightFire", ref Achtung.Settings.replaceFightFire, null);
		DrawSettingGap(list);
		DrawCheckboxSetting(list, "RescueEnabled", ref Achtung.Settings.rescueEnabled, ToggleRescue);

		DrawSubheading(list, "ForcingJobBehavior");
		DrawCheckboxSetting(list, "BuildingSmart", ref Achtung.Settings.buildingSmart, null);
		DrawSettingGap(list);
		DrawCheckboxSetting(list, "IgnoreForbidden", ref Achtung.Settings.ignoreForbidden, _ => ForbidUtility_IsForbidden_Patch.FixPatch());
		DrawSettingGap(list);
		DrawCheckboxSetting(list, "IgnoreRestrictions", ref Achtung.Settings.ignoreRestrictions, null);
		DrawSettingGap(list);
		DrawCheckboxSetting(list, "IgnoreAssignments", ref Achtung.Settings.ignoreAssignments, null);

		DrawSubheading(list, "ForcingStopRules");
		DrawValueSetting(list, "BreakLevel", false, ref Achtung.Settings.breakLevel);
		DrawSettingGap(list);
		DrawValueSetting(list, "HealthLevel", false, ref Achtung.Settings.healthLevel);

		DrawSubheading(list, "ForcingFeedback");
		DrawValueSetting(list, "WorkMarkers", false, ref Achtung.Settings.workMarkers);
		DrawSettingGap(list);
		DrawCheckboxSetting(list, "ShowForceEndLetter", ref Achtung.Settings.forcedEndedLetter, null);
	}

	static string ForcedItemsString(int n)
		=> n == 0 ? "Disabled".Translate().ToString() : n >= UnlimitedForcedItems ? "MaxForcedItemsUnlimited".Translate().ToString() : $"{n}";

	static void DrawPositioningModeOptions(Listing_Standard list)
	{
		switch (Achtung.Settings.forceCommandMenuMode)
		{
			case CommandMenuMode.PressForMenu:
			case CommandMenuMode.PressForPosition:
				DrawSettingGap(list);
				DrawValueSetting(list, "ForceCommandMenuKey", false, ref Achtung.Settings.forceCommandMenuKey);
				break;
			case CommandMenuMode.Delayed:
				DrawSettingGap(list);
				DrawSliderSetting(list, "Delay", ref Achtung.Settings.menuDelay, 0, 2000, n => $"{n} ms");
				break;
		}
	}

	static void DrawCheckboxSetting(Listing_Standard list, string name, ref bool value, Action<bool> onChange)
		=> list.CheckboxEnhanced(name, ref value, null, onChange);

	static void DrawValueSetting<T>(Listing_Standard list, string name, bool useValueForExplain, ref T value)
		=> list.ValueLabeled(name, useValueForExplain, ref value);

	static void DrawSliderSetting(Listing_Standard list, string name, ref int value, int min, int max, Func<int, string> converter)
		=> list.SliderLabeled(name, ref value, min, max, converter);

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
