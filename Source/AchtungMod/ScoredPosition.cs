using System;
using Verse;

namespace AchtungMod
{
	public class ScoredPosition : IComparable
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

		public int CompareTo(object obj)
		{
			if (obj == this) return 0;
			return score.CompareTo((obj as ScoredPosition).score);
		}

		public static Func<ScoredPosition, bool> EqualsToFunction(IntVec3 pos)
		{
			return (sp) => sp.v == pos;
		}
	}
}