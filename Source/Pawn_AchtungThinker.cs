using Verse;
using Verse.AI;

namespace AchtungMod
{
	// a way to store an extra property on a pawn
	// we subclass Pawn_Thinker and hope for nobody doing the same thing
	//
	public class Pawn_AchtungThinker : Pawn_Thinker
	{
		public ForcedJobs forcedJobs = new ForcedJobs();

		public Pawn_AchtungThinker(Pawn pawn) : base(pawn) { }
	}
}
