using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace AchtungMod
{
	// we render a copy of the colonist while dragging colonists to their destination
	//
	public class Marker : Thing
	{
		public static MethodInfo RenderPawnInternalMethod
		{
			get
			{
				Type[] paramTypes = new Type[] { typeof(Vector3), typeof(Quaternion), typeof(bool), typeof(Rot4), typeof(Rot4), typeof(RotDrawMode), typeof(bool) };
				MethodInfo methodInfo = typeof(PawnRenderer).GetMethod("RenderPawnInternal", BindingFlags.NonPublic | BindingFlags.Instance, null, paramTypes, null);
				if (methodInfo == null)
				{
					Log.Error("Method RenderPawnInternal not found in PawnRenderer");
				}
				return methodInfo;
			}
		}

		/*public override Graphic Graphic
		{
			get
			{
				return GraphicDatabase.Get<Graphic_Single>("Marker");
			}
		}*/

		public class MarkerThingDef : ThingDef
		{
			public Pawn pawn;
		}

		public static ThingDef ThingDef(Pawn pawn)
		{
			MarkerThingDef markerThingDef = new MarkerThingDef
			{
				pawn = pawn,
				category = ThingCategory.Ethereal,
				altitudeLayer = AltitudeLayer.MetaOverlays,
				drawerType = DrawerType.RealtimeOnly,
				label = "Marker",
				defName = "Marker",
				useHitPoints = false,
				selectable = false,
				seeThroughFog = true,
				size = new IntVec2(1, 1),
				thingClass = typeof(Marker)
			};
			ShortHashGiver.GiveShortHash(markerThingDef);
			return markerThingDef;
		}

		public Pawn ControlledPawn()
		{
			MarkerThingDef markerThingDef = (MarkerThingDef)def;
			return markerThingDef.pawn;
		}

		public override void DrawAt(Vector3 drawLoc)
		{
			Pawn pawn = ControlledPawn();
			PawnRenderer renderer = ControlledPawn().Drawer.renderer;

			// we always want to render the pawn from the front for easier identification so we cannot use pawn.DrawAt(drawLoc) 
			// because this will render the pawn pointing in different directions
			//
			Type[] paramTypes = new Type[] { typeof(Vector3), typeof(Quaternion), typeof(bool), typeof(Rot4), typeof(Rot4), typeof(RotDrawMode), typeof(bool) };
			MethodInfo method = renderer.GetType().GetMethod("RenderPawnInternal", BindingFlags.NonPublic | BindingFlags.Instance, null, paramTypes, null);
			if (RenderPawnInternalMethod != null)
			{
				object[] rendererParams = new object[] { drawLoc, Quaternion.identity, true, Rot4.South, Rot4.South, RotDrawMode.Fresh, false };
				RenderPawnInternalMethod.Invoke(renderer, rendererParams);
			}

			// base.DrawAt(drawLoc); - draws our marker but below the pawn :-(
			GenDraw.DrawTargetHighlight(new TargetInfo(this.TrueCenter().ToIntVec3())); // a nice alternative
		}

		public override void Tick()
		{
			// keep this to prevent NotImplemented warning
		}
	}
}
