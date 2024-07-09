using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Brrainz
{
	[StaticConstructorOnStartup]
	public class DynamicWorkTypes
	{
		internal static readonly Dictionary<WorkTypeDef, WorkTypeDef> disabledWorkTypePairs = [];

		static DynamicWorkTypes()
		{
			var harmony = new Harmony("brrainz.lib.dynamicworktypes");
			var method = SymbolExtensions.GetMethodInfo((Pawn pawn) => pawn.GetDisabledWorkTypes(default));
			var postfix = SymbolExtensions.GetMethodInfo(() => Pawn_GetDisabledWorkTypes_Postfix(default));
			harmony.Patch(method, postfix: new HarmonyMethod(postfix) { priority = Priority.Last });
		}

		public static List<WorkTypeDef> Pawn_GetDisabledWorkTypes_Postfix(List<WorkTypeDef> input)
		{
			var output = new List<WorkTypeDef>(input);
			foreach (var pair in disabledWorkTypePairs)
				if (input.Contains(pair.Key))
					output.Add(pair.Value);
			return output;
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
			Find.Maps.Do(map => map.mapPawns.AllPawnsSpawned.DoIf(p => p.workSettings?.priorities != null, p =>
			{
				if (p.workSettings.EverWork)
				{
					if (copyFrom != -1)
					{
						var value = p.workSettings.priorities.values[copyFrom];
						p.workSettings.priorities.values.Insert(index, value);
					}
					else
						p.workSettings.priorities.values.Remove(index);
				}
				p.workSettings.workGiversDirty = true;
				p.cachedDisabledWorkTypes = null;
				p.cachedDisabledWorkTypesPermanent = null;
				p.workSettings.CacheWorkGiversInOrder();
				p.Notify_DisabledWorkTypesChanged();
			}));
		}

		static void Update(PawnTableDef workTable)
		{
			var moveWorkTypeLabelDown = false;
			for (var i = 0; i < workTable.columns.Count; i++)
			{
				moveWorkTypeLabelDown = !moveWorkTypeLabelDown;
				workTable.columns[i].moveWorkTypeLabelDown = moveWorkTypeLabelDown;
			}
			
			/*  Fluffy.WorkTab compatible */

			var controller = AccessTools.TypeByName("WorkTab.Controller");
			var workTabType = AccessTools.TypeByName("WorkTab.MainTabWindow_WorkTab");
			var workTypeExtensionsType = AccessTools.TypeByName("WorkTab.WorkType_Extensions");
			var workGiverType = AccessTools.TypeByName("PawnColumnDef_WorkGiver");
			if (controller == null || workTabType == null) 
				return;
			
			var columns = new List<PawnColumnDef>(workTable.columns);
			// clear column header tip cache
			foreach (var pawnColumnDefWorker in columns.Select(c => c.Worker).Where(w => w != null))
			{
				Traverse.Create(pawnColumnDefWorker).Field("_headerTip").SetValue(null);
			}
			Traverse.Create(workTypeExtensionsType).Field("workListCache").SetValue(new Dictionary<WorkTypeDef, string>());
			Traverse.Create(workTypeExtensionsType).Field("_workgiversByType").SetValue(new Dictionary<WorkTypeDef, List<WorkGiverDef>>());
			
			// remove all expanded columns(By ctrl+click), this is added by Fluffy.WorkTab
			columns.RemoveAll(col => workGiverType.IsInstanceOfType(col));
			
			// update work tab cached columns
			Traverse.Create(controller).Field("allColumns").SetValue(columns);
			
			// work tab rebuild
			var workTabInstance = AccessTools.Property(workTabType, "Instance").GetValue(null);
			if (workTabInstance != null)
			{
				// AccessTools.Method(workTabType, "RebuildTable").Invoke(workTabInstance, null);
				AccessTools.Method(workTabType, "Notify_ColumnsChanged").Invoke(workTabInstance, null);
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

			var columnDef = (PawnColumnDef)Activator.CreateInstance(typeof(PawnColumnDef));
			Traverse.Create(columnDef).Field("workgiver").SetValue(workgiver);
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
			workTable.columns.Insert(idx, columnDef);
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

			PawnTableDef workTable = PawnTableDefOf.Work;
			workTable.columns.RemoveAll(col => col.workType == oldWorkGiver);
			Update(workTable);

			Reload<WorkTypeDef>();
			Reload<WorkGiverDef>();
			Reload<PawnColumnDef>();
			Reload<PawnTableDef>();
			UpdatePawns(index, -1);
		}
	}
}
