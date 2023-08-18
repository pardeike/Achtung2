﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Verse;

namespace AchtungMod
{
	public readonly struct XY : IEquatable<XY>
	{
		public readonly short x;
		public readonly short y;
		public XY(int x, int y) { this.x = (short)x; this.y = (short)y; }
		public XY(short x, short y) { this.x = x; this.y = y; }
		public static XY Zero => new XY(0, 0);
		public static XY Invalid => new XY(-500, -500);
		public readonly bool IsValid => x != -500;
		public readonly bool IsInvalid => x == -500;
		public static XY[] Adjacent => new XY[8] {
			new XY(1, 0), new XY(-1, 0), new XY(0, 1), new XY(0, -1),
			new XY(1, 1), new XY(-1, -1), new XY(-1, 1), new XY(1, -1)
		};
		public readonly int MagnitudeManhattan => x < 0 ? -x : x + (y < 0 ? -y : y);

		public static implicit operator XY(IntVec3 v3) => new XY((short)v3.x, (short)v3.z);
		public static implicit operator IntVec3(XY xy) => new IntVec3(xy.x, 0, xy.y);
		public static implicit operator XY(LocalTargetInfo info) { var cell = info.cellInt; return new XY((short)cell.x, (short)cell.z); }
		public static implicit operator LocalTargetInfo(XY xy) => new LocalTargetInfo() { thingInt = null, cellInt = xy };

		public static XY operator +(XY a, XY b) => new XY((short)(a.x + b.x), (short)(a.y + b.y));
		public static XY operator -(XY a, XY b) => new XY((short)(a.x - b.x), (short)(a.y - b.y));
		public static bool operator ==(XY a, XY b) => a.x == b.x && a.y == b.y;
		public static bool operator ==(IntVec3 v3, XY b) => v3.x == b.x && v3.z == b.y;
		public static bool operator ==(XY a, IntVec3 v3) => a.x == v3.x && a.y == v3.z;
		public static bool operator !=(XY a, XY b) => a.x != b.x || a.y != b.y;
		public static bool operator !=(IntVec3 v3, XY b) => v3.x != b.x || v3.z != b.y;
		public static bool operator !=(XY a, IntVec3 v3) => a.x != v3.x || a.y != v3.z;
		public override readonly bool Equals(object obj) => obj is XY other && x == other.x && y == other.y;
		public readonly bool Equals(XY other) => x == other.x && y == other.y;
		public override readonly int GetHashCode() => x + (y << 10);
		public override readonly string ToString() => $"({x},{y})";

		public static XY FromString(string str)
		{
			var array = str.TrimStart('(').TrimEnd(')').Split(',');
			try
			{
				var invariantCulture = CultureInfo.InvariantCulture;
				return new XY(Convert.ToInt16(array[0], invariantCulture), Convert.ToInt16(array[1], invariantCulture));
			}
			catch (Exception ex)
			{
				Log.Warning(str + " is not a valid XY format. Exception: " + ex);
				return Invalid;
			}
		}
	}

	[StaticConstructorOnStartup]
	public static class XYExtensions
	{
		static XYExtensions()
		{
			ParseHelper.Parsers<XY>.Register(new Func<string, XY>(str => XY.FromString(str)));
		}

		public static IEnumerable<XY> ToXY(this IEnumerable<IntVec3> enumeration) => enumeration.Select(v3 => new XY((short)v3.x, (short)v3.z));
		public static bool InBounds(this XY xy, Map map)
		{
			if (xy.x < 0 || xy.y < 0)
				return false;
			var size = map.Size;
			return xy.x < size.x && xy.y < size.z;
		}
	}
}