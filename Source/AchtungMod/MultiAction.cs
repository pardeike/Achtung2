using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace AchtungMod
{
	public class MultiAction
	{
		public Colonist colonist;
		public bool draftMode;
		public FloatMenuOption option;

		public string Key
		{
			get
			{
				return option.Label;
			}
		}

		public MultiAction(Colonist colonist, bool draftMode, FloatMenuOption option)
		{
			this.colonist = colonist;
			this.draftMode = draftMode;
			this.option = option;
		}

		public bool IsForced()
		{
			return Tools.GetDraftingStatus(colonist.pawn) != draftMode;
		}

		public string EnhancedLabel(List<Colonist> colonists)
		{
			string suffix = (colonists.Count() == 1) ? " (" + colonists.First().pawn.NameStringShort + ")" : " (" + colonists.Count() + "x)";
			return option.Label + suffix;
		}

		public static bool Enabled(MultiAction action)
		{
			return action.option.Disabled == false;
		}

		public Action GetAction()
		{
			return new Action(delegate
			{
				if (option.Disabled == false)
				{
					if (IsForced())
					{
						Tools.SetDraftStatus(colonist.pawn, draftMode);
					}
					option.action();
				}
			});
		}
	}
}