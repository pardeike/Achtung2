using RimWorld;
using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Video;
using Verse;

namespace AchtungMod
{
	internal class Dialog_ModFeatures : Window
	{
		const float listWidth = 240;
		const float videoWidth = 640;
		const float videoHeight = 480;
		const float margin = 20;

		Vector2 scrollPosition;
		Texture currentTexture;
		RenderTexture renderTexture;
		float titleHeight;
		VideoPlayer videoPlayer;
		string title = "";

		readonly string resourceDir;
		readonly string[] topicResources;
		readonly Texture2D[] topicTextures;

		string TopicTranslated(int i) => $"Topic_{topicResources[i].Substring(3).Replace(".png", "").Replace(".mp4", "")}".Translate();
		string TopicType(int i) => topicResources[i].EndsWith(".png") ? "image" : "video";
		string TopicPath(int i) => $"{resourceDir}{Path.DirectorySeparatorChar}{topicResources[i]}";
		public override Vector2 InitialSize => new Vector2(listWidth + videoWidth + margin * 3, videoHeight + titleHeight + margin * 3);

		public Dialog_ModFeatures(Type type)
		{
			doCloseX = true;
			forcePause = true;
			absorbInputAroundWindow = true;
			silenceAmbientSound = true;
			closeOnClickedOutside = true;

			var rootDir = LoadedModManager.RunningMods.FirstOrDefault(mod => mod.assemblies.loadedAssemblies.Contains(type.Assembly))?.RootDir;
			if (rootDir == null)
				throw new Exception($"Could not find root mod directory for {type.Assembly.FullName}");
			resourceDir = $"{rootDir}{Path.DirectorySeparatorChar}{ModMetaData.AboutFolderName}{Path.DirectorySeparatorChar}Resources";

			topicResources = Directory.GetFiles(resourceDir)
				.Select(f => Path.GetFileName(f))
				.ToArray();
			topicTextures = new Texture2D[topicResources.Length];
		}

		public override float Margin => margin;
		public int TopicCount => topicResources.Length;

		public override void PreOpen()
		{
			Text.Font = GameFont.Medium;
			titleHeight = Text.CalcHeight(title, 10000);
			renderTexture = new RenderTexture((int)videoWidth, (int)videoHeight, 24, RenderTextureFormat.ARGB32);
			videoPlayer = Find.Camera.gameObject.AddComponent<VideoPlayer>();
			videoPlayer = Find.Root.gameObject.AddComponent<VideoPlayer>();
			videoPlayer.playOnAwake = false;
			videoPlayer.renderMode = VideoRenderMode.RenderTexture;
			videoPlayer.waitForFirstFrame = true;
			videoPlayer.aspectRatio = VideoAspectRatio.FitInside;
			videoPlayer.audioOutputMode = VideoAudioOutputMode.Direct;
			videoPlayer.targetTexture = renderTexture;
			ShowTopic(0);
			base.PreOpen();
		}

		public override void PreClose()
		{
			videoPlayer.Stop();
			videoPlayer.targetTexture = null;
			base.PreClose();
			UnityEngine.Object.DestroyImmediate(videoPlayer, true);
			renderTexture.Release();
		}

		public void ShowTopic(int i)
		{
			var path = TopicPath(i);
			title = TopicTranslated(i);

			if (TopicType(i) == "image")
			{
				videoPlayer.Stop();
				if (topicTextures[i] == null)
				{
					topicTextures[i] = new Texture2D(1, 1, TextureFormat.ARGB32, false);
					topicTextures[i].LoadImage(File.ReadAllBytes(path));
				}
				currentTexture = topicTextures[i];
				return;
			}

			currentTexture = renderTexture;
			videoPlayer.Stop();
			videoPlayer.url = path;
			videoPlayer.frame = 0;
			videoPlayer.Play();
		}

		public override void DoWindowContents(Rect inRect)
		{
			var font = Text.Font;
			var titleRect = new Rect(listWidth + margin, 0f, inRect.width - listWidth - margin, titleHeight);
			Text.Font = GameFont.Medium;
			Widgets.Label(titleRect, title);
			Text.Font = GameFont.Small;

			var rowHeight = titleHeight * 2;
			var rowSpacing = titleHeight / 2;
			var hasScrollbar = topicResources.Length > 10;
			var viewRect = new Rect(0f, 0f, listWidth - (hasScrollbar ? 16 : 0), (rowHeight + rowSpacing) * topicResources.Length - rowSpacing);
			Widgets.BeginScrollView(new Rect(0f, 0f, listWidth, inRect.height), ref scrollPosition, viewRect, true);
			for (var i = 0; i < topicResources.Length; i++)
			{
				var r = new Rect(0f, (rowHeight + rowSpacing) * i, viewRect.width, rowHeight);
				if (Widgets.ButtonText(r, TopicTranslated(i)))
					ShowTopic(i);
			}
			Widgets.EndScrollView();

			if (currentTexture != null)
			{
				var previewRect = new Rect(listWidth + margin, titleHeight + margin, videoWidth, videoHeight);
				Widgets.DrawBoxSolid(previewRect, Color.black);
				GUI.DrawTexture(previewRect, currentTexture);
			}

			Text.Font = font;
		}
	}
}
