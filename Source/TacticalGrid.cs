using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace AchtungMod;

[StaticConstructorOnStartup]
public class TacticalGrid
{
	public Pawn pawn;
	readonly int mapSizeX;
	HashSet<IntVec3> cells;
	readonly TiledCellBoolDrawer drawer;

	static readonly Material stripedMaterial;
	static TacticalGrid()
	{
		var texture = ContentFinder<Texture2D>.Get("Striped");
		texture.wrapMode = TextureWrapMode.Repeat;
		texture.filterMode = FilterMode.Point;
		var req = new MaterialRequest(texture, ShaderDatabase.Transparent, Color.white.ToTransparent(0.33f));
		stripedMaterial = MaterialPool.MatFrom(req);
		stripedMaterial.renderQueue = 3600;
	}

	public TacticalGrid(Pawn pawn)
	{
		this.pawn = pawn;
		cells = [];
		var map = pawn.Map;
		mapSizeX = map.Size.x;
		var offset = AltitudeLayer.SmallWire.AltitudeFor() - AltitudeLayer.MapDataOverlay.AltitudeFor() - 0.1f;
		drawer = new TiledCellBoolDrawer(
			stripedMaterial,
			index => cells.Contains(CellIndicesUtility.IndexToCell(index, mapSizeX)),
			map.Size.x, map.Size.z
		);
	}

	public void SetCells(HashSet<IntVec3> newCells)
	{
		cells = newCells;
		drawer.SetDirty();
	}

	public bool Contains(IntVec3 cell) => cells.Contains(cell);

	public void Update()
	{
		drawer.MarkForDraw();
		drawer.Update();
	}
}