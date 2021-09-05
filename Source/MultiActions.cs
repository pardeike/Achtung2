using RimWorld;
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
		readonly List<MultiAction> allActions;
		int totalColonistsInvolved = 0;

		public MultiActions(IEnumerable<Colonist> colonists, Vector3 clickPos)
		{
			this.clickPos = clickPos;
			allActions = new List<MultiAction>();
			colonists.Do(colonist =>
			{
				AddColonist(colonist);
				totalColonistsInvolved++;
			});
		}

		public void AddColonist(Colonist colonist)
		{
			var existingLabels = new HashSet<string>();
			FloatMenuMakerMap.ChoicesAtFor(clickPos, colonist.pawn).Do(option =>
			{
				AddMultiAction(colonist, colonist.pawn.Drafted, option);
				_ = existingLabels.Add(option.Label);
			});

			var draftState = colonist.pawn.Drafted;
			_ = Tools.SetDraftStatus(colonist.pawn, !draftState, true);
			FloatMenuMakerMap.ChoicesAtFor(clickPos, colonist.pawn).Do(option =>
			{
				if (existingLabels.Contains(option.Label) == false)
					AddMultiAction(colonist, !draftState, option);
			});
			_ = Tools.SetDraftStatus(colonist.pawn, draftState, true);
		}

		public void AddMultiAction(Colonist colonist, bool draftMode, FloatMenuOption option)
		{
			if (Tools.IsGoHereOption(option) == false) allActions.Add(new MultiAction(colonist, draftMode, option));
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

		private T AllEqual<T, S>(IEnumerable<FloatMenuOption> options, Func<FloatMenuOption, S> compareFunc, Func<FloatMenuOption, T> valueFunc, T defaultValue) where T : class where S : class
		{
			if (options.Count() == 0) return defaultValue;
			var valueToCompare = compareFunc(options.First());
			var result = valueFunc(options.First());
			if (options.All(option =>
			{
				var val = compareFunc(option);
				return val == null || val == valueToCompare;
			}) == false)
				result = defaultValue;
			return result;
		}

		private static float AllEqualFloat(IEnumerable<FloatMenuOption> options, Func<FloatMenuOption, float> eval, float defaultValue)
		{
			if (options.Count() == 0) return defaultValue;
			var result = eval(options.First());
			if (options.All(option => eval(option) == result) == false)
				result = defaultValue;
			return result;
		}

		static readonly FloatMenuSizeMode noSizeMode = (FloatMenuSizeMode)999;
		private static FloatMenuSizeMode AllEqualFloatMenuSizeMode(IEnumerable<FloatMenuOption> options, Func<FloatMenuOption, FloatMenuSizeMode> eval)
		{
			if (options.Count() == 0) return noSizeMode;
			var result = eval(options.First());
			if (options.All(option => eval(option) == result) == false)
				result = noSizeMode;
			return result;
		}

		private static Color AllEqualColor(IEnumerable<FloatMenuOption> options, Func<FloatMenuOption, Color> eval, Color defaultValue)
		{
			if (options.Count() == 0) return defaultValue;
			var result = eval(options.First());
			if (!options.All(option => eval(option) == result))
				result = defaultValue;
			return result;
		}

		static readonly Color noColor = new Color();
		public FloatMenuOption GetOption(string title, IEnumerable<MultiAction> multiActions)
		{
			var pawns = multiActions.Select(ma => ma.colonist.pawn).ToList();
			var actions = multiActions.ToList();
			var priority = actions.Max(a => a.option.Priority);

			var options = actions.Select(action => action.option);
			var mouseoverGuiAction = AllEqual(options, o => o.mouseoverGuiAction?.Method, o => o.mouseoverGuiAction, null);
			var revalidateClickTarget = AllEqual(options, o => o.revalidateClickTarget, o => o.revalidateClickTarget, null);
			var extraPartWidth = AllEqualFloat(options, o => o.extraPartWidth, 0f);
			var extraPartOnGUI = AllEqual(options, o => o.extraPartOnGUI?.Method, o => o.extraPartOnGUI, null);
			var revalidateWorldClickTarget = AllEqual(options, o => o.revalidateWorldClickTarget, o => o.revalidateWorldClickTarget, null);

			var option = new FloatMenuOption(title, delegate
			{
				actions.Do(multiAction =>
				{
					var colonistAction = multiAction.GetAction();
					colonistAction();
				});
			}, priority, mouseoverGuiAction, revalidateClickTarget, extraPartWidth, extraPartOnGUI, revalidateWorldClickTarget);

			// support for our special force menu options. we need to call all internal
			// subactions from when the extra buttons calls them
			//
			var forcedOptions = options.OfType<ForcedFloatMenuOption>().ToList();
			if (extraPartOnGUI != null && forcedOptions.Count > 0)
			{
				option = new ForcedMultiFloatMenuOption(pawns, title)
				{
					options = options.OfType<ForcedFloatMenuOption>().ToList(),
				};
				option.extraPartOnGUI = drawRect => ((ForcedMultiFloatMenuOption)option).RenderExtraPartOnGui(drawRect);
			}

			option.action = delegate
			{
				actions.Do(multiAction =>
				{
					var colonistAction = multiAction.GetAction();
					colonistAction();
				});
			};
			option.Disabled = actions.All(a => a.option.Disabled);
			option.mouseoverGuiAction = mouseoverGuiAction;
			option.revalidateClickTarget = revalidateClickTarget;
			option.extraPartWidth = extraPartWidth;
			if (option.extraPartOnGUI == null)
				option.extraPartOnGUI = extraPartOnGUI;
			option.revalidateWorldClickTarget = revalidateWorldClickTarget;

			var itemIcon = AllEqual(options, o => o.itemIcon, o => o.itemIcon, null);
			if (itemIcon != null)
			{
				option.itemIcon = itemIcon;
				option.iconColor = AllEqualColor(options, o => o.iconColor, noColor);
			}

			var sizeMode = AllEqualFloatMenuSizeMode(options, o => o.sizeMode);
			if (sizeMode != noSizeMode)
				option.sizeMode = sizeMode;

			return option;
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
