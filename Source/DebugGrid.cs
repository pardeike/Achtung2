using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace AchtungMod;

public static class DebugGrid
{
	static readonly Dictionary<IntVec3, float> grid = [];
	static readonly Vector3 shifted = new(0.5f, 0f, 0.5f);
	static readonly float altitude = Altitudes.AltitudeFor(AltitudeLayer.Pawn - 1);
	static bool updating = false;

	public static void OnGUI()
	{
		lock (grid)
		{
			if (updating) return;

			var pawnAltitude = Altitudes.AltitudeFor(AltitudeLayer.Pawn - 1);
			foreach (var (cell, val) in grid)
			{
				var pos = new Vector3(cell.x, pawnAltitude, cell.z);
				Draw(pos, new Color(0f, 0f, 1f, val));
			}
		}
	}

	public static void Mark(this IntVec3 cell, float val)
	{
		if (val == 0f)
		{
			_ = grid.Remove(cell);
			return;
		}
		grid[cell] = val;
	}

	public static void Run(Action action)
	{
		lock (grid) updating = true;
		grid.Clear();
		action();
		updating = false;
	}

	static void Draw(Vector3 pos, Color color)
	{
		pos.y = altitude;
		var matrix = new Matrix4x4();
		matrix.SetTRS(pos + shifted, Quaternion.identity, Vector3.one);
		var material = SolidColorMaterials.SimpleSolidColorMaterial(color);
		Graphics.DrawMesh(MeshPool.plane10, matrix, material, 0);
	}

}