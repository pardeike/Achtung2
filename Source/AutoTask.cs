using System;
using System.Collections.Generic;
using Verse;
using Verse.Noise;

namespace AchtungMod;

public interface IAutoTask : IExposable
{
	void Start(Action onComplete);
	void Tick();
	bool Stopping { get; set; }
}

public class AutoTasks : IExposable
{
	List<IAutoTask> tasks = [];

	public void Start(IAutoTask task)
	{
		tasks.Add(task);
		task.Start(() => tasks.Remove(task));
	}

	public void Tick()
	{
		for (var i = 0; i < tasks.Count; i++)
			tasks[i].Tick();
	}

	public void ExposeData()
	{
		Scribe_Collections.Look(ref tasks, "tasks", LookMode.Deep);
		if (Scribe.mode == LoadSaveMode.PostLoadInit)
			StartAll();
	}

	void StartAll()
	{
		foreach (var task in tasks)
			task.Start(() => tasks.Remove(task));
	}
}