using UnityEngine;
using Verse;

namespace AchtungMod
{
	public enum AchtungModKey
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
		public bool positioningEnabled = true;
		public AchtungModKey achtungKey = AchtungModKey.Alt;
		public AchtungModKey forceCommandMenuKey = AchtungModKey.Ctrl;
		public BreakLevel breakLevel = BreakLevel.AlmostExtreme;
		public HealthLevel healthLevel = HealthLevel.InPainShock;

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref positioningEnabled, "positioningEnabled", true, true);
			Scribe_Values.Look(ref achtungKey, "achtungKey", AchtungModKey.Alt, true);
			Scribe_Values.Look(ref forceCommandMenuKey, "forceCommandMenuKey", AchtungModKey.Ctrl, true);
			Scribe_Values.Look(ref breakLevel, "BreakLevel", BreakLevel.AlmostExtreme, true);
			Scribe_Values.Look(ref healthLevel, "HealthLevel", HealthLevel.InPainShock, true);
		}

		public void DoWindowContents(Rect canvas)
		{
			var list = new Listing_Standard { ColumnWidth = canvas.width / 2 };
			list.Begin(canvas);
			list.Gap();

			list.CheckboxEnhanced("PositioningEnabled", ref Achtung.Settings.positioningEnabled);
			list.Gap();
			list.ValueLabeled("AchtungModifier", ref Achtung.Settings.achtungKey);
			list.Gap();
			list.ValueLabeled("ForceCommandMenu", ref Achtung.Settings.forceCommandMenuKey);
			list.Gap();
			list.ValueLabeled("BreakLevel", ref Achtung.Settings.breakLevel);
			list.Gap();
			list.ValueLabeled("HealthLevel", ref Achtung.Settings.healthLevel);

			list.Gap(18f);
			list.Note("Notes");
			list.Gap(6f);
			list.Note("Note1");
			list.Gap(6f);
			list.Note("Note2");

			list.End();
		}
	}
}