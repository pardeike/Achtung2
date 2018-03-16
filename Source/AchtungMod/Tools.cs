using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;
using System;

namespace AchtungMod
{
	[StaticConstructorOnStartup]
	static class Tools
	{
		public static Material markerMaterial;
		public static Material lineMaterial;
		public static string goHereLabel;

		public static FieldInfo draftHandlerField = typeof(Pawn_DraftController).GetField("draftedInt", BindingFlags.NonPublic | BindingFlags.Instance);

		private static string _version = null;
		public static string Version
		{
			get
			{
				if (_version == null)
				{
					_version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
					var vparts = Version.Split(".".ToCharArray());
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
			markerMaterial = MaterialPool.MatFrom("AchtungMarker", ShaderDatabase.Transparent);
			lineMaterial = MaterialPool.MatFrom(GenDraw.LineTexPath, ShaderDatabase.Transparent, new Color(1f, 0.5f, 0.5f));
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
						|| Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand)
						|| Input.GetKey(KeyCode.LeftApple) || Input.GetKey(KeyCode.RightApple);
				default:
					break;
			}
			return false;
		}

		public static bool PawnsUnderMouse()
		{
			return UI.MouseCell()
				.GetThingList(Find.VisibleMap)
				.OfType<Pawn>()
				.Any();
		}

		public static IEnumerable<T> Do<T>(this IEnumerable<T> sequence, Action<T> action)
		{
			if (sequence == null) return null;
			var enumerator = sequence.GetEnumerator();
			while (enumerator.MoveNext()) action(enumerator.Current);
			return sequence;
		}

		public static IEnumerable<T> DoIf<T>(this IEnumerable<T> sequence, Func<T, bool> condition, Action<T> action)
		{
			return sequence.Where(condition).Do(action);
		}

		public static List<Colonist> GetSelectedColonists()
		{
			return Find.Selector.SelectedObjects.OfType<Pawn>()
				.Where(pawn =>
					pawn.drafter != null
					&& pawn.IsColonistPlayerControlled
					&& pawn.Downed == false
					&& pawn.jobs.IsCurrentJobPlayerInterruptible())
				.Select(pawn => new Colonist(pawn))
				.ToList();
		}

		public static bool IsGoHereOption(FloatMenuOption option)
		{
			return option.Label == goHereLabel;
		}

		public static bool HasNonEmptyFloatMenu(Vector3 clickLoc, Pawn pawn)
		{
			var choices = FloatMenuMakerMap.ChoicesAtFor(clickLoc, pawn);
			return choices.Any(option => Tools.IsGoHereOption(option) == false);
		}

		public static bool GetDraftingStatus(Pawn pawn)
		{
			if (pawn.drafter == null)
				pawn.drafter = new Pawn_DraftController(pawn);
			return pawn.drafter.Drafted;
		}

		public static bool SetDraftStatus(Pawn pawn, bool drafted)
		{
			var previousStatus = GetDraftingStatus(pawn);
			if (previousStatus != drafted)
			{
				// we don't use the indirect method because it has lots of side effects
				//
				draftHandlerField?.SetValue(pawn.drafter, drafted);
			}
			return previousStatus;
		}

		public static bool ForceDraft(Pawn pawn, bool drafted)
		{
			var oldState = SetDraftStatus(pawn, drafted);
			return oldState != drafted;
		}

		public static void DrawMarker(Vector3 pos)
		{
			pos.y = Altitudes.AltitudeFor(AltitudeLayer.Pawn - 1);
			DrawScaledMesh(MeshPool.plane10, markerMaterial, pos, Quaternion.identity, 1.25f, 1.25f);
		}

		public static void DrawScaledMesh(Mesh mesh, Material mat, Vector3 pos, Quaternion q, float mx, float mz, float my = 1f)
		{
			var s = new Vector3(mx, my, mz);
			var matrix = new Matrix4x4();
			matrix.SetTRS(pos, q, s);
			Graphics.DrawMesh(mesh, matrix, mat, 0);
		}

		public static void DrawLineBetween(Vector3 A, Vector3 B, float thickness)
		{
			if ((Mathf.Abs((float)(A.x - B.x)) >= 0.01f) || (Mathf.Abs((float)(A.z - B.z)) >= 0.01f))
			{
				var pos = (Vector3)((A + B) / 2f);
				if (A != B)
				{
					A.y = B.y;
					var z = (A - B).MagnitudeHorizontal();
					var q1 = Quaternion.LookRotation(A - B);
					var q2 = Quaternion.LookRotation(B - A);
					var w = 0.5f;
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

		public static void CheckboxEnhanced(this Listing_Standard listing, string name, ref bool value, string tooltip = null)
		{
			var startHeight = listing.CurHeight;

			Text.Font = GameFont.Small;
			GUI.color = Color.white;
			listing.CheckboxLabeled((name + "Title").Translate(), ref value);

			Text.Font = GameFont.Tiny;
			listing.ColumnWidth -= 34;
			GUI.color = Color.gray;
			listing.Label((name + "Explained").Translate());
			listing.ColumnWidth += 34;

			var rect = listing.GetRect(0);
			rect.height = listing.CurHeight - startHeight;
			rect.y -= rect.height;
			if (Mouse.IsOver(rect))
			{
				Widgets.DrawHighlight(rect);
				if (!tooltip.NullOrEmpty()) TooltipHandler.TipRegion(rect, tooltip);
			}

			listing.Gap();
		}

		public static void ValueLabeled<T>(this Listing_Standard listing, string name, ref T value, string tooltip = null)
		{
			var startHeight = listing.CurHeight;

			var rect = listing.GetRect(Text.LineHeight + listing.verticalSpacing);

			Text.Font = GameFont.Small;
			GUI.color = Color.white;

			var savedAnchor = Text.Anchor;

			Text.Anchor = TextAnchor.MiddleLeft;
			Widgets.Label(rect, (name + "Title").Translate());

			Text.Anchor = TextAnchor.MiddleRight;
			if (typeof(T).IsEnum)
				Widgets.Label(rect, (typeof(T).Name + "Option" + value.ToString()).Translate());
			else
				Widgets.Label(rect, value.ToString());

			Text.Anchor = savedAnchor;

			var key = name + "Explained";
			if (key.CanTranslate())
			{
				Text.Font = GameFont.Tiny;
				listing.ColumnWidth -= 34;
				GUI.color = Color.gray;
				listing.Label(key.Translate());
				listing.ColumnWidth += 34;
			}

			rect = listing.GetRect(0);
			rect.height = listing.CurHeight - startHeight;
			rect.y -= rect.height;
			if (Mouse.IsOver(rect))
			{
				Widgets.DrawHighlight(rect);
				if (!tooltip.NullOrEmpty()) TooltipHandler.TipRegion(rect, tooltip);

				if (Event.current.isMouse && Event.current.button == 0 && Event.current.type == EventType.MouseDown)
				{
					var keys = Enum.GetValues(typeof(T)).Cast<T>().ToArray();
					for (var i = 0; i < keys.Length; i++)
					{
						var newValue = keys[(i + 1) % keys.Length];
						if (keys[i].ToString() == value.ToString())
						{
							value = newValue;
							break;
						}
					}
					Event.current.Use();
				}
			}

			listing.Gap();
		}

	}
}