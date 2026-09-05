using System;
using System.Collections.Generic;
using System.Linq;

namespace AchtungMod;

internal static class ConstructionOrder
{
	// The caller supplies stable neighbour order. Iteration avoids recursion limits
	// for large forced selections, while postorder leaves each branch's entry last.
	internal static List<T> PostOrder<T>(T entry, Func<T, IEnumerable<T>> neighbours)
	{
		var result = new List<T>();
		var visited = new HashSet<T>();
		var pending = new Stack<(T item, bool finish)>();
		pending.Push((entry, false));
		while (pending.Count > 0)
		{
			var (item, finish) = pending.Pop();
			if (finish)
			{
				result.Add(item);
				continue;
			}
			if (!visited.Add(item))
				continue;
			pending.Push((item, true));
			foreach (var neighbour in neighbours(item).Reverse())
				if (!visited.Contains(neighbour))
					pending.Push((neighbour, false));
		}
		return result;
	}
}
