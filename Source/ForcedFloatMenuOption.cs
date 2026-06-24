using RimWorld;
using RimWorld.Planet;
using System;
using UnityEngine;
using Verse;

namespace AchtungMod;

public class ForcedFloatMenuOption(string label, Action action, MenuOptionPriority priority, Action<Rect> mouseoverGuiAction, Thing revalidateClickTarget, float extraPartWidth, Func<Rect, bool> extraPartOnGUI, WorldObject revalidateWorldClickTarget, bool playSelectionSound, int orderInPriority)
	: FloatMenuOption(label, action, priority, mouseoverGuiAction, revalidateClickTarget, extraPartWidth, extraPartOnGUI, revalidateWorldClickTarget, playSelectionSound, orderInPriority)
{
	public Pawn forcePawn;
	public IntVec3 forceCell;
	public WorkGiver_Scanner forceWorkgiver;

	public static T CopyOptionState<T>(FloatMenuOption option, T result) where T : FloatMenuOption
	{
		result.autoTakeable = option.autoTakeable;
		result.autoTakeablePriority = option.autoTakeablePriority;
		result.targetsDespawned = option.targetsDespawned;
		result.tutorTag = option.tutorTag;
		result.thingStyle = option.thingStyle;
		result.forceBasicStyle = option.forceBasicStyle;
		result.tooltip = option.tooltip;
		result.extraPartRightJustified = option.extraPartRightJustified;
		result.graphicIndexOverride = option.graphicIndexOverride;
		result.drawPlaceHolderIcon = option.drawPlaceHolderIcon;
		result.shownItem = option.shownItem;
		result.iconThing = option.iconThing;
		result.iconTex = option.iconTex;
		result.iconTexCoords = option.iconTexCoords;
		result.iconJustification = option.iconJustification;
		result.iconColor = option.iconColor;
		result.forceThingColor = option.forceThingColor;
		result.isGoto = option.isGoto;
		result.sizeMode = option.sizeMode;
		result.SetSizeMode(option.sizeMode);
		return result;
	}

	public static FloatMenuOption CreateForcedMenuItem(FloatMenuOption option, Pawn pawn, LocalTargetInfo target, WorkGiver_Scanner workgiver)
	{
		if (option.action == null)
			return CopyOptionState(option, new FloatMenuOption(
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
			));

		var forcedOption = new ForcedFloatMenuOption(
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
		return CopyOptionState(option, forcedOption);
	}
}
