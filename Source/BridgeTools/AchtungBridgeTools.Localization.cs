using RimBridgeServer.Sdk;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace AchtungMod;

public sealed partial class AchtungBridgeTools
{
	const float SettingsColumnWidth = 399f;
	const float SettingsHeaderWidth = 423f;
	const float SettingsSubheadingWidth = SettingsColumnWidth - 12f;

	sealed class SettingsTextCheck
	{
		public string kind { get; set; }
		public string font { get; set; }
		public string key { get; set; }
		public string text { get; set; }
		public float availableWidth { get; set; }
		public float textWidth { get; set; }
		public float wrappedHeight { get; set; }
		public float lineHeight { get; set; }
		public bool fitsOneLine { get; set; }
	}

	sealed class SettingsValueCheck
	{
		public string setting { get; set; }
		public string titleKey { get; set; }
		public string title { get; set; }
		public string valueKey { get; set; }
		public string value { get; set; }
		public float availableWidth { get; set; }
		public float titleWidth { get; set; }
		public float valueWidth { get; set; }
		public float minimumGap { get; set; }
		public float combinedWidth { get; set; }
		public float remainingWidth { get; set; }
		public bool titleFitsOneLine { get; set; }
		public bool valueFitsOneLine { get; set; }
		public bool fitsWithoutOverlap { get; set; }
	}

	sealed class SettingsLayoutAssertions
	{
		public bool requestedLanguageActive { get; set; }
		public bool allTitleValuePairsFit { get; set; }
		public bool allFixedWidthHeadersFit { get; set; }
		public int titleValueCheckCount { get; set; }
		public int fixedTextCheckCount { get; set; }
		public int overlapCount { get; set; }
		public int fixedTextClipCount { get; set; }
	}

	sealed class SettingsLayoutAuditResult
	{
		public bool success { get; set; }
		public string expectedLanguage { get; set; }
		public string activeLanguage { get; set; }
		public string activeLanguageLegacy { get; set; }
		public string prefsLanguage { get; set; }
		public object screen { get; set; }
		public object geometry { get; set; }
		public SettingsLayoutAssertions assertions { get; set; }
		public SettingsValueCheck[] overlapping { get; set; } = [];
		public SettingsTextCheck[] clippedFixedText { get; set; } = [];
		public List<SettingsValueCheck> valueChecks { get; set; } = [];
		public List<SettingsTextCheck> textChecks { get; set; } = [];
	}

	[Tool(
		"achtung/audit_settings_layout",
		Description = "Measure every Achtung settings title and selectable value with RimWorld's active-language font, including the title/value pairs that share one row.")]
	public static async Task<object> AuditSettingsLayout(
		IRimBridgeContext ctx,
		CancellationToken cancellationToken,
		[ToolParameter(Description = "Live settings-column content width in logical pixels.", Required = false, DefaultValue = SettingsColumnWidth)] float columnWidth = SettingsColumnWidth,
		[ToolParameter(Description = "Minimum clear space required between a left-aligned title and right-aligned value.", Required = false, DefaultValue = 12f)] float minimumGap = 12f,
		[ToolParameter(Description = "Optional legacy language folder name that must be active while measuring.", Required = false)] string expectedLanguage = null)
	{
		if (ctx == null)
			return new { success = false, error = "RimBridge context was not injected." };
		if (columnWidth < 200f || columnWidth > 1000f)
			return new { success = false, error = "columnWidth must be between 200 and 1000 logical pixels." };
		if (minimumGap < 0f || minimumGap > 100f)
			return new { success = false, error = "minimumGap must be between 0 and 100 logical pixels." };

		return await ctx.MainThread.InvokeAsync(
			() => AuditSettingsLayoutOnMainThread(columnWidth, minimumGap, expectedLanguage),
			cancellationToken);
	}

	static SettingsLayoutAuditResult AuditSettingsLayoutOnMainThread(float columnWidth, float minimumGap, string expectedLanguage)
	{
		var savedFont = Text.Font;
		try
		{
			Text.Font = GameFont.Small;
			var valueChecks = new List<SettingsValueCheck>();
			AddEnumValueChecks<CommandMenuMode>(valueChecks, "ForceCommandMenuMode", columnWidth, minimumGap);
			AddEnumValueChecks<AchtungModKey>(valueChecks, "ForceCommandMenuKey", columnWidth, minimumGap);
			AddEnumValueChecks<DraftedColonistDraggingMode>(valueChecks, "DraftedColonistDraggingMode", columnWidth, minimumGap);
			AddEnumValueChecks<AchtungModKey>(valueChecks, "AchtungModifier", columnWidth, minimumGap);
			AddEnumValueChecks<BreakLevel>(valueChecks, "BreakLevel", columnWidth, minimumGap);
			AddEnumValueChecks<HealthLevel>(valueChecks, "HealthLevel", columnWidth, minimumGap);
			AddEnumValueChecks<WorkMarkers>(valueChecks, "WorkMarkers", columnWidth, minimumGap);
			AddValueCheck(valueChecks, "MaxForcedItems", "MaxForcedItemsTitle", "Disabled", columnWidth, minimumGap);
			AddValueCheck(valueChecks, "MaxForcedItems", "MaxForcedItemsTitle", "MaxForcedItemsUnlimited", columnWidth, minimumGap);
			AddLiteralValueCheck(valueChecks, "Delay", "DelayTitle", "2000 ms", columnWidth, minimumGap);

			var textChecks = new List<SettingsTextCheck>();
			foreach (var key in new[] { "PositioningSettingsHeader", "ForcingSettingsHeader" })
				textChecks.Add(MeasureText("column-header", GameFont.Medium, key, SettingsHeaderWidth));
			foreach (var key in new[]
			{
				"PositioningRightClickTitle",
				"PositioningDraggingTitle",
				"ForcingCommandsTitle",
				"ForcingJobBehaviorTitle",
				"ForcingStopRulesTitle",
				"ForcingFeedbackTitle"
			})
				textChecks.Add(MeasureText("subheading", GameFont.Small, key, Math.Max(0f, columnWidth - (SettingsColumnWidth - SettingsSubheadingWidth))));
			var overlapping = valueChecks.Where(check => check.fitsWithoutOverlap == false).ToArray();
			var clippedFixedText = textChecks
				.Where(check => (check.kind == "column-header" || check.kind == "subheading") && check.fitsOneLine == false)
				.ToArray();
			var activeLanguage = LanguageDatabase.activeLanguage?.folderName;
			var activeLanguageLegacy = LanguageDatabase.activeLanguage?.LegacyFolderName;
			var requestedLanguageActive = expectedLanguage.NullOrEmpty()
				|| string.Equals(expectedLanguage, activeLanguage, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(expectedLanguage, activeLanguageLegacy, StringComparison.OrdinalIgnoreCase);
			return new SettingsLayoutAuditResult
			{
				success = requestedLanguageActive && overlapping.Length == 0 && clippedFixedText.Length == 0,
				expectedLanguage = expectedLanguage,
				activeLanguage = activeLanguage,
				activeLanguageLegacy = activeLanguageLegacy,
				prefsLanguage = Prefs.LangFolderName,
				screen = new
				{
					width = UI.screenWidth,
					height = UI.screenHeight,
					uiScale = Prefs.UIScale
				},
				geometry = new
				{
					columnWidth,
					minimumGap,
					referenceColumnWidth = SettingsColumnWidth,
					headerWidth = SettingsHeaderWidth
				},
				assertions = new SettingsLayoutAssertions
				{
					requestedLanguageActive = requestedLanguageActive,
					allTitleValuePairsFit = overlapping.Length == 0,
					allFixedWidthHeadersFit = clippedFixedText.Length == 0,
					titleValueCheckCount = valueChecks.Count,
					fixedTextCheckCount = textChecks.Count,
					overlapCount = overlapping.Length,
					fixedTextClipCount = clippedFixedText.Length
				},
				overlapping = overlapping,
				clippedFixedText = clippedFixedText,
				valueChecks = valueChecks,
				textChecks = textChecks
			};
		}
		finally
		{
			Text.Font = savedFont;
		}
	}

	static void AddEnumValueChecks<T>(List<SettingsValueCheck> checks, string setting, float columnWidth, float minimumGap)
		where T : struct, Enum
	{
		foreach (var value in Enum.GetValues(typeof(T)).Cast<T>())
			AddValueCheck(checks, setting, setting + "Title", typeof(T).Name + "Option" + value, columnWidth, minimumGap);
	}

	static void AddValueCheck(List<SettingsValueCheck> checks, string setting, string titleKey, string valueKey, float columnWidth, float minimumGap)
	{
		var title = titleKey.Translate().ToString();
		var value = valueKey.Translate().ToString();
		AddMeasuredValueCheck(checks, setting, titleKey, title, valueKey, value, columnWidth, minimumGap);
	}

	static void AddLiteralValueCheck(List<SettingsValueCheck> checks, string setting, string titleKey, string value, float columnWidth, float minimumGap)
	{
		var title = titleKey.Translate().ToString();
		AddMeasuredValueCheck(checks, setting, titleKey, title, "(literal)", value, columnWidth, minimumGap);
	}

	static void AddMeasuredValueCheck(List<SettingsValueCheck> checks, string setting, string titleKey, string title, string valueKey, string value, float columnWidth, float minimumGap)
	{
		var titleWidth = Text.CalcSize(title).x;
		var valueWidth = Text.CalcSize(value).x;
		var remainingWidth = columnWidth - titleWidth - valueWidth;
		checks.Add(new SettingsValueCheck
		{
			setting = setting,
			titleKey = titleKey,
			title = title,
			valueKey = valueKey,
			value = value,
			availableWidth = Round(columnWidth),
			titleWidth = Round(titleWidth),
			valueWidth = Round(valueWidth),
			minimumGap = Round(minimumGap),
			combinedWidth = Round(titleWidth + valueWidth + minimumGap),
			remainingWidth = Round(remainingWidth),
			titleFitsOneLine = titleWidth <= columnWidth,
			valueFitsOneLine = valueWidth <= columnWidth,
			fitsWithoutOverlap = remainingWidth >= minimumGap
		});
	}

	static SettingsTextCheck MeasureText(string kind, GameFont font, string key, float availableWidth)
	{
		var savedFont = Text.Font;
		try
		{
			Text.Font = font;
			var value = key.Translate().ToString();
			var textWidth = Text.CalcSize(value).x;
			return new SettingsTextCheck
			{
				kind = kind,
				font = font.ToString(),
				key = key,
				text = value,
				availableWidth = Round(availableWidth),
				textWidth = Round(textWidth),
				wrappedHeight = Round(Text.CalcHeight(value, availableWidth)),
				lineHeight = Round(Text.LineHeight),
				fitsOneLine = textWidth <= availableWidth
			};
		}
		finally
		{
			Text.Font = savedFont;
		}
	}

	static float Round(float value) => (float)Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
