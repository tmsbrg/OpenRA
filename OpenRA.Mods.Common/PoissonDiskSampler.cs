#region Copyright & License Information
/*
 * Copyright 2007-2016 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using OpenRA.Support;

namespace OpenRA.Mods.Common
{
	public class PoissonDiskSampler
	{
		public int edgeDistance;
		public int newPointsCount;
		MersenneTwister rng;

		public PoissonDiskSampler(int edgeDistance, int newPointsCount, MersenneTwister rng)
		{
			this.newPointsCount = newPointsCount;
			this.rng = rng;
			this.edgeDistance = edgeDistance;
		}

		public List<CPos> Generate(Map map, int minDistance)
		{
			var processPoints = new List<CPos>();
			var samplePoints = new List<CPos>();

			var tl = map.AllCells.TopLeft.ToMPos(map);
			var br = map.AllCells.BottomRight.ToMPos(map);
			var topLeft = new MPos(tl.U + edgeDistance, tl.V + edgeDistance);
			var bottomRight = new MPos(br.U - edgeDistance, br.V - edgeDistance);

			// region too small
			if (bottomRight.U < topLeft.U || bottomRight.V < topLeft.V) return samplePoints;

			var region = new CellRegion(map.Grid.Type, topLeft.ToCPos(map), bottomRight.ToCPos(map));

			var firstPoint = region.ChooseRandomCell(rng);

			// HACK: hardcoding 1024(size of WPos in a CPos) we're actually building our own grid positions
			// based on WPos and the given minimal distance given in cells
			var cellSize = (float)minDistance * 1024.0f / (float)Math.Sqrt(2.0);

			// bunch of calculations to get width and height for our grid
			var brw = map.CenterOfCell(region.BottomRight);
			var grid = new CPos?[(int)Math.Ceiling(brw.X / cellSize),(int)Math.Ceiling(brw.Y / cellSize)];

			processPoints.Add(firstPoint);
			samplePoints.Add(firstPoint);
			var firstGridPoint = pointToGrid(firstPoint, map, cellSize);
			grid[firstGridPoint.X,firstGridPoint.Y] = firstPoint;

			while (processPoints.Count > 0)
			{
				var point = PopRandom(processPoints);
				for (var i = 0; i < newPointsCount; i++)
				{
					var newPoint = GeneratePointAround(point, minDistance);

					if (region.Contains(newPoint) && !inNeighbourhood(grid, newPoint, minDistance, map, cellSize))
					{
						processPoints.Add(newPoint);
						samplePoints.Add(newPoint);
						var gridPoint = pointToGrid(newPoint, map, cellSize);
						grid[gridPoint.X,gridPoint.Y] = newPoint;
					}
				}
			}

			return samplePoints;
		}

		CPos PopRandom(List<CPos> list)
		{
			int i = rng.Next(list.Count);
			var item = list[i];
			list.RemoveAt(i);
			return item;
		}

		CPos GeneratePointAround(CPos point, int minDistance)
		{
			var radius = minDistance * (rng.NextFloat() + 1);
			var angle = 2 * Math.PI * rng.NextFloat();
			return new CPos((int)(point.X + radius * Math.Cos(angle)), (int)(point.Y + radius * Math.Sin(angle)));
		}

		bool inGrid(int x, int y, CPos?[,] grid)
		{
			return x >= 0 && y >= 0 && x < grid.GetLength(0) && y < grid.GetLength(1);
		}

		bool inNeighbourhood(CPos?[,] grid, CPos point, int minDistance, Map map, float cellSize)
		{
			var gridPoint = pointToGrid(point, map, cellSize);
			for (var x = gridPoint.X - 2; x <= gridPoint.X + 2; x++)
			{
				for (var y = gridPoint.Y - 2; y <= gridPoint.Y + 2; y++)
				{
					if (inGrid(x, y, grid) && grid[x,y].HasValue && Distance(grid[x,y].Value, point) < (float)minDistance)
					{
						return true;
					}
				}
			}
			return false;
		}

		float Distance(CPos p1, CPos p2)
		{
			return (p1 - p2).Length;
		}

		// not really a normal CPos, this actually returns our GridPoint
		CPos pointToGrid(CPos point, Map map, float cellSize)
		{
			var wPoint = map.CenterOfCell(point);
			return new CPos((int)(wPoint.X / cellSize), (int)(wPoint.Y / cellSize));
		}
	}
}
