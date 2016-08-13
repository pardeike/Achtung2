using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;

namespace AchtungMod
{
	class Tools
	{
		// just like CheckboxLabeled but with a static text at the end (like iOS)
		//
		public static void ValueLabeled(Listing_Standard listing, string label, object value, string tooltip = null)
		{
			float lineHeight = Text.LineHeight;
			Rect rect = listing.GetRect(lineHeight);
			if (!tooltip.NullOrEmpty())
			{
				if (Mouse.IsOver(rect))
				{
					Widgets.DrawHighlight(rect);
				}
				TooltipHandler.TipRegion(rect, tooltip);
			}

			TextAnchor savedAnchor = Text.Anchor;

			Text.Anchor = TextAnchor.MiddleLeft;
			Widgets.Label(rect, label);

			Text.Anchor = TextAnchor.MiddleRight;
			Widgets.Label(rect, value.ToString());

			Text.Anchor = savedAnchor;

			listing.Gap(listing.verticalSpacing);
		}

		// ugly hack to find a types fullname in any existing assembly
		//
		public static List<Type> FindTypesInSolution(string typeFullName)
		{
			List<Assembly> assemblies = AppDomain.CurrentDomain.GetAssemblies().ToList()
				.FindAll(a => a.FullName.StartsWith("System,") == false)
				.FindAll(a => a.FullName.StartsWith("System.") == false)
				.FindAll(a => a.FullName.StartsWith("mscorlib,") == false)
				.FindAll(a => a.FullName.StartsWith("Community Core Library,") == false)
				.FindAll(a => a.FullName.StartsWith("UnityEngine.") == false)
				.FindAll(a => a.FullName.StartsWith("AchtungMod,") == false)
				.FindAll(a => a.FullName.StartsWith("Assembly-CSharp") == false);
			List<Type> result = new List<Type>();
			assemblies.ForEach(assembly => result.AddRange(assembly.GetTypes().ToList().FindAll(t => t.FullName == typeFullName)));
			return result;
		}

		// removes the last part (parts defined by a separator)
		//
		public static string RemoveLastPartSeparatedBy(string str, char separator)
		{
			List<string> parts = str.Split(new char[] { separator }).ToList();
			parts.RemoveLast();
			return String.Join(separator.ToString(), parts.ToArray());
		}

		// Checks if a type has all named methods (simple check, not including arguments)
		//
		public static bool HasAllMethods(Type type, string[] methodNames)
		{
			List<string> matches = methodNames.ToList().FindAll(name =>
			{
				return type.GetMethod(name) != null ||
					type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static) != null ||
					type.GetMethod(name, BindingFlags.Public | BindingFlags.Static) != null;
			});
			return methodNames.Length == matches.Count;
		}

		// Ugly hack for mods that detour FloatMenuMakerMap
		//
		public static Type TypeOfFloatMenuMakerMap = null;
		public static Type GetTypeOfFloatMenuMakerMap()
		{
			string[] methodsRequired = new string[] { "AddDraftedOrders", "AddUndraftedOrders", "AddHumanlikeOrders" };

			if (TypeOfFloatMenuMakerMap == null)
			{
				// this is the original FloatMenuMakerMap
				TypeOfFloatMenuMakerMap = typeof(RimWorld.FloatMenuMakerMap);

				// let's see if Community Core Library has a detour for it
				FieldInfo detouredField = typeof(CommunityCoreLibrary.Detours).GetField("detoured", BindingFlags.NonPublic | BindingFlags.Static);
				FieldInfo destinationsField = typeof(CommunityCoreLibrary.Detours).GetField("destinations", BindingFlags.NonPublic | BindingFlags.Static);
				if (detouredField != null && destinationsField != null)
				{
					List<string> detoured = (List<string>)detouredField.GetValue(null);
					string sourceString = detoured.FindLast(detour => detour.StartsWith(TypeOfFloatMenuMakerMap.FullName + "."));
					if (sourceString != null)
					{
						List<string> destinations = (List<string>)destinationsField.GetValue(null);
						string destinationString = destinations[detoured.IndexOf(sourceString)];
						string fullname = RemoveLastPartSeparatedBy(destinationString, '.');
						List<Type> types = FindTypesInSolution(fullname).FindAll(t => HasAllMethods(t, methodsRequired));
						if (types.Count == 1)
						{
							// seems we found an implementation
							TypeOfFloatMenuMakerMap = types.First();
						}
					}
				}
			}

			return TypeOfFloatMenuMakerMap;
		}
	}
}