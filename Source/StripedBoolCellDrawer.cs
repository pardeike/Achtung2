using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace AchtungMod;

/// <summary>
/// A custom cell drawer for striped overlays.  It generates UVs, applies an
/// altitude offset at draw time, and optionally fades out the outer edges
/// by adding quads with alpha gradients.
/// </summary>
public class StripedCellBoolDrawer(
	Func<int, bool> cellBoolGetter,
	Func<int, Color> extraColorGetter,
	int mapSizeX,
	int mapSizeZ,
	float fadeWidth = 0.25f,
	float altitudeOffset = 0f)
{
	private bool wantDraw;
	public Material material;
	private bool dirty = true;

	// Meshes; each holds up to 16383 quads.
	internal readonly List<Mesh> meshes = [];

	private readonly int mapSizeX = mapSizeX;
	private readonly int mapSizeZ = mapSizeZ;
	private readonly float fadeWidth = Mathf.Max(0f, fadeWidth);
	private readonly float altitudeOffset = altitudeOffset;
	private readonly Func<int, bool> cellBoolGetter = cellBoolGetter ?? throw new ArgumentNullException(nameof(cellBoolGetter));
	private readonly Func<int, Color> extraColorGetter = extraColorGetter ?? throw new ArgumentNullException(nameof(extraColorGetter));

	// Working lists reused to reduce allocations.
	private static readonly List<Vector3> s_verts = [];
	private static readonly List<int> s_tris = [];
	private static readonly List<Color> s_colors = [];
	private static readonly List<Vector2> s_uvs = [];
	private const int MaxQuadsPerMesh = 16383;

	public void MarkForDraw() => wantDraw = true;
	public void SetDirty() => dirty = true;

	public void Update()
	{
		if (!wantDraw) return;
		if (dirty) { RegenerateMesh(); dirty = false; }
		if (material == null) return;
		// Apply altitude offset via a translation matrix.  The `matrix` parameter
		// combines translation, rotation and scale into a single transform:contentReference[oaicite:1]{index=1}.
		var translate = Matrix4x4.TRS(new Vector3(0f, altitudeOffset, 0f),
											Quaternion.identity,
											Vector3.one);
		for (var i = 0; i < meshes.Count; i++)
			Graphics.DrawMesh(meshes[i], translate, material, 0);
		wantDraw = false;
	}

	public void RegenerateMesh()
	{
		// clear old data
		foreach (var m in meshes) m.Clear();
		var meshIndex = 0;
		var quadCount = 0;
		if (meshes.Count == 0) meshes.Add(new Mesh { name = "StripedCellBoolDrawer" });
		var currentMesh = meshes[meshIndex];

		var baseY = AltitudeLayer.MapDataOverlay.AltitudeFor(); // base altitude
		var maxX = mapSizeX;
		var maxZ = mapSizeZ;

		for (var j = 0; j < maxX; j++)
		{
			for (var k = 0; k < maxZ; k++)
			{
				var idx = CellIndicesUtility.CellToIndex(j, k, maxX);
				if (!cellBoolGetter(idx)) continue;
				// colour for the interior
				var c = extraColorGetter(idx);
				c.a = 1f; // full opacity inside

				// interior quad
				AddQuad(new Vector3(j, baseY, k),
						new Vector3(j, baseY, k + 1),
						new Vector3(j + 1, baseY, k + 1),
						new Vector3(j + 1, baseY, k),
						c, c, c, c,
						ref quadCount, ref currentMesh, ref meshIndex);

				// east neighbour
				if (j + 1 >= maxX ||
					!cellBoolGetter(CellIndicesUtility.CellToIndex(j + 1, k, maxX)))
				{
					var fade0 = c;
					var fade1 = c; fade1.a = 0f;
					AddQuad(new Vector3(j + 1, baseY, k),
							new Vector3(j + 1, baseY, k + 1),
							new Vector3(j + 1 + fadeWidth, baseY, k + 1),
							new Vector3(j + 1 + fadeWidth, baseY, k),
							fade0, fade0, fade1, fade1,
							ref quadCount, ref currentMesh, ref meshIndex);
				}
				// west neighbour
				if (j - 1 < 0 ||
					!cellBoolGetter(CellIndicesUtility.CellToIndex(j - 1, k, maxX)))
				{
					var fade0 = c;
					var fade1 = c; fade1.a = 0f;
					AddQuad(new Vector3(j, baseY, k),
							new Vector3(j, baseY, k + 1),
							new Vector3(j - fadeWidth, baseY, k + 1),
							new Vector3(j - fadeWidth, baseY, k),
							fade0, fade0, fade1, fade1,
							ref quadCount, ref currentMesh, ref meshIndex);
				}
				// north neighbour
				if (k + 1 >= maxZ ||
					!cellBoolGetter(CellIndicesUtility.CellToIndex(j, k + 1, maxX)))
				{
					var fade0 = c;
					var fade1 = c; fade1.a = 0f;
					AddQuad(new Vector3(j, baseY, k + 1),
							new Vector3(j + 1, baseY, k + 1),
							new Vector3(j + 1, baseY, k + 1 + fadeWidth),
							new Vector3(j, baseY, k + 1 + fadeWidth),
							fade0, fade0, fade1, fade1,
							ref quadCount, ref currentMesh, ref meshIndex);
				}
				// south neighbour
				if (k - 1 < 0 ||
					!cellBoolGetter(CellIndicesUtility.CellToIndex(j, k - 1, maxX)))
				{
					var fade0 = c;
					var fade1 = c; fade1.a = 0f;
					AddQuad(new Vector3(j, baseY, k),
							new Vector3(j + 1, baseY, k),
							new Vector3(j + 1, baseY, k - fadeWidth),
							new Vector3(j, baseY, k - fadeWidth),
							fade0, fade0, fade1, fade1,
							ref quadCount, ref currentMesh, ref meshIndex);
				}
			}
		}
		FinalizeWorkingDataIntoMesh(currentMesh);
	}

	// Helper to add a quad; it handles vertex, colour, UV and triangle lists
	// and splits meshes once the 16383‑quad limit is reached.
	private void AddQuad(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3,
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
				meshes.Add(new Mesh { name = "StripedCellBoolDrawer" });
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