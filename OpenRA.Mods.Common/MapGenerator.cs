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
using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Support;
using OpenRA.Traits;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Mods.Common
{
	public class MapGeneratorSettings
	{
		public int playerNum = 4;
		public int playerDistFromCenter = 70;

		public int width = 90;
		public int height = 90;
		public string tileset = "";

		public int startingMineNum = 2;
		public int startingMineDistance = 10;
		public int startingMineSize = 32;
		public int startingMineInterDistance = 4;

		public int extraMineNum = 10;
		public int extraMineDistance = 10;
		public int extraMineSize = 42;

		public int debrisNumGroups = 20;
		public int debrisNumPerGroup = 7;
		public int debrisGroupSize = 6;

		public int cliffNum = 7;
		public int cliffAverageSize = 8;
		public int cliffSizeVariance = 5;
		public int cliffJitter = 40;

	}

	public class MapGenerator
	{
		public MapGeneratorSettings settings = new MapGeneratorSettings();
		ActorInfo world;
		MersenneTwister rng;
		CellLayer<bool> occupiedMap;

		public MapGenerator(ActorInfo world, MersenneTwister rng)
		{
			this.world = world;
			this.rng = rng;
			this.settings.tileset = Game.ModData.DefaultTileSets.Select(t => t.Key).First();
		}

		public Map GenerateEmpty()
		{
			// Require at least a 2x2 playable area so that the
			// ground is visible through the edge shroud
			var width = Math.Max(2, settings.width);
			var height = Math.Max(2, settings.height);

			var maxTerrainHeight = Game.ModData.Manifest.Get<MapGrid>().MaximumTerrainHeight;

			var tileset = GetTileset();
			var map = new Map(Game.ModData, tileset, width + 2, height + maxTerrainHeight + 2);

			var tl = new PPos(1, 1);
			var br = new PPos(width, height + maxTerrainHeight);
			map.SetBounds(tl, br);

			map.PlayerDefinitions = new MapPlayers(map.Rules, 0).ToMiniYaml();
			map.FixOpenAreas();

			return map;
		}

		public Map GenerateRandom()
		{
			var map = GenerateEmpty();
			var mapPlayers = new MapPlayers(map.Rules, 0);

			GenerateRandomMap(map, mapPlayers, world);
			map.PlayerDefinitions = mapPlayers.ToMiniYaml();

			return map;
		}

		TileSet GetTileset()
		{
			return Game.ModData.DefaultTileSets[settings.tileset];
		}

		CVec GetRandomVecWithDistance(float distance)
		{
			var angle = rng.NextFloat() * Math.PI * 2;
			var x = distance * (float)Math.Cos(angle);
			var y = distance * (float)Math.Sin(angle);
			return new CVec((int)x, (int)y);
		}

		CPos GetMineLocation(CPos spawnLocation, Map map)
		{
			// assumes this location is valid(inside map and not intersecting with enemy)
			return spawnLocation + GetRandomVecWithDistance((float)settings.startingMineDistance);
		}

		int actorNum = 0;
		int NextActorNumber()
		{
			return actorNum++;
		}

		bool CanPlaceActor(CPos pos, int minDistance, List<CPos> locations, Map map)
		{
			foreach (var location in locations)
			{
				var otherpos = location;
				var distance = (otherpos - pos).Length;
				if (distance < minDistance ) return false;
			}
			return true;
		}

		List<CPos> GetPoissonLocations(Map map, int num, int minDistance, int edgeDistance, int size=1)
		{
			var sampler = new PoissonDiskSampler(edgeDistance, 6, rng);

			var points = sampler.Generate(map, minDistance);
			var locations = new List<CPos>(num);
			while (locations.Count < num && points.Count > 0)
			{
				int i = rng.Next(points.Count);
				var point = points[i];
				points.RemoveAt(i);
				if (!IsAreaOccupied(point, size))
				{
					locations.Add(point);
				}
			}
			return locations;
		}

		List<CPos> GetSpawnLocations(Map map)
		{
			// map center function?
			var br = map.ProjectedBottomRight;
			var tl = map.ProjectedTopLeft;
			var width = br.X - tl.X;
			var height = br.Y - tl.Y;
			var center = tl + new WVec(width / 2, height / 2, 0);

			var distWidth = (float)settings.playerDistFromCenter / 100.0f * width * 0.5f;
			var distHeight = (float)settings.playerDistFromCenter / 100.0f * height * 0.5f;
			var angle_per_player = Math.PI * 2 / settings.playerNum;


			var angle = rng.NextFloat() * Math.PI * 2;
			var spawnLocations = new List<CPos>();
			for (var i = 0; i < settings.playerNum; i++)
			{
				var x = distWidth * (float)Math.Cos(angle);
				var y = distHeight * (float)Math.Sin(angle);
				var pos = map.CellContaining(center + new WVec((int)x, (int)y, 0));
				spawnLocations.Add(pos);
				angle += angle_per_player;
			}
			return spawnLocations;
		}

		void PlaceResourceMine(CPos location, int size, ActorInfo mineInfo, Map map, ActorInfo world, bool isStartingMine=false)
		{
			var resources = world.TraitInfos<ResourceTypeInfo>();
			var resourceLayer = map.Resources;

			var mine = new ActorReference(mineInfo.Name);
			mine.Add(new OwnerInit("Neutral"));

			mine.Add(new LocationInit(location));

			map.ActorDefinitions.Add(new MiniYamlNode("Actor"+NextActorNumber(), mine.Save()));

			// add resources around mine
			var resourceName = mineInfo.TraitInfo<SeedsResourceInfo>().ResourceType;
			var resourceType = resources.Where(t => t.Name == resourceName).FirstOrDefault();

			var type = (byte)resourceType.ResourceType;
			var index = (byte)resourceType.MaxDensity;

			for (var i = 0; i < size; i++)
			{
				var tries = 50;
				var success = false;
				for (var t = 0; t < tries; t++)
				{
					var cell = Util.RandomWalk(location, rng)
						.Take(size)
						.SkipWhile(p => !resourceLayer.Contains(p) ||
								resourceLayer[p].Type != 0)
						.Cast<CPos?>().FirstOrDefault();

					if (cell.HasValue && (isStartingMine || !occupiedMap[cell.Value]))
					{
						resourceLayer[cell.Value] = new ResourceTile(type, index);
						success = true;
						break;
					}
				}
				if (!success)
				{
					break;
				}
			}
		}

		void PlaceDebris(CPos location, int size, int num, Map map)
		{
			var tileset = GetTileset();
			if (tileset.Generator == null || tileset.Generator.Debris == null
					|| tileset.Generator.Debris.LandDebris == null) return;

			var debris = tileset.Generator.Debris.LandDebris;

			for (var i = 0; i < num; i++)
			{
				var index = debris[rng.Next(0, debris.Count())];
				var template = tileset.Templates[index];
				CPos cell = location + GetRandomVecWithDistance(rng.Next(0, size));

				if (!IsAreaOccupied(cell, 1))
				{
					PlaceTile(cell, template, map);
					OccupyTilesInSquare(cell, 1);
				}
			}
		}

		void PlaceTile(CPos cell, TerrainTemplateInfo template, Map map)
		{
			var mapTiles = map.Tiles;
			var mapHeight = map.Height;

			var baseHeight = mapHeight.Contains(cell) ? mapHeight[cell] : (byte)0;

			var i = 0;
			for (var y = 0; y < template.Size.Y; y++)
			{
				for (var x = 0; x < template.Size.X; x++, i++)
				{
					if (template.Contains(i) && template[i] != null)
					{
						var index = template.PickAny ? (byte)Game.CosmeticRandom.Next(0, template.TilesCount) : (byte)i;
						var c = cell + new CVec(x, y);
						if (!mapTiles.Contains(c))
							continue;

						mapTiles[c] = new TerrainTile(template.Id, index);
						mapHeight[c] = (byte)(baseHeight + template[index].Height).Clamp(0, map.Grid.MaximumTerrainHeight);
					}
				}
			}
		}


		void AddCliffs(Map map)
		{
			var tileset = GetTileset();
			if (tileset.Generator == null || tileset.Generator.SimpleCliffs == null) return;

			var cliffTileSize = 2;
			var cliffDistance = cliffTileSize * 4; // arbitrary, but to keep them from being too close
			var cliffStartLocations = GetPoissonLocations(map, settings.cliffNum, cliffDistance, cliffTileSize, cliffTileSize);

			var cliffDirections = new []
			{
				new CVec(cliffTileSize, 0),
				new CVec(0, cliffTileSize),
				new CVec(-cliffTileSize, 0),
				new CVec(0, -cliffTileSize)
			};
			foreach (var startLocation in cliffStartLocations)
			{
				var cliffsDirection1 = new List<CPos>(settings.cliffNum);
				var cliffsDirection2 = new List<CPos>(settings.cliffNum);
				var direction1 = rng.Next(4);
				var direction2 = rng.Next(3);
				if (direction2 >= direction1) direction2++;

				var size = rng.Next(settings.cliffAverageSize - settings.cliffSizeVariance,
						settings.cliffAverageSize + settings.cliffSizeVariance);
				if (size < 2) size = 2;
				for (var i = 0; i < size; i++)
				{
					List<CPos> list;
					CVec direction;
					if (rng.Next(2) > 0) // 50%
					{
						list = cliffsDirection1;
						direction = cliffDirections[direction1];
						direction1 = JitterCliff(direction1);
					}
					else
					{
						list = cliffsDirection2;
						direction = cliffDirections[direction2];
						direction2 = JitterCliff(direction2);
					}
					var oldPos = (list.Count > 0) ? list.Last() : startLocation;
					var pos = oldPos + direction;
					if (!IsAreaOccupied(pos, cliffTileSize))
					{
						list.Add(pos);
						OccupyTilesInSquare(pos, cliffTileSize);
					}
				}

				CPos? prevPos = null;
				CPos? currPos = null;
				foreach (var nextPos in EnumerateCliffs(cliffsDirection1, startLocation, cliffsDirection2))
				{
					if (currPos.HasValue)
					{
						PlaceCliff(currPos.Value, prevPos, nextPos, tileset, map);
					}
					prevPos = currPos;
					currPos = nextPos;
				}
				if (currPos.HasValue)
				{
					PlaceCliff(currPos.Value, prevPos, null, tileset, map);
				}
			}
		}

		int JitterCliff(int direction)
		{
			if (rng.Next(100) < settings.cliffJitter)
			{
				var change = (rng.Next(2) > 0) ? 1 : -1;
				return (direction + 4 + change) % 4;
			}
			return direction;
		}

		IEnumerable<CPos> EnumerateCliffs(List<CPos> cliffsDirection1, CPos startLocation, List<CPos> cliffsDirection2)
		{
			foreach (var pos in Enumerable.Reverse(cliffsDirection1))
			{
				yield return pos;
			}
			yield return startLocation;
			foreach (var pos in cliffsDirection2)
			{
				yield return pos;
			}
			yield break;
		}

		enum Direction {
			NORTH,
			EAST,
			SOUTH,
			WEST
		}

		void PlaceCliff(CPos pos, CPos? prevPos, CPos? nextPos, TileSet tileset, Map map)
		{
			var tiles = tileset.Generator.SimpleCliffs;
			var cliffType = tiles.WE;
			if (!prevPos.HasValue && nextPos.HasValue)
			{
				var dir = GetDirection(pos, nextPos.Value);
				switch (dir)
				{
					case Direction.NORTH:
						cliffType = tiles._N;
						break;
					case Direction.EAST:
						cliffType = tiles._E;
						break;
					case Direction.SOUTH:
						cliffType = tiles._S;
						break;
					case Direction.WEST:
						cliffType = tiles._W;
						break;
				}
			}
			else if (prevPos.HasValue && !nextPos.HasValue)
			{
				var dir = GetDirection(pos, prevPos.Value);
				switch (dir)
				{
					case Direction.NORTH:
						cliffType = tiles.N_;
						break;
					case Direction.EAST:
						cliffType = tiles.E_;
						break;
					case Direction.SOUTH:
						cliffType = tiles.S_;
						break;
					case Direction.WEST:
						cliffType = tiles.W_;
						break;
				}
			}
			else if (prevPos.HasValue && nextPos.HasValue)
			{
				var dirPrev = GetDirection(pos, prevPos.Value);
				var dirNext = GetDirection(pos, nextPos.Value);
				switch(dirPrev)
				{
					case Direction.NORTH:
						switch(dirNext)
						{
							case Direction.EAST:
								cliffType = tiles.NE;
								break;
							case Direction.SOUTH:
								cliffType = tiles.NS;
								break;
							case Direction.WEST:
								cliffType = tiles.NW;
								break;
						}
						break;
					case Direction.EAST:
						switch(dirNext)
						{
							case Direction.NORTH:
								cliffType = tiles.EN;
								break;
							case Direction.SOUTH:
								cliffType = tiles.ES;
								break;
							case Direction.WEST:
								cliffType = tiles.EW;
								break;
						}
						break;
					case Direction.SOUTH:
						switch(dirNext)
						{
							case Direction.NORTH:
								cliffType = tiles.SN;
								break;
							case Direction.EAST:
								cliffType = tiles.SE;
								break;
							case Direction.WEST:
								cliffType = tiles.SW;
								break;
						}
						break;
					case Direction.WEST:
						switch(dirNext)
						{
							case Direction.NORTH:
								cliffType = tiles.WN;
								break;
							case Direction.EAST:
								cliffType = tiles.WE;
								break;
							case Direction.SOUTH:
								cliffType = tiles.WS;
								break;
						}
						break;
				}
			}
			var index = cliffType[rng.Next(cliffType.Count())];
			var template = tileset.Templates[index];
			PlaceTile(pos, template, map);
		}

		// assumes given CPos differ only on one axis, and are never the same position
		Direction GetDirection(CPos from, CPos to)
		{
			if (from.Y > to.Y) return Direction.NORTH;
			if (from.X > to.X) return Direction.WEST;
			if (from.Y < to.Y) return Direction.SOUTH;
			return Direction.EAST;
		}

		bool IsAreaOccupied(CPos pos, int size)
		{
			for (var x = 0; x < size; x++)
			{
				for (var y = 0; y < size; y++)
				{
					var p = pos + new CVec(x, y);
					if (occupiedMap.Contains(p) && occupiedMap[pos + new CVec(x, y)]) return true;
				}
			}
			return false;
		}

		void OccupyTilesInSquare(CPos pos, int size)
		{
			for (var x = 0; x < size; x++)
			{
				for (var y = 0; y < size; y++)
				{
					var p = pos + new CVec(x, y);
					if (occupiedMap.Contains(p)) occupiedMap[pos + new CVec(x, y)] = true;
				}
			}
		}


		// TODO: use built in Map.FindTilesInCircle
		void OccupyTilesInDisk(CPos pos, int radius)
		{
			for (var y = -radius; y <= radius; y++)
			{
				for (var x = -radius; x <= radius; x++)
				{
					var p = pos + new CVec(x, y);
					if (occupiedMap.Contains(p) && (x * x) + (y * y) <= radius * radius)
					{
						occupiedMap[p] = true;
					}
				}
			}
		}

		void GenerateRandomMap(Map map, MapPlayers mapPlayers, ActorInfo world)
		{
			var players = mapPlayers.Players;
			var actors = map.Rules.Actors;
			var mineTypes = actors.Values.Where(a => a.TraitInfoOrDefault<SeedsResourceInfo>() != null && !a.Name.StartsWith("^"));

			if (!actors.ContainsKey("mpspawn")) return;
			if (!players.ContainsKey("Neutral")) return;
			if (!players.ContainsKey("Creeps")) return;
			if (!mineTypes.Any()) return;

			var neutral = players["Neutral"];
			var creeps = players["Creeps"];

			var mpspawn = actors["mpspawn"];
			var mineInfo = mineTypes.First();

			var minDistFromPlayers = 5;
			var playerLandSize = settings.startingMineDistance + minDistFromPlayers;

			// contains true for every cell that's occupied, false otherwise
			occupiedMap = new CellLayer<bool>(map);

			// Create spawn points
			var spawnLocations = GetSpawnLocations(map);
			foreach (var location in spawnLocations)
			{
				var spawn = new ActorReference(mpspawn.Name);
				spawn.Add(new OwnerInit(neutral.Name));

				spawn.Add(new LocationInit(location));

				map.ActorDefinitions.Add(new MiniYamlNode("Actor"+NextActorNumber(), spawn.Save()));

				// add mines around spawn points
				var mineLocations = new List<CPos>();
				for (var i = 0; i < settings.startingMineNum; i++)
				{
					var tries = 10;
					var success = false;
					for (var t = 0; t < tries; t++)
					{
						success = false;
						var pos = GetMineLocation(location, map);
						if (CanPlaceActor(pos, settings.startingMineInterDistance, mineLocations, map))
						{
							mineLocations.Add(pos);
							success = true;
							break;
						}
					}
					if (!success) break;
				}
				foreach (var mineLocation in mineLocations)
				{
					PlaceResourceMine(mineLocation, settings.startingMineSize, mineInfo, map, world, true);
				}

				OccupyTilesInDisk(location, playerLandSize);
			}

			AddCliffs(map);

			// Debris
			var debrisLocations = GetPoissonLocations(map, settings.debrisNumGroups, settings.debrisGroupSize,
					settings.debrisGroupSize);

			foreach (var location in debrisLocations)
			{
				PlaceDebris(location, settings.debrisGroupSize, settings.debrisNumPerGroup, map);
			}

			// Extra resources
			var extraResourceLocations = GetPoissonLocations(map, settings.extraMineNum, settings.extraMineDistance,
					settings.extraMineDistance);

			foreach (var location in extraResourceLocations)
			{
				PlaceResourceMine(location, settings.extraMineSize, mineInfo, map, world);
			}

			// Add players
			var creepEnemies = new List<string>();
			for (var i = 0; i < spawnLocations.Count(); i++)
			{
				var name = "Multi"+i;
				var player = new PlayerReference
				{
					Name = name,
					Playable = true,
					Faction = "Random",
					Enemies = new [] { "Creeps" }
				};
				players.Add(name, player);
				creepEnemies.Add(name);
			}
			creeps.Enemies = creepEnemies.ToArray();
		}
	}
}
