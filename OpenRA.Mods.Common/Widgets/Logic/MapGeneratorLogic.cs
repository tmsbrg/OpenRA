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
using System.IO;
using System.Linq;
using OpenRA;
using OpenRA.Widgets;
using OpenRA.FileSystem;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	// TODO: sync map generator settings
	public class MapGeneratorLogic : ChromeLogic
	{
		[ObjectCreator.UseCtor]
		internal MapGeneratorLogic(Widget widget, ModData modData, Action onExit, Action<string> onSelect, World world, TileSet tileset)
		{
			widget.Get<ButtonWidget>("BUTTON_CANCEL").OnClick = () => { Ui.CloseWindow(); onExit(); };
			widget.Get<ButtonWidget>("BUTTON_GENERATE").OnClick = () =>
			{
				var uid = GenerateMap(world, tileset);
				Ui.CloseWindow();
				onSelect(uid);
			};
		}

		string GenerateMap(World world, TileSet tileset)
		{
			var worldActor = Game.ModData.DefaultRules.Actors["world"];

			var mapGenerator = new MapGenerator(worldActor, world.SharedRandom, tileset);
			var map = mapGenerator.GenerateRandom(90, 90);

			// TODO: map saving should be in MapGenerator
			map.Title = "Random Map";
			map.Author = "Random map generator";
			map.Visibility = MapVisibility.Lobby;
			map.RequiresMod = Game.ModData.Manifest.Mod.Id;

			// TODO: test directory for write-access, and if there even is a directory, see SaveMapLogic.cs
			// TODO: also this places the map file in OpenRA's directory. Preferably we'd put it in ~/.openra
			var kv = Game.ModData.MapCache.MapLocations.First();
			var folder = kv.Key as IReadWritePackage;
			var classification = kv.Value;
			var path = Platform.ResolvePath(Path.Combine(folder.Name, "__random__.oramap"));

			// TODO: test that it actually saves correctly
			folder.Delete(path);
			var package = new ZipFile(Game.ModData.DefaultFileSystem, path, true);
			map.Save(package);

			Game.ModData.MapCache[map.Uid].UpdateFromMap(map.Package, folder, classification, null, map.Grid.Type);

			return map.Uid;
		}
	}
}
