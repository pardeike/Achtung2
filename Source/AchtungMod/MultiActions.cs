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

		public FloatMenuOption GetOption(string title, IEnumerable<MultiAction> multiActions)
		{
			var actions = multiActions.ToList();
			var priority = actions.Max(a => a.option.Priority);
			var option = new FloatMenuOption(title, delegate
			{
				actions.Do(multiAction =>
				{
					var colonistAction = multiAction.GetAction();
					colonistAction();
				});
			}, priority)
			{ Disabled = actions.All(a => a.option.Disabled) };
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