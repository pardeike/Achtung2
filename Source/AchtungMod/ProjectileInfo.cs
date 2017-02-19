using UnityEngine;
using Verse;

namespace AchtungMod
{
	public class ProjectileInfo
	{
		public Thing equipment;
		public Thing launcher;
		public Vector3 origin;
		public LocalTargetInfo targ;

		public ProjectileInfo(Thing launcher, Vector3 origin, LocalTargetInfo targ, Thing equipment)
		{
			this.launcher = launcher;
			this.origin = origin;
			this.targ = targ;
			this.equipment = equipment;
		}
	}
}