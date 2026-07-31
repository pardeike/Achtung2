using RimWorld.Planet;
using Verse;

namespace AchtungMod;

public sealed class AchtungMultiplayerState(World world) : WorldComponent(world)
{
	bool initialized;
	bool rescueEnabled = true;
	BreakLevel breakLevel = BreakLevel.AlmostExtreme;
	HealthLevel healthLevel = HealthLevel.InPainShock;
	bool ignoreForbidden;
	bool ignoreRestrictions;
	bool ignoreAssignments;
	bool buildingSmart = true;
	int maxForcedItems = 64;
	bool forcedEndedLetter = true;

	public static AchtungMultiplayerState Instance
		=> Find.World?.GetComponent<AchtungMultiplayerState>();

	public bool Initialized => initialized;

	public AchtungSimulationSettingsSnapshot Snapshot()
		=> new(
			rescueEnabled,
			breakLevel,
			healthLevel,
			ignoreForbidden,
			ignoreRestrictions,
			ignoreAssignments,
			buildingSmart,
			maxForcedItems,
			forcedEndedLetter);

	public void Store(AchtungSimulationSettingsSnapshot snapshot)
	{
		initialized = true;
		rescueEnabled = snapshot.RescueEnabled;
		breakLevel = snapshot.BreakLevel;
		healthLevel = snapshot.HealthLevel;
		ignoreForbidden = snapshot.IgnoreForbidden;
		ignoreRestrictions = snapshot.IgnoreRestrictions;
		ignoreAssignments = snapshot.IgnoreAssignments;
		buildingSmart = snapshot.BuildingSmart;
		maxForcedItems = snapshot.MaxForcedItems;
		forcedEndedLetter = snapshot.ForcedEndedLetter;
	}

	public override void ExposeData()
	{
		if (Scribe.mode == LoadSaveMode.Saving && (initialized == false || MultiplayerSupport.IsActive == false))
			Store(AchtungSimulationSettingsSnapshot.Capture());

		Scribe_Values.Look(ref initialized, "initialized", false, true);
		Scribe_Values.Look(ref rescueEnabled, "rescueEnabled", true, true);
		Scribe_Values.Look(ref breakLevel, "breakLevel", BreakLevel.AlmostExtreme, true);
		Scribe_Values.Look(ref healthLevel, "healthLevel", HealthLevel.InPainShock, true);
		Scribe_Values.Look(ref ignoreForbidden, "ignoreForbidden", false, true);
		Scribe_Values.Look(ref ignoreRestrictions, "ignoreRestrictions", false, true);
		Scribe_Values.Look(ref ignoreAssignments, "ignoreAssignments", false, true);
		Scribe_Values.Look(ref buildingSmart, "buildingSmart", true, true);
		Scribe_Values.Look(ref maxForcedItems, "maxForcedItems", 64, true);
		Scribe_Values.Look(ref forcedEndedLetter, "forcedEndedLetter", true, true);
	}
}
