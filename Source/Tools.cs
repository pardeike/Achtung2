using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;
using static Harmony.AccessTools;

namespace AchtungMod
{
	[StaticConstructorOnStartup]
	static class Tools
	{
		public static Material forceIconMaterial;
		public static Material markerMaterial;
		public static Material lineMaterial;
		public static string goHereLabel;

		public static FieldInfo draftHandlerField = typeof(Pawn_DraftController).GetField("draftedInt", BindingFlags.NonPublic | BindingFlags.Instance);
		public static readonly FieldRef<ReservationManager, List<ReservationManager.Reservation>> Reservations = FieldRefAccess<ReservationManager, List<ReservationManager.Reservation>>("reservations");

		private static string _version;
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
			forceIconMaterial = MaterialPool.MatFrom("ForceIcon", ShaderDatabase.Cutout);
			markerMaterial = MaterialPool.MatFrom("AchtungMarker", ShaderDatabase.Transparent);
			lineMaterial = MaterialPool.MatFrom(GenDraw.LineTexPath, ShaderDatabase.Transparent, new Color(1f, 0.5f, 0.5f));
			goHereLabel = "GoHere".Translate();
		}

#if DEBUG
		public static void Debug(string text)
		{
			Log.Warning(text);
		}
		public static void Debug(Thing thing, string text)
		{
			if (text != null && Find.Selector.IsSelected(thing))
				Log.Warning(text);
		}
#else
		public static void Debug(string text) { }
		public static void Debug(Thing thing, string text) { }
#endif

		public static string PawnOverBreakLevel(Pawn pawn)
		{
			var mb = pawn.mindState.mentalBreaker;
			switch (Achtung.Settings.breakLevel)
			{
				case BreakLevel.Minor:
					return mb.BreakMinorIsImminent ? "BreakRiskMinor".Translate() : null;
				case BreakLevel.Major:
					return mb.BreakMajorIsImminent ? "BreakRiskMajor".Translate() : null;
				case BreakLevel.AlmostExtreme:
					return mb.BreakExtremeIsApproaching ? "BreakRiskExtreme".Translate() : null;
				case BreakLevel.Extreme:
					return mb.BreakExtremeIsImminent ? "BreakRiskExtreme".Translate() : null;
			}
			return null;
		}

		public static bool PawnOverHealthLevel(Pawn pawn)
		{
			switch (Achtung.Settings.healthLevel)
			{
				case HealthLevel.ShouldBeTendedNow:
					return HealthAIUtility.ShouldBeTendedNowByPlayer(pawn) || HealthAIUtility.ShouldHaveSurgeryDoneNow(pawn);
				case HealthLevel.PrefersMedicalRest:
					return HealthAIUtility.ShouldSeekMedicalRest(pawn);
				case HealthLevel.NeedsMedicalRest:
					return HealthAIUtility.ShouldSeekMedicalRestUrgent(pawn);
				case HealthLevel.InPainShock:
					return pawn.health.InPainShock;
			}
			return false;
		}

		public static Vector3 RotateBy(Vector3 offsetFromCenter, int rotation, bool was45)
		{
			var offset = new Vector3(offsetFromCenter.x, offsetFromCenter.y, offsetFromCenter.z);
			if ((Math.Abs(rotation) % 90) != 0)
			{
				if (was45)
					offset /= Mathf.Sqrt(2);
				else
					offset *= Mathf.Sqrt(2);
			}
			offset = offset.RotatedBy(rotation);
			return offset;
		}

		public static bool IsModKey(KeyCode code, AchtungModKey key)
		{
			switch (key)
			{
				case AchtungModKey.Alt:
					return code == KeyCode.LeftAlt || code == KeyCode.RightAlt;
				case AchtungModKey.Ctrl:
					return code == KeyCode.LeftControl || code == KeyCode.RightControl;
				case AchtungModKey.Shift:
					return code == KeyCode.LeftShift || code == KeyCode.RightShift;
				case AchtungModKey.Meta:
					return code == KeyCode.LeftWindows || code == KeyCode.RightWindows
						|| code == KeyCode.LeftCommand || code == KeyCode.RightCommand
						|| code == KeyCode.LeftApple || code == KeyCode.RightApple;
			}
			return false;
		}

