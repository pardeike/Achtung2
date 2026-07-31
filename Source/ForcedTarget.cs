using RimWorld;
using System;
using Verse;

namespace AchtungMod;

public class ForcedTarget : IExposable, IEquatable<ForcedTarget>
{
	public LocalTargetInfo item = LocalTargetInfo.Invalid;
	public XY XY => item.Cell;
	public int materialScore = 0;

	public ForcedTarget()
	{
		item = LocalTargetInfo.Invalid;
		materialScore = 0;
	}

	public ForcedTarget(LocalTargetInfo item, int materialScore)
	{
		this.item = item;
		this.materialScore = materialScore;
	}

	public void ExposeData()
	{
		// Thing targets are temporarily invalid until their saved load ID is resolved.
		// Cell targets are already valid and have no Thing cross-reference to consume.
		if (Scribe.mode != LoadSaveMode.ResolvingCrossRefs || item.IsValid == false)
			Scribe_TargetInfo.Look(ref item, "item");
		Scribe_Values.Look(ref materialScore, "materialScore", 0, true);
	}

	public bool Equals(ForcedTarget other) => item == other.item;
	public override int GetHashCode() => item.GetHashCode();

	public bool IsValidTarget() => item.HasThing == false || item.ThingDestroyed == false;

	public bool IsBuilding()
	{
		if (item.HasThing == false)
			return false;
		var thing = item.thingInt;
		if (thing is Frame frame)
			return frame.def.entityDefToBuild == ThingDefOf.Wall;
		if (thing is Blueprint_Build blueprint)
			return blueprint.def.entityDefToBuild == ThingDefOf.Wall;
		return false;
	}

	public override string ToString()
	{
		if (item.HasThing)
			return $"{item.thingInt.def.defName}@{item.Cell.x}x{item.Cell.z}({materialScore})";
		return $"{item.Cell.x}x{item.Cell.z}({materialScore})";
	}
}
