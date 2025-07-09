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
			FloatMenuOption option,
			Pawn pawn,
			LocalTargetInfo target,
			WorkGiver_Scanner workgiver
		)
		{
			if (option.action == null)
				return new FloatMenuOption(
					option.labelInt,
					option.action,
					option.priorityInt,
					option.mouseoverGuiAction,
					option.revalidateClickTarget,
					option.extraPartWidth,
					option.extraPartOnGUI,
					option.revalidateWorldClickTarget,
					option.playSelectionSound,
					option.orderInPriority
				);

			return new ForcedFloatMenuOption(
				option.labelInt,
				option.action,
				option.priorityInt,
				option.mouseoverGuiAction,
				option.revalidateClickTarget,
				option.extraPartWidth,
				option.extraPartOnGUI,
				option.revalidateWorldClickTarget,
				option.playSelectionSound,
				option.orderInPriority
			)
			{
				forcePawn = pawn,
				forceCell = target.Cell,
				forceWorkgiver = workgiver
			};
		}
	}
}