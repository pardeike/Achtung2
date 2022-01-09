namespace AchtungMod
{
	/*
	public class HintWithSettingsButton : StandardHint
	{
		const float buttonWidth = 140f;
		const float buttonHeight = 30f;

		public bool isMenu;
		public bool withSettingsButton;

		public override bool ShouldAvoidScreenPosition(ScreenPosition position)
		{
			return isMenu && position == ScreenPosition.right;
		}

		public override float ContentHeight => message.Height(ContentWidth, GameFont.Small) + (withSettingsButton ? padding + buttonHeight : 0f);

		public override void DoWindowContents(Rect innerRect)
		{
			if (withSettingsButton == false)
			{
				base.DoWindowContents(innerRect);
				return;
			}

			var originalRect = innerRect;
			originalRect.yMax -= buttonHeight;
			base.DoWindowContents(originalRect);

			var buttonRect = innerRect;
			buttonRect.yMin = buttonRect.yMax - borderWidth - buttonHeight;
			buttonRect.width = buttonWidth;
			buttonRect.height = buttonHeight;
			buttonRect = buttonRect.CenteredOnXIn(innerRect);
			if (Widgets.ButtonText(buttonRect, "Settings"))
			{
				Event.current.Use();
				Achtung.tutor.CloseTimedHint();

				var dialog = new Dialog_ModSettings();
				var me = LoadedModManager.GetMod<Achtung>();
				dialog.selMod = me;
				Find.WindowStack.Add(dialog);
			}
		}
	}
	*/
}
