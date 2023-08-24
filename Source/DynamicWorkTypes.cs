using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace AchtungMod
{
	public class DynamicWorkTypes
	{
		static void Reload<T>() where T : Def
		{
			DefDatabase<T>.ClearCachedData();
			DefDatabase<T>.ResolveAllReferences(false, true);
		}

		static void UpdatePawns()
		{
			Find.Maps.Do(map => map.mapPawns.AllPawnsSpawned.DoIf(p => p.workSettings != null, p =>
			{
				p.workSettings.priorities = null;
				p.workSettings.workGiversDirty = true;
				p.workSettings.EnableAndInitialize();
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

			var controller = AccessTools.TypeByName("WorkTab.Controller");
			if (controller != null)
			{
				var columns = new List<PawnColumnDef>(workTable.columns);
				Traverse.Create(controller).Field("allColumns").SetValue(columns);
			}
		}

		public static WorkTypeDef AddWorkTypeDef(WorkTypeDef def, WorkGiverDef workgiver)
		{
			DefDatabase<WorkTypeDef>.Add(def);

			var saved = workgiver.workType;
			workgiver.workType = def;

			Reload<WorkTypeDef>();
			Reload<WorkGiverDef>();

			var workerClass = AccessTools.TypeByName("WorkTab.PawnColumnWorker_WorkType") ?? typeof(PawnColumnWorker_WorkPriority);
			var pawnColumnDefType = AccessTools.TypeByName("WorkTab.PawnColumnDef_WorkGiver") ?? typeof(PawnColumnDef);

			var columnDef = (PawnColumnDef)Activator.CreateInstance(pawnColumnDefType);
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
			UpdatePawns();

			return saved;
		}

		public static void RemoveWorkTypeDef(WorkTypeDef def, WorkTypeDef savedDef, WorkGiverDef workGiver)
		{
			var oldWorkGiver = workGiver.workType;
			workGiver.workType = savedDef;
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
			UpdatePawns();
		}
	}
}
