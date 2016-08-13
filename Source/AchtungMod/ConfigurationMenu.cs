using System.Diagnostics;
using System.Reflection;
using CommunityCoreLibrary;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace AchtungMod
{
	[StaticConstructorOnStartup]
	public static class Settings
	{
		public static bool modActive = true;
		public static bool altKeyInverted = false;
	}

	public class ConfigurationMenu : ModConfigurationMenu
	{
		public override float DoWindowContents(UnityEngine.Rect rect)
		{
			var listing = new Listing_Standard(rect);
			{
				listing.ColumnWidth = rect.width - 4f;
				listing.Indent(4);

				// mod active
				Text.Font = GameFont.Small;
				GUI.color = Color.white;
				listing.CheckboxLabeled("AchtungEnabled".Translate(), ref Settings.modActive);
				Text.Font = GameFont.Tiny;
				listing.ColumnWidth -= 34;
				GUI.color = Color.gray;
				listing.Label("AchtungEnabledExplained".Translate());
				listing.ColumnWidth += 34;
				listing.Gap();

				// alt key settings
				Text.Font = GameFont.Small;
				GUI.color = Color.white;
				listing.CheckboxLabeled("AltKeyInverted".Translate(), ref Settings.altKeyInverted);
				Text.Font = GameFont.Tiny;
				listing.ColumnWidth -= 34;
				GUI.color = Color.gray;
				if (Settings.altKeyInverted)
				{
					listing.Label("AltKeyInvertedExplained1".Translate());
				}
				else
				{
					listing.Label("AltKeyInvertedExplained2".Translate());
				}
				listing.ColumnWidth += 34;
				listing.Gap();

				// version
				Text.Font = GameFont.Small;
				GUI.color = Color.white;
				Tools.ValueLabeled(listing, "ModVersion".Translate(), BootInjector.Version);
				//listing.Gap();

				Text.Font = GameFont.Tiny;
				GUI.color = Color.white;
				listing.Label("(c) 2016 Andreas Pardeike");
				listing.Gap();
			}
			listing.End();
			return listing.CurHeight;
		}

		public override void ExposeData()
		{
			Scribe_Values.LookValue<bool>(ref Settings.modActive, "Active", true, true);
			Scribe_Values.LookValue<bool>(ref Settings.altKeyInverted, "AltKeyInverted", false, true);
		}
	}
}