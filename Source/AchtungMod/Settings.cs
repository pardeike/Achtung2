using System.IO;
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
		public bool reverseMenuKey = false;
		public ModKey relativeMovementKey = ModKey.Shift;
		public BreakLevel breakLevel = BreakLevel.AlmostExtreme;
		public HealthLevel healthLevel = HealthLevel.InPainShock;
		public float aggressiveness = 0.5f;
		public bool debugPositions = false;

		public void ExposeData()
		{
			Scribe_Values.Look(ref modActive, "Active", true, true);
			Scribe_Values.Look(ref forceDraftKey, "ForceDraft", ModKey.Alt, true);
			Scribe_Values.Look(ref ignoreMenuKey, "IgnoreMenu", ModKey.Alt, true);
			Scribe_Values.Look(ref reverseMenuKey, "ReverseMenu", false, true);
			Scribe_Values.Look(ref relativeMovementKey, "RelativeMovement", ModKey.Shift, true);
			Scribe_Values.Look(ref breakLevel, "BreakLevel", BreakLevel.AlmostExtreme, true);
			Scribe_Values.Look(ref healthLevel, "HealthLevel", HealthLevel.InPainShock, true);
			Scribe_Values.Look(ref aggressiveness, "Aggressiveness", 0.5f, true);
			Scribe_Values.Look(ref debugPositions, "DebugPositions", false, true);
		}

		//

		public static Settings instance = new Settings();
		public static string settingsPath = Path.Combine(Path.GetDirectoryName(GenFilePaths.ModsConfigFilePath), "AchtungModSettings.xml");

		public static void Load()
		{
			if (File.Exists(settingsPath))
			{
				Scribe.loader.InitLoading(settingsPath);
				if (Scribe.mode == LoadSaveMode.LoadingVars)
				{
					Scribe_Deep.Look(ref Settings.instance, "Settings", new object[0]);
				}
				Scribe.loader.FinalizeLoading();
				Scribe.mode = LoadSaveMode.Inactive;
			}
		}

		public static void Save()
		{
			Scribe.saver.InitSaving(settingsPath, "AchtungConfiguration");
			if (Scribe.mode == LoadSaveMode.Saving)
			{
				Scribe_Deep.Look(ref Settings.instance, "Settings", new object[0]);
			}
			Scribe.saver.FinalizeSaving();
			Scribe.mode = LoadSaveMode.Inactive;
		}
	}
}