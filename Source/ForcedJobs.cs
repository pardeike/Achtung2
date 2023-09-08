using System.Collections.Generic;
using Verse;

namespace AchtungMod
{
	public class ForcedJobs : IExposable
	{
		public List<ForcedJob> jobs = new();
		public int count; // optimization

		public ForcedJobs()
		{
			jobs = new List<ForcedJob>();
			count = 0;
		}

		public void UpdateCount()
		{
			count = jobs.Count;
		}

		public void ExposeData()
		{
			jobs ??= new List<ForcedJob>();
			if (Scribe.mode == LoadSaveMode.Saving)
				_ = jobs.RemoveAll(job => job == null || job.IsEmpty());

			Scribe_Collections.Look(ref jobs, "jobs", LookMode.Deep);
			if (Scribe.mode == LoadSaveMode.PostLoadInit)
				UpdateCount();
		}
	}
}
