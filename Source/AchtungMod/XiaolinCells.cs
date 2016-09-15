using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;

namespace AchtungMod
{
	public class XiaolinWu
	{
		public HashSet<IntVec3> cells;
		public double strongness;

		public static void swap(ref double a, ref double b)
		{
			double c = a; a = b; b = c;
		}

		public static int ipart(double x)
		{
			return (int)x;
		}

		public static int round(double x)
		{
			return ipart(x + 0.5);
		}

		public static double fpart(double x)
		{
			return x < 0 ? 1 - (x - Math.Floor(x)) : x - Math.Floor(x);
		}

		public static double rfpart(double x)
		{
			return 1 - fpart(x);
		}

		public void plot(int x, int y, double c)
		{
			if (c <= strongness) cells.Add(new IntVec3(x, 0, y));
		}

		public XiaolinWu(Vector3 start, bool includeStart, Vector3 end, bool includeEnd, float strongness)
		{
			cells = new HashSet<IntVec3>();
			this.strongness = strongness;

			double x0 = start.x;
			double y0 = start.z;
			double x1 = end.x;
			double y1 = end.z;

			bool steep = Math.Abs(y1 - y0) > Math.Abs(x1 - x0);
			if (steep)
			{
				swap(ref x0, ref y0);
				swap(ref x1, ref y1);
			}
			bool switched = (x0 > x1);
			if (switched)
			{
				swap(ref x0, ref x1);
				swap(ref y0, ref y1);
			}

			double dx = x1 - x0;
			double dy = y1 - y0;
			double gradient = dy / dx;

			int xend = round(x0);
			double yend = y0 + gradient * (xend - x0);
			double xgap = rfpart(x0 + 0.5);
			int xpxl1 = xend;
			int ypxl1 = ipart(yend);
			if ((includeStart && !switched) || (includeEnd && switched))
			{
				if (steep)
				{
					plot(ypxl1, xpxl1, rfpart(yend) * xgap);
					plot(ypxl1 + 1, xpxl1, fpart(yend) * xgap);
				}
				else
				{
					plot(xpxl1, ypxl1, rfpart(yend) * xgap);
					plot(xpxl1, ypxl1 + 1, fpart(yend) * xgap);
				}
			}
			double intery = yend + gradient;

			xend = round(x1);
			yend = y1 + gradient * (xend - x1);
			xgap = fpart(x1 + 0.5);
			int xpxl2 = xend;
			int ypxl2 = ipart(yend);
			if ((includeEnd && !switched) || (includeStart && switched))
			{
				if (steep)
				{
					plot(ypxl2, xpxl2, rfpart(yend) * xgap);
					plot(ypxl2 + 1, xpxl2, fpart(yend) * xgap);

				}
				else
				{
					plot(xpxl2, ypxl2, rfpart(yend) * xgap);
					plot(xpxl2, ypxl2 + 1, fpart(yend) * xgap);
				}
			}

			if (steep)
			{
				for (int x = xpxl1 + 1; x <= xpxl2 - 1; x++)
				{
					plot(ipart(intery), x, rfpart(intery));
					plot(ipart(intery) + 1, x, fpart(intery));
					intery = intery + gradient;
				}
			}
			else
			{
				for (int x = xpxl1 + 1; x <= xpxl2 - 1; x++)
				{
					plot(x, ipart(intery), rfpart(intery));
					plot(x, ipart(intery) + 1, fpart(intery));
					intery = intery + gradient;
				}
			}
		}
	}
}