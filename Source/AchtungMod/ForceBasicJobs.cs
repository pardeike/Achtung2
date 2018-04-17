using System;
using RimWorld;
using Verse;

namespace AchtungMod
{
	public class Forced_BuildRoof : ForcedJob
	{
		internal override Type GetWorkGiverType() { return typeof(WorkGiver_BuildRoof); }
	}

	public class Forced_ClearSnow : ForcedJob
	{
		internal override Type GetWorkGiverType() { return typeof(WorkGiver_ClearSnow); }
	}

	public class Forced_ConstructRemoveFloor : ForcedJob
	{
		internal override Type GetWorkGiverType() { return typeof(WorkGiver_ConstructRemoveFloor); }
	}

	public class Forced_ConstructSmoothFloor : ForcedJob
	{
		internal override Type GetWorkGiverType() { return typeof(WorkGiver_ConstructSmoothFloor); }
	}

	public class Forced_Deconstruct : ForcedJob
	{
		internal override Type GetWorkGiverType() { return typeof(WorkGiver_Deconstruct); }
	}

	public class Forced_FixBrokenDownBuilding : ForcedJob
	{
		internal override Type GetWorkGiverType() { return typeof(WorkGiver_FixBrokenDownBuilding); }
	}

	public class Forced_GrowerHarvest : ForcedJob
	{
		internal override Type GetWorkGiverType() { return typeof(WorkGiver_GrowerHarvest); }
	}

	public class Forced_GrowerSow : ForcedJob
	{
		internal override Type GetWorkGiverType() { return typeof(WorkGiver_GrowerSow); }

		// we need to restrict sowing to zones or else cells beyond expected will be accepted
		//
		internal override bool HasJobOnCell(WorkGiver_Scanner workGiver, Pawn pawn, IntVec3 cell)
		{
			var zone = pawn.Map.zoneManager.ZoneAt(cell) as Zone_Growing;
			if (zone == null) return false;
			return base.HasJobOnCell(workGiver, pawn, cell);
		}
	}

	public class Forced_HaulGeneral : ForcedJob
	{
		internal override Type GetWorkGiverType() { return typeof(WorkGiver_HaulGeneral); }
	}

	public class Forced_Miner : ForcedJob
	{
		internal override Type GetWorkGiverType() { return typeof(WorkGiver_Miner); }
	}

	public class Forced_PlantsCut : ForcedJob
	{
		internal override Type GetWorkGiverType() { return typeof(WorkGiver_PlantsCut); }
	}

	public class Forced_Refuel : ForcedJob
	{
		internal override Type GetWorkGiverType() { return typeof(WorkGiver_Refuel); }
	}

	public class Forced_RemoveRoof : ForcedJob
	{
		internal override Type GetWorkGiverType() { return typeof(WorkGiver_RemoveRoof); }
	}

	public class Forced_Repair : ForcedJob
	{
		internal override Type GetWorkGiverType() { return typeof(WorkGiver_Repair); }
	}

	public class Forced_Uninstall : ForcedJob
	{
		internal override Type GetWorkGiverType() { return typeof(WorkGiver_Uninstall); }
	}
}