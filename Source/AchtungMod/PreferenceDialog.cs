using UnityEngine;
using Verse;

namespace AchtungMod
{
	public class PreferenceDialog : Window
	{
		public override Vector2 InitialSize
		{
			get
			{
				return GetDialogSize();
			}
		}

		public Vector2 GetDialogSize()
		{
			return new Vector2(500, 620);
		}

		public override void PreOpen()
		{
			base.PreOpen();
			absorbInputAroundWindow = false;
			closeOnClickedOutside = false;
			closeOnEscapeKey = true;
			doCloseButton = false;
			doCloseX = true;
			draggable = true;
			drawShadow = true;
			focusWhenOpened = true;
			forcePause = false;
			preventCameraMotion = false;
			preventDrawTutor = true;
			resizeable = true;
		}

		public override void PreClose()
		{
			Settings.Save();
			base.PreClose();
		}

		public override void DoWindowContents(Rect rect)
		{
			rect.height = 2000f;
			var listing = new Listing_Standard();

			listing.Begin(rect);

			Text.Font = GameFont.Medium;
			listing.Label("AchtungSettingsTitle".Translate());
			Text.Font = GameFont.Tiny;
			GUI.color = Color.white;
			listing.Label("Version " + Tools.Version + ", © 2016 Andreas Pardeike");
			listing.Gap();
			Text.Font = GameFont.Medium;

			listing.CheckboxEnhanced("AchtungEnabled", ref Settings.instance.modActive);
			listing.ValueLabeled("ForceDraft", ref Settings.instance.forceDraftKey);
			listing.ValueLabeled("IgnoreMenu", ref Settings.instance.ignoreMenuKey);
			listing.CheckboxEnhanced("ReverseIgnoreMenu", ref Settings.instance.reverseMenuKey);
			listing.ValueLabeled("RelativeMovement", ref Settings.instance.relativeMovementKey);
			listing.ValueLabeled("BreakLevel", ref Settings.instance.breakLevel);
			listing.ValueLabeled("HealthLevel", ref Settings.instance.healthLevel);

			Text.Font = GameFont.Small;
			GUI.color = Color.white;
			listing.Label("Auto Combat Aggressiveness " + GenText.ToStringPercent(Settings.instance.aggressiveness));
			Settings.instance.aggressiveness = listing.Slider(Settings.instance.aggressiveness, 0f, 1f);

			listing.CheckboxEnhanced("DebugPositions", ref Settings.instance.debugPositions);

			listing.End();
		}
	}
}