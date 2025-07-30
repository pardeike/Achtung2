using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Verse;

namespace AchtungMod;

[StaticConstructorOnStartup]
public class TacticalGrid : ICellBoolGiver
{
	public Pawn pawn;

	readonly CellBoolDrawer drawer;
	readonly int mapSizeX;
	HashSet<IntVec3> cells;

	static readonly Material stripedMaterial;

	static TacticalGrid()
	{
		var texture = ContentFinder<Texture2D>.Get("Striped", true);
		texture.wrapMode = TextureWrapMode.Repeat;
		texture.filterMode = FilterMode.Point;
		var req = new MaterialRequest(texture, ShaderDatabase.Transparent, Color.white.ToTransparent(0.25f));
		stripedMaterial = MaterialPool.MatFrom(req);
		stripedMaterial.renderQueue = 3600;
	}

	public TacticalGrid(Pawn pawn)
	{
		this.pawn = pawn;
		cells = [];
		var map = pawn.Map;
		mapSizeX = map.Size.x;
		drawer = new CellBoolDrawer(this, map.Size.x, map.Size.z, 0);
		drawer.material = stripedMaterial;
	}

	public void SetCells(HashSet<IntVec3> newCells)
	{
		cells = newCells;
		drawer.SetDirty();
	}

	public bool Contains(IntVec3 cell) => cells.Contains(cell);

	public void Update()
	{
		void FixUVs()
		{
			foreach (var mesh in drawer.meshes)
			{
				var verts = mesh.vertices;
				var uvs = new Vector2[verts.Length];
				for (var i = 0; i < verts.Length; i++)
					uvs[i] = new Vector2(verts[i].x, verts[i].z);
				mesh.uv = uvs;
			}
		}

		void ActuallyDraw() // copy of CellBoolDrawer.ActuallyDraw
		{
			if (drawer.dirty)
			{
				drawer.RegenerateMesh();
				FixUVs(); // added
			}

			var oldAltitude = AltitudeLayer.MapDataOverlay.AltitudeFor();
			var newAltitude = AltitudeLayer.SmallWire.AltitudeFor() - 0.1f;
			var translate = Matrix4x4.TRS(
				new Vector3(0f, newAltitude - oldAltitude, 0f),
				Quaternion.identity,
				Vector3.one
			);
			for (var i = 0; i < drawer.meshes.Count; i++)
				Graphics.DrawMesh(drawer.meshes[i], translate, stripedMaterial, 0);
		}

		void CellBoolDrawerUpdate() // copy of CellBoolDrawer.CellBoolDrawerUpdate
		{
			if (!drawer.wantDraw)
				return;
			ActuallyDraw();
			drawer.wantDraw = false;
		}

		drawer.MarkForDraw();
		CellBoolDrawerUpdate();
	}

	// ICellBoolGiver

	public Color Color => throw new System.NotImplementedException("should never be called");

	public bool GetCellBool(int index)
	{
		var intVec = CellIndicesUtility.IndexToCell(index, mapSizeX);
		return cells.Contains(intVec);
	}
	public Color GetCellExtraColor(int index) => Color.white; // must be white

}