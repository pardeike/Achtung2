using CommunityCoreLibrary;
using Verse;
using System.Reflection;
using System;
using RimWorld;
using UnityEngine;
using System.Collections.Generic;

namespace AchtungMod
{
	public class BootInjector : SpecialInjector
	{
		// smart way to fetch the build version from the assembly
		//
		private static string _version = null;
		public static string Version
		{
			get
			{
				if (_version == null)
				{
					_version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
					string[] vparts = Version.Split(".".ToCharArray());
					if (vparts.Length > 3)
					{
						_version = vparts[0] + "." + vparts[1] + "." + vparts[2];
					}
				}
				return _version;
			}
		}

		// Our way in is the method RimWorld.Selector.SelectorOnGUI which we detour
		// to our own version in Patcher.cs
		//
		public override bool Inject()
		{
			var class1 = typeof(RimWorld.Selector);
			var class2 = typeof(Patcher);
			string methodName = "SelectorOnGUI";

			try
			{
				MethodInfo method1 = class1.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
				MethodInfo method2 = class2.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public);

				bool success = Detours.TryDetourFromTo(method1, method2);
				return success;
			}
			catch (Exception e)
			{
				Log.Error("Could not detour " + methodName);
				Log.Error("Exception " + e);
				return false;
			}
		}
	}

	// this will be called whenever a new game is loaded. we use it to notify the
	// user in case we are disabled
	//
	public class StartGameInjector : SpecialInjector
	{
		public override bool Inject()
		{
			if (Settings.modActive == false)
			{
				Find.WindowStack.Add(new Notification());
			}
			return true;
		}
	}
}