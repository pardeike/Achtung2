using UnityEngine;
using Verse;

namespace AchtungMod
{
	public enum ModKey
	{
		None,
		Alt,
		Ctrl,
		Shift,
		Meta
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

	public class AchtungSettings : ModSettings
	{
		public bool forceDraft = false;
		public ModKey forceCommandMenuKey = ModKey.Ctrl;
		public ModKey relativeMovementKey = ModKey.Alt;
		public ModKey showWeaponRangesKey = ModKey.Ctrl;
		public BreakLevel breakLevel = BreakLevel.AlmostExtreme;
		public HealthLevel healthLevel = HealthLevel.InPainShock;

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref forceDraft, "forceDraft", false, true);
			Scribe_Values.Look(ref forceCommandMenuKey, "forceCommandMenuKey", ModKey.Ctrl, true);
			Scribe_Values.Look(ref relativeMovementKey, "relativeMovementKey", ModKey.Alt, true);
			Scribe_Values.Look(ref showWeaponRangesKey, "showWeaponRangesKey", ModKey.Ctrl, true);
			Scribe_Values.Look(ref breakLevel, "BreakLevel", BreakLevel.AlmostExtreme, true);
			Scribe_Values.Look(ref healthLevel, "HealthLevel", HealthLevel.InPainShock, true);
		}

		public void DoWindowContents(Rect canvas)
		{
			var list = new Listing_Standard { ColumnWidth = canvas.width / 2 };
			list.Begin(canvas);
			list.Gap();

			list.CheckboxEnhanced("ForceDraft", ref Achtung.Settings.forceDraft);
			list.Gap();
			list.ValueLabeled("ForceCommandMenu", ref Achtung.Settings.forceCommandMenuKey);
			list.Gap();
			list.ValueLabeled("RelativeMovement", ref Achtung.Settings.relativeMovementKey);
			list.Gap();
			list.ValueLabeled("ShowWeaponRanges", ref Achtung.Settings.showWeaponRangesKey);
			list.Gap();
			list.ValueLabeled("BreakLevel", ref Achtung.Settings.breakLevel);
			list.Gap();
			list.ValueLabeled("HealthLevel", ref Achtung.Settings.healthLevel);

			list.End();
		}
	}
}