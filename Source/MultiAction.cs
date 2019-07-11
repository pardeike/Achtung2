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

		public string Key => option.Label;

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

		public string EnhancedLabel(IEnumerable<Colonist> colonists)
		{
			var suffix = "";
			if (colonists != null)
			{
				var names = colonists.ToArray();
				suffix = (names.Length == 1) ? " (" + names[0].pawn.Name.ToStringShort + ")" : " (" + names.Length + "x)";
			}
			return option.Label + suffix;
		}

		public Action GetAction()
		{
			return delegate
			{
				if (option.Disabled == false)
				{
					if (IsForced())
						Tools.SetDraftStatus(colonist.pawn, draftMode, false);
					option.action();
				}
			};
		}
	}
}