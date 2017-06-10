using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using System.Linq;
using UnityEngine;
using System.Reflection;

namespace AchtungMod
{
	public class JobDriver_SowAll : JobDriver_Thoroughly
	{
		public ThingDef currentPlantDef = null;
		public float xpPerTick = 0.154f;

		public static MethodInfo _ThrowTextMethod = null;
		public static MethodInfo ThrowTextMethod
		{
			get
			{
				if (_ThrowTextMethod == null)
				{
					Type type = Type.GetType("RimWorld.MoteThrower", false, false);
					if (type == null) type = Type.GetType("RimWorld.MoteMaker", false, false);
					if (type != null) _ThrowTextMethod = type.GetMethod("ThrowText", BindingFlags.Static | BindingFlags.Public);
				}
				return _ThrowTextMethod;
			}
		}

		public override string GetPrefix()
		{
			return "SowZone";
		}

		public override void ExposeData()
		{
			base.ExposeData();
			// TODO: check if this has to be done somehow
			// Scribe_Values.LookValue<Faction>(ref this.faction, "faction", null, false);
		}

		public override IEnumerable<LocalTargetInfo> CanStart(Pawn pawn, Vector3 clickPos)
		{
			base.CanStart(pawn, clickPos);
			if (pawn.workSettings.GetPriority(WorkTypeDefOf.Growing) == 0) return null;
			LocalTargetInfo cell = IntVec3.FromVector3(clickPos);
			IEnumerable<IntVec3> cells = AllWorkAt(cell, pawn, true);
			IEnumerable<LocalTargetInfo> result = (cells != null && cells.Count() > 0) ? new List<LocalTargetInfo> { cell } : null;

			return result;
		}

		private bool CanSowHere(IntVec3 cell, ThingDef def, bool fastMode)
		{
			if (GenPlant.GrowthSeasonNow(cell, pawn.Map) == false) return false;
			if (fastMode == false && pawn.CanReach(cell, PathEndMode.Touch, pawn.NormalMaxDanger()) == false) return false;
			if (fastMode == false && pawn.CanReserve(cell, 1) == false) return false;
			float maxHarvestWork = 550;
			bool failureCondition = cell.GetThingList(pawn.Map).Any(t =>
			{
				if (t.def == def) return true;
				if (((t is Blueprint) || (t is Frame)) && (t.Faction == pawn.Faction)) return true;
				if (t.def.BlockPlanting && !(t is Plant)) return true;
				if (t.def.BlockPlanting && t is Plant && t.def.plant.harvestWork >= maxHarvestWork) return true;
				return false;
			});
			return failureCondition == false;
		}

		// work in zone/room
		public IEnumerable<IntVec3> AllWorkAt(LocalTargetInfo target, Pawn pawn, bool fastMode = false)
		{
			if (pawn.skills == null) return null;

			Room room = RegionAndRoomQuery.RoomAt(target.Cell, pawn.Map);
			if (room != null && room.IsHuge == false)
			{
				HashSet<IntVec3> growerCells = new HashSet<IntVec3>(Find.VisibleMap.listerBuildings.allBuildingsColonist
					 .Where(b =>
					 {
						 if (b == null || b.GetRoom() != room || b.Faction != pawn.Faction) return false;

						 IPlantToGrowSettable grower = b as IPlantToGrowSettable;
						 if (grower == null) return false;

						 if (grower.CanAcceptSowNow() == false) return false;
						 ThingDef def1 = grower.GetPlantDefToGrow();
						 if (def1 == null) return false;

						 int growSkills1 = pawn.skills.GetSkill(SkillDefOf.Growing).Level;
						 return (def1.plant.sowMinSkill <= 0 || growSkills1 >= def1.plant.sowMinSkill);
					 })
					 .Cast<IPlantToGrowSettable>()
					 .SelectMany(g =>
					 {
						 ThingDef def = g.GetPlantDefToGrow();
						 return ((Building)g).OccupiedRect().Cells.Where(c => CanSowHere(c, def, fastMode));
					 }));

				HashSet<IntVec3> zoneCells = new HashSet<IntVec3>();
				HashSet<IntVec3> roomCells = new HashSet<IntVec3>(room.Cells);
				while (roomCells.Count > 0)
				{
					IntVec3 cell = roomCells.First();
					Zone_Growing zone = Find.VisibleMap.zoneManager.ZoneAt(cell) as Zone_Growing;
					if (zone != null && zone.cells.Count > 0)
					{
						if (zone.CanAcceptSowNow() && zone.allowSow)
						{
							ThingDef def2 = zone.GetPlantDefToGrow();
							if (def2 != null)
							{
								int growSkills2 = pawn.skills.GetSkill(SkillDefOf.Growing).Level;
								if (def2.plant.sowMinSkill <= 0 || growSkills2 >= def2.plant.sowMinSkill)
								{
									zone.cells
										 .Where(c => CanSowHere(c, zone.GetPlantDefToGrow(), fastMode))
										 .Do(c => zoneCells.Add(c));
								}
							}
						}
						zone.cells.DoIf(c => (c != cell), c => roomCells.Remove(c));
					}
					roomCells.Remove(cell);
				}

				return zoneCells.Union(growerCells).Distinct()
					 .OrderBy(c => Math.Abs(c.x - pawn.Position.x) + Math.Abs(c.z - pawn.Position.z));
			}

			Zone_Growing outdoorZone = Find.VisibleMap.zoneManager.ZoneAt(target.Cell) as Zone_Growing;
			if (outdoorZone == null || outdoorZone.cells.Count == 0) return null;
			if (outdoorZone.CanAcceptSowNow() == false || outdoorZone.allowSow == false) return null;
			ThingDef def3 = outdoorZone.GetPlantDefToGrow();
			if (def3 == null) return null;

			int growSkills3 = pawn.skills.GetSkill(SkillDefOf.Growing).Level;
			if (def3.plant.sowMinSkill > 0 && growSkills3 < def3.plant.sowMinSkill) return null;

			return outdoorZone.Cells
				 .Where(c => CanSowHere(c, outdoorZone.GetPlantDefToGrow(), fastMode))
				 .OrderBy(c => Math.Abs(c.x - pawn.Position.x) + Math.Abs(c.z - pawn.Position.z));
		}

		public override LocalTargetInfo FindNextWorkItem()
		{
			IEnumerable<IntVec3> cells = AllWorkAt(TargetA, pawn);
			if (cells == null || cells.Count() == 0) return null;
			currentWorkCount = cells.Count();
			if (totalWorkCount < currentWorkCount) totalWorkCount = currentWorkCount;
			IntVec3 c = cells.First();

			Zone_Growing zone = Find.VisibleMap.zoneManager.ZoneAt(c) as Zone_Growing;
			if (zone != null)
			{
				currentPlantDef = zone.GetPlantDefToGrow();
			}
			else
			{
				currentPlantDef = Find.VisibleMap.listerBuildings.allBuildingsColonist
					 .Where(b => b.OccupiedRect().Contains(c))
					 .Cast<IPlantToGrowSettable>()
					 .Select(b => b.GetPlantDefToGrow())
					 .FirstOrDefault();
			}
			return c;
		}

		// A14 & A15 compatibility
		public void ThrowText(Vector3 loc, string txt)
		{
			if (ThrowTextMethod != null)
			{
				ThrowTextMethod.Invoke(null, new object[] { loc, txt, 220 });
			}
		}

		public bool Harvest(Plant plant)
		{
			if (pawn.skills != null) pawn.skills.Learn(SkillDefOf.Growing, xpPerTick);

			float workSpeed = pawn.GetStatValue(StatDefOf.PlantWorkSpeed, true);
			subCounter += workSpeed;
			if (subCounter >= plant.def.plant.harvestWork)
			{
				subCounter = 0;
				if (plant.def.plant.harvestedThingDef != null)
				{
					if (pawn.RaceProps.Humanlike && plant.def.plant.harvestFailable && Rand.Value > pawn.GetStatValue(StatDefOf.PlantHarvestYield, true))
					{
						Vector3 loc = (Vector3)((pawn.DrawPos + plant.DrawPos) / 2f);
						ThrowText(loc, "HarvestFailed".Translate());
					}
					else
					{
						int count = plant.YieldNow();
						if (count > 0)
						{
							Thing t = ThingMaker.MakeThing(plant.def.plant.harvestedThingDef, null);
							t.stackCount = count;
							if (pawn.Faction != Faction.OfPlayer)
							{
								t.SetForbidden(true, true);
							}
							GenPlace.TryPlaceThing(t, pawn.Position, pawn.Map, ThingPlaceMode.Near, null);
							pawn.records.Increment(RecordDefOf.PlantsHarvested);
						}
					}
				}
				plant.PlantCollected();
				return true;
			}
			return false;
		}

		public IEnumerable<Plant> PlantsToCutFirstAt(IntVec3 cell)
		{
			return cell.GetThingList(pawn.Map).Where(p => p is Plant && p.def != currentPlantDef && p.def.BlockPlanting && p.Destroyed == false).Cast<Plant>();
		}

		public override bool DoWorkToItem()
		{
			Plant otherPlant = PlantsToCutFirstAt(currentItem.Cell).FirstOrDefault();
			if (otherPlant != null)
			{
				bool done = Harvest(otherPlant);
				if (done == false) return false;
			}

			Plant plant = currentItem.Thing as Plant;
			if (currentItem.Thing == null)
			{
				currentItem = new LocalTargetInfo(GenSpawn.Spawn(currentPlantDef, currentItem.Cell, pawn.Map));
				plant = (Plant)currentItem.Thing;
				plant.Growth = 0f;
				plant.sown = true;
			}

			if (pawn.skills != null) pawn.skills.Learn(SkillDefOf.Growing, 0.154f);

			float workSpeed = pawn.GetStatValue(StatDefOf.PlantWorkSpeed, true);
			subCounter += workSpeed;
			if (subCounter >= plant.def.plant.sowWork)
			{
				subCounter = 0f;
				plant.Growth = 0.05f;
				Find.VisibleMap.mapDrawer.MapMeshDirty(plant.Position, MapMeshFlag.Things);
				pawn.records.Increment(RecordDefOf.PlantsSown);
				return true;
			}

			return false;
		}

		public override string GetReport()
		{
			// Zone_Growing zone = Find.VisibleMap.zoneManager.ZoneAt(TargetA.Cell) as Zone_Growing;
			string name = currentPlantDef == null ? "" : " " + currentPlantDef.label;
			return (GetPrefix() + "Report").Translate(new object[] { name, Math.Floor(Progress() * 100f) + "%" });
		}
	}
}