		public static bool IsModKeyPressed(AchtungModKey key)
		{
			switch (key)
			{
				case AchtungModKey.Alt:
					return Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
				case AchtungModKey.Ctrl:
					return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
				case AchtungModKey.Shift:
					return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
				case AchtungModKey.Meta:
					return Input.GetKey(KeyCode.LeftWindows) || Input.GetKey(KeyCode.RightWindows)
						|| Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand)
						|| Input.GetKey(KeyCode.LeftApple) || Input.GetKey(KeyCode.RightApple);
			}
			return false;
		}

		public static bool IsForcedJob()
		{
			return IsModKeyPressed(AchtungModKey.Alt);
		}

		public static bool IsOfType<T>(this WorkGiver workgiver) where T : class
		{
			return ((workgiver as T) != null);
		}

		public static bool IsOfType<T>(this WorkGiverDef def) where T : class
		{
			return ((def.Worker as T) != null);
		}

		public static bool Has45DegreeOffset(List<Colonist> colonists)
		{
			return colonists.All(c1 =>
			{
				return colonists.All(c2 =>
				{
					var delta = c1.pawn.Position - c2.pawn.Position;
					return ((Math.Abs(delta.x) + Math.Abs(delta.z)) % 2 == 0);
				});
			});
		}

		public static bool IsFreeTarget(Pawn pawn, ForcedTarget target)
		{
			var allReservations = Tools.Reservations(pawn.Map.reservationManager);
			return allReservations.Any(res => res.Target.Cell == target.item.Cell && res.Claimant != pawn) == false;
		}

		public static bool WillBlock(this LocalTargetInfo info)
		{
			if (info.HasThing == false) return false;
			var thing = info.Thing;
			var thingDef = thing.def;
			var blueprint = thing as Blueprint;
			if (blueprint != null)
			{
				var def = blueprint.def.entityDefToBuild as ThingDef;
				if (def != null)
					thingDef = def;
			}
			var frame = thing as Frame;
			if (frame != null)
			{
				var def = frame.def.entityDefToBuild as ThingDef;
				if (def != null)
					thingDef = def;
			}
			return thingDef.passability == Traversability.Impassable;
		}

		public static int[] NeighbourCount(IntVec3 pos, PathGrid grid, HashSet<int> planned, int mapWidth)
		{
			var idx = CellIndicesUtility.CellToIndex(pos.x, pos.z, mapWidth);
			if (grid.Walkable(pos) == false || planned.Contains(idx))
				return new[] { -1, 0, 0 };
			var counts = new int[4];
			for (var i = 0; i < 4; i++)
			{
				var vec = pos + GenAdj.CardinalDirectionsAround[i];
				idx = CellIndicesUtility.CellToIndex(vec.x, vec.z, mapWidth);
				counts[i] = planned.Contains(idx) == false && grid.Walkable(vec) ? 0 : 1;
			}
			var count = counts.Sum();
			var isCorner = count == 2 && counts[0] != counts[2];
			var isRoomCorner = false;
			if (isCorner)
			{
				for (var i = 0; i < 4; i++)
				{
					var j = (i + 1) % 4;
					if (counts[i] == 0 && counts[j] == 0)
					{
						var vec = pos + GenAdj.CardinalDirectionsAround[i] + GenAdj.CardinalDirectionsAround[j];
						idx = CellIndicesUtility.CellToIndex(vec.x, vec.z, mapWidth);
						if (planned.Contains(idx) == false && grid.Walkable(vec))
						{
							isRoomCorner = true;
							break;
						}
					}
				}
			}
			return new[] { count, isCorner ? 1 : 0, isRoomCorner ? 1 : 0 };
		}

		private static int NeighbourSubScore(IntVec3 pos, PathGrid pathGrid, int mapWidth, HashSet<int> planned)
		{
			var result = 0;
			for (var i = 0; i < 4; i++)
			{
				var vec = pos + GenAdj.CardinalDirectionsAround[i];
				var subInfo = NeighbourCount(vec, pathGrid, planned, mapWidth);
				if (subInfo[0] != -1)
					result += 4 - subInfo[0];
			}
			return result;
		}

		public static int NeighbourScore(IntVec3 pos, PathGrid pathGrid, int mapWidth, HashSet<int> planned)
		{
			var neighbourCountInfo = NeighbourCount(pos, pathGrid, planned, mapWidth);
			var blockedCount = neighbourCountInfo[0];
			var IsCorner = neighbourCountInfo[1] == 1;
			var IsRoomCorner = neighbourCountInfo[2] == 1;

			// full enclosed or pos itself unwalkable
			if (blockedCount == -1 || blockedCount == 4)
				return -100;

			// end of tunnel
			if (blockedCount == 3)
				return 11;

			var neighbourScore = NeighbourSubScore(pos, pathGrid, mapWidth, planned);

			// safe corners (1)
			if (blockedCount == 2 && IsCorner)
			{
				if (neighbourScore == 6)
					return 10;
				if (neighbourScore == 7)
					return 9;
				if (neighbourScore == 5 && IsRoomCorner)
					return 8;
				if (neighbourScore == 4 && IsRoomCorner)
					return 7;
			}

			// perimeter
			if (blockedCount == 1)
			{
				if (neighbourScore == 8) // between two corners
					return 6;
				if (neighbourScore == 10) // at the wall
					return 5;
				if (neighbourScore == 11) // at the wall but close to negative corner
					return 4;
				if (neighbourScore == 9) // one corner
					return 3;
				if (neighbourScore == 12) // at the end of a free standing block
					return 2;
			}

			// safe corners (2)
			if (blockedCount == 2 && IsCorner)
			{
				if (neighbourScore == 5)
					return 1;
			}

			// center
			if (blockedCount == 0)
				return 0;

			// all the unsafe positions - we should never get here if we re-iterate
			return -neighbourScore;
		}

		public static IEnumerable<IntVec3> AllCells(this LocalTargetInfo item)
		{
			if (item.HasThing)
			{
				var thing = item.Thing;
				var size = thing.def.size;
				if (size.x + size.z == 1)
					yield return thing.Position;
				else
					foreach (var cell in thing.OccupiedRect().Cells)
						yield return cell;
				yield break;
			}
			yield return item.Cell;
		}

		public static IEnumerable<IntVec3> AllCells(this Thing thing)
		{
			var size = thing.def.size;
			if (size.x + size.z == 1)
				yield return thing.Position;
			else
				foreach (var cell in thing.OccupiedRect().Cells)
					yield return cell;
		}

		public static void Do<T>(this IEnumerable<T> sequence, Action<T> action)
		{
			if (sequence == null) return;
			var enumerator = sequence.GetEnumerator();
			while (enumerator.MoveNext()) action(enumerator.Current);
		}

		public static void DoIf<T>(this IEnumerable<T> sequence, Func<T, bool> condition, Action<T> action)
		{
			sequence.Where(condition).Do(action);
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

		public static int IsEnclosed(Map map, int maxCount, IntVec3 pos, IntVec3 direction)
		{
			var pathGrid = map.pathGrid;
			var count = 0;
			(new FloodFiller(map)).FloodFill(pos + direction, cell =>
			{
				return cell != pos && pathGrid.Walkable(cell);
			}, (cell, len) =>
			{
				count++;
				return count >= maxCount;
			});
			return count;
		}

		public static void DraftWithSound(List<Colonist> colonists, bool draftStatus)
		{
			var gotDrafted = false;
			var gotUndrafted = false;
			colonists.DoIf(colonist => colonist.pawn.Drafted == false,
				colonist =>
				{
					var oldStatus = SetDraftStatus(colonist.pawn, draftStatus);
					if (oldStatus != draftStatus)
					{
						if (draftStatus)
							gotDrafted = true;
						else
							gotUndrafted = true;
					}
				});
			if (gotDrafted)
				SoundDefOf.DraftOn.PlayOneShotOnCamera(null);
			if (gotUndrafted)
				SoundDefOf.DraftOff.PlayOneShotOnCamera(null);
		}

		public static void CancelDrafting(List<Colonist> colonists)
		{
			var gotDrafted = false;
			var gotUndrafted = false;
			colonists.Do(colonist =>
			{
				var newDraftStatus = SetDraftStatus(colonist.pawn, colonist.originalDraftStatus);
				if (colonist.originalDraftStatus && !newDraftStatus)
					gotDrafted = true;
				if (colonist.originalDraftStatus == false && newDraftStatus)
					gotUndrafted = true;
				colonist.pawn.mindState.priorityWork.Clear();
				if (colonist.pawn.jobs.curJob != null && colonist.pawn.jobs.IsCurrentJobPlayerInterruptible())
				{
					colonist.pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
				}
			});
			if (gotDrafted)
				SoundDefOf.DraftOn.PlayOneShotOnCamera(null);
			if (gotUndrafted)
				SoundDefOf.DraftOff.PlayOneShotOnCamera(null);
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

		public static void DrawForceIcon(Vector3 pos)
		{
			// for strong visual debugging
			// DebugPosition(pos, new Color(1f, 1f, 0f, 0.3f));

			pos += new Vector3(0.75f, Altitudes.AltitudeFor(AltitudeLayer.MoteOverhead), 0.75f);
			var a = 0.08f * (GenTicks.TicksAbs + 13 * pos.x + 7 * pos.z);
			var rot = Quaternion.Euler(0f, Mathf.Sin(a) * 10f, 0f);
			DrawScaledMesh(MeshPool.plane10, forceIconMaterial, pos, rot, 0.5f, 0.5f);
		}

		public static void DebugPosition(Vector3 pos, Color color)
		{
			pos.y = Altitudes.AltitudeFor(AltitudeLayer.Pawn - 1);
			var material = SolidColorMaterials.SimpleSolidColorMaterial(color);
			DrawScaledMesh(MeshPool.plane10, material, pos + new Vector3(0.5f, 0f, 0.5f), Quaternion.identity, 1.0f, 1.0f);
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
			if (Mathf.Abs(A.x - B.x) >= 0.01f || Mathf.Abs(A.z - B.z) >= 0.01f)
			{
				var pos = (A + B) / 2f;
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

		public static IEnumerable<Colonist> OrderColonistsAlongLine(IEnumerable<Colonist> colonists, Vector3 lineStart, Vector3 lineEnd)
		{
			var vector = lineEnd - lineStart;
			vector.y = 0;
			var rotation = Quaternion.FromToRotation(vector, Vector3.right);
			return colonists.OrderBy(colonist =>
			{
				var vec = rotation * colonist.pawn.DrawPos;
				return vec.x * 1000 + vec.z;
			});
		}

		public static void Note(this Listing_Standard listing, string name)
		{
			if (name.CanTranslate())
			{
				Text.Font = GameFont.Tiny;
				listing.ColumnWidth -= 34;
				GUI.color = Color.white;
				listing.Label(name.Translate());
				listing.ColumnWidth += 34;
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