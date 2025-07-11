using Verse;
using Verse.AI;

namespace AchtungMod;

// a way to store an extra property on a pawn
// we subclass Pawn_Thinker and hope for nobody doing the same thing
//
public class Pawn_AchtungThinker(Pawn pawn) : Pawn_Thinker(pawn)
{
	public ForcedJobs forcedJobs = new();
}