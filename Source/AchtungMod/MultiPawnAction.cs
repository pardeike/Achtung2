using Verse;
using System.Collections.Generic;
using System;
using mattmc3.Common.Collections.Generic;

namespace AchtungMod
{
	// Utility to group menu items by name and collect multiple colonists
	// actions for each menu item. Will track draft/undraft status for each
	// item and will auto change draft modus when menu is selected based on
	// each colonists current draft status
	//
	public class MultiPawnAction
	{
		ActionMode _mode;
		OrderedDictionary<Pawn, FloatMenuOption> _pawnActions;

		public MultiPawnAction(ActionMode mode)
		{
			_mode = mode;
			_pawnActions = new OrderedDictionary<Pawn, FloatMenuOption>();
		}

		// convenience method
		//
		public static void AddToActions(OrderedDictionary<string, MultiPawnAction> actions, Pawn colonist, ActionMode mode, FloatMenuOption option)
		{
			string name = option.Label;
			if (actions.ContainsKey(name) == false)
			{
				actions[name] = new MultiPawnAction(mode);
			}
			actions[name].AddAction(colonist, option);
		}

		public void AddAction(Pawn pawn, FloatMenuOption option)
		{
			_pawnActions[pawn] = option;
		}

		public ICollection<Pawn> Colonists()
		{
			return _pawnActions.Keys;
		}

		// multiple colonist actions probably have multiple priorities.
		// we return the highest prio which should work great too if we only
		// have one colonist selected or all colonists have the same priority
		// on a specific command
		//
		public MenuOptionPriority GetPriority()
		{
			MenuOptionPriority priority = MenuOptionPriority.Low;
			foreach (Pawn colonist in _pawnActions.Keys)
			{
				MenuOptionPriority prio = _pawnActions[colonist].priority;
				if (prio > priority)
				{
					priority = prio;
				}
			}
			return priority;
		}

		// Here, we wrap each menu action in our own action that will
		// handle the drafting before the real action is run
		//
		public Action GetAction(Pawn actor)
		{
			return new Action(delegate
			{
				FloatMenuOption option = _pawnActions[actor];
				if (option.Disabled == false)
				{
					switch (_mode)
					{
						case ActionMode.Drafted:
							if (actor.drafter.Drafted == false)
							{
								Worker.SetDraftStatus(actor, true);
							}
							break;
						case ActionMode.Undrafted:
							if (actor.drafter.Drafted == true)
							{
								Worker.SetDraftStatus(actor, false);
							}
							break;
						default:
							break;
					}
				}
				option.action();
			});
		}

		public ActionMode GetMode()
		{
			return _mode;
		}

		// our disabled status follows the status of each sub item
		//
		public bool IsDisabled()
		{
			bool allActionsDisabled = true;
			foreach (Pawn pawn in Colonists())
			{
				FloatMenuOption option = _pawnActions[pawn];
				allActionsDisabled = allActionsDisabled && option.Disabled;
			}
			return allActionsDisabled;
		}
	}
}
