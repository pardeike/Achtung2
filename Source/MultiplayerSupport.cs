using Multiplayer.API;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using Verse.AI;

namespace AchtungMod;

public enum ThoroughWorkType
{
	CleanRoom,
	FightFire
}

public sealed class AchtungSimulationSettingsSnapshot : IEquatable<AchtungSimulationSettingsSnapshot>
{
	public readonly bool RescueEnabled;
	public readonly BreakLevel BreakLevel;
	public readonly HealthLevel HealthLevel;
	public readonly bool IgnoreForbidden;
	public readonly bool IgnoreRestrictions;
	public readonly bool IgnoreAssignments;
	public readonly bool BuildingSmart;
	public readonly int MaxForcedItems;
	public readonly bool ForcedEndedLetter;

	public AchtungSimulationSettingsSnapshot(
		bool rescueEnabled,
		BreakLevel breakLevel,
		HealthLevel healthLevel,
		bool ignoreForbidden,
		bool ignoreRestrictions,
		bool ignoreAssignments,
		bool buildingSmart,
		int maxForcedItems,
		bool forcedEndedLetter)
	{
		RescueEnabled = rescueEnabled;
		BreakLevel = breakLevel;
		HealthLevel = healthLevel;
		IgnoreForbidden = ignoreForbidden;
		IgnoreRestrictions = ignoreRestrictions;
		IgnoreAssignments = ignoreAssignments;
		BuildingSmart = buildingSmart;
		MaxForcedItems = Math.Clamp(maxForcedItems, 0, AchtungSettings.UnlimitedForcedItems);
		ForcedEndedLetter = forcedEndedLetter;
	}

	public bool Equals(AchtungSimulationSettingsSnapshot other)
		=> other != null
			&& RescueEnabled == other.RescueEnabled
			&& BreakLevel == other.BreakLevel
			&& HealthLevel == other.HealthLevel
			&& IgnoreForbidden == other.IgnoreForbidden
			&& IgnoreRestrictions == other.IgnoreRestrictions
			&& IgnoreAssignments == other.IgnoreAssignments
			&& BuildingSmart == other.BuildingSmart
			&& MaxForcedItems == other.MaxForcedItems
			&& ForcedEndedLetter == other.ForcedEndedLetter;

	public override bool Equals(object obj)
		=> obj is AchtungSimulationSettingsSnapshot other && Equals(other);

	public override int GetHashCode()
	{
		unchecked
		{
			var hash = 17;
			hash = hash * 31 + RescueEnabled.GetHashCode();
			hash = hash * 31 + BreakLevel.GetHashCode();
			hash = hash * 31 + HealthLevel.GetHashCode();
			hash = hash * 31 + IgnoreForbidden.GetHashCode();
			hash = hash * 31 + IgnoreRestrictions.GetHashCode();
			hash = hash * 31 + IgnoreAssignments.GetHashCode();
			hash = hash * 31 + BuildingSmart.GetHashCode();
			hash = hash * 31 + MaxForcedItems;
			hash = hash * 31 + ForcedEndedLetter.GetHashCode();
			return hash;
		}
	}

	public static AchtungSimulationSettingsSnapshot Capture()
	{
		var settings = Achtung.Settings;
		return new AchtungSimulationSettingsSnapshot(
			settings.rescueEnabled,
			settings.breakLevel,
			settings.healthLevel,
			settings.ignoreForbidden,
			settings.ignoreRestrictions,
			settings.ignoreAssignments,
			settings.buildingSmart,
			settings.maxForcedItems,
			settings.forcedEndedLetter);
	}

	public void Apply()
	{
		var settings = Achtung.Settings;
		settings.rescueEnabled = RescueEnabled;
		settings.breakLevel = BreakLevel;
		settings.healthLevel = HealthLevel;
		settings.ignoreForbidden = IgnoreForbidden;
		settings.ignoreRestrictions = IgnoreRestrictions;
		settings.ignoreAssignments = IgnoreAssignments;
		settings.buildingSmart = BuildingSmart;
		settings.maxForcedItems = MaxForcedItems;
		settings.forcedEndedLetter = ForcedEndedLetter;
		AchtungSettings.ApplyRuntimeEffects();
	}
}

public static class MultiplayerSupport
{
	sealed class DesyncTraceScope : IDisposable
	{
		static readonly FieldInfo ignoreTracesField = AppDomain.CurrentDomain.GetAssemblies()
			.Select(assembly => assembly.GetType("Multiplayer.Client.Desyncs.DeferredStackTracing", false))
			.FirstOrDefault(type => type != null)?
			.GetField("ignoreTraces", BindingFlags.Public | BindingFlags.Static);
		bool active;

		public DesyncTraceScope()
		{
			if (ignoreTracesField == null)
				return;
			ignoreTracesField.SetValue(null, (int)ignoreTracesField.GetValue(null) + 1);
			active = true;
		}

		public void Dispose()
		{
			if (active == false)
				return;
			var current = (int)ignoreTracesField.GetValue(null);
			ignoreTracesField.SetValue(null, Math.Max(0, current - 1));
			active = false;
		}
	}

	public static bool IsActive => MP.enabled && MP.IsInMultiplayer;
	public static bool ShouldShowCommandFeedback => IsActive == false || MP.IsExecutingSyncCommandIssuedBySelf;

	public static void Install()
	{
		if (MP.enabled == false)
			return;

		MP.RegisterAll(typeof(MultiplayerSupport).Assembly);
		Log.Message("Achtung: multiplayer compatibility enabled");
	}

	public static void ApplySharedSettings()
	{
		if (IsActive == false)
			return;

		var state = AchtungMultiplayerState.Instance;
		if (state == null)
			return;
		if (state.Initialized == false)
		{
			if (MP.IsHosting == false)
				return;
			state.Store(AchtungSimulationSettingsSnapshot.Capture());
		}

		var shared = state.Snapshot();
		if (shared.Equals(AchtungSimulationSettingsSnapshot.Capture()) == false)
			shared.Apply();
	}

	public static void SynchronizeSettings(AchtungSimulationSettingsSnapshot before)
	{
		var after = AchtungSimulationSettingsSnapshot.Capture();
		if (IsActive == false)
		{
			AchtungMultiplayerState.Instance?.Store(after);
			return;
		}

		if (before.Equals(after))
			return;

		before.Apply();
		ApplySettings(
			after.RescueEnabled,
			after.BreakLevel,
			after.HealthLevel,
			after.IgnoreForbidden,
			after.IgnoreRestrictions,
			after.IgnoreAssignments,
			after.BuildingSmart,
			after.MaxForcedItems,
			after.ForcedEndedLetter);
	}

	[SyncMethod]
	static void ApplySettings(
		bool rescueEnabled,
		BreakLevel breakLevel,
		HealthLevel healthLevel,
		bool ignoreForbidden,
		bool ignoreRestrictions,
		bool ignoreAssignments,
		bool buildingSmart,
		int maxForcedItems,
		bool forcedEndedLetter)
	{
		var snapshot = new AchtungSimulationSettingsSnapshot(
			rescueEnabled,
			breakLevel,
			healthLevel,
			ignoreForbidden,
			ignoreRestrictions,
			ignoreAssignments,
			buildingSmart,
			maxForcedItems,
			forcedEndedLetter);
		AchtungMultiplayerState.Instance?.Store(snapshot);
		snapshot.Apply();
	}

	[SyncMethod]
	public static void SetDraftStatus(Pawn pawn, bool drafted)
	{
		if (pawn != null)
			_ = Tools.SetDraftStatusLocal(pawn, drafted);
	}

	[SyncMethod]
	public static void CancelDrafting(List<Pawn> pawns, List<bool> originalDraftStatuses)
	{
		if (pawns == null || originalDraftStatuses == null)
			return;

		var count = Math.Min(pawns.Count, originalDraftStatuses.Count);
		for (var i = 0; i < count; i++)
		{
			var pawn = pawns[i];
			if (pawn == null)
				continue;
			_ = Tools.SetDraftStatusLocal(pawn, originalDraftStatuses[i]);
			pawn.mindState?.priorityWork?.Clear();
			if (pawn.jobs?.curJob != null && pawn.jobs.IsCurrentJobPlayerInterruptible())
				pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
		}
	}

	[SyncMethod]
	public static void OrderTo(Pawn pawn, int x, int z)
	{
		if (pawn?.Map == null)
			return;
		using var traceScope = new DesyncTraceScope();
		Tools.OrderToLocal(pawn, x, z);
	}

	[SyncMethod]
	public static void StartThoroughWork(List<Pawn> pawns, IntVec3 clickedCell, ThoroughWorkType workType)
	{
		if (pawns == null || clickedCell.IsValid == false)
			return;
		using var traceScope = new DesyncTraceScope();

		foreach (var pawn in pawns.Where(pawn => pawn?.Map != null).Distinct().OrderBy(pawn => pawn.thingIDNumber))
		{
			JobDriver_Thoroughly driver = workType switch
			{
				ThoroughWorkType.CleanRoom => new JobDriver_CleanRoom(),
				ThoroughWorkType.FightFire => new JobDriver_FightFire(),
				_ => null
			};
			if (driver == null)
				return;

			LocalTargetInfo target = clickedCell;
			if (driver.CanStart(pawn, target)?.Any() == true)
				driver.StartJob(pawn, target, target);
		}
	}

	[SyncMethod]
	public static void ForceWork(List<Pawn> pawns, WorkGiverDef workgiverDef, IntVec3 clickedCell, int cellRadius)
	{
		if (pawns == null || workgiverDef?.Worker is not WorkGiver_Scanner workgiver || clickedCell.IsValid == false)
			return;
		using var traceScope = new DesyncTraceScope();

		var orderedPawns = pawns
			.Where(pawn => pawn?.Map != null)
			.Distinct()
			.OrderBy(pawn => pawn.thingIDNumber)
			.ToList();
		var initialExpansionCount = 1 + orderedPawns.Count * 2;
		var radius = Math.Clamp(cellRadius, 0, (int)GenRadial.MaxRadialPatternRadius - 1);
		foreach (var pawn in orderedPawns)
			_ = ForcedMultiFloatMenuOption.ApplyForceWork(pawn, workgiver, clickedCell, initialExpansionCount, radius);
	}

	[SyncMethod]
	public static void EnterPortal(List<Pawn> pawns, MapPortal portal)
	{
		if (pawns == null || portal?.Map == null)
			return;
		using var traceScope = new DesyncTraceScope();

		foreach (var pawn in pawns.Where(pawn => pawn?.Map == portal.Map).Distinct().OrderBy(pawn => pawn.thingIDNumber))
		{
			if (FloatMenuOptionProvider_EnterMapPortal.CanEnterPortal(pawn, portal).Accepted == false)
				continue;
			var job = JobMaker.MakeJob(JobDefOf.EnterPortal, portal);
			job.playerForced = true;
			_ = pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc, false);
		}
	}
}
