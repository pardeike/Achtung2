using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Brrainz;

public class DynamicWorkTypes
{
	internal static readonly Dictionary<WorkTypeDef, WorkTypeDef> disabledWorkTypePairs = [];

	public static void Initialize()
	{
		var harmony = new Harmony("brrainz.lib.dynamicworktypes");

		var method1 = SymbolExtensions.GetMethodInfo((Pawn pawn) => pawn.GetDisabledWorkTypes(default));
		var postfix = SymbolExtensions.GetMethodInfo(() => Pawn_GetDisabledWorkTypes_Postfix(default));
		_ = harmony.Patch(method1, postfix: new HarmonyMethod(postfix) { priority = Priority.Last });

		var method2 = SymbolExtensions.GetMethodInfo((Pawn pawn) => pawn.Notify_DisabledWorkTypesChanged());
		var prefix = SymbolExtensions.GetMethodInfo(() => Pawn_Notify_DisabledWorkTypesChanged_Prefix(default));
		_ = harmony.Patch(method2, prefix: new HarmonyMethod(prefix) { priority = Priority.First });
	}

	public static List<WorkTypeDef> Pawn_GetDisabledWorkTypes_Postfix(List<WorkTypeDef> input)
	{
		var output = new List<WorkTypeDef>(input);
		foreach (var pair in disabledWorkTypePairs)
			if (input.Contains(pair.Key))
				output.Add(pair.Value);
		return output;
	}

	static void Pawn_Notify_DisabledWorkTypesChanged_Prefix(Pawn __instance)
	{
		if (Scribe.mode != LoadSaveMode.PostLoadInit) return;
		var allDefsCount = DefDatabase<WorkTypeDef>.AllDefsListForReading.Count;
		var priorities = __instance.workSettings?.priorities?.values;
		if (priorities != null)
			while (priorities.Count < allDefsCount)
				priorities.Add(Pawn_WorkSettings.DefaultPriority);
	}

	static void Reload<T>() where T : Def
	{
		if (typeof(T) == typeof(WorkTypeDef))
			DefDatabase<WorkTypeDef>.AllDefs.Do(def => def.workGiversByPriority = []);

		DefDatabase<T>.ClearCachedData();
		DefDatabase<T>.ResolveAllReferences(false, true);
	}

	static void UpdatePawns(int index, int copyFrom)
	{
		// World.ExposeData installs the definition before world components and pawn
		// priorities are read. DefMap.ExposeData sizes those incoming arrays itself.
		// Traversing or shifting them during loading would inspect incomplete state.
		if (Scribe.mode == LoadSaveMode.LoadingVars || Scribe.mode == LoadSaveMode.ResolvingCrossRefs)
			return;
		// Pawns in caravans, containers and world storage keep the same indexed priorities.
		var pawns = PawnsFinder.All_AliveOrDead.ToList();
		// MapPawns excludes held dead pawns, whose settings are still needed after resurrection.
		var heldPawns = new List<Pawn>();
		foreach (var map in Find.Maps)
		{
			ThingOwnerUtility.GetAllThingsRecursively(map, ThingRequest.ForGroup(ThingRequestGroup.Pawn), heldPawns, allowUnreal: true, alsoGetSpawnedThings: false);
			pawns.AddRange(heldPawns);
		}
		pawns.Distinct().ToList().DoIf(p => p.workSettings?.priorities != null, p =>
		{
			var priorities = p.workSettings.priorities.values;
			if (copyFrom != -1)
			{
				var value = copyFrom < priorities.Count ? priorities[copyFrom] : Pawn_WorkSettings.DefaultPriority;
				while (priorities.Count < index)
					priorities.Add(Pawn_WorkSettings.DefaultPriority);
				priorities.Insert(index, value);
			}
			else if (index >= 0 && index < priorities.Count)
				priorities.RemoveAt(index);
			p.workSettings.workGiversDirty = true;
			p.cachedDisabledWorkTypes = null;
			p.cachedDisabledWorkTypesPermanent = null;
			p.workSettings.CacheWorkGiversInOrder();
			p.Notify_DisabledWorkTypesChanged();
		});
	}

