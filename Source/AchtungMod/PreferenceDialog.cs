using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;

namespace AchtungMod
{
	public class PreferenceDialog : Window
	{
		public override void PreOpen()
		{
			base.PreOpen();
			absorbInputAroundWindow = true;
			closeOnClickedOutside = false;
			closeOnEscapeKey = true;
			doCloseButton = true;
			doCloseX = true;
			draggable = false;
			drawShadow = true;
			focusWhenOpened = true;
			forcePause = true;
			preventCameraMotion = true;
			preventDrawTutor = true;
			resizeable = false;

			windowRect.height = CreateWindowContents(windowRect);
		}

		public override void PreClose()
		{
			base.PreClose();
			Settings.Save();
		}

		public float CreateWindowContents(Rect rect)
		{
			var listing = new Listing_Standard(rect);
			{
				// title
				Text.Font = GameFont.Medium;
				listing.Label("AchtungOptionsTitle".Translate());
				listing.Gap();

				// mod active
				Text.Font = GameFont.Small;
				GUI.color = Color.white;
				listing.CheckboxLabeled("AchtungEnabled".Translate(), ref Settings.instance.modActive);
				Text.Font = GameFont.Tiny;
				listing.ColumnWidth -= 34;
				GUI.color = Color.gray;
				listing.Label("AchtungEnabledExplained".Translate());
				listing.ColumnWidth += 34;
				listing.Gap();

				// force draft settings
				Text.Font = GameFont.Small;
				GUI.color = Color.white;
				listing.Label("ForceDraft".Translate());
				Text.Font = GameFont.Small;
				GUI.color = Color.white;
				listing.ModKeyChoice("ModKey", ref Settings.instance.forceDraftKey);
				Text.Font = GameFont.Tiny;
				listing.ColumnWidth -= 34;
				GUI.color = Color.gray;
				listing.Label("ForceDraftExplained".Translate());
				listing.ColumnWidth += 34;
				listing.Gap();

				// Ignore menu settings
				Text.Font = GameFont.Small;
				GUI.color = Color.white;
				listing.Label("IgnoreMenu".Translate());
				Text.Font = GameFont.Small;
				GUI.color = Color.white;
				listing.ModKeyChoice("ModKey", ref Settings.instance.ignoreMenuKey);
				Text.Font = GameFont.Tiny;
				listing.ColumnWidth -= 34;
				GUI.color = Color.gray;
				listing.Label("IgnoreMenuExplained".Translate());
				listing.ColumnWidth += 34;
				listing.Gap();

				// Relative movement settings
				Text.Font = GameFont.Small;
				GUI.color = Color.white;
				listing.Label("RelativeMovement".Translate());
				Text.Font = GameFont.Small;
				GUI.color = Color.white;
				listing.ModKeyChoice("ModKey", ref Settings.instance.relativeMovementKey);
				Text.Font = GameFont.Tiny;
				listing.ColumnWidth -= 34;
				GUI.color = Color.gray;
				listing.Label("RelativeMovementExplained".Translate());
				listing.ColumnWidth += 34;
				listing.Gap();

				// version
				Text.Font = GameFont.Small;
				GUI.color = Color.white;
				listing.ValueLabeled("ModVersion".Translate(), Tools.Version);

				Text.Font = GameFont.Tiny;
				GUI.color = Color.white;
				listing.Label("(c) 2016 Andreas Pardeike");
				listing.Gap();
			}
			listing.End();
			return listing.CurHeight + Margin * 2 + CloseButSize.y + Margin;
		}

		public override void DoWindowContents(Rect rect)
		{
			CreateWindowContents(rect);
		}
	}
}