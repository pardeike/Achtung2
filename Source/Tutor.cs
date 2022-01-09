using HarmonyLib;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;

namespace AchtungMod
{
	public static class Tutor
	{
		[HarmonyPatch(typeof(Root))]
		[HarmonyPatch(nameof(Root.OnGUI))]
		static class Root_OnGUI_Patch
		{
			[HarmonyPriority(Priority.First)]
			public static void Postfix()
			{
				DrawHints();
			}
		}

		public abstract class Hint
		{
			public bool acknowledged;
			[NonSerialized] public Vector2 centerOfInterest;

			public abstract Vector2 WindowSize { get; }
			public abstract Vector2 WindowOffset { get; }
			public abstract void DoWindowContents(Rect canvas);

			public void Acknowledge()
			{
				acknowledged = true;
				SaveHints();
			}
		}

		private static readonly string filePath;
		private static ConcurrentDictionary<string, Hint> hints = new ConcurrentDictionary<string, Hint>();
		private static Hint currentHint;

		private static void LoadHints()
		{
			try
			{
				if (File.Exists(filePath) == false) return;
				var text = File.ReadAllText(filePath, Encoding.UTF8);
				hints = new ConcurrentDictionary<string, Hint>();
				foreach (var item in text.Split('\n'))
				{
					var parts = item.Split('\t');
					if (parts.Length != 3) continue;
					var type = Type.GetType(parts[1], false);
					if (type == null || type.IsSubclassOf(typeof(Hint)) == false) continue;
					var context = parts[0];
					var hint = (Hint)JsonUtility.FromJson(parts[2], type);
					hints[context] = hint;
				}
			}
			catch (Exception ex)
			{
				Log.Error($"Exception while reading {filePath}: {ex}");
			}
		}

		private static void SaveHints()
		{
			try
			{
				var builder = new StringBuilder();
				foreach (var item in hints)
				{
					var json = JsonUtility.ToJson(item.Value);
					_ = builder.Append(item.Key)
						.Append('\t')
						.Append(item.Value.GetType().FullName)
						.Append('\t')
						.Append(json)
						.Append('\n');
				}
				File.WriteAllText(filePath, builder.ToString());
			}
			catch (Exception ex)
			{
				Log.Error($"Exception while writing {filePath}: {ex}");
			}
		}

		static Tutor()
		{
			var runningModClasses = Traverse.Create(typeof(LoadedModManager)).Field("runningModClasses").GetValue<Dictionary<Type, Mod>>();
			var tutorAssembly = typeof(Tutor).Assembly;
			var info = runningModClasses.FirstOrDefault(info => info.Key.Assembly == tutorAssembly);
			if (info.Value != null)
			{
				var modId = info.Value.Content.PackageId.Replace('.', '-').Replace(' ', '-');
				filePath = Path.Combine(GenFilePaths.ConfigFolderPath, $"Tutor-{modId}.json");
				LoadHints();
			}
			else
				hints = new ConcurrentDictionary<string, Hint>();
		}

		static void DrawHints()
		{
			if (Event.current.type != EventType.Repaint) return;
			if (currentHint == null) return;
			var renderedHint = currentHint;
			currentHint = null;

			var windowSize = renderedHint.WindowSize;
			var rect = new Rect(renderedHint.centerOfInterest + renderedHint.WindowOffset - windowSize / 2, windowSize);

			Find.WindowStack.ImmediateWindow(1294583817, rect, WindowLayer.Super, () =>
			{
				rect = rect.AtZero();
				renderedHint.DoWindowContents(rect);
			}, false, false, 0f);

			/*
			if (!LongEventHandler.AnyEventWhichDoesntUseStandardWindowNowOrWaiting)
			{
				Find.WindowStack.ImmediateWindow(1294583817, rect, WindowLayer.Super, () =>
				{
					rect = rect.AtZero();
					renderedHint.DoWindowContents(rect);
				}, false, false, 0f);
			}
			else
			{
				rect = rect.AtZero();
				renderedHint.DoWindowContents(rect);
			}
			*/
		}

		public static void RegisterContext(string context, Hint hint, bool forceUpdate = false)
		{
			if (hints.ContainsKey(context) && forceUpdate == false) return;
			hint.acknowledged = false;
			hints[context] = hint;
			SaveHints();
		}

		public static T DoHintableAction<T>(string context, Vector2 centerOfInterest, Func<Action, T> action)
		{
			if (hints.TryGetValue(context, out var hint) == false || hint.acknowledged) return action(() => { });
			var result = action(() =>
			{
				hint.acknowledged = true;
				hints[context] = hint;
				SaveHints();
			});
			if (currentHint == null)
			{
				currentHint = hint;
				var windowOffset = Find.WindowStack.currentlyDrawnWindow?.windowRect.position ?? Vector2.zero;
				currentHint.centerOfInterest = centerOfInterest + windowOffset;
			}
			return result;
		}
	}
}
