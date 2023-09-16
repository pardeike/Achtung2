using System;
using UnityEngine;

namespace AchtungMod
{
	public class TimeSlice
	{
		private readonly float timeSlice;
		private readonly int minIterations;
		private readonly int maxIterations;
		private readonly Action taskAction;

		public TimeSlice(float timeSlice, int minIterations, int maxIterations, Action taskAction)
		{
			this.timeSlice = timeSlice;
			this.minIterations = minIterations;
			this.maxIterations = maxIterations;
			this.taskAction = taskAction;
		}

		public void Execute()
		{
			var endTime = Time.realtimeSinceStartup * 1000.0f + timeSlice;
			var iterations = 0;

			while (iterations < minIterations && iterations < maxIterations)
			{
				var taskStartTime = Time.realtimeSinceStartup * 1000.0f;
				if (taskStartTime >= endTime)
					break;

				taskAction();
				iterations++;

				var taskEndTime = Time.realtimeSinceStartup * 1000.0f;
				endTime = Mathf.Min(endTime, taskStartTime + timeSlice - (taskEndTime - taskStartTime));
			}
		}
	}
}
