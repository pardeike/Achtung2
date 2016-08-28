using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using System.Linq;
using UnityEngine;

namespace AchtungMod
{
	public class JobDriver_SowAll : JobDriver_Thoroughly
	{
		public WorkGiver_GrowerSow grower = null;
		public ThingDef plantDef = null;
		public float xpPerTick = 0.154f;

		public override string GetPrefix()
		{
			return "SowZone";
		}

		public override bool CanStart(Pawn pawn, IntVec3 loc)
		{
			base.CanStart(pawn, loc);
			if (pawn.workSettings.GetPriority(WorkTypeDefOf.Growing) == 0) return false;
			IEnumerable<IntVec3> cells = AllWorkAt(loc, pawn);
			return (cells != null && cells.Count() > 0);
		}

		private bool NoSowingHere(IntVec3 cell)
		{
			if (GenPlant.GrowthSeasonNow(cell) == false) return true;

			if (pawn.CanReach(cell, PathEndMode.Touch, pawn.NormalMaxDanger()) == false) return true;

			if (pawn.CanReserve(cell, 1) == false) return true;

			float maxHarvestWork = 550;
			if (cell.GetThingList().Any(t =>
			{
				if (t.def == plantDef) return true;
				if (((t is Blueprint) || (t is Frame)) && (t.Faction == pawn.Faction)) return true;
				if (t.def.BlockPlanting && !(t is Plant)) return true;
				if (t.def.BlockPlanting && t is Plant && t.def.plant.harvestWork >= maxHarvestWork) return true;
				return false;
			})) return true;

			return false;
		}

		public IEnumerable<Plant> PlantsToCutFirstAt(IntVec3 cell)
		{
			return cell.GetThingList().Where(p => p is Plant && p.def != plantDef && p.def.BlockPlanting && p.Destroyed == false).Cast<Plant>();
		}

		// work in zone
		public IEnumerable<IntVec3> SortedWorkInZone(Zone_Growing zone)
		{
			if (zone == null || zone.cells.Count == 0) return null;
			if (zone.CanAcceptSowNow() == false || zone.allowSow == false) return null;
			plantDef = zone.GetPlantDefToGrow();
			if (plantDef == null) return null;

			int growSkills = pawn.skills.GetSkill(SkillDefOf.Growing).level;
			if (plantDef.plant.sowMinSkill > 0 && growSkills < plantDef.plant.sowMinSkill) return null;

			return zone.Cells
				 .Where(c => NoSowingHere(c) == false)
				 .OrderBy(c => Math.Abs(c.x - pawn.Position.x) + Math.Abs(c.z - pawn.Position.z));
		}

		// work in grower
		public IEnumerable<IntVec3> SortedWorkInGrower(Building building)
		{
			IPlantToGrowSettable grower = building as IPlantToGrowSettable;
			if (building == null || grower == null) return null;

			if (grower.CanAcceptSowNow() == false) return null;
			plantDef = grower.GetPlantDefToGrow();
			if (plantDef == null) return null;

			int growSkills = pawn.skills.GetSkill(SkillDefOf.Growing).level;
			if (plantDef.plant.sowMinSkill > 0 && growSkills < plantDef.plant.sowMinSkill) return null;

			return building.OccupiedRect().Cells
				 .Where(c => NoSowingHere(c) == false)
				 .OrderBy(c => Math.Abs(c.x - pawn.Position.x) + Math.Abs(c.z - pawn.Position.z));
		}

		// work in zone/room
		public IEnumerable<IntVec3> AllWorkAt(IntVec3 pos, Pawn pawn)
		{
			if (pawn.skills == null) return null;

			Room room = RoomQuery.RoomAt(pos);
			if (room != null && room.IsHuge == false)
			{
				List<Building> plantGrowers = Find.ListerBuildings.allBuildingsColonist
					.Where(b => b.GetRoom() == room && b is IPlantToGrowSettable && b.Faction == pawn.Faction)
					.ToList();

				List<IntVec3> cells = room.Cells
					.OrderBy(c => Math.Abs(c.x - pawn.Position.x) + Math.Abs(c.z - pawn.Position.z))
					.ToList();

				return cells.SelectMany(c =>
					{
						Zone_Growing roomZone = Find.ZoneManager.ZoneAt(c) as Zone_Growing;
						if (roomZone != null)
						{
							IEnumerable<IntVec3> workCells = SortedWorkInZone(roomZone);
							if (workCells != null) return workCells;
						}

						Building plantGrower = plantGrowers.FirstOrDefault(g => g.OccupiedRect().Contains(c));
						if (plantGrower != null)
						{
							IEnumerable<IntVec3> workCells = SortedWorkInGrower(plantGrower);
							if (workCells != null) return workCells;
						}

						return new List<IntVec3>();
					})
					.Distinct();
			}

			Zone_Growing outdoorZone = Find.ZoneManager.ZoneAt(pos) as Zone_Growing;
			return SortedWorkInZone(outdoorZone);
		}

		public override TargetInfo FindNextWorkItem()
		{
			IEnumerable<IntVec3> cells = AllWorkAt(TargetA.Cell, pawn);
			Controller.SetDebugPositions(cells);
			if (cells == null || cells.Count() == 0) return null;
			currentWorkCount = cells.Count();
			if (totalWorkCount < currentWorkCount) totalWorkCount = currentWorkCount;
			return cells.FirstOrDefault();
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
					if ((pawn.RaceProps.Humanlike && plant.def.plant.harvestFailable) && (Rand.Value < pawn.GetStatValue(StatDefOf.HarvestFailChance, true)))
					{
						Vector3 loc = (Vector3)((pawn.DrawPos + plant.DrawPos) / 2f);
						MoteThrower.ThrowText(loc, "HarvestFailed".Translate(), 220);
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
							GenPlace.TryPlaceThing(t, pawn.Position, ThingPlaceMode.Near, null);
							pawn.records.Increment(RecordDefOf.PlantsHarvested);
						}
					}
				}
				plant.PlantCollected();
				return true;
			}
			return false;
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
				currentItem = new TargetInfo(GenSpawn.Spawn(plantDef, currentItem.Cell));
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
				Find.MapDrawer.MapMeshDirty(plant.Position, MapMeshFlag.Things);
				pawn.records.Increment(RecordDefOf.PlantsSown);
				return true;
			}

			return false;
		}

		public override string GetReport()
		{
			Zone_Growing zone = Find.ZoneManager.ZoneAt(TargetA.Cell) as Zone_Growing;
			string name = plantDef == null ? "" : " " + plantDef.label;
			return (GetPrefix() + "Report").Translate(new object[] { name, Math.Floor(Progress() * 100f) + "%" });
		}
	}
}