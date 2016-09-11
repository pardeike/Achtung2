using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;
using System;

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

		private static string _version = null;
		public static string Version
		{
			get
			{
				if (_version == null)
				{
					_version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
					string[] vparts = Version.Split(".".ToCharArray());
					if (vparts.Length > 3)
					{
						_version = vparts[0] + "." + vparts[1] + "." + vparts[2];
					}
				}
				return _version;
			}
		}

		static Tools()
		{
			markerMaterial = MaterialPool.MatFrom("Marker", ShaderDatabase.MoteGlow);
			lineMaterial = MaterialPool.MatFrom("Line", ShaderDatabase.MoteGlow);
			goHereLabel = "GoHere".Translate();
		}

		public static bool IsModKeyPressed(ModKey key)
		{
			switch (key)
			{
				case ModKey.Alt:
					return Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
				case ModKey.Ctrl:
					return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
				case ModKey.Shift:
					return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
				case ModKey.Meta:
					return Input.GetKey(KeyCode.LeftWindows) || Input.GetKey(KeyCode.RightWindows)
						|| Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);
				default:
					break;
			}
			return false;
		}

		public static IEnumerable<IntVec3> CellsBetween(IntVec3 start, IntVec3 end)
		{
			HashSet<IntVec3> cells = new HashSet<IntVec3>();
			int dx = Math.Sign(end.x - start.x);
			for (int x = start.x; dx != 0 && x != end.x; x += dx)
			{
				int z = (int)(GenMath.LerpDouble(start.x, end.x, start.z, end.z, x) + 0.5f);
				cells.Add(new IntVec3(x, 0, z));
			}
			int dz = Math.Sign(end.z - start.z);
			for (int z = start.z; dz != 0 && z != end.z; z += dz)
			{
				int x = (int)(GenMath.LerpDouble(start.z, end.z, start.x, end.x, z) + 0.5f);
				cells.Add(new IntVec3(x, 0, z));
			}
			return cells;
		}

		public static void ApplyScore(HashSet<ScoredPosition> result, Func<ScoredPosition, float> func)
		{
			result
				.Select(sp => new KeyValuePair<ScoredPosition, float>(sp, func(sp)))
				.Do(kv => kv.Key.Add(kv.Value));
		}

		public static void ApplyScoreLerped(HashSet<ScoredPosition> result, Func<ScoredPosition, float> func, float factorMin, float factorMax)
		{
			IEnumerable<KeyValuePair<ScoredPosition, float>> scores = result.Select(sp => new KeyValuePair<ScoredPosition, float>(sp, func(sp)));
			if (scores.Count() > 0)
			{
				float minScore = scores.Min(kv => kv.Value);
				float maxScore = scores.Max(kv => kv.Value);
				scores.Do(kv => kv.Key.Add(GenMath.LerpDouble(minScore, maxScore, factorMin, factorMax, kv.Value)));
			}
		}

		public static float PawnCellDistance(Pawn enemy, IntVec3 cell)
		{
			float dx = cell.x - enemy.DrawPos.x;
			float dz = cell.z - enemy.DrawPos.z;
			return 0.1f + dx * dx + dz * dz;
		}

		public static IEnumerable<T> Do<T>(this IEnumerable<T> sequence, Action<T> action)
		{
			if (sequence == null) return null;
			//bool logFirstException = true;
			IEnumerator<T> enumerator = sequence.GetEnumerator();
			while (enumerator.MoveNext()) action(enumerator.Current);
			/*{
				try
				{
					action(enumerator.Current);
				}
				catch (Exception ex)
				{
					if (logFirstException)
					{
						Log.Error("Iterating " + typeof(T) + ", element " + enumerator.Current + ", " + action + " cause exeception: " + ex);
						Log.Error(ex.StackTrace);
						logFirstException = false;
					}
				}
			}
			if (logFirstException == false) Log.Error("Exception context: " + Environment.StackTrace);*/
			return sequence;
		}

		public static IEnumerable<T> DoIf<T>(this IEnumerable<T> sequence, Func<T, bool> condition, Action<T> action)
		{
			return sequence.Where(condition).Do(action);
		}

		public static void Clear<T>(this IEnumerable<T> sequence)
		{
			if (sequence is List<T>)
				(sequence as List<T>).Clear();
			else
				sequence = sequence.Where(o => false);
		}

		public static IEnumerable<Pawn> UserSelectedAndReadyPawns()
		{
			return Find.Selector.SelectedObjects.OfType<Pawn>()
				.Where(pawn =>
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

		public static void ValueLabeled(this Listing_Standard listing, string label, object value, string tooltip = null)
		{
			float lineHeight = Text.LineHeight;
			Rect rect = listing.GetRect(lineHeight);
			if (!tooltip.NullOrEmpty())
			{
				if (Mouse.IsOver(rect))
				{
					Widgets.DrawHighlight(rect);
				}
				TooltipHandler.TipRegion(rect, tooltip);
			}

			TextAnchor savedAnchor = Text.Anchor;

			Text.Anchor = TextAnchor.MiddleLeft;
			Widgets.Label(rect, label);

			Text.Anchor = TextAnchor.MiddleRight;
			Widgets.Label(rect, value.ToString());

			Text.Anchor = savedAnchor;

			listing.Gap(listing.verticalSpacing);
		}

		public static void ModKeyChoice(this Listing_Standard listing, String prefix, ref ModKey value)
		{
			float lineHeight = Text.LineHeight;
			Rect rect = listing.GetRect(lineHeight);

			TextAnchor savedAnchor = Text.Anchor;
			Text.Anchor = TextAnchor.LowerCenter;

			IEnumerable<ModKey> keys = Enum.GetValues(typeof(ModKey)).Cast<ModKey>();
			rect.width = rect.width / keys.Count();

			Vector3 vec3 = Gen.MouseMapPosVector3();
			Vector2 pos = new Vector2(vec3.x, vec3.z);
			for (int i = 0; i < keys.Count(); i++)
			{
				ModKey val = keys.ElementAt(i);
				if (Mouse.IsOver(rect) && Event.current.isMouse) value = val;

				if (Mouse.IsOver(rect)) Widgets.DrawMenuSection(rect, false);
				if (value == val)
				{
					GUI.color = Color.green;
					Widgets.DrawHighlight(rect);
					GUI.color = Color.white;
				}
				String label = (prefix + val).Translate();
				Widgets.Label(rect, label);
				rect.x += rect.width;
			}

			Text.Anchor = savedAnchor;

			listing.Gap(listing.verticalSpacing);
		}

	}
}