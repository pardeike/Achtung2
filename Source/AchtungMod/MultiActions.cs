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
			totalColonistsInvolved = colonists.Count();
			colonists.Do(AddColonist);
		}

		public void AddColonist(Colonist colonist)
		{
			bool forced;

			forced = Tools.ForceDraft(colonist.pawn, true);
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

		public int Count()
		{
			return allActions.Count();
		}

		public IEnumerable<string> GetKeys()
		{
			return allActions.Select(action => action.Key).Distinct();
		}

		public IEnumerable<MultiAction> ActionsForKey(string key)
		{
			IEnumerable<MultiAction> actionsForKey = allActions.Where(action => action.Key == key);
			return actionsForKey.Select(actionForKey =>
			{
				Colonist colonist = actionForKey.colonist;
				return actionsForKey.Where(action => action.colonist == colonist)
					.OrderBy(action => action.IsForced() ? 2 : 1).First();
			});
		}

		public IEnumerable<Colonist> ColonistsForActions(IEnumerable<MultiAction> subActions)
		{
			return subActions.Select(action => action.colonist).Distinct();
		}

		public FloatMenuOption GetOption(string title, IEnumerable<MultiAction> multiActions)
		{
			MenuOptionPriority priority = multiActions.Max(a => a.option.Priority);
			FloatMenuOption option = new FloatMenuOption(title, delegate
			{
				multiActions.Do(multiAction =>
				{
					Action colonistAction = multiAction.GetAction();
					colonistAction();
				});
			}, priority);
			option.Disabled = multiActions.All(a => a.option.Disabled);
			return option;
		}

		public Window GetWindow()
		{
			List<FloatMenuOption> options = new List<FloatMenuOption>();

			GetKeys().Do(key =>
			{
				IEnumerable<MultiAction> subActions = ActionsForKey(key);
				IEnumerable<Colonist> colonists = ColonistsForActions(subActions);
				string title = subActions.First().EnhancedLabel(totalColonistsInvolved > 1 ? colonists : null);
				FloatMenuOption option = GetOption(title, subActions);
				options.Add(option);
			});

			return new FloatMenu(options);
		}
	}
}