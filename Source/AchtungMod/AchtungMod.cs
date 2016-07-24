using CommunityCoreLibrary;
using Verse;
using System.Reflection;
using System;

namespace AchtungMod
{
    public class _Injector : SpecialInjector
    {
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
}