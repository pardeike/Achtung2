using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace AchtungMod;

public class TiledCellBoolDrawer(
	Material material,
	Func<int, bool> cellBoolGetter,
	int mapSizeX,
	int mapSizeZ,
	float fadeWidth = 0.25f)
{
	private bool wantDraw;
	private bool dirty = true;

	internal readonly List<Mesh> meshes = [];

	private readonly Material material = material ?? throw new ArgumentNullException(nameof(material));
	private readonly int mapSizeX = mapSizeX;
	private readonly int mapSizeZ = mapSizeZ;
	private readonly float fadeWidth = Mathf.Max(0f, fadeWidth);

	private readonly float altitude = AltitudeLayer.SmallWire.AltitudeFor() - 0.1f;
	private readonly Func<int, bool> cellBoolGetter = cellBoolGetter ?? throw new ArgumentNullException(nameof(cellBoolGetter));

	private static readonly List<Vector3> s_verts = [];
	private static readonly List<int> s_tris = [];
	private static readonly List<Color> s_colors = [];
	private static readonly List<Vector2> s_uvs = [];
	private const int MaxQuadsPerMesh = 16383;
	private static readonly Color white = Color.white;

	public void MarkForDraw() => wantDraw = true;
	public void SetDirty() => dirty = true;

	public void Update()
	{
		if (!wantDraw) return;
		if (dirty)
		{
			RegenerateMesh();
			dirty = false;
		}
		if (material == null) return;
		for (var i = 0; i < meshes.Count; i++)
			Graphics.DrawMesh(meshes[i], Matrix4x4.identity, material, 0);
		wantDraw = false;
	}

	public void RegenerateMesh()
	{
		foreach (var m in meshes) m.Clear();
		var meshIndex = 0;
		var quadCount = 0;
		if (meshes.Count == 0) meshes.Add(new Mesh { name = "TiledCellBoolDrawer" });
		var currentMesh = meshes[meshIndex];

		var baseY = altitude;
		var maxX = mapSizeX;
		var maxZ = mapSizeZ;

		for (var j = 0; j < maxX; j++)
		{
			for (var k = 0; k < maxZ; k++)
			{
				var idx = CellIndicesUtility.CellToIndex(j, k, maxX);
				if (!cellBoolGetter(idx)) continue;

				// Interior quad
				AddQuad(new Vector3(j, baseY, k),
						new Vector3(j, baseY, k + 1),
						new Vector3(j + 1, baseY, k + 1),
						new Vector3(j + 1, baseY, k),
						white, white, white, white,
						ref quadCount, ref currentMesh, ref meshIndex);

				// Right edge (east)
				if (j + 1 >= maxX ||
					!cellBoolGetter(CellIndicesUtility.CellToIndex(j + 1, k, maxX)))
				{
					var fadeInner = white;
					var fadeOuter = new Color(1f, 1f, 1f, 0f);
					// vertices: inner bottom, outer bottom, outer top, inner top
					AddQuad(new Vector3(j + 1, baseY, k),
							new Vector3(j + 1 + fadeWidth, baseY, k),
							new Vector3(j + 1 + fadeWidth, baseY, k + 1),
							new Vector3(j + 1, baseY, k + 1),
							fadeInner, fadeOuter, fadeOuter, fadeInner,
							ref quadCount, ref currentMesh, ref meshIndex);
				}

				// Left edge (west)
				if (j - 1 < 0 ||
					!cellBoolGetter(CellIndicesUtility.CellToIndex(j - 1, k, maxX)))
				{
					var fadeInner = white;
					var fadeOuter = new Color(1f, 1f, 1f, 0f);
					AddQuad(new Vector3(j, baseY, k),
							new Vector3(j - fadeWidth, baseY, k),
							new Vector3(j - fadeWidth, baseY, k + 1),
							new Vector3(j, baseY, k + 1),
							fadeInner, fadeOuter, fadeOuter, fadeInner,
							ref quadCount, ref currentMesh, ref meshIndex);
				}

				// Top edge (north – k+1)
				if (k + 1 >= maxZ ||
					!cellBoolGetter(CellIndicesUtility.CellToIndex(j, k + 1, maxX)))
				{
					var fadeInner = white;
					var fadeOuter = new Color(1f, 1f, 1f, 0f);
					AddQuad(new Vector3(j, baseY, k + 1),
							new Vector3(j, baseY, k + 1 + fadeWidth),
							new Vector3(j + 1, baseY, k + 1 + fadeWidth),
							new Vector3(j + 1, baseY, k + 1),
							fadeInner, fadeOuter, fadeOuter, fadeInner,
							ref quadCount, ref currentMesh, ref meshIndex);
				}

				// Bottom edge (south – k-1)
				if (k - 1 < 0 ||
					!cellBoolGetter(CellIndicesUtility.CellToIndex(j, k - 1, maxX)))
				{
					var fadeInner = white;
					var fadeOuter = new Color(1f, 1f, 1f, 0f);
					AddQuad(new Vector3(j, baseY, k),
							new Vector3(j, baseY, k - fadeWidth),
							new Vector3(j + 1, baseY, k - fadeWidth),
							new Vector3(j + 1, baseY, k),
							fadeInner, fadeOuter, fadeOuter, fadeInner,
							ref quadCount, ref currentMesh, ref meshIndex);
				}
			}
		}
		FinalizeWorkingDataIntoMesh(currentMesh);
	}

	private void AddQuad(
		Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3,
		Color c0, Color c1, Color c2, Color c3,
		ref int quadCount, ref Mesh mesh, ref int meshIndex)
	{
		var baseIndex = s_verts.Count;
		s_verts.Add(v0); s_verts.Add(v1); s_verts.Add(v2); s_verts.Add(v3);
		s_colors.Add(c0); s_colors.Add(c1); s_colors.Add(c2); s_colors.Add(c3);
		s_uvs.Add(new Vector2(v0.x, v0.z));
		s_uvs.Add(new Vector2(v1.x, v1.z));
		s_uvs.Add(new Vector2(v2.x, v2.z));
		s_uvs.Add(new Vector2(v3.x, v3.z));
		s_tris.Add(baseIndex); s_tris.Add(baseIndex + 1); s_tris.Add(baseIndex + 2);
		s_tris.Add(baseIndex); s_tris.Add(baseIndex + 2); s_tris.Add(baseIndex + 3);
		quadCount++;
		if (quadCount >= MaxQuadsPerMesh)
		{
			FinalizeWorkingDataIntoMesh(mesh);
			meshIndex++;
			if (meshes.Count <= meshIndex)
				meshes.Add(new Mesh { name = "TiledCellBoolDrawer" });
			mesh = meshes[meshIndex];
			quadCount = 0;
		}
	}

	private void FinalizeWorkingDataIntoMesh(Mesh mesh)
	{
		if (s_verts.Count == 0) return;
		mesh.SetVertices(s_verts);
		mesh.SetTriangles(s_tris, 0);
		mesh.SetColors(s_colors);
		mesh.SetUVs(0, s_uvs);
		mesh.RecalculateBounds();
		s_verts.Clear(); s_tris.Clear(); s_colors.Clear(); s_uvs.Clear();
	}
}