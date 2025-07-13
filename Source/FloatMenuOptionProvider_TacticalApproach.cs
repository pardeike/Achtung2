using System.Linq;
using RimWorld;
using Verse;

namespace AchtungMod;

public class FloatMenuOptionProvider_TacticalApproach : FloatMenuOptionProvider
{
	public override bool Drafted => true;
	public override bool Undrafted => false;
	public override bool Multiselect => false;
	public override bool RequiresManipulation => true;

	public override FloatMenuOption GetSingleOption(FloatMenuContext context)
	{
		if (CanUseTacticalWeapon(context.FirstSelectedPawn) == false)
			return null;
		return new("Tactical Approach", () => StartWork(context), MenuOptionPriority.Low);
	}

	static void StartWork(FloatMenuContext context)
	{
		var pawn = context.FirstSelectedPawn;
		var enemy = context.ClickedPawns.FirstOrDefault();
		if (enemy != null)
		{
			var task = new AutoTask_TacticalApproach(pawn, enemy);
			Achtung.autoTasks.Start(task);
		}
	}

	static bool ShieldBlocksRange(Pawn pawn)
	{
		var wornApparel = pawn.apparel.WornApparel;
		for (var i = 0; i < wornApparel.Count; i++)
			if (wornApparel[i].def.IsShieldThatBlocksRanged)
				return true;
		return false;
	}

	static bool CanUseTacticalWeapon(Pawn pawn)
	{
		var equipment = pawn.equipment;
		var primaryDef = equipment.Primary?.def;
		if (primaryDef?.IsRangedWeapon == false) return false;
		if (equipment.PrimaryEq.PrimaryVerb.HarmsHealth() == false) return false;
		if (primaryDef.IsWeaponUsingProjectiles && ShieldBlocksRange(pawn)) return false;
		return true;
	}
}