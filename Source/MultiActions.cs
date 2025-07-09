﻿using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace AchtungMod
{
	public class MultiActions
	{
		public Vector3 clickPos;
		public readonly List<Pawn> allPawns;
		readonly List<MultiAction> allActions;
		readonly bool everyoneHasGoto = false;
		readonly int totalColonistsInvolved = 0;
		int gotoActionCount = 0;

		public MultiActions(IEnumerable<Colonist> colonists, Vector3 clickPos)
		{
			this.clickPos = clickPos;
			allPawns = colonists.Select(colonist => colonist.pawn).ToList();
			allActions = [];
			totalColonistsInvolved = colonists.Count();
			colonists.Do(AddColonist);
			if (gotoActionCount == totalColonistsInvolved)
				if (colonists.Any(colonist => colonist.pawn.Drafted))
					everyoneHasGoto = true;
		}

		public bool EveryoneHasGoto => everyoneHasGoto;

		public void AddColonist(Colonist colonist)
		{
			var selectedPawns = Find.MapUI.selector.SelectedPawns;

			var existingLabels = new HashSet<string>();
			FloatMenuMakerMap.GetOptions([colonist.pawn], clickPos, out _).Do(option =>
			{
				AddMultiAction(colonist, colonist.pawn.Drafted, option);
				_ = existingLabels.Add(option.Label);
			});

			var draftState = colonist.pawn.Drafted;
			_ = Tools.SetDraftStatus(colonist.pawn, !draftState);
			FloatMenuMakerMap.GetOptions([colonist.pawn], clickPos, out _).Do(option =>
			{
				if (existingLabels.Contains(option.Label) == false)
					AddMultiAction(colonist, !draftState, option);
			});
			_ = Tools.SetDraftStatus(colonist.pawn, draftState);
		}

		public void AddMultiAction(Colonist colonist, bool draftMode, FloatMenuOption option)
		{
			var action = new MultiAction(colonist, draftMode, option);
			if (Tools.IsGoHereOption(option) == false)
				allActions.Add(action);
			else
				gotoActionCount++;
		}

		public int Count(bool onlyActive)
		{
			if (onlyActive)
				return allActions.Count(action => action.option.Disabled == false);
			return allActions.Count();
		}

		public IEnumerable<string> GetKeys()
		{
			return allActions.Select(action => action.Key).Distinct();
		}

		public IEnumerable<MultiAction> ActionsForKey(string key)
		{
			var actionsForKey = allActions.Where(action => action.Key == key).ToList();
			return actionsForKey.Select(actionForKey =>
			{
				var colonist = actionForKey.colonist;
				return actionsForKey.Where(action => action.colonist == colonist)
					.OrderBy(action => action.IsForced() ? 2 : 1).First();
			});
		}

		public static IEnumerable<Colonist> ColonistsForActions(IEnumerable<MultiAction> subActions)
		{
			return subActions.Select(action => action.colonist).Distinct();
		}

		private T AllEqual<T>(IEnumerable<FloatMenuOption> options, Func<FloatMenuOption, FloatMenuOption, bool> compareFunc, Func<FloatMenuOption, T> valueFunc, T defaultValue = default)
		{
			if (options.Count() == 0)
				return defaultValue;
			var firstOption = options.First();
			var result = valueFunc(options.First());
			if (options.All(option => compareFunc(firstOption, option)) == false)
				result = defaultValue;
			return result;
		}

		private T AllEqual<T>(IEnumerable<FloatMenuOption> options, Func<FloatMenuOption, T> valueFunc, T defaultValue = default) where T : IComparable
		{
			if (options.Count() == 0)
				return defaultValue;
			var result = valueFunc(options.First());
			if (options.All(option => valueFunc(option)?.CompareTo(result) == 0) == false)
				result = defaultValue;
			return result;
		}

		private T? AllEqual<T>(IEnumerable<FloatMenuOption> options, Func<FloatMenuOption, T?> eval) where T : struct, IComparable
		{
			if (options.Count() == 0)
				return null;
			var result = eval(options.First());
			if (options.All(option =>
			{
				var valueToCompare = eval(option);
				var h1 = valueToCompare.HasValue;
				var h2 = result.HasValue;
				if (h1 == false && h2 == false)
					return true;
				if (h1 == false || h2 == false)
					return false;
				return valueToCompare.Value.CompareTo(result.Value) == 0;
			}) == false)
				result = null;
			return result;
		}

		static readonly FloatMenuSizeMode noSizeMode = (FloatMenuSizeMode)999;
		private static FloatMenuSizeMode AllEqualFloatMenuSizeMode(IEnumerable<FloatMenuOption> options, Func<FloatMenuOption, FloatMenuSizeMode> eval)
		{
			if (options.Count() == 0)
				return noSizeMode;
			var result = eval(options.First());
			if (options.All(option => eval(option) == result) == false)
				result = noSizeMode;
			return result;
		}

		public FloatMenuOption GetOption(string title, IEnumerable<MultiAction> multiActions)
		{
			var pawns = multiActions.Select(ma => ma.colonist.pawn).ToList();
			var actions = multiActions.ToList();
			var priority = actions.Max(a => a.option.Priority);
			var orderInPriority = actions.Max(a => a.option.orderInPriority);

			var options = actions.Select(action => action.option);
			var autoTakeable = AllEqual(options, o => o.autoTakeable);
			var autoTakeablePriority = AllEqual(options, o => o.autoTakeablePriority);
			var mouseoverGuiAction = AllEqual(options, (o1, o2) => o1.mouseoverGuiAction == o2.mouseoverGuiAction, o => o.mouseoverGuiAction);
			var revalidateClickTarget = AllEqual(options, (o1, o2) => o1.revalidateClickTarget == o2.revalidateClickTarget, o => o.revalidateClickTarget);
			var extraPartWidth = AllEqual(options, o => o.extraPartWidth);
			var extraPartOnGUI = AllEqual(options, (o1, o2) => o1.extraPartOnGUI?.Method == o2.extraPartOnGUI?.Method, o => o.extraPartOnGUI);
			var revalidateWorldClickTarget = AllEqual(options, (o1, o2) => o1.revalidateWorldClickTarget == o2.revalidateWorldClickTarget, o => o.revalidateWorldClickTarget);
			var tutorTag = AllEqual(options, o => o.tutorTag);
			var thingStyle = AllEqual(options, (o1, o2) => o1.thingStyle == o2.thingStyle, o => o.thingStyle);
			var forceBasicStyle = AllEqual(options, o => o.forceBasicStyle);
			var tooltip = AllEqual(options, (o1, o2) => o1.tooltip?.text == o2.tooltip?.text, o => o.tooltip);
			var extraPartRightJustified = AllEqual(options, o => o.extraPartRightJustified);
			var graphicIndexOverride = AllEqual(options, o => o.graphicIndexOverride);
			var drawPlaceHolderIcon = AllEqual(options, o => o.drawPlaceHolderIcon);
			var playSelectionSound = AllEqual(options, o => o.playSelectionSound);
			var shownItem = AllEqual(options, (o1, o2) => o1.shownItem == o2.shownItem, o => o.shownItem);
			var iconThing = AllEqual(options, (o1, o2) => o1.iconThing == o2.iconThing, o => o.iconThing);
			var iconTex = AllEqual(options, (o1, o2) => o1.iconTex == o2.iconTex, o => o.iconTex);
			var iconTexCoords = AllEqual(options, (o1, o2) => o1.iconTexCoords == o2.iconTexCoords, o => o.iconTexCoords);
			var iconJustification = AllEqual(options, o => o.iconJustification);
			var iconColor = AllEqual(options, (o1, o2) => o1.iconColor == o2.iconColor, o => o.iconColor);
			var forceThingColor = AllEqual(options, (o1, o2) => o1.forceThingColor == o2.forceThingColor, o => o.forceThingColor);

			var option = new FloatMenuOption(
				title,
				() => actions.Do(multiAction => multiAction.GetAction()()),
				iconThing,
				iconColor,
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
				autoTakeable = autoTakeable,
				autoTakeablePriority = autoTakeablePriority,
				tutorTag = tutorTag,
				thingStyle = thingStyle,
				forceBasicStyle = forceBasicStyle,
				tooltip = tooltip,
				extraPartRightJustified = extraPartRightJustified,
				graphicIndexOverride = graphicIndexOverride,
				drawPlaceHolderIcon = drawPlaceHolderIcon,
				shownItem = shownItem,
				iconTex = iconTex,
				iconTexCoords = iconTexCoords,
				iconJustification = iconJustification,
				forceThingColor = forceThingColor
			};

			var hasForcedOptions = options.Any(o => o is ForcedFloatMenuOption);
			if (hasForcedOptions)
				option = new ForcedMultiFloatMenuOption(pawns, options.ToList(), extraPartOnGUI, title);

			option.action = () => actions.Do(multiAction => multiAction.GetAction()());
			option.Disabled = actions.All(a => a.option.Disabled);
			var sizeMode = AllEqualFloatMenuSizeMode(options, o => o.sizeMode);
			if (sizeMode != noSizeMode)
				option.sizeMode = sizeMode;

			return option;
		}

		public List<FloatMenuOption> GetAutoTakeableActions()
		{
			var options = new List<FloatMenuOption>();
			var keys = GetKeys();
			if (keys.Any(key => ActionsForKey(key).Any(action => action.option.Disabled == false && action.option.autoTakeable == false)))
				return options;
			keys.Do(key =>
			{
				var subActions = ActionsForKey(key).ToList();
				var allAutoTakeable = subActions.All(action => action.option.autoTakeable);
				if (allAutoTakeable)
				{
					var colonists = ColonistsForActions(subActions);
					var title = subActions.First().EnhancedLabel(totalColonistsInvolved > 1 ? colonists : null);
					var option = GetOption(title, subActions);
					options.Add(option);
				}
			});
			options.SortBy(option => -option.autoTakeablePriority);
			return options;
		}

		public Window GetWindow()
		{
			var options = new List<FloatMenuOption>();
			GetKeys().Do(key =>
			{
				var subActions = ActionsForKey(key).ToList();
				var colonists = ColonistsForActions(subActions);
				var title = subActions.First().EnhancedLabel(totalColonistsInvolved > 1 ? colonists : null);
				var option = GetOption(title, subActions);
				options.Add(option);
			});

			return new FloatMenu(options) { givesColonistOrders = true };
		}
	}
}