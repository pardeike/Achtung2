using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace AchtungMod
{
	public enum ActionMode
	{
		Drafted,
		Undrafted,
		Other
	}

	[StaticConstructorOnStartup]
	static class Tools
	{
		public static Material markerMaterial;
		public static Material lineMaterial;
		public static string goHereLabel;

		static Tools()
		{
			markerMaterial = MaterialPool.MatFrom("Marker", ShaderDatabase.MoteGlow);
			lineMaterial = MaterialPool.MatFrom("Line", ShaderDatabase.MoteGlow);
			goHereLabel = "GoHere".Translate();
		}

		public static List<Pawn> UserSelectedAndReadyPawns()
		{
			List<Pawn> allPawns = Find.Selector.SelectedObjects.OfType<Pawn>().ToList();
			return allPawns.FindAll(pawn =>
					pawn.drafter != null
					&& pawn.IsColonistPlayerControlled
					&& pawn.Downed == false
					&& pawn.drafter.CanTakeOrderedJob()
			);
		}

		public static bool IsGoHereOption(FloatMenuOption option)
		{
			return option.Label == goHereLabel;
		}

		public static bool GetDraftingStatus(Pawn pawn)
		{
			if (pawn.drafter == null)
			{
				pawn.drafter = new Pawn_DraftController(pawn);
			}
			return pawn.drafter.Drafted;
		}

		public static bool SetDraftStatus(Pawn pawn, bool drafted, bool fake = true)
		{
			bool previousStatus = GetDraftingStatus(pawn);
			if (pawn.drafter.Drafted != drafted)
			{
				if (fake) // we don't use the indirect method because it has lots of side effects
				{
					DraftStateHandler draftHandler = pawn.drafter.draftStateHandler;
					FieldInfo draftHandlerField = typeof(DraftStateHandler).GetField("draftedInt", BindingFlags.NonPublic | BindingFlags.Instance);
					if (draftHandlerField == null)
					{
						Log.Error("No field 'draftedInt' in DraftStateHandler");
					}
					else
					{
						draftHandlerField.SetValue(draftHandler, drafted);
					}
				}
				else
				{
					pawn.drafter.Drafted = drafted;
				}
			}
			return previousStatus;
		}

		public static bool ForceDraft(Pawn pawn, bool drafted)
		{
			bool oldState = SetDraftStatus(pawn, drafted, false);
			return oldState != drafted;
		}

		public static void DrawMarker(Vector3 pos)
		{
			pos.y = Altitudes.AltitudeFor(AltitudeLayer.Pawn - 1);
			Tools.DrawScaledMesh(MeshPool.plane10, markerMaterial, pos, Quaternion.identity, 1.5f, 1.5f);
		}

		public static void DebugPosition(Vector3 pos, Color color)
		{
			pos.y = Altitudes.AltitudeFor(AltitudeLayer.Pawn - 1);
			Material material = SolidColorMaterials.SimpleSolidColorMaterial(color);
			Tools.DrawScaledMesh(MeshPool.plane10, material, pos + new Vector3(0.5f, 0f, 0.5f), Quaternion.identity, 1.0f, 1.0f);
		}

		public static void DrawScaledMesh(Mesh mesh, Material mat, Vector3 pos, Quaternion q, float mx, float my, float mz = 1f)
		{
			Vector3 s = new Vector3(mx, mz, my);
			Matrix4x4 matrix = new Matrix4x4();
			matrix.SetTRS(pos, q, s);
			Graphics.DrawMesh(mesh, matrix, mat, 0);
		}

		public static void DrawLineBetween(Vector3 A, Vector3 B, float thickness)
		{
			if ((Mathf.Abs((float)(A.x - B.x)) >= 0.01f) || (Mathf.Abs((float)(A.z - B.z)) >= 0.01f))
			{
				Vector3 pos = (Vector3)((A + B) / 2f);
				if (A != B)
				{
					A.y = B.y;
					float z = (A - B).MagnitudeHorizontal();
					Quaternion q1 = Quaternion.LookRotation(A - B);
					Quaternion q2 = Quaternion.LookRotation(B - A);
					float w = 0.5f;
					DrawScaledMesh(MeshPool.plane10, lineMaterial, pos, q1, w, z);
					DrawScaledMesh(MeshPool.pies[180], lineMaterial, A, q1, w, w);
					DrawScaledMesh(MeshPool.pies[180], lineMaterial, B, q2, w, w);
				}
			}
		}

		public static Vector2 LabelDrawPosFor(Vector3 drawPos, float worldOffsetZ)
		{
			drawPos.z += worldOffsetZ;
			Vector2 vector2 = Find.Camera.WorldToScreenPoint(drawPos);
			vector2.y = Screen.height - vector2.y;
			return vector2;
		}

	}
}