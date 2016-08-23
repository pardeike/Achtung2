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

		public MultiActions(List<Colonist> colonists, Vector3 clickPos)
		{
			this.clickPos = clickPos;
			allActions = new List<MultiAction>();
			colonists.ForEach(AddColonist);
		}

		public void AddColonist(Colonist colonist)
		{
			bool forceDrafted = Tools.ForceDraft(colonist.pawn, true);
			FloatMenuMakerMap.ChoicesAtFor(clickPos, colonist.pawn).ForEach(option => AddMultiAction(colonist, true, option));
			if (forceDrafted) Tools.SetDraftStatus(colonist.pawn, false, false);

			bool forceUndrafted = Tools.ForceDraft(colonist.pawn, false);
			FloatMenuMakerMap.ChoicesAtFor(clickPos, colonist.pawn).ForEach(option => AddMultiAction(colonist, false, option));
			if (forceUndrafted) Tools.SetDraftStatus(colonist.pawn, true, false);
		}

		public void AddMultiAction(Colonist colonist, bool draftMode, FloatMenuOption option)
		{
			if (Tools.IsGoHereOption(option) == false) allActions.Add(new MultiAction(colonist, draftMode, option));
		}

		public int Count()
		{
			return allActions.Count();
		}

		public List<string> GetKeys()
		{
			return allActions.Select(action => action.Key).Distinct().ToList();
		}

		public List<MultiAction> ActionsForKey(string key)
		{
			List<MultiAction> actionsForKey = allActions.Where(action => action.Key == key).ToList();
			return actionsForKey.Select(actionForKey =>
			{
				Colonist colonist = actionForKey.colonist;
				return actionsForKey.Where(action => action.colonist == colonist)
					.OrderBy(action => action.IsForced() ? 2 : 1).First();
			}).ToList();
		}

		public List<Colonist> ColonistsForActions(List<MultiAction> subActions)
		{
			return subActions.Select(action => action.colonist).Distinct().ToList();
		}

		public FloatMenuOption GetOption(string title, List<MultiAction> multiActions)
		{
			MenuOptionPriority priority = multiActions.Max(a => a.option.priority);
			FloatMenuOption option = new FloatMenuOption(title, delegate
			{
				foreach (MultiAction multiAction in multiActions)
				{
					Action colonistAction = multiAction.GetAction();
					colonistAction();
				}
			}, priority);
			option.Disabled = multiActions.All(a => a.option.Disabled);
			return option;
		}

		public Window GetWindow()
		{
			List<FloatMenuOption> options = new List<FloatMenuOption>();

			GetKeys().ForEach(key =>
			{
				List<MultiAction> subActions = ActionsForKey(key);
				List<Colonist> colonists = ColonistsForActions(subActions);
				string title = subActions.First().EnhancedLabel(colonists);
				FloatMenuOption option = GetOption(title, subActions);
				options.Add(option);
			});

			return new FloatMenu(options);
		}
	}
}