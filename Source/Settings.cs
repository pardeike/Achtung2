using Brrainz;
using RimWorld;
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
	public bool positioningEnabled = true;
	public bool rescueEnabled = true;
	public AchtungModKey achtungKey = AchtungModKey.Alt;
	public CommandMenuMode forceCommandMenuMode = CommandMenuMode.Delayed;
	public AchtungModKey forceCommandMenuKey = AchtungModKey.Ctrl;
	public BreakLevel breakLevel = BreakLevel.AlmostExtreme;
	public HealthLevel healthLevel = HealthLevel.InPainShock;
	public bool ignoreForbidden = false;
	public bool ignoreRestrictions = false;
	public bool ignoreAssignments = false;
	public WorkMarkers workMarkers = WorkMarkers.Animated;
	public bool buildingSmartDefault = false;
	public int maxForcedItems = 64;
	public int menuDelay = 250;

	public static readonly int UnlimitedForcedItems = 2000;

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_Values.Look(ref positioningEnabled, "positioningEnabled", true, true);
		Scribe_Values.Look(ref rescueEnabled, "rescueEnabled", true, true);
		Scribe_Values.Look(ref achtungKey, "achtungKey", AchtungModKey.Alt, true);
		Scribe_Values.Look(ref forceCommandMenuMode, "forceCommandMenuMode", CommandMenuMode.Auto, true);
		Scribe_Values.Look(ref forceCommandMenuKey, "forceCommandMenuKey", AchtungModKey.Ctrl, true);
		Scribe_Values.Look(ref breakLevel, "BreakLevel", BreakLevel.AlmostExtreme, true);
		Scribe_Values.Look(ref healthLevel, "HealthLevel", HealthLevel.InPainShock, true);
		Scribe_Values.Look(ref ignoreForbidden, "ignoreForbidden", false, true);
		Scribe_Values.Look(ref ignoreRestrictions, "ignoreRestrictions", false, true);
		Scribe_Values.Look(ref ignoreAssignments, "ignoreAssignments", false, true);
		Scribe_Values.Look(ref workMarkers, "workMarkers", WorkMarkers.Animated, true);
		Scribe_Values.Look(ref buildingSmartDefault, "buildingSmartDefault", false, true);
		Scribe_Values.Look(ref maxForcedItems, "maxForcedItems", 64, true);
		Scribe_Values.Look(ref menuDelay, "menuDelay", 250, true);

		if (Scribe.mode == LoadSaveMode.PostLoadInit && Achtung.harmony != null)
			ForbidUtility_IsForbidden_Patch.FixPatch();
	}

	public static void DoWindowContents(Rect canvas)
	{
		var helpRect = canvas;
		helpRect.height = Text.LineHeight + 2;
		helpRect.x -= 17;
		helpRect.y -= 33 + 4;
		helpRect.xMin = helpRect.xMax - "Tutorial".Translate().GetWidthCached() - 42;
		if (Widgets.ButtonText(helpRect, "Tutorial".Translate()))
			ModFeatures.ShowAgain<Achtung>(true);

		var columnWidth = (canvas.width - 30) / 2 - 2;
		var list = new Listing_Standard { ColumnWidth = columnWidth };
		list.Begin(canvas);

		list.Gap(4);
		list.CheckboxEnhanced("PositioningEnabled", ref Achtung.Settings.positioningEnabled);
		if (Achtung.Settings.positioningEnabled)
		{
			list.Gap(10);
			list.ValueLabeled("AchtungModifier", false, ref Achtung.Settings.achtungKey);
			list.Gap(10);
			list.ValueLabeled("ForceCommandMenuMode", true, ref Achtung.Settings.forceCommandMenuMode);
			switch (Achtung.Settings.forceCommandMenuMode)
			{
				case CommandMenuMode.Auto:
					break;
				case CommandMenuMode.PressForMenu:
				case CommandMenuMode.PressForPosition:
					list.Gap(-2);
					list.ValueLabeled("ForceCommandMenuKey", false, ref Achtung.Settings.forceCommandMenuKey);
					break;
				case CommandMenuMode.Delayed:
					list.Gap(-2);
					list.SliderLabeled("Delay", ref Achtung.Settings.menuDelay, 0, 2000, n => $"{n} ms");
					break;
			}
		}
		list.Gap(18);
		list.CheckboxEnhanced("RescueEnabled", ref Achtung.Settings.rescueEnabled);
		var rescuing = DefDatabase<WorkTypeDef>.GetNamedSilentFail(Tools.RescuingWorkTypeDef.defName);
		var doctorRescueWorkGiver = DefDatabase<WorkGiverDef>.GetNamed("DoctorRescue");
		if (rescuing == null && Achtung.Settings.rescueEnabled)
			Tools.savedWorkTypeDef = DynamicWorkTypes.AddWorkTypeDef(Tools.RescuingWorkTypeDef, WorkTypeDefOf.Doctor, doctorRescueWorkGiver);
		else if (rescuing != null && Achtung.Settings.rescueEnabled == false)
			DynamicWorkTypes.RemoveWorkTypeDef(Tools.RescuingWorkTypeDef, Tools.savedWorkTypeDef, doctorRescueWorkGiver);

		list.NewColumn();
		list.curX += 30 - Listing.ColumnSpacing;

		list.Gap(4);
		list.ValueLabeled("BreakLevel", false, ref Achtung.Settings.breakLevel);
		list.Gap(10);
		list.ValueLabeled("HealthLevel", false, ref Achtung.Settings.healthLevel);
		list.Gap(10);
		list.CheckboxEnhanced("IgnoreForbidden", ref Achtung.Settings.ignoreForbidden, null, () => ForbidUtility_IsForbidden_Patch.FixPatch());
		list.CheckboxEnhanced("IgnoreRestrictions", ref Achtung.Settings.ignoreRestrictions);
		list.CheckboxEnhanced("IgnoreAssignments", ref Achtung.Settings.ignoreAssignments);
		list.Gap(10);
		list.CheckboxEnhanced("BuildingSmartDefault", ref Achtung.Settings.buildingSmartDefault);
		list.Gap(10);
		list.ValueLabeled("WorkMarkers", false, ref Achtung.Settings.workMarkers);
		list.Gap(10);
		static string forcedItemsString(int n) => n == 0 ? "Disabled".Translate().ToString() : n >= UnlimitedForcedItems ? "MaxForcedItemsUnlimited".Translate().ToString() : $"{n}";
		list.SliderLabeled("MaxForcedItems", ref Achtung.Settings.maxForcedItems, 0, UnlimitedForcedItems, forcedItemsString);

		list.End();

		list = new Listing_Standard { ColumnWidth = canvas.width };
		canvas.yMin = canvas.yMax - 80;
		list.Begin(canvas);
		list.Note("Notes", GameFont.Medium);
		list.Gap(4);
		list.Note("Note1");
		list.End();
	}
}