using LudeonTK;
using RimWorld;
using Unity.Collections;
using UnityEngine;
using Verse;
using Verse.AI;

namespace AchtungMod;

public class AchtungCastPositionFinder
{
	CastPositionRequest req;

	IntVec3 casterLoc;

	IntVec3 targetLoc;

	Verb verb;

	float rangeFromTarget;

	float rangeFromTargetSquared;

	float optimalRangeSquared;

	float rangeFromCasterToCellSquared;

	float rangeFromTargetToCellSquared;

	int inRadiusMark;

	NativeArray<byte>.ReadOnly avoidGrid;

	float maxRangeFromCasterSquared;

	float maxRangeFromTargetSquared;

	float maxRangeFromLocusSquared;

	IntVec3 bestSpot = IntVec3.Invalid;

	float bestSpotPref = 0.001f;

	NativeArray<byte> emptyByteArray = NativeArrayUtility.EmptyArray<byte>();

	~AchtungCastPositionFinder()
	{
		emptyByteArray.Dispose();
	}

	public bool TryFindCastPosition(CastPositionRequest newReq, out IntVec3 dest)
	{
		req = newReq;
		casterLoc = req.caster.Position;
		targetLoc = req.target.Position;
		verb = req.verb;
		AvoidGrid avoidGrid;
		this.avoidGrid = newReq.caster.TryGetAvoidGrid(out avoidGrid, false)
			? avoidGrid.Grid
			: emptyByteArray.AsReadOnly();
		if (verb == null)
		{
			var caster = req.caster;
			Log.Error((caster?.ToString()) + " tried to find casting position without a verb.");
			dest = IntVec3.Invalid;
			req = default;
			return false;
		}
		if (req.maxRegions > 0)
		{
			var region = casterLoc.GetRegion(req.caster.Map, RegionType.Set_Passable);
			if (region == null)
			{
				Log.Error("TryFindCastPosition requiring region traversal but root region is null.");
				dest = IntVec3.Invalid;
				req = default;
				verb = null;
				return false;
			}
			inRadiusMark = Rand.Int;
			RegionTraverser.MarkRegionsBFS(region, null, newReq.maxRegions, inRadiusMark, RegionType.Set_Passable);
			if (req.maxRangeFromLocus > 0.01f)
			{
				var locusReg = req.locus.GetRegion(req.caster.Map, RegionType.Set_Passable);
				if (locusReg == null)
				{
					var text = "locus ";
					var locus = req.locus;
					Log.Error(text + locus.ToString() + " has no region");
					dest = IntVec3.Invalid;
					req = default;
					verb = null;
					return false;
				}
				if (locusReg.mark != inRadiusMark)
				{
					inRadiusMark = Rand.Int;
					RegionTraverser.BreadthFirstTraverse(region, null, delegate (Region r)
					{
						r.mark = inRadiusMark;
						req.maxRegions++;
						return r == locusReg;
					}, 999999, RegionType.Set_Passable);
				}
			}
		}
		var cellRect = CellRect.WholeMap(req.caster.Map);
		if (req.maxRangeFromCaster > 0.01f)
		{
			var num = Mathf.CeilToInt(req.maxRangeFromCaster);
			var cellRect2 = new CellRect(casterLoc.x - num, casterLoc.z - num, num * 2 + 1, num * 2 + 1);
			_ = cellRect.ClipInsideRect(cellRect2);
		}
		var num2 = Mathf.CeilToInt(req.maxRangeFromTarget);
		var cellRect3 = new CellRect(targetLoc.x - num2, targetLoc.z - num2, num2 * 2 + 1, num2 * 2 + 1);
		_ = cellRect.ClipInsideRect(cellRect3);
		if (req.maxRangeFromLocus > 0.01f)
		{
			var num3 = Mathf.CeilToInt(req.maxRangeFromLocus);
			var cellRect4 = new CellRect(targetLoc.x - num3, targetLoc.z - num3, num3 * 2 + 1, num3 * 2 + 1);
			_ = cellRect.ClipInsideRect(cellRect4);
		}
		bestSpot = IntVec3.Invalid;
		bestSpotPref = 0.001f;
		maxRangeFromCasterSquared = req.maxRangeFromCaster * req.maxRangeFromCaster;
		maxRangeFromTargetSquared = req.maxRangeFromTarget * req.maxRangeFromTarget;
		maxRangeFromLocusSquared = req.maxRangeFromLocus * req.maxRangeFromLocus;
		rangeFromTarget = (req.caster.Position - req.target.Position).LengthHorizontal;
		rangeFromTargetSquared = (req.caster.Position - req.target.Position).LengthHorizontalSquared;
		optimalRangeSquared = verb.EffectiveRange * 0.8f * (verb.verbProps.range * 0.8f);
		if (req.preferredCastPosition != null && req.preferredCastPosition.Value.IsValid)
		{
			EvaluateCell(req.preferredCastPosition.Value);
			if (bestSpot.IsValid && bestSpotPref > 0.001f)
			{
				dest = req.preferredCastPosition.Value;
				req = default;
				verb = null;
				return true;
			}
		}
		EvaluateCell(req.caster.Position);
		if (bestSpotPref >= 1.0)
		{
			dest = req.caster.Position;
			req = default;
			verb = null;
			return true;
		}
		var num4 = -1f / CellLine.Between(req.target.Position, req.caster.Position).Slope;
		var cellLine = new CellLine(req.target.Position, num4);
		var flag = cellLine.CellIsAbove(req.caster.Position);
		foreach (var intVec in cellRect)
		{
			if (cellLine.CellIsAbove(intVec) == flag && cellRect.Contains(intVec))
			{
				EvaluateCell(intVec);
			}
		}
		if (bestSpot.IsValid && bestSpotPref > 0.33f)
		{
			dest = bestSpot;
			req = default;
			verb = null;
			return true;
		}
		foreach (var intVec2 in cellRect)
		{
			if (cellLine.CellIsAbove(intVec2) != flag && cellRect.Contains(intVec2))
			{
				EvaluateCell(intVec2);
			}
		}
		if (bestSpot.IsValid)
		{
			dest = bestSpot;
			req = default;
			verb = null;
			return true;
		}
		dest = casterLoc;
		req = default;
		verb = null;
		return false;
	}

	void EvaluateCell(IntVec3 c)
	{
		if (req.validator != null && !req.validator(c))
		{
			return;
		}
		if (maxRangeFromTargetSquared > 0.01f && maxRangeFromTargetSquared < 250000f && (c - req.target.Position).LengthHorizontalSquared > maxRangeFromTargetSquared)
		{
			if (DebugViewSettings.drawCastPositionSearch)
			{
				req.caster.Map.debugDrawer.FlashCell(c, 0f, "range target", 50);
			}
			return;
		}
		if (maxRangeFromLocusSquared > 0.01 && (c - req.locus).LengthHorizontalSquared > maxRangeFromLocusSquared)
		{
			if (DebugViewSettings.drawCastPositionSearch)
			{
				req.caster.Map.debugDrawer.FlashCell(c, 0.1f, "range home", 50);
			}
			return;
		}
		if (maxRangeFromCasterSquared > 0.01f)
		{
			rangeFromCasterToCellSquared = (c - req.caster.Position).LengthHorizontalSquared;
			if (rangeFromCasterToCellSquared > maxRangeFromCasterSquared)
			{
				if (DebugViewSettings.drawCastPositionSearch)
				{
					req.caster.Map.debugDrawer.FlashCell(c, 0.2f, "range caster", 50);
				}
				return;
			}
		}
		if (!c.WalkableBy(req.caster.Map, req.caster))
		{
			return;
		}
		if (req.caster.Position != c && !c.InAllowedArea(req.caster))
		{
			return;
		}
		if (req.maxRegions > 0 && c.GetRegion(req.caster.Map, RegionType.Set_Passable).mark != inRadiusMark)
		{
			if (DebugViewSettings.drawCastPositionSearch)
			{
				req.caster.Map.debugDrawer.FlashCell(c, 0.64f, "reg radius", 50);
			}
			return;
		}
		if (!req.caster.Map.reachability.CanReach(req.caster.Position, c, PathEndMode.OnCell, TraverseParms.For(req.caster, Danger.Some, TraverseMode.ByPawn, false, false, false, true)))
		{
			if (DebugViewSettings.drawCastPositionSearch)
			{
				req.caster.Map.debugDrawer.FlashCell(c, 0.4f, "can't reach", 50);
			}
			return;
		}
		var num = CastPositionPreference(c);
		if (avoidGrid.Length > 0)
		{
			var b = avoidGrid[req.caster.Map.cellIndices.CellToIndex(c)];
			num *= Mathf.Max(0.1f, (37.5f - b) / 37.5f);
		}
		if (DebugViewSettings.drawCastPositionSearch)
		{
			req.caster.Map.debugDrawer.FlashCell(c, num / 4f, num.ToString("F3"), 50);
		}
		if (num < bestSpotPref)
		{
			return;
		}
		if (!verb.CanHitTargetFrom(c, req.target))
		{
			if (DebugViewSettings.drawCastPositionSearch)
			{
				req.caster.Map.debugDrawer.FlashCell(c, 0.6f, "can't hit", 50);
			}
			return;
		}
		if (!req.caster.Map.pawnDestinationReservationManager.CanReserve(c, req.caster, false))
		{
			if (DebugViewSettings.drawCastPositionSearch)
			{
				req.caster.Map.debugDrawer.FlashCell(c, num * 0.9f, "resvd", 50);
			}
			return;
		}
		if (PawnUtility.KnownDangerAt(c, req.caster.Map, req.caster))
		{
			if (DebugViewSettings.drawCastPositionSearch)
			{
				req.caster.Map.debugDrawer.FlashCell(c, 0.9f, "danger", 50);
			}
			return;
		}
		bestSpot = c;
		bestSpotPref = num;
	}

	float CastPositionPreference(IntVec3 c)
	{
		var flag = true;
		var list = req.caster.Map.thingGrid.ThingsListAtFast(c);
		for (var i = 0; i < list.Count; i++)
		{
			var thing = list[i];
			var fire = thing as Fire;
			if (fire != null && fire.parent == null)
			{
				return -1f;
			}
			if (thing.def.passability == Traversability.PassThroughOnly)
			{
				flag = false;
			}
		}
		var num = 0.3f;
		if (req.caster.kindDef.aiAvoidCover)
		{
			num += 8f - CoverUtility.TotalSurroundingCoverScore(c, req.caster.Map);
		}
		if (req.wantCoverFromTarget)
		{
			num += CoverUtility.CalculateOverallBlockChance(c, req.target.Position, req.caster.Map) * 0.55f;
		}
		var num2 = (req.caster.Position - c).LengthHorizontal;
		if (rangeFromTarget > 100f)
		{
			num2 -= rangeFromTarget - 100f;
			if (num2 < 0f)
			{
				num2 = 0f;
			}
		}
		num *= Mathf.Pow(0.967f, num2);
		var num3 = 1f;
		rangeFromTargetToCellSquared = (c - req.target.Position).LengthHorizontalSquared;
		var num4 = Mathf.Abs(rangeFromTargetToCellSquared - optimalRangeSquared) / optimalRangeSquared;
		num4 = 1f - num4;
		num4 = 0.7f + 0.3f * num4;
		num3 *= num4;
		if (rangeFromTargetToCellSquared < 25f)
		{
			num3 *= 0.5f;
		}
		num *= num3;
		if (rangeFromCasterToCellSquared > rangeFromTargetSquared)
		{
			num *= 0.4f;
		}
		if (!flag)
		{
			num *= 0.4f;
		}
		return num;
	}
}