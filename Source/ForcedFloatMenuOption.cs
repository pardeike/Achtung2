using RimWorld;
using RimWorld.Planet;
using System;
using UnityEngine;
using Verse;

namespace AchtungMod
{
	public class ForcedFloatMenuOption(
		string label,
		Action action,
		MenuOptionPriority priority,
		Action<Rect> mouseoverGuiAction,
		Thing revalidateClickTarget,
		float extraPartWidth,
		Func<Rect, bool> extraPartOnGUI,
		WorldObject revalidateWorldClickTarget,
		bool playSelectionSound,
		int orderInPriority
		) : FloatMenuOption(
		label,
		action,
		priority,
		mouseoverGuiAction,
		revalidateClickTarget,
		extraPartWidth,
		extraPartOnGUI,
		revalidateWorldClickTarget,
		playSelectionSound,
		orderInPriority
		)
	{
		public Pawn forcePawn;
		public IntVec3 forceCell;
		public WorkGiver_Scanner forceWorkgiver;

		public static FloatMenuOption CreateForcedMenuItem(
			string label,
			Action action,
			MenuOptionPriority priority,
			Action<Rect> mouseoverGuiAction,
			Thing revalidateClickTarget,
			float extraPartWidth,
			Func<Rect, bool> extraPartOnGUI,
			WorldObject revalidateWorldClickTarget,
			bool playSelectionSound,
			int orderInPriority,
			Pawn pawn,
			Vector3 clickPos,
			WorkGiver_Scanner workgiver
		)
		{
			if (action == null)
				return new FloatMenuOption(
					label,
					action,
					priority,
					mouseoverGuiAction,
					revalidateClickTarget,
					extraPartWidth,
					extraPartOnGUI,
					revalidateWorldClickTarget,
					playSelectionSound,
					orderInPriority
				);

			return new ForcedFloatMenuOption(
				label,
				action,
				priority,
				mouseoverGuiAction,
				revalidateClickTarget,
				extraPartWidth,
				extraPartOnGUI,
				revalidateWorldClickTarget,
				playSelectionSound,
				orderInPriority
			)
			{
				forcePawn = pawn,
				forceCell = IntVec3.FromVector3(clickPos),
				forceWorkgiver = workgiver
			};
		}
	}
}