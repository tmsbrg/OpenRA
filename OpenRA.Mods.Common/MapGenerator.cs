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
		public int playerMinDistance = 4;

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
	}

	public class MapGenerator
	{
		ActorInfo world;
		MersenneTwister rng;
		public MapGeneratorSettings settings = new MapGeneratorSettings();

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

		int mineDistance = 10;

		CVec GetRandomVecWithDistance(float distance)
		{
			var angle = rng.NextFloat() * Math.PI * 2;
			var x = distance * (float)Math.Cos(angle);
			var y = distance * (float)Math.Sin(angle);
			return new CVec((int)x, (int)y);
		}

		MPos GetMineLocation(MPos spawnLocation, Map map)
		{
			var spawnLocationCell = spawnLocation.ToCPos(map);

			// assumes this location is valid(inside map and not intersecting with enemy)
			return (spawnLocationCell + GetRandomVecWithDistance((float)mineDistance)).ToMPos(map);
		}

		int actorNum = 0;
		int NextActorNumber()
		{
			return actorNum++;
		}

		bool CanPlaceActor(MPos pos, int minDistance, List<MPos> locations, Map map)
		{
			var cpos = pos.ToCPos(map);
			foreach (var location in locations)
			{
				var otherpos = location.ToCPos(map);
				var distance = (otherpos - cpos).Length;
				if (distance < minDistance ) return false;
			}
			return true;
		}

		MPos RandomLocation(Rectangle bounds)
		{
			return new MPos(rng.Next(bounds.Left, bounds.Right), rng.Next(bounds.Top, bounds.Bottom));
		}

		List<MPos> TryGetLocations(int num, int minDistance, Map map, Func<MPos> getPos)
		{
			return TryGetLocations(num, getPos, (pos, locations) => CanPlaceActor(pos, minDistance, locations, map));
		}

		List<MPos> TryGetLocations(int num, Func<MPos> getPos, Func<MPos, List<MPos>, bool> checkPos)
		{
			var locations = new List<MPos>();
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

		void PlaceResourceMine(MPos location, int size, ActorInfo mineInfo, Map map, ActorInfo world)
		{
			var resources = world.TraitInfos<ResourceTypeInfo>();
			var resourceLayer = map.Resources;

			var mine = new ActorReference(mineInfo.Name);
			mine.Add(new OwnerInit("Neutral"));

			mine.Add(new LocationInit(location.ToCPos(map)));

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
					var cell = Util.RandomWalk(location.ToCPos(map), rng)
						.Take(size)
						.SkipWhile(p => !resourceLayer.Contains(p) ||
								resourceLayer[p].Type != 0)
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

		void PlaceDebris(MPos location, int size, int num, Map map)
		{
			var tileset = GetTileset();
			if (tileset.Generator == null || tileset.Generator.Debris == null
					|| tileset.Generator.Debris.LandDebris == null) return;

			var debris = tileset.Generator.Debris.LandDebris;

			for (var i = 0; i < num; i++)
			{
				var index = debris[rng.Next(0, debris.Count())];
				var template = tileset.Templates[index];
				CPos cell = location.ToCPos(map) + GetRandomVecWithDistance(rng.Next(0, size));

				PlaceTile(cell, template, map);
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

			var playerLandSize = mineDistance + settings.playerMinDistance;

			var bounds = Rectangle.FromLTRB(
					map.Bounds.Left + playerLandSize,
					map.Bounds.Top + playerLandSize,
					map.Bounds.Right - playerLandSize,
					map.Bounds.Bottom - playerLandSize);

			if (bounds.Left >= bounds.Right || bounds.Top >= bounds.Bottom) return;

			var spawnLocations = new List<MPos>();
			var bestSpawnLocations = new List<MPos>();
			var tries = 20;
			for (var t = 0; t < tries; t++)
			{
				spawnLocations = TryGetLocations(settings.playerNum, playerLandSize * 2, map, () => RandomLocation(bounds));
				if (spawnLocations.Count() == settings.playerNum)
				{
					break;
				}
				else if (spawnLocations.Count() > bestSpawnLocations.Count())
				{
					bestSpawnLocations = spawnLocations;
				}
			}
			if (spawnLocations.Count() != settings.playerNum)
			{
				spawnLocations = bestSpawnLocations;
				Console.WriteLine("MapGenerator: Couldn't place all players(placed "+spawnLocations.Count()+" instead of "+settings.playerNum+")");
			}

			foreach (var location in spawnLocations)
			{
				// add spawn point
				var spawn = new ActorReference(mpspawn.Name);
				spawn.Add(new OwnerInit(neutral.Name));

				spawn.Add(new LocationInit(location.ToCPos(map)));

				map.ActorDefinitions.Add(new MiniYamlNode("Actor"+NextActorNumber(), spawn.Save()));

				var mineLocations = TryGetLocations(settings.startingMineNum, settings.startingMineInterDistance, map, () => GetMineLocation(location, map));

				// add mines around spawn points
				foreach (var mineLocation in mineLocations)
				{
					PlaceResourceMine(mineLocation, settings.startingMineSize, mineInfo, map, world);
				}
			}

			var debrisLocations = TryGetLocations(settings.debrisNumGroups, () => RandomLocation(bounds),
					(p, locs) => CanPlaceActor(p, settings.debrisGroupSize, locs, map) &&
					 CanPlaceActor(p, playerLandSize, spawnLocations, map));

			foreach (var location in debrisLocations)
			{
				PlaceDebris(location, settings.debrisGroupSize, settings.debrisNumPerGroup, map);
			}

			var extraResourceLocations = TryGetLocations(settings.extraMineNum, () => RandomLocation(bounds),
					(p, locs) => CanPlaceActor(p, settings.extraMineDistance, locs, map) &&
					 CanPlaceActor(p, playerLandSize, spawnLocations, map));

			foreach (var location in extraResourceLocations)
			{
				PlaceResourceMine(location, settings.extraMineSize, mineInfo, map, world);
			}

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
