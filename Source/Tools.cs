using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace AchtungMod;

enum AchtungCursor
{
	Default,
	Position
}

[StaticConstructorOnStartup]
static class Tools
{
	public static readonly Material forceIconMaterial = MaterialPool.MatFrom("ForceIcon", ShaderDatabase.Cutout);
	public static readonly Material markerMaterial = MaterialPool.MatFrom("AchtungMarker", ShaderDatabase.Transparent);
	public static readonly Texture2D dragPosition = LoadTexture("DragPosition");
	public static readonly string goHereLabel = "GoHere".Translate();
	public static WorkTypeDef savedWorkTypeDef = null;

	public static WorkTypeDef RescuingWorkTypeDef => new()
	{
		defName = "Rescuing",
		labelShort = "WorkType_Rescue_Label".Translate(),
		pawnLabel = "WorkType_Rescue_PawnLabel".Translate(),
		gerundLabel = "WorkType_Rescue_GerundLabel".Translate(),
		description = "WorkType_Rescue_Description".Translate(),
		verb = "Rescue",
		naturalPriority = 1310,
		alwaysStartActive = true,
		workTags = WorkTags.Caring | WorkTags.Commoner | WorkTags.AllWork
	};

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
					_version = vparts[0] + "." + vparts[1] + "." + vparts[2];
			}
			return _version;
		}
	}

	static Texture2D LoadTexture(string path)
	{
		var modRootDir = LoadedModManager.GetMod<Achtung>().Content.RootDir;
		var fullPath = Path.Combine(modRootDir, "Textures", $"{path}.png");
		var data = File.ReadAllBytes(fullPath);
		if (data == null || data.Length == 0)
			throw new Exception($"Cannot read texture {fullPath}");
		var tex = new Texture2D(2, 2, TextureFormat.ARGB32, false, true);
		if (tex.LoadImage(data, false) == false)
			throw new Exception($"Cannot create texture {fullPath}");
		tex.Apply(false, false);
		return tex;
	}

	public static void SetCursor(AchtungCursor mode)
	{
		switch (mode)
		{
			case AchtungCursor.Position:
				Cursor.SetCursor(dragPosition, CustomCursor.CursorHotspot, CursorMode.Auto);
				break;

			default:
				if (Prefs.CustomCursorEnabled)
					CustomCursor.Activate();
				else
					CustomCursor.Deactivate();
				break;
		}
	}

	public static string PawnOverBreakLevel(Pawn pawn)
	{
		if (pawn.InMentalState) return "";

		var mb = pawn.mindState.mentalBreaker;
		return Achtung.Settings.breakLevel switch
		{
			BreakLevel.Minor => mb.BreakMinorIsImminent ? "BreakRiskMinor".Translate() : null,
			BreakLevel.Major => mb.BreakMajorIsImminent ? "BreakRiskMajor".Translate() : null,
			BreakLevel.AlmostExtreme => mb.BreakExtremeIsApproaching ? "BreakRiskExtreme".Translate() : null,
			BreakLevel.Extreme => mb.BreakExtremeIsImminent ? "BreakRiskExtreme".Translate() : null,
			_ => null,
		};
	}

	public static bool PawnOverHealthLevel(Pawn pawn)
	{
		return Achtung.Settings.healthLevel switch
		{
			HealthLevel.ShouldBeTendedNow => HealthAIUtility.ShouldBeTendedNowByPlayer(pawn) || HealthAIUtility.ShouldHaveSurgeryDoneNow(pawn),
			HealthLevel.PrefersMedicalRest => HealthAIUtility.ShouldSeekMedicalRest(pawn),
			HealthLevel.NeedsMedicalRest => HealthAIUtility.ShouldSeekMedicalRestUrgent(pawn),
			HealthLevel.InPainShock => pawn.health.InPainShock,
			_ => false,
		};
	}

	public static Vector3 RotateBy(Vector3 offsetFromCenter, int rotation, bool was45)
	{
		var offset = new Vector3(offsetFromCenter.x, offsetFromCenter.y, offsetFromCenter.z);
		if ((Math.Abs(rotation) % 90) != 0)
		{
			if (was45) offset /= Mathf.Sqrt(2);
			else offset *= Mathf.Sqrt(2);
		}
		offset = offset.RotatedBy(rotation);
		return offset;
	}

	public static bool IsModKey(KeyCode code, AchtungModKey key)
	{
		return key switch
		{
			AchtungModKey.Alt => code == KeyCode.LeftAlt || code == KeyCode.RightAlt,
			AchtungModKey.Ctrl => code == KeyCode.LeftControl || code == KeyCode.RightControl,
			AchtungModKey.Shift => code == KeyCode.LeftShift || code == KeyCode.RightShift,
			AchtungModKey.Meta => code == KeyCode.LeftWindows || code == KeyCode.RightWindows || code == KeyCode.LeftCommand || code == KeyCode.RightCommand || code == KeyCode.LeftApple || code == KeyCode.RightApple,
			_ => false,
		};
	}

	public static bool IsModKeyPressed(AchtungModKey key)
	{
		return key switch
		{
			AchtungModKey.Alt => Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt),
			AchtungModKey.Ctrl => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl),
			AchtungModKey.Shift => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift),
			AchtungModKey.Meta => Input.GetKey(KeyCode.LeftWindows) || Input.GetKey(KeyCode.RightWindows) || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand) || Input.GetKey(KeyCode.LeftApple) || Input.GetKey(KeyCode.RightApple),
			_ => false,
		};
	}

	public static bool IsOfType<T>(this WorkGiver workgiver) where T : class => ((workgiver as T) != null);

	public static bool Has45DegreeOffset(List<Colonist> colonists)
	{
		return colonists.All(c1 => colonists.All(c2 =>
		{
			var delta = c1.pawn.Position - c2.pawn.Position;
			return (Math.Abs(delta.x) + Math.Abs(delta.z)) % 2 == 0;
		}));
	}

	public static IEnumerable<IntVec3> TargetCells(this Job job)
	{
		if (job == null) yield break;
		if (job.targetA.cellInt.IsValid) yield return job.targetA.cellInt;
		if (job.targetB.cellInt.IsValid) yield return job.targetB.cellInt;
		if (job.targetC.cellInt.IsValid) yield return job.targetC.cellInt;
	}

	public static IEnumerable<Thing> TargetThings(this Job job)
	{
		if (job == null) yield break;
		if (job.targetA.thingInt != null) yield return job.targetA.thingInt;
		if (job.targetB.thingInt != null) yield return job.targetB.thingInt;
		if (job.targetC.thingInt != null) yield return job.targetC.thingInt;
	}

	public static bool IsFreeTarget(Pawn pawn, ForcedTarget target)
	{
		var otherRervations = pawn.Map.reservationManager.reservations
			.Where(reservation => reservation.claimant != pawn)
			.ToArray();
		if (otherRervations.Length == 0) return true;
		var item = target.item;
		return otherRervations.All(reservation => reservation.target != item);
	}

	public static bool WillBlock(this LocalTargetInfo info)
	{
		if (info.HasThing == false) return false;

		var thing = info.thingInt;
		var thingDef = thing.def;
		if (thing is Blueprint blueprint)
		{
			var def = blueprint.def.entityDefToBuild as ThingDef;
			if (def != null)
				thingDef = def;
		}
		if (thing is Frame frame)
		{
			var def = frame.def.entityDefToBuild as ThingDef;
			if (def != null)
				thingDef = def;
		}
		return thingDef.passability == Traversability.Impassable;
	}

	public static IEnumerable<XY> Expand(this IEnumerable<XY> existing, Map map, int radius)
	{
		if (radius == 0) yield break;

		var visited = new HashSet<XY>(existing);
		var queue = new Queue<XY>(visited);
		var added = new Queue<XY>();
		for (var i = 1; i <= radius && queue.Count > 0; i++)
		{
			while (queue.Count > 0)
			{
				var cell = queue.Dequeue();
				var j = Rand.Range(0, 8);
				for (var k = 0; k < 8; k++)
				{
					var newCell = cell + XY.Adjacent[(j + k) % 8];
					if (visited.Contains(newCell))
						continue;
					if (newCell.InBounds(map) == false)
						continue;
					_ = visited.Add(newCell);
					added.Enqueue(newCell);
					yield return newCell;
				}
			}
			queue = added;
			added = new Queue<XY>();
		}
	}

	public static void Expand(this Map map, Func<Map, IEnumerator<int>> func, int count)
	{
		var total = 0;
		while (total < count)
		{
			var added = 0;
			var i = func(map);
			while (i.MoveNext())
			{
				var n = i.Current;
				if (n < 0) continue;
				added = n;
				break;
			}
			if (added == 0) break;
			total += added;
		}
	}

	public static IEnumerable<XY> AllCells(this Thing thing)
	{
		if (thing == null)
			yield break;

		var size = thing.def.size;
		if (size.x == 1 && size.z == 1)
		{
			yield return thing.Position;
			yield break;
		}

		var rect = GenAdj.OccupiedRect(thing.Position, thing.Rotation, size);
		for (var z = rect.minZ; z <= rect.maxZ; z++)
			for (var x = rect.minX; x <= rect.maxX; x++)
				yield return new XY(x, z);
	}

	public static void Do<T>(this IEnumerable<T> sequence, Action<T> action)
	{
		if (sequence == null) return;
		var enumerator = sequence.GetEnumerator();
		while (enumerator.MoveNext())
			action(enumerator.Current);
	}

	public static void DoIf<T>(this IEnumerable<T> sequence, Func<T, bool> condition, Action<T> action)
		=> sequence.Where(condition).Do(action);

	public static List<Colonist> GetSelectedColonists()
	 => [.. Find.Selector.SelectedObjects.OfType<Pawn>()
			.Where(pawn =>
				pawn.drafter != null
				&& pawn.IsPlayerControlled
				&& pawn.Downed == false
				&& (pawn.jobs?.IsCurrentJobPlayerInterruptible() ?? false))
			.Select(pawn => new Colonist(pawn))];

	public static bool IsGoHereOption(FloatMenuOption option) => option.Label == goHereLabel;

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
			if (colonist.pawn.jobs?.curJob != null && colonist.pawn.jobs.IsCurrentJobPlayerInterruptible())
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
		pawn.drafter ??= new Pawn_DraftController(pawn);
		return pawn.drafter.Drafted;
	}

	public static bool SetDraftStatus(Pawn pawn, bool drafted)
	{
		var previousStatus = GetDraftingStatus(pawn);
		if (previousStatus != drafted)
			pawn.drafter.draftedInt = drafted;
		return previousStatus;
	}

	public static void OrderTo(Pawn pawn, int x, int z)
	{
		var bestCell = new IntVec3(x, 0, z);
		var job = JobMaker.MakeJob(JobDefOf.Goto, bestCell);
		job.playerForced = true;
		job.collideWithPawns = false;
		if (pawn.Map.exitMapGrid.IsExitCell(bestCell))
			job.exitMapOnArrival = true;

		if (pawn.jobs?.IsCurrentJobPlayerInterruptible() ?? false)
			_ = pawn.jobs.TryTakeOrderedJob(job);
	}

	public static void CancelWorkOn(Pawn newWorker, LocalTargetInfo workItem)
	{
		var forcedWork = ForcedWork.Instance;
		newWorker.Map?.mapPawns
			.PawnsInFaction(Faction.OfPlayer)
			.DoIf(pawn => pawn.jobs?.curJob != null && forcedWork.HasForcedJob(pawn) == false, pawn =>
			 {
				 var isForced = pawn.jobs.curJob.playerForced;
				 var hasTarget = pawn.jobs.curJob.AnyTargetIs(workItem);
				 var destination = pawn.jobs.curJob.GetDestination(pawn);
				 if (isForced && hasTarget && destination == workItem)
				 {
					 pawn.ClearReservationsForJob(pawn.CurJob);
					 pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, false);
					 forcedWork.Remove(pawn);
				 }
			 });
	}

	public static void DrawMarker(Vector3 pos)
	{
		pos.y = Altitudes.AltitudeFor(AltitudeLayer.Pawn - 1);
		DrawScaledMesh(MeshPool.plane10, markerMaterial, pos, Quaternion.identity, 1.25f, 1.25f);
	}

	public static void DrawForceIcon(int x, int z)
	{
		var pos = new Vector3(x + 0.75f, Altitudes.AltitudeFor(AltitudeLayer.MoteOverhead), z + 0.75f);
		if (Achtung.Settings.workMarkers == WorkMarkers.Static)
		{
			DrawScaledMesh(MeshPool.plane10, forceIconMaterial, pos, Quaternion.identity, 0.5f, 0.5f);
			return;
		}
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

	public static void Note(this Listing_Standard listing, string name, GameFont font = GameFont.Small)
	{
		if (name.CanTranslate() == false) return;
		Text.Font = font;
		listing.ColumnWidth -= 34;
		GUI.color = Color.white;
		_ = listing.Label(name.Translate());
		listing.ColumnWidth += 34;
		Text.Font = GameFont.Small;
	}

	public static Vector2 LabelDrawPosFor(IntVec3 cell, float worldOffsetZ)
	{
		var drawPos = cell.ToVector3Shifted();
		drawPos.z += worldOffsetZ;
		var vector2 = Find.Camera.WorldToScreenPoint(drawPos) / Prefs.UIScale;
		vector2.y = UI.screenHeight - vector2.y;
		return vector2;
	}

	public static void CheckboxEnhanced(this Listing_Standard listing, string name, ref bool value, string tooltip = null, Action onChange = null)
	{
		var startHeight = listing.CurHeight;

		Text.Font = GameFont.Small;
		GUI.color = Color.white;
		var oldValue = value;
		listing.CheckboxLabeled((name + "Title").Translate(), ref value);
		if (onChange != null && value != oldValue)
			onChange();

		Text.Font = GameFont.Tiny;
		listing.ColumnWidth -= 34;
		GUI.color = Color.gray;
		_ = listing.Label((name + "Explained").Translate());
		listing.ColumnWidth += 34;
		Text.Font = GameFont.Small;

		var rect = listing.GetRect(0);
		rect.height = listing.CurHeight - startHeight;
		rect.y -= rect.height;
		if (Mouse.IsOver(rect))
		{
			Widgets.DrawHighlight(rect);
			if (!tooltip.NullOrEmpty())
				TooltipHandler.TipRegion(rect, tooltip);
		}

		listing.Gap(6);
	}

	public static void SliderLabeled(this Listing_Standard listing, string name, ref int value, int min, int max, Func<int, string> converter, string tooltip = null)
	{
		var startHeight = listing.CurHeight;

		var rect = listing.GetRect(Text.LineHeight + listing.verticalSpacing);

		Text.Font = GameFont.Small;
		GUI.color = Color.white;

		var savedAnchor = Text.Anchor;

		Text.Anchor = TextAnchor.MiddleLeft;
		Widgets.Label(rect, (name + "Title").Translate());

		Text.Anchor = TextAnchor.MiddleRight;
		Widgets.Label(rect, converter(value));

		Text.Anchor = savedAnchor;

		var key = name + "Explained";
		if (key.CanTranslate())
		{
			Text.Font = GameFont.Tiny;
			listing.ColumnWidth -= 34;
			GUI.color = Color.gray;
			_ = listing.Label(key.Translate());
			listing.ColumnWidth += 34;
			Text.Font = GameFont.Small;
		}

		value = (int)listing.Slider(value, min, max);
		rect = listing.GetRect(0);
		rect.height = listing.CurHeight - startHeight;
		rect.y -= rect.height;
		if (Mouse.IsOver(rect))
		{
			Widgets.DrawHighlight(rect);
			if (!tooltip.NullOrEmpty())
				TooltipHandler.TipRegion(rect, tooltip);
		}

		listing.Gap(6);
	}

	public static void ValueLabeled<T>(this Listing_Standard listing, string name, bool useValueForExplain, ref T value, string tooltip = null)
	{
		var startHeight = listing.CurHeight;

		var rect = listing.GetRect(Text.LineHeight + listing.verticalSpacing);

		Text.Font = GameFont.Small;
		GUI.color = Color.white;

		var savedAnchor = Text.Anchor;

		Text.Anchor = TextAnchor.MiddleLeft;
		Widgets.Label(rect, (name + "Title").Translate());

		Text.Anchor = TextAnchor.MiddleRight;
		var valueLabel = typeof(T).Name + "Option" + value.ToString();
		if (typeof(T).IsEnum)
			Widgets.Label(rect, valueLabel.Translate());
		else
			Widgets.Label(rect, value.ToString());

		Text.Anchor = savedAnchor;

		var key = (useValueForExplain ? valueLabel : name) + "Explained";
		if (key.CanTranslate())
		{
			Text.Font = GameFont.Tiny;
			listing.ColumnWidth -= 34;
			GUI.color = Color.gray;
			_ = listing.Label(key.Translate());
			listing.ColumnWidth += 34;
			Text.Font = GameFont.Small;
		}

		rect = listing.GetRect(0);
		rect.height = listing.CurHeight - startHeight;
		rect.y -= rect.height;
		if (Mouse.IsOver(rect))
		{
			Widgets.DrawHighlight(rect);
			if (!tooltip.NullOrEmpty())
				TooltipHandler.TipRegion(rect, tooltip);

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

		listing.Gap(6);
	}

	public static int EnvTicks()
	{
		var n = Environment.TickCount;
		return n >= 0 ? n : -n;
	}
}