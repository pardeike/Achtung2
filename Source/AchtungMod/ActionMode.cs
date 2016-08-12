using Verse;

namespace AchtungMod
{
	public enum ActionMode
	{
		Drafted,
		Undrafted,
		Other
	}

	// probably unused but stays here for reference
	//
	static class ActionModeMethods
	{
		public static string GetPrefix(this ActionMode mode)
		{
			switch (mode)
			{
				case ActionMode.Drafted:
					return Translator.Translate("CommandDraftLabel") + ": ";
				case ActionMode.Undrafted:
					return Translator.Translate("CommandUndraftLabel") + ": ";
				default:
					return "";
			}
		}
	}
}