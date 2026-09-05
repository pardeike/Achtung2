using AchtungMod;

static void Equal<T>(IEnumerable<T> actual, IEnumerable<T> expected, string name)
{
	if (!actual.SequenceEqual(expected))
		throw new Exception($"{name}: [{string.Join(",", actual)}] != [{string.Join(",", expected)}]");
}

static List<int> Line(int entry, int length = 7) => ConstructionOrder.PostOrder(entry,
	cell => new[] { cell + 1, cell - 1 }.Where(next => next >= 0 && next < length));

Equal(Line(3), new[] { 6, 5, 4, 0, 1, 2, 3 }, "middle entry completes each branch without alternating ends");
Equal(Line(0), Enumerable.Range(0, 7).Reverse(), "west approach leaves west entry open");
Equal(Line(6), Enumerable.Range(0, 7), "east approach reverses placement direction");

List<int> ClickedLine(int clicked) => ConstructionOrder.PostOrder(3,
	cell => new[] { cell + 1, cell - 1 }.Where(next => next >= 0 && next < 7)
		.OrderBy(next => (next - clicked) * (next - clicked)).ThenBy(next => next));
Equal(ClickedLine(0), new[] { 0, 1, 2, 6, 5, 4, 3 }, "left click prioritizes left branch from same approach");
Equal(ClickedLine(6), new[] { 6, 5, 4, 0, 1, 2, 3 }, "right click prioritizes right branch from same approach");

var offsets = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
var cells = Enumerable.Range(0, 4).SelectMany(x => Enumerable.Range(0, 2).Select(z => (x, z))).ToArray();
List<(int x, int z)> Order(IEnumerable<(int x, int z)> insertionOrder)
{
	var occupied = insertionOrder.ToHashSet();
	return ConstructionOrder.PostOrder((x: 0, z: 0), cell => offsets
		.Select(offset => (x: cell.x + offset.Item1, z: cell.z + offset.Item2))
		.Where(occupied.Contains));
}
var doubleWall = Order(cells);
Equal(doubleWall.OrderBy(cell => cell.x).ThenBy(cell => cell.z), cells, "double wall has every cell exactly once");
Equal(Order(cells.Reverse()), doubleWall, "insertion order cannot change traversal");
Equal(doubleWall.TakeLast(1), new[] { (0, 0) }, "double wall entry stays last");
Equal(Line(0, 50000), Enumerable.Range(0, 50000).Reverse(), "large selection avoids recursive stack overflow");
Console.WriteLine("Construction ordering: 9 focused checks passed.");