	static void Update(PawnTableDef workTable)
	{
		var moveWorkTypeLabelDown = false;
		for (var i = 0; i < workTable.columns.Count; i++)
		{
			moveWorkTypeLabelDown = !moveWorkTypeLabelDown;
			workTable.columns[i].moveWorkTypeLabelDown = moveWorkTypeLabelDown;
		}

		var controller = AccessTools.TypeByName("WorkTab.Controller");
		if (controller != null)
		{
			var columns = new List<PawnColumnDef>(workTable.columns);
			_ = Traverse.Create(controller).Field("allColumns").SetValue(columns);
		}
	}

	public static WorkTypeDef AddWorkTypeDef(WorkTypeDef def, WorkTypeDef copyFrom, WorkGiverDef workgiver)
	{
		DefDatabase<WorkTypeDef>.Add(def);
		disabledWorkTypePairs[copyFrom] = def;

		var saved = workgiver.workType;
		workgiver.workType = def;

		Reload<WorkTypeDef>();
		Reload<WorkGiverDef>();

		var workerClass = AccessTools.TypeByName("WorkTab.PawnColumnWorker_WorkType") ?? typeof(PawnColumnWorker_WorkPriority);
		var pawnColumnDefType = AccessTools.TypeByName("WorkTab.PawnColumnDef_WorkGiver") ?? typeof(PawnColumnDef);

		var columnDef = (PawnColumnDef)Activator.CreateInstance(pawnColumnDefType);
		_ = Traverse.Create(columnDef).Field("workgiver").SetValue(workgiver);
		columnDef.defName = $"WorkPriority_{def.defName}";
		columnDef.workType = def;
		columnDef.workerClass = workerClass;
		columnDef.sortable = true;
		columnDef.generated = true;
		columnDef.modContentPack = def.modContentPack;
		columnDef.PostLoad();
		DefDatabase<PawnColumnDef>.Add(columnDef);

		var workTable = PawnTableDefOf.Work;
		var prio = columnDef.workType.naturalPriority;
		var idx = workTable.columns.FirstIndexOf(col => col.workType != null && col.workType.naturalPriority < prio);
		workTable.columns.Insert(idx < 0 ? workTable.columns.Count : idx, columnDef);
		Update(workTable);

		Reload<PawnColumnDef>();
		Reload<PawnTableDef>();

		var index = DefDatabase<WorkTypeDef>.defsList.IndexOf(def);
		var indexToCopy = DefDatabase<WorkTypeDef>.defsList.IndexOf(copyFrom);
		UpdatePawns(index, indexToCopy);

		return saved;
	}

	public static void RemoveWorkTypeDef(WorkTypeDef def, WorkTypeDef savedDef, WorkGiverDef workGiver)
	{
		_ = disabledWorkTypePairs.Remove(savedDef);

		var oldWorkGiver = workGiver.workType;
		workGiver.workType = savedDef;
		var index = DefDatabase<WorkTypeDef>.defsList.IndexOf(def);
		DefDatabase<WorkTypeDef>.Remove(def);

		var columnDef = DefDatabase<PawnColumnDef>.AllDefsListForReading.FirstOrDefault(d => d.workType == oldWorkGiver);
		if (columnDef != null)
			DefDatabase<PawnColumnDef>.Remove(columnDef);

		var workTable = PawnTableDefOf.Work;
		_ = workTable.columns.RemoveAll(col => col.workType == oldWorkGiver);
		Update(workTable);

		Reload<WorkTypeDef>();
		Reload<WorkGiverDef>();
		Reload<PawnColumnDef>();
		Reload<PawnTableDef>();
		UpdatePawns(index, -1);
	}
}
