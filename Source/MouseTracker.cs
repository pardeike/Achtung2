using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace AchtungMod
{
	public class MouseTracker
	{
		DragState dragging = DragState.stopped;
		enum DragState
		{
			starting,
			dragging,
			stopped
		}

		public static MouseTracker instance;

		public IntVec3 center;
		public Vector2 start;
		public int lastDelta = -1;
		public Dictionary<Pawn, Action<int>> mouseMovedActions = new();
		public Dictionary<Pawn, Action> mouseUpActions = new();

		public static MouseTracker GetInstance()
		{
			instance ??= new MouseTracker();
			return instance;
		}

		public static void StartDragging(Pawn pawn, IntVec3 center, Action<int> mouseMovedCallback, Action mouseUpCallback)
		{
			var tracker = GetInstance();
			if (tracker.dragging == DragState.stopped)
			{
				tracker.center = center;
				tracker.dragging = DragState.starting;
			}
			tracker.mouseMovedActions[pawn] = mouseMovedCallback;
			tracker.mouseUpActions[pawn] = mouseUpCallback;
		}

		public void OnGUI()
		{
			if (dragging == DragState.stopped)
				return;

			var mousePos = UI.MousePositionOnUI;
			if (dragging == DragState.starting)
			{
				start = mousePos;
				lastDelta = -1;
				dragging = DragState.dragging;
			}

			if (Input.GetMouseButton(0) == false)
			{
				dragging = DragState.stopped;
				foreach (var action in mouseUpActions.Values)
					action();
				mouseMovedActions.Clear();
				mouseUpActions.Clear();
				return;

			}

			var delta = Mathf.RoundToInt((mousePos - start).magnitude / UI.CurUICellSize());
			if (delta > 0)
				GenDraw.DrawRadiusRing(center, delta, Color.black);

			if (delta != lastDelta && delta < GenRadial.MaxRadialPatternRadius)
			{
				foreach (var action in mouseMovedActions.Values)
					action(delta);
				lastDelta = delta;
			}
		}
	}
}