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
		Widget widget;
		MapGenerator mapGenerator;

		[ObjectCreator.UseCtor]
		internal MapGeneratorLogic(Widget widget, ModData modData, Action onExit, Action<string> onSelect, World world)
		{
			this.widget = widget;
			var worldActor = Game.ModData.DefaultRules.Actors["world"];
			mapGenerator = new MapGenerator(worldActor, world.SharedRandom);
			var settings = mapGenerator.settings;

			BindSetting("PLAYER_NUM", (x) => { settings.playerNum = x; }, () => { return settings.playerNum; });
			BindSetting("PLAYER_DIST", (x) => { settings.playerMinDistance = x; }, () => { return settings.playerMinDistance; });
			BindSetting("WIDTH", (x) => { settings.width = x; }, () => { return settings.width; });
			BindSetting("HEIGHT", (x) => { settings.height = x; }, () => { return settings.height; });
			BindSetting("STARTING_MINE_NUM", (x) => { settings.startingMineNum = x; }, () => { return settings.startingMineNum; });
			BindSetting("STARTING_MINE_DIST", (x) => { settings.startingMineDistance = x; }, () => { return settings.startingMineDistance; });
			BindSetting("STARTING_MINE_SIZE", (x) => { settings.startingMineSize = x; }, () => { return settings.startingMineSize; });
			BindSetting("STARTING_MINE_INTER_DIST", (x) => { settings.startingMineInterDistance = x; }, () => { return settings.startingMineInterDistance; });
			BindSetting("EXTRA_MINE_NUM", (x) => { settings.extraMineNum = x; }, () => { return settings.extraMineNum; });
			BindSetting("EXTRA_MINE_DIST", (x) => { settings.extraMineDistance = x; }, () => { return settings.extraMineDistance; });
			BindSetting("EXTRA_MINE_SIZE", (x) => { settings.extraMineSize = x; }, () => { return settings.extraMineSize; });
			BindSetting("DEBRIS_NUM_GROUPS", (x) => { settings.debrisNumGroups = x; }, () => { return settings.debrisNumGroups; });
			BindSetting("DEBRIS_NUM_PER_GROUP", (x) => { settings.debrisNumPerGroup = x; }, () => { return settings.debrisNumPerGroup; });
			BindSetting("DEBRIS_GROUP_SIZE", (x) => { settings.debrisGroupSize = x; }, () => { return settings.debrisGroupSize; });

			var tilesetDropDown = widget.GetOrNull<DropDownButtonWidget>("TILESET_DROPDOWN");
			if (tilesetDropDown != null)
			{
				var tilesets = modData.DefaultTileSets.Select(t => t.Key).ToList();
				tilesetDropDown.Text = settings.tileset;
				Func<string, ScrollItemWidget, ScrollItemWidget> setupItem = (option, template) =>
				{
					var item = ScrollItemWidget.Setup(template,
						() => tilesetDropDown.Text == option,
						() => { tilesetDropDown.Text = option; settings.tileset = option; });
					item.Get<LabelWidget>("LABEL").GetText = () => option;
					return item;
				};
				tilesetDropDown.OnClick = () =>
					tilesetDropDown.ShowDropDown("LABEL_DROPDOWN_TEMPLATE", 210, tilesets, setupItem);
			}

			widget.Get<ButtonWidget>("BUTTON_CANCEL").OnClick = () => { Ui.CloseWindow(); onExit(); };
			widget.Get<ButtonWidget>("BUTTON_GENERATE").OnClick = () =>
			{
				var uid = GenerateMap();
				Ui.CloseWindow();
				onSelect(uid);
			};
		}

		void BindSetting(string name, Action<int> setValue, Func<int> getValue)
		{
			var slider = widget.GetOrNull<SliderWidget>(name+"_SLIDER");
			var field = widget.GetOrNull<TextFieldWidget>(name+"_FIELD");
			if (slider == null || field == null) return;

			slider.OnChange += x =>
			{
				var v = (int)Math.Round(x);
				setValue(v);
				field.Text = v.ToString();
			};
			slider.GetValue = () => getValue();

			field.IsValid = () =>
			{
				int n;
				if (!int.TryParse(field.Text, out n)) return false;
				return n >= slider.MinimumValue && n <= slider.MaximumValue;
			};
			field.OnTextEdited = () =>
			{
				if (!field.IsValid()) return;
				setValue(int.Parse(field.Text));
			};
			field.OnLoseFocus = () =>
			{
				if (field.IsValid()) return;
				field.Text = getValue().ToString();
			};
			slider.Value = getValue();
			field.Text = getValue().ToString();
		}

		string GenerateMap()
		{
			var map = mapGenerator.GenerateRandom();

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
