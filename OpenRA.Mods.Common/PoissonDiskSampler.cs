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
		public int width;
		public int height;
		public int newPointsCount;
		MersenneTwister rng;

		public PoissonDiskSampler(int width, int height, int newPointsCount, MersenneTwister rng)
		{
			this.width = width;
			this.height = height;
			this.newPointsCount = newPointsCount;
			this.rng = rng;
		}

		public List<CPos> Generate(int minDistance)
		{
			float cellSize = (float)minDistance / (float)Math.Sqrt(2.0);

			var grid = new CPos?[(int)Math.Ceiling(width / cellSize),(int)Math.Ceiling(height / cellSize)];

			var processPoints = new List<CPos>();
			var samplePoints = new List<CPos>();

			var firstPoint = new CPos(rng.Next(0, width), rng.Next(0, height));

			processPoints.Add(firstPoint);
			samplePoints.Add(firstPoint);
			var firstGridPoint = pointToGrid(firstPoint, cellSize);
			grid[firstGridPoint.X,firstGridPoint.Y] = firstPoint;

			while (processPoints.Count > 0)
			{
				var point = PopRandom(processPoints);
				for (var i = 0; i < newPointsCount; i++)
				{
					var newPoint = GeneratePointAround(point, minDistance);

					if (inBounds(newPoint.X, newPoint.Y) && !inNeighbourhood(grid, newPoint, minDistance, cellSize))
					{
						processPoints.Add(newPoint);
						samplePoints.Add(newPoint);
						var gridPoint = pointToGrid(newPoint, cellSize);
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

		bool inBounds(int x, int y)
		{
			return x >= 0 && y >= 0 && x < width && y < height;
		}

		bool inGrid(int x, int y, CPos?[,] grid)
		{
			return x >= 0 && y >= 0 && x < grid.GetLength(0) && y < grid.GetLength(1);
		}

		bool inNeighbourhood(CPos?[,] grid, CPos point, int minDistance, float cellSize)
		{
			var gridPoint = pointToGrid(point, cellSize);
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

		CPos pointToGrid(CPos point, float cellSize)
		{
			return new CPos((int)(point.X / cellSize), (int)(point.Y / cellSize));
		}
	}
}
