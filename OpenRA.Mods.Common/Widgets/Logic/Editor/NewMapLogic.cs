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
using OpenRA.Widgets;
using OpenRA.Support;
using OpenRA.Traits;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	public class NewMapLogic : ChromeLogic
	{
		Widget panel;

		[ObjectCreator.UseCtor]
		public NewMapLogic(Action onExit, Action<string> onSelect, Widget widget, World world, ModData modData)
		{
			panel = widget;

			panel.Get<ButtonWidget>("CANCEL_BUTTON").OnClick = () => { Ui.CloseWindow(); onExit(); };

			var tilesetDropDown = panel.Get<DropDownButtonWidget>("TILESET");
			var tilesets = modData.DefaultTileSets.Select(t => t.Key).ToList();
			Func<string, ScrollItemWidget, ScrollItemWidget> setupItem = (option, template) =>
			{
				var item = ScrollItemWidget.Setup(template,
					() => tilesetDropDown.Text == option,
					() => { tilesetDropDown.Text = option; });
				item.Get<LabelWidget>("LABEL").GetText = () => option;
				return item;
			};
			tilesetDropDown.Text = tilesets.First();
			tilesetDropDown.OnClick = () =>
				tilesetDropDown.ShowDropDown("LABEL_DROPDOWN_TEMPLATE", 210, tilesets, setupItem);

			var widthTextField = panel.Get<TextFieldWidget>("WIDTH");
			var heightTextField = panel.Get<TextFieldWidget>("HEIGHT");

			panel.Get<ButtonWidget>("CREATE_BUTTON").OnClick = () =>
			{
				int width, height;
				int.TryParse(widthTextField.Text, out width);
				int.TryParse(heightTextField.Text, out height);

				// Require at least a 2x2 playable area so that the
				// ground is visible through the edge shroud
				width = Math.Max(2, width);
				height = Math.Max(2, height);

				var maxTerrainHeight = world.Map.Grid.MaximumTerrainHeight;
				var tileset = modData.DefaultTileSets[tilesetDropDown.Text];
				var map = new Map(Game.ModData, tileset, width + 2, height + maxTerrainHeight + 2);

				var tl = new PPos(1, 1 + maxTerrainHeight);
				var br = new PPos(width, height + maxTerrainHeight);
				map.SetBounds(tl, br);

				MapPlayers mapPlayers = new MapPlayers(map.Rules, 0);
				map.FixOpenAreas();

				// for map generation testing
				GenerateRandomMap(map, mapPlayers, world);

				// done after generating map, so map generation can add players
				map.PlayerDefinitions = mapPlayers.ToMiniYaml();

				Action<string> afterSave = uid =>
				{
					// HACK: Work around a synced-code change check.
					// It's not clear why this is needed here, but not in the other places that load maps.
					Game.RunAfterTick(() =>
					{
						ConnectionLogic.Connect(System.Net.IPAddress.Loopback.ToString(),
							Game.CreateLocalServer(uid), "",
							() => Game.LoadEditor(uid),
							() => { Game.CloseServer(); onExit(); });
					});

					Ui.CloseWindow();
					onSelect(uid);
				};

				Ui.OpenWindow("SAVE_MAP_PANEL", new WidgetArgs()
				{
					{ "onSave", afterSave },
					{ "onExit", () => { Ui.CloseWindow(); onExit(); } },
					{ "map", map },
					{ "playerDefinitions", map.PlayerDefinitions },
					{ "actorDefinitions", map.ActorDefinitions }
				});
			};
		}

		int mineDistance = 10;

		CVec GetRandomVecWithDistance(float distance, MersenneTwister rng)
		{
			var angle = rng.NextFloat() * Math.PI * 2;
			var x = distance * (float)Math.Cos(angle);
			var y = distance * (float)Math.Sin(angle);
			return new CVec((int)x, (int)y);
		}

		MPos GetMineLocation(MPos spawnLocation, MersenneTwister rng, Map map)
		{
			var spawnLocationCell = spawnLocation.ToCPos(map);

			// assumes this location is valid(inside map and not intersecting with enemy)
			return (spawnLocationCell + GetRandomVecWithDistance((float)mineDistance, rng)).ToMPos(map);
		}

		int actorNum = 0;
		int NextActorNumber()
		{
			return actorNum++;
		}

		bool CanPlacePlayer(MPos pos, int size, List<MPos> spawnLocations, Map map)
		{
			var cpos = pos.ToCPos(map);
			foreach (var location in spawnLocations)
			{
				var otherpos = location.ToCPos(map);
				var distance = (otherpos - cpos).Length;
				if (distance < size * 2 ) return false;
			}
			return true;
		}

		List<MPos> TryGetSpawnLocations(int playerNum, int playerLandSize, Map map, MersenneTwister rng, Rectangle bounds)
		{
			var spawnLocations = new List<MPos>();
			for (var i = 0; i < playerNum; i++)
			{
				var tries = 10;
				var success = false;
				for (var t = 0; t < tries; t++)
				{
					success = false;
					var pos = new MPos(rng.Next(bounds.Left, bounds.Right),
									   rng.Next(bounds.Top, bounds.Bottom));
					if (CanPlacePlayer(pos, playerLandSize, spawnLocations, map))
					{
						spawnLocations.Add(pos);
						success = true;
						break;
					}
				}
				if (!success) break;
			}
			return spawnLocations;
		}


		void GenerateRandomMap(Map map, MapPlayers mapPlayers, World world)
		{
			var players = mapPlayers.Players;
			var actors = map.Rules.Actors;
			var rng = world.SharedRandom;
			var mines = actors.Values.Where(a => a.TraitInfoOrDefault<SeedsResourceInfo>() != null && !a.Name.StartsWith("^"));
			var resources = world.WorldActor.TraitsImplementing<ResourceType>();

			if (!actors.ContainsKey("mpspawn")) return;
			if (!players.ContainsKey("Neutral")) return;
			if (!players.ContainsKey("Creeps")) return;
			if (!mines.Any()) return;

			var neutral = players["Neutral"];
			var creeps = players["Creeps"];

			var mpspawn = actors["mpspawn"];
			var mineInfo = mines.First();

			var playerMinDistance = 4;
			var playerLandSize = mineDistance + playerMinDistance;
			var playerNum = 5;

			var bounds = Rectangle.FromLTRB(
					map.Bounds.Left + playerLandSize,
					map.Bounds.Top + playerLandSize,
					map.Bounds.Right - playerLandSize,
					map.Bounds.Bottom - playerLandSize);

			if (bounds.Left >= bounds.Right || bounds.Top >= bounds.Bottom) return;

			var spawnLocations = new List<MPos>();
			var bestSpawnLocationsNum = 0;
			var bestSpawnLocations = new List<MPos>();
			var tries = 20;
			for (var t = 0; t < tries; t++)
			{
				spawnLocations = TryGetSpawnLocations(playerNum, playerLandSize, map, rng, bounds);
				if (spawnLocations.Count() == playerNum)
				{
					break;
				}
				else if (spawnLocations.Count() > bestSpawnLocationsNum)
				{
					bestSpawnLocations = spawnLocations;
					bestSpawnLocationsNum = spawnLocations.Count();
				}
			}
			if (spawnLocations.Count() != playerNum)
			{
				spawnLocations = bestSpawnLocations;
				playerNum = bestSpawnLocationsNum;
				Console.WriteLine("Couldn't place all players(placed "+spawnLocations.Count()+" instead of "+playerNum+")");
			}

			foreach (var location in spawnLocations)
			{
				// add spawn point
				var spawn = new ActorReference(mpspawn.Name);
				spawn.Add(new OwnerInit(neutral.Name));

				spawn.Add(new LocationInit(location.ToCPos(map)));

				map.ActorDefinitions.Add(new MiniYamlNode("Actor"+NextActorNumber(), spawn.Save()));

				// add mine
				var mine = new ActorReference(mineInfo.Name);
				mine.Add(new OwnerInit(neutral.Name));

				var mineLocation = GetMineLocation(location, rng, map).ToCPos(map);

				mine.Add(new LocationInit(mineLocation));

				map.ActorDefinitions.Add(new MiniYamlNode("Actor"+NextActorNumber(), mine.Save()));

				// add resources around mine
				// TODO: check like AllowResourceAt

				var resourceCells = new []
				{
					mineLocation,
					mineLocation + new CVec(1, 0),
					mineLocation + new CVec(-1, 0),
					mineLocation + new CVec(0, 1),
					mineLocation + new CVec(0, -1),
					mineLocation + new CVec(1, 1),
					mineLocation + new CVec(1, -1),
					mineLocation + new CVec(-1, 1),
					mineLocation + new CVec(-1, -1)
				};

				foreach (var cell in resourceCells)
				{
					var resourceName = mineInfo.TraitInfo<SeedsResourceInfo>().ResourceType;
					var resourceType = resources.Where(t => t.Info.Name == resourceName)
						.Select(t => t.Info).FirstOrDefault();

					var type = (byte)resourceType.ResourceType;
					var index = (byte)resourceType.MaxDensity;

					map.MapResources.Value[cell] = new ResourceTile(type, index);
				}
			}



			var creepEnemies = new List<string>();
			for (var i = 0; i < playerNum; i++)
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
