using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace AchtungMod;

[StaticConstructorOnStartup]
public static class Extensions
{
	static Extensions()
	{
		ParseHelper.Parsers<XY>.Register(new Func<string, XY>(str => XY.FromString(str)));
	}

	public static IEnumerable<XY> ToXY(this IEnumerable<IntVec3> enumeration) => enumeration.Select(v3 => new XY((short)v3.x, (short)v3.z));
	public static bool InBounds(this XY xy, Map map)
	{
		if (xy.x < 0 || xy.y < 0) return false;
		var size = map.Size;
		return xy.x < size.x && xy.y < size.z;
	}
}