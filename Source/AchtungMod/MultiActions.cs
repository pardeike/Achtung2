using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace AchtungMod
{
	public class MultiActions
	{
		public Vector3 clickPos;
		List<MultiAction> allActions;
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
			var forced = Tools.ForceDraft(colonist.pawn, true);
			FloatMenuMakerMap.ChoicesAtFor(clickPos, colonist.pawn).Do(option => AddMultiAction(colonist, true, option));
			if (forced) Tools.SetDraftStatus(colonist.pawn, false);

			forced = Tools.ForceDraft(colonist.pawn, false);
			FloatMenuMakerMap.ChoicesAtFor(clickPos, colonist.pawn).Do(option => AddMultiAction(colonist, false, option));
			if (forced) Tools.SetDraftStatus(colonist.pawn, true);
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

		public IEnumerable<Colonist> ColonistsForActions(IEnumerable<MultiAction> subActions)
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
				return val == null || (val as S) == valueToCompare;
			}) == false)
				result = defaultValue;
			return result;
		}

		private float AllEqualFloat(IEnumerable<FloatMenuOption> options, Func<FloatMenuOption, float> eval, float defaultValue)
		{
			if (options.Count() == 0) return defaultValue;
			var result = eval(options.First());
			if (options.All(option => eval(option) == result) == false)
				result = defaultValue;
			return result;
		}

		public FloatMenuOption GetOption(string title, IEnumerable<MultiAction> multiActions)
		{
			var actions = multiActions.ToList();
			var priority = actions.Max(a => a.option.Priority);

			var options = actions.Select(action => action.option);
			var mouseoverGuiAction = AllEqual(options, o => o.mouseoverGuiAction?.Method, o => o.mouseoverGuiAction, null);
			var revalidateClickTarget = AllEqual(options, o => o.revalidateClickTarget, o => o.revalidateClickTarget, null);
			var extraPartWidth = AllEqualFloat(options, o => o.extraPartWidth, 0f);
			var extraPartOnGUI = AllEqual(options, o => o.extraPartOnGUI?.Method, o => o.extraPartOnGUI, null);
			var revalidateWorldClickTarget = AllEqual(options, o => o.revalidateWorldClickTarget, o => o.revalidateWorldClickTarget, null);

			var option = new FloatMenuOption(priority);

			// support for our special force menu options. we need to call all internal
			// subactions from when the extra buttons calls them
			//
			var forcedOptions = options.OfType<ForcedFloatMenuOption>().ToList();
			if (extraPartOnGUI != null && forcedOptions.Count > 0)
			{
				option = new ForcedMultiFloatMenuOption
				{
					options = options.OfType<ForcedFloatMenuOption>().ToList(),
				};
				option.extraPartOnGUI = drawRect => ((ForcedMultiFloatMenuOption)option).RenderExtraPartOnGui(drawRect);
			}

			option.Label = title;
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

			return new FloatMenu(options);
		}
	}
}