using System;
using System.Collections.Generic;
using Verse;

namespace AchtungMod;

public static class Visibility
{
	static readonly int[,] oct = new int[,]
	{
		{ 1, 0, 0, 1 },
		{ 0, 1, 1, 0 },
		{ 0,-1, 1, 0 },
		{-1, 0, 0, 1 },
		{-1, 0, 0,-1 },
		{ 0,-1,-1, 0 },
		{ 0, 1,-1, 0 },
		{ 1, 0, 0,-1 }
	};

	public static HashSet<IntVec3> GetShootingCells(Pawn colonist, Pawn enemy)
	{
		var map = colonist.Map;
		var radius = enemy.CurrentEffectiveVerb.EffectiveRange;
		var cells = new HashSet<IntVec3>();
		var positions = new List<IntVec3>();
		ShootLeanUtility.LeanShootingSourcesFromTo(enemy.Position, colonist.Position, map, positions);
		foreach (var enemyPos in positions)
			cells.AddRange(GetVisibleCellsAround(map, enemyPos, (int)(radius + 0.5f), cell => cell.CanBeSeenOver(map) == false, null));
		return cells;
	}

	public static HashSet<IntVec3> GetVisibleCellsAround(Map map, IntVec3 start, int radius, Func<IntVec3, bool> isBlocking, IntVec3? target = null)
	{
		var visible = new HashSet<IntVec3>();
		var radiusSq = radius * radius;

		void CastLight(int column, int startNum, int startDen, int endNum, int endDen, int xx, int xy, int yx, int yy)
		{
			if (startNum * endDen <= endNum * startDen || column > radius) return;
			var blocked = false;
			var savedNum = 0;
			var savedDen = 1;
			for (var dy = column; dy >= 0; dy--)
			{
				var dx = column;
				var mapX = start.x + dx * xx + dy * xy;
				var mapZ = start.z + dx * yx + dy * yy;
				var cell = new IntVec3(mapX, start.y, mapZ);
				var leftNum = dy * 2 + 1;
				var leftDen = dx * 2 - 1;
				var rightNum = dy * 2 - 1;
				var rightDen = dx * 2 + 1;
				if (rightNum * startDen >= startNum * rightDen) continue;
				if (leftNum * endDen <= endNum * leftDen) break;
				var dist2 = dx * dx + dy * dy;
				var inBounds = cell.InBounds(map);
				var cellBlocked = !inBounds || isBlocking(cell);
				if (!cellBlocked && dist2 <= radiusSq) visible.Add(cell);
				if (blocked)
				{
					if (cellBlocked)
					{
						if (rightNum * savedDen < savedNum * rightDen)
						{
							savedNum = rightNum;
							savedDen = rightDen;
						}
					}
					else
					{
						blocked = false;
						startNum = savedNum;
						startDen = savedDen;
					}
				}
				if (!blocked && cellBlocked)
				{
					blocked = true;
					savedNum = rightNum;
					savedDen = rightDen;
					CastLight(column + 1, startNum, startDen, leftNum, leftDen, xx, xy, yx, yy);
					startNum = savedNum;
					startDen = savedDen;
				}
			}
			if (!blocked) CastLight(column + 1, startNum, startDen, endNum, endDen, xx, xy, yx, yy);
		}

		var skipA = -1;
		var skipB = -1;
		if (target.HasValue && target.Value != start)
		{
			var dx = target.Value.x - start.x;
			var dz = target.Value.z - start.z;
			var angle = Math.Atan2(dz, dx);
			if (angle < 0) angle += Math.PI * 2;
			var dir = (int)Math.Floor((angle + Math.PI / 8) / (Math.PI / 4)) % 8;
			skipA = (dir + 3) & 7;
			skipB = (dir + 4) & 7;
		}

		for (var i = 0; i < 8; i++)
		{
			if (i == skipA || i == skipB) continue;
			CastLight(1, 1, 1, 0, 1, oct[i, 0], oct[i, 1], oct[i, 2], oct[i, 3]);
		}

		return visible;
	}
}