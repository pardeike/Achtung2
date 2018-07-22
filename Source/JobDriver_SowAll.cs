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
		public ThingDef currentPlantDef;
		public float xpPerTick = 0.11f;

		public static MethodInfo _ThrowTextMethod;
		public static MethodInfo ThrowTextMethod
		{
			get
			{
				if (_ThrowTextMethod == null)
				{
					var type = Type.GetType("RimWorld.MoteThrower", false, false);
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

		public override EffecterDef GetWorkIcon()
		{
			return EffecterDefOf.Sow;
		}

		public override IEnumerable<LocalTargetInfo> CanStart(Pawn thePawn, Vector3 clickPos)
		{
			base.CanStart(thePawn, clickPos);
			if (thePawn.workSettings.GetPriority(WorkTypeDefOf.Growing) == 0) return null;
			LocalTargetInfo cell = IntVec3.FromVector3(clickPos);
			var cells = AllWorkAt(cell, thePawn, true);
			IEnumerable<LocalTargetInfo> result = (cells != null && cells.Count() > 0) ? new List<LocalTargetInfo> { cell } : null;

			return result;
		}

		private bool CanSowHere(IntVec3 cell, ThingDef def, bool fastMode)
		{
			if (PlantUtility.GrowthSeasonNow(cell, pawn.Map) == false) return false;
			if (PlantUtility.AdjacentSowBlocker(def, cell, pawn.Map) != null) return false;
			if (def.CanEverPlantAt(cell, pawn.Map) == false) return false;
			if (fastMode == false && pawn.CanReach(cell, PathEndMode.Touch, pawn.NormalMaxDanger()) == false) return false;
			if (fastMode == false && pawn.CanReserve(cell, 1) == false) return false;
			float maxHarvestWork = 550;
			var failureCondition = cell.GetThingList(pawn.Map).Any(t =>
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
		public IEnumerable<IntVec3> AllWorkAt(LocalTargetInfo target, Pawn thePawn, bool fastMode = false)
		{
			if (thePawn.skills == null) return null;

			var room = RegionAndRoomQuery.RoomAt(target.Cell, thePawn.Map);
			if (room != null && room.IsHuge == false && room.Group.AnyRoomTouchesMapEdge == false)
			{
				var growerCells = new HashSet<IntVec3>(thePawn.Map.listerBuildings.allBuildingsColonist
					 .Where(b =>
					 {
						 if (b == null || b.GetRoom() != room || b.Faction != thePawn.Faction) return false;

						 var grower = b as IPlantToGrowSettable;
						 if (grower == null) return false;

						 if (grower.CanAcceptSowNow() == false) return false;
						 var def1 = grower.GetPlantDefToGrow();
						 if (def1 == null) return false;

						 var growSkills1 = thePawn.skills.GetSkill(SkillDefOf.Plants).Level;
						 return (def1.plant.sowMinSkill <= 0 || growSkills1 >= def1.plant.sowMinSkill);
					 })
					 .Cast<IPlantToGrowSettable>()
					 .SelectMany(g =>
					 {
						 var def = g.GetPlantDefToGrow();
						 return ((Building)g).OccupiedRect().Cells.Where(c => CanSowHere(c, def, fastMode));
					 }));

				var zoneCells = new HashSet<IntVec3>();
				var roomCells = new HashSet<IntVec3>(room.Cells);
				while (roomCells.Count > 0)
				{
					var cell = roomCells.First();
					var zone = thePawn.Map.zoneManager.ZoneAt(cell) as Zone_Growing;
					if (zone != null && zone.cells.Count > 0)
					{
						if (zone.CanAcceptSowNow() && zone.allowSow)
						{
							var def2 = zone.GetPlantDefToGrow();
							if (def2 != null)
							{
								var growSkills2 = thePawn.skills.GetSkill(SkillDefOf.Plants).Level;
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
					 .OrderBy(c => Math.Abs(c.x - thePawn.Position.x) + Math.Abs(c.z - thePawn.Position.z));
			}

			var outdoorZone = thePawn.Map.zoneManager.ZoneAt(target.Cell) as Zone_Growing;
			if (outdoorZone == null || outdoorZone.cells.Count == 0) return null;
			if (outdoorZone.CanAcceptSowNow() == false || outdoorZone.allowSow == false) return null;
			var def3 = outdoorZone.GetPlantDefToGrow();
			if (def3 == null) return null;

			var growSkills3 = thePawn.skills.GetSkill(SkillDefOf.Plants).Level;
			if (def3.plant.sowMinSkill > 0 && growSkills3 < def3.plant.sowMinSkill) return null;

			return outdoorZone.Cells
				 .Where(c => CanSowHere(c, outdoorZone.GetPlantDefToGrow(), fastMode))
				 .OrderBy(c => Math.Abs(c.x - thePawn.Position.x) + Math.Abs(c.z - thePawn.Position.z));
		}

		public override LocalTargetInfo FindNextWorkItem()
		{
			var cells = AllWorkAt(TargetA, pawn)?.ToList();
			if (cells == null || cells.Count == 0) return null;
			currentWorkCount = cells.Count;
			if (totalWorkCount < currentWorkCount) totalWorkCount = currentWorkCount;
			var c = cells.First();

			var zone = pawn.Map.zoneManager.ZoneAt(c) as Zone_Growing;
			if (zone != null)
			{
				currentPlantDef = zone.GetPlantDefToGrow();
			}
			else
			{
				currentPlantDef = pawn.Map.listerBuildings.allBuildingsColonist
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
			ThrowTextMethod?.Invoke(null, new object[] { loc, txt, 220 });
		}

		public bool Harvest(Plant plant)
		{
			pawn.skills?.Learn(SkillDefOf.Plants, xpPerTick);

			var workSpeed = pawn.GetStatValue(StatDefOf.PlantWorkSpeed, true);
			subCounter += workSpeed;
			if (subCounter >= plant.def.plant.harvestWork)
			{
				subCounter = 0;
				if (plant.def.plant.harvestedThingDef != null)
				{
					if (pawn.RaceProps.Humanlike && plant.def.plant.harvestFailable && Rand.Value > pawn.GetStatValue(StatDefOf.PlantHarvestYield, true))
					{
						var loc = (pawn.DrawPos + plant.DrawPos) / 2f;
						ThrowText(loc, "HarvestFailed".Translate());
					}
					else
					{
						var count = plant.YieldNow();
						if (count > 0)
						{
							var t = ThingMaker.MakeThing(plant.def.plant.harvestedThingDef, null);
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
			var otherPlant = PlantsToCutFirstAt(currentItem.Cell).FirstOrDefault();
			if (otherPlant != null)
			{
				var done = Harvest(otherPlant);
				if (done == false) return false;
			}

			var plant = currentItem.Thing as Plant;
			if (currentItem.Thing == null)
			{
				currentItem = new LocalTargetInfo(GenSpawn.Spawn(currentPlantDef, currentItem.Cell, pawn.Map));
				plant = (Plant)currentItem.Thing;
				plant.Growth = 0f;
				plant.sown = true;
			}

			pawn.skills?.Learn(SkillDefOf.Plants, xpPerTick, false);

			var workSpeed = pawn.GetStatValue(StatDefOf.PlantWorkSpeed, true);
			subCounter += workSpeed;
			if (subCounter >= plant?.def.plant.sowWork)
			{
				subCounter = 0f;
				plant.Growth = 0.05f;
				pawn.Map.mapDrawer.MapMeshDirty(plant.Position, MapMeshFlag.Things);
				pawn.records.Increment(RecordDefOf.PlantsSown);

				return true;
			}

			return false;
		}

		public override void CleanupLastItem()
		{
			if (currentItem == null || currentItem.Thing == null)
				return;

			if (subCounter > 0 && currentItem.Thing.Destroyed == false)
				currentItem.Thing.Destroy(DestroyMode.Vanish);
		}

		public override string GetReport()
		{
			var name = currentPlantDef == null ? "" : " " + currentPlantDef.label;
			return (GetPrefix() + "Report").Translate(name, Math.Floor(Progress() * 100f) + "%");
		}

		protected override IEnumerable<Toil> MakeNewToils()
		{
			var toil = base.MakeNewToils().First();
			toil.PlaySustainerOrSound(() => SoundDefOf.Interact_Sow);
			yield return toil;
		}

		public override bool TryMakePreToilReservations()
		{
			return true;
		}
	}
}