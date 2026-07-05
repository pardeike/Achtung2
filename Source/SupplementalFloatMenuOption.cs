using RimWorld;
using System;
using Verse;

namespace AchtungMod;

public interface IAchtungSupplementalFloatMenuOption
{
}

public class SupplementalFloatMenuOption(string label, Action action, MenuOptionPriority priority)
	: FloatMenuOption(label, action, priority), IAchtungSupplementalFloatMenuOption
{
}
