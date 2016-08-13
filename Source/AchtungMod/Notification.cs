using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;

namespace AchtungMod
{
	public class Notification : Window
	{
		public Vector2 ContentMargin = new Vector2(10, 18);
		public Vector2 WindowSize = new Vector2(440, 220);
		public float HeaderHeight = 48;
		public float WindowPadding = 18;

		public Rect ContentRect;
		public Rect HeaderRect;

		public Notification()
		{
			this.closeOnEscapeKey = true;
			this.doCloseButton = true;
			this.doCloseX = true;
			this.absorbInputAroundWindow = true;
			this.forcePause = true;
			ComputeSizes();
		}

		protected void ComputeSizes()
		{
			Vector2 ContentSize = new Vector2(WindowSize.x - WindowPadding * 2 - ContentMargin.x * 2, WindowSize.y - WindowPadding * 2 - ContentMargin.y * 2 - HeaderHeight);
			ContentRect = new Rect(ContentMargin.x, ContentMargin.y + HeaderHeight, ContentSize.x, ContentSize.y);
			HeaderRect = new Rect(ContentMargin.x, ContentMargin.y, ContentSize.x, HeaderHeight);
		}


		public override Vector2 InitialSize
		{
			get
			{
				return new Vector2(WindowSize.x, WindowSize.y);
			}
		}

		public override bool CausesMessageBackground()
		{
			return true;
		}

		public override void DoWindowContents(Rect inRect)
		{
			GUI.color = Color.white;

			Text.Font = GameFont.Medium;
			Widgets.Label(HeaderRect, "Achtung! Mod");

			Text.Font = GameFont.Small;
			string optionsMenuName = "ModConfigurationOptions".Translate();
			Widgets.Label(ContentRect, "ModDeactivated".Translate(optionsMenuName));
		}
	}
}
