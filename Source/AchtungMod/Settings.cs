using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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

	public class Settings : IExposable
	{
		public bool modActive = true;
		public ModKey forceDraftKey = ModKey.Alt;
		public ModKey ignoreMenuKey = ModKey.Alt;
		public ModKey relativeMovementKey = ModKey.Shift;
		public BreakLevel breakLevel = BreakLevel.AlmostExtreme;
		public HealthLevel healthLevel = HealthLevel.InPainShock;
		public float aggressiveness = 0.5f;
		public bool debugPositions = false;

		public void ExposeData()
		{
			Scribe_Values.LookValue<bool>(ref modActive, "Active", true, true);
			Scribe_Values.LookValue<ModKey>(ref forceDraftKey, "ForceDraft", ModKey.Alt, true);
			Scribe_Values.LookValue<ModKey>(ref ignoreMenuKey, "IgnoreMenu", ModKey.Alt, true);
			Scribe_Values.LookValue<ModKey>(ref relativeMovementKey, "RelativeMovement", ModKey.Shift, true);
			Scribe_Values.LookValue<BreakLevel>(ref breakLevel, "BreakLevel", BreakLevel.AlmostExtreme, true);
			Scribe_Values.LookValue<HealthLevel>(ref healthLevel, "HealthLevel", HealthLevel.InPainShock, true);
			Scribe_Values.LookValue<float>(ref aggressiveness, "Aggressiveness", 0.5f, true);
			Scribe_Values.LookValue<bool>(ref debugPositions, "DebugPositions", false, true);
		}

		//

		public static Settings instance = new Settings();
		public static string settingsPath = Path.Combine(Path.GetDirectoryName(GenFilePaths.ModsConfigFilePath), "AchtungModSettings.xml");

		public static void Load()
		{
			if (File.Exists(settingsPath))
			{
				Scribe.InitLoading(settingsPath);
				if (Scribe.mode == LoadSaveMode.LoadingVars)
				{
					Scribe_Deep.LookDeep<Settings>(ref Settings.instance, "Settings", new object[0]);
				}
				Scribe.FinalizeLoading();
				Scribe.mode = LoadSaveMode.Inactive;
			}
		}

		public static void Save()
		{
			Scribe.InitWriting(settingsPath, "AchtungConfiguration");
			if (Scribe.mode == LoadSaveMode.Saving)
			{
				Scribe_Deep.LookDeep<Settings>(ref Settings.instance, "Settings", new object[0]);
			}
			Scribe.FinalizeWriting();
			Scribe.mode = LoadSaveMode.Inactive;
		}
	}
}