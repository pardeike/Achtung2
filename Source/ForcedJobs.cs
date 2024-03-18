using System.Collections.Generic;
using Verse;

namespace AchtungMod
{
	public class ForcedJobs : IExposable
	{
		public List<ForcedJob> jobs = [];
		public int count; // optimization

		public ForcedJobs()
		{
			jobs = [];
			count = 0;
		}

		public void UpdateCount()
		{
			count = jobs.Count;
		}

		public void ExposeData()
		{
			jobs ??= [];
			if (Scribe.mode == LoadSaveMode.Saving)
				_ = jobs.RemoveAll(job => job == null || job.IsEmpty());

			Scribe_Collections.Look(ref jobs, "jobs", LookMode.Deep);
			if (Scribe.mode == LoadSaveMode.PostLoadInit)
				UpdateCount();
		}
	}
}
