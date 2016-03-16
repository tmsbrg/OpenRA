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
	public class MapGenerator
	{
		ActorInfo world;
		MersenneTwister rng;
		TileSet tileset;

		public MapGenerator(ActorInfo world, MersenneTwister rng, TileSet tileset)
		{
			this.world = world;
			this.rng = rng;
			this.tileset = tileset;
		}

		public Map GenerateEmpty(int width, int height)
		{
			// Require at least a 2x2 playable area so that the
			// ground is visible through the edge shroud
			width = Math.Max(2, width);
			height = Math.Max(2, height);

			var maxTerrainHeight = Game.ModData.Manifest.Get<MapGrid>().MaximumTerrainHeight;

			var map = new Map(Game.ModData, tileset, width + 2, height + maxTerrainHeight + 2);

			var tl = new PPos(1, 1);
			var br = new PPos(width, height + maxTerrainHeight);
			map.SetBounds(tl, br);

			map.PlayerDefinitions = new MapPlayers(map.Rules, 0).ToMiniYaml();
			map.FixOpenAreas();

			return map;
		}

		public Map GenerateRandom(int width, int height)
		{
			var map = GenerateEmpty(width, height);
			var mapPlayers = new MapPlayers(map.Rules, 0);

			GenerateRandomMap(map, mapPlayers, world);
			map.PlayerDefinitions = mapPlayers.ToMiniYaml();

			return map;
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

			var playerMinDistance = 4;
			var playerLandSize = mineDistance + playerMinDistance;
			var playerNum = 5;

			var startingResourceSize = 32;
			var startingMineNum = 2;
			var startingMineInterDistance = 4;

			var extraResourceSize = 42;
			var extraMineNum = 10;
			var extraMineDistance = 10;

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
				spawnLocations = TryGetLocations(playerNum, playerLandSize * 2, map, () => RandomLocation(bounds));
				if (spawnLocations.Count() == playerNum)
				{
					break;
				}
				else if (spawnLocations.Count() > bestSpawnLocations.Count())
				{
					bestSpawnLocations = spawnLocations;
				}
			}
			if (spawnLocations.Count() != playerNum)
			{
				spawnLocations = bestSpawnLocations;
				Console.WriteLine("MapGenerator: Couldn't place all players(placed "+spawnLocations.Count()+" instead of "+playerNum+")");
			}

			foreach (var location in spawnLocations)
			{
				// add spawn point
				var spawn = new ActorReference(mpspawn.Name);
				spawn.Add(new OwnerInit(neutral.Name));

				spawn.Add(new LocationInit(location.ToCPos(map)));

				map.ActorDefinitions.Add(new MiniYamlNode("Actor"+NextActorNumber(), spawn.Save()));

				var mineLocations = TryGetLocations(startingMineNum, startingMineInterDistance, map, () => GetMineLocation(location, map));

				// add mines around spawn points
				foreach (var mineLocation in mineLocations)
				{
					PlaceResourceMine(mineLocation, startingResourceSize, mineInfo, map, world);
				}
			}

			var extraResourceLocations = TryGetLocations(extraMineNum, () => RandomLocation(bounds),
					(p, locs) => CanPlaceActor(p, extraMineDistance, locs, map) &&
					 CanPlaceActor(p, playerLandSize, spawnLocations, map));

			foreach (var location in extraResourceLocations)
			{
				PlaceResourceMine(location, extraResourceSize, mineInfo, map, world);
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
