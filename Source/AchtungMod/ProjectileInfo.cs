using UnityEngine;
using Verse;

namespace AchtungMod
{
	public class ProjectileInfo
	{
		public Thing equipment;
		public Thing launcher;
		public Vector3 origin;
		public TargetInfo targ;

		public ProjectileInfo(Thing launcher, Vector3 origin, TargetInfo targ, Thing equipment)
		{
			this.launcher = launcher;
			this.origin = origin;
			this.targ = targ;
			this.equipment = equipment;
		}
	}
}