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
		if (Scribe.mode == LoadSaveMode.LoadingVars)
		{
			// Vanilla's LocalTargetInfo loader registers a null Thing wanted ID
			// for cell coordinates, then has no resolved object with which to
			// consume it. Parse that existing on-disk representation directly.
			var node = Scribe.loader.curXmlParent["item"];
			var value = node?.InnerText;
			if (value?.Length > 0 && value[0] == '(')
				item = new LocalTargetInfo(IntVec3.FromString(value));
			else if (node == null)
				item = LocalTargetInfo.Invalid;
			else
				Scribe_TargetInfo.Look(ref item, "item");
		}
		else if (Scribe.mode != LoadSaveMode.ResolvingCrossRefs || item.IsValid == false)
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
