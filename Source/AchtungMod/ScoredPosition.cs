using Verse;

namespace AchtungMod
{
	public class ScoredPosition
	{
		public IntVec3 v;
		public float score;

		public ScoredPosition(IntVec3 v)
		{
			this.v = v;
			this.score = 100f;
		}

		public ScoredPosition(IntVec3 v, float score = 0f)
		{
			this.v = v;
			this.score = score;
		}

		public ScoredPosition Add(float s, float factor = 1f)
		{
			score += s * factor;
			return this;
		}
	}
}