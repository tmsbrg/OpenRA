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

		// TODO: UI
		public int cliffNum = 5;
		public int cliffAverageSize = 8;
		public int cliffSizeVariance = 5;
		public int cliffJitter = 40;

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

		CPos RandomLocation(Map map)
		{
			var br = map.ProjectedBottomRight;
			var tl = map.ProjectedTopLeft;
			var wpos = new WPos(rng.Next(tl.X, br.X), rng.Next(tl.Y, br.Y), 0);
			return map.CellContaining(wpos);
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

		List<CPos> TryGetLocations(int num, int minDistance, Map map, Func<CPos> getPos)
		{
			return TryGetLocations(num, getPos, (pos, locations) => CanPlaceActor(pos, minDistance, locations, map));
		}

		List<CPos> TryGetLocations(int num, Func<CPos> getPos, Func<CPos, List<CPos>, bool> checkPos)
		{
			var locations = new List<CPos>();
			for (var i = 0; i < num; i++)
			{
				var tries = 10;
				var success = false;
				for (var t = 0; t < tries; t++)
				{
					success = false;
					var pos = getPos();
					if (checkPos(pos, locations))
					{
						locations.Add(pos);
						success = true;
						break;
					}
				}
				if (!success) break;
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
				var tries = 10;
				var success = false;
				for (var t = 0; t < tries; t++)
				{
					var cell = Util.RandomWalk(location, rng)
						.Take(size)
						.SkipWhile(p => !resourceLayer.Contains(p) ||
								resourceLayer[p].Type != 0 ||
								(!isStartingMine && occupiedMap[p]))
						.Cast<CPos?>().FirstOrDefault();

					if (cell != null)
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
			var cliffDistance = cliffTileSize * settings.cliffAverageSize - settings.cliffSizeVariance;
			var cliffStartLocations = GetPoissonLocations(map, settings.cliffNum, cliffDistance, cliffTileSize, cliffTileSize);

			// draw lines from start location
			// determine size with settings for: average size, size random factor
			// setting for chance to take curved route
			// can grow from either end. Cannot get too close to spawn locations or each others' lines
			// line is a list of CPos. Cells of upper left corner of each tile

			// select tiles by looking at their position in the list, if previous tile is north and next is east, tile is
			// NE_I or NE_O depending on facing

			foreach (var location in cliffStartLocations)
			{
				var index = tileset.Generator.SimpleCliffs.WE_S[0];
				var template = tileset.Templates[index];
				PlaceTile(location, template, map);
				OccupyTilesInSquare(location, cliffTileSize);
			}
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

				// TODO: remove TryGetLocations
				var mineLocations = TryGetLocations(settings.startingMineNum, () => GetMineLocation(location, map),
						(p, locs) => CanPlaceActor(p, settings.startingMineInterDistance, locs, map) &&
						 map.Contains(p));

				// add mines around spawn points
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
