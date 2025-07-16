using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace AchtungMod;

[StaticConstructorOnStartup]
public static class RadialSlice
{
	static readonly IntVec3[] _shallowSliceOffsets;
	static readonly IntVec3[] _steepSliceOffsets;
	static readonly float[] _shallowRadiiSq;
	static readonly float[] _steepRadiiSq;
	const int MaxRadius = 50;

	static RadialSlice()
	{
		var shallowCells = new List<Tuple<IntVec3, float>>();
		var steepCells = new List<Tuple<IntVec3, float>>();
		var tan225 = Mathf.Tan(22.5f * Mathf.Deg2Rad);

		for (var x = 0; x <= MaxRadius; x++)
			for (var z = 0; z <= MaxRadius; z++)
				if (x >= z)
				{
					float radiusSq = x * x + z * z;
					if (radiusSq <= MaxRadius * MaxRadius)
					{
						var tuple = new Tuple<IntVec3, float>(new IntVec3(x, 0, z), radiusSq);
						if (z <= x * tan225) { shallowCells.Add(tuple); }
						else { steepCells.Add(tuple); }
					}
				}

		shallowCells.Sort((a, b) => a.Item2.CompareTo(b.Item2));
		steepCells.Sort((a, b) => a.Item2.CompareTo(b.Item2));

		_shallowSliceOffsets = new IntVec3[shallowCells.Count];
		_shallowRadiiSq = new float[shallowCells.Count];
		for (var i = 0; i < shallowCells.Count; i++) { _shallowSliceOffsets[i] = shallowCells[i].Item1; _shallowRadiiSq[i] = shallowCells[i].Item2; }

		_steepSliceOffsets = new IntVec3[steepCells.Count];
		_steepRadiiSq = new float[steepCells.Count];
		for (var i = 0; i < steepCells.Count; i++) { _steepSliceOffsets[i] = steepCells[i].Item1; _steepRadiiSq[i] = steepCells[i].Item2; }
	}

	static float AngleToFlat(Vector3 a, Vector3 b) => AngleTo(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
	static float AngleTo(Vector2 a, Vector2 b) => Mathf.Atan2(b.y - a.y, b.x - a.x) * 57.29578f;

	public static IEnumerable<IntVec3> RadialCellsInSlice(
	Pawn pawn, Pawn enemy, float radius, bool widerAngle = false, bool useCenter = false,
	Func<IntVec3, bool> canBeSeenOver = null)
	{
		var angle = AngleToFlat(enemy.DrawPos, pawn.DrawPos);
		var normalizedAngle = (angle % 360f + 360f) % 360f;

		if (widerAngle)
		{
			var anchorDir = (int)(normalizedAngle / 22.5f);
			var anchorCenterAngle = (anchorDir * 22.5f) + 11.25f;
			var neighborDir = (normalizedAngle < anchorCenterAngle) ? anchorDir - 1 : anchorDir + 1;
			neighborDir = (neighborDir + 16) % 16;
			return EnumerateComposedSlice(enemy.Position, radius, anchorDir, neighborDir, useCenter, canBeSeenOver);
		}
		else
		{
			var direction = (int)(normalizedAngle / 22.5f);
			return EnumerateSingleSlice(enemy.Position, radius, direction, useCenter, canBeSeenOver);
		}
	}

	static IEnumerable<IntVec3> EnumerateSingleSlice(
		IntVec3 center, float radius, int direction, bool useCenter,
		Func<IntVec3, bool> canBeSeenOver)
	{
		var radiusSq = radius * radius;
		var startIndex = useCenter ? 0 : 1;
		var transformType = direction / 2;

		IntVec3[] sliceToUse;
		float[] radiiToUse;
		var dirMod4 = direction % 4;
		if (dirMod4 == 0 || dirMod4 == 3)
		{
			sliceToUse = _shallowSliceOffsets;
			radiiToUse = _shallowRadiiSq;
		}
		else
		{
			sliceToUse = _steepSliceOffsets;
			radiiToUse = _steepRadiiSq;
		}

		for (var i = startIndex; i < sliceToUse.Length; i++)
		{
			if (radiiToUse[i] > radiusSq)
				break;

			var cell = center + Transform(sliceToUse[i], transformType);

			if (canBeSeenOver != null && !canBeSeenOver(cell))
				yield break;

			yield return cell;
		}
	}

	static IEnumerable<IntVec3> EnumerateComposedSlice(
		IntVec3 center, float radius, int dir1, int dir2, bool useCenter,
		Func<IntVec3, bool> canBeSeenOver)
	{
		if (useCenter)
			yield return center;

		var enum1 = EnumerateSingleSlice(center, radius, dir1, false, canBeSeenOver).GetEnumerator();
		var enum2 = EnumerateSingleSlice(center, radius, dir2, false, canBeSeenOver).GetEnumerator();

		var has1 = enum1.MoveNext();
		var has2 = enum2.MoveNext();

		while (has1 || has2)
		{
			if (has1 && has2 && enum1.Current == enum2.Current)
			{
				yield return enum1.Current;
				has1 = enum1.MoveNext();
				has2 = enum2.MoveNext();
				continue;
			}

			var enum1IsNext = false;
			if (has1 && has2)
			{
				float distSq1 = (enum1.Current - center).LengthHorizontalSquared;
				float distSq2 = (enum2.Current - center).LengthHorizontalSquared;
				enum1IsNext = distSq1 <= distSq2;
			}
			else if (has1)
			{
				enum1IsNext = true;
			}

			if (enum1IsNext)
			{
				yield return enum1.Current;
				has1 = enum1.MoveNext();
			}
			else if (has2)
			{
				yield return enum2.Current;
				has2 = enum2.MoveNext();
			}
		}
	}

	static IntVec3 Transform(IntVec3 offset, int transformType)
	{
		return transformType switch
		{
			0 => new IntVec3(offset.x, 0, offset.z),
			1 => new IntVec3(offset.z, 0, offset.x),
			2 => new IntVec3(-offset.z, 0, offset.x),
			3 => new IntVec3(-offset.x, 0, offset.z),
			4 => new IntVec3(-offset.x, 0, -offset.z),
			5 => new IntVec3(-offset.z, 0, -offset.x),
			6 => new IntVec3(offset.z, 0, -offset.x),
			7 => new IntVec3(offset.x, 0, -offset.z),
			_ => offset,
		};
	}
}