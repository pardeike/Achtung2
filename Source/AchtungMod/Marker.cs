using Verse;

namespace AchtungMod
{
	// simple graphic that we render while dragging colonists to their destination
	//
	public class Marker : Thing
	{
		public override Graphic Graphic
		{
			get
			{
				return GraphicDatabase.Get<Graphic_Single>("Marker");
			}
		}

		public override void Tick()
		{
			// keep this to prevent NotImplemented warning
		}
	}
}