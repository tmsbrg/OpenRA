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
using System.IO;
using System.Linq;
using OpenRA.FileSystem;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	public class SaveMapLogic : ChromeLogic
	{
		enum MapFileType { Unpacked, OraMap }

		struct MapFileTypeInfo
		{
			public string Extension;
			public string UiLabel;
		}

		class SaveDirectory
		{
			public readonly Folder Folder;
			public readonly string DisplayName;
			public readonly MapClassification Classification;

			public SaveDirectory(Folder folder, MapClassification classification)
			{
				Folder = folder;
				DisplayName = Platform.UnresolvePath(Folder.Name);
				Classification = classification;
			}
		}

		[ObjectCreator.UseCtor]
		public SaveMapLogic(Widget widget, ModData modData, Action<string> onSave, Action onExit,
			Map map, List<MiniYamlNode> playerDefinitions, List<MiniYamlNode> actorDefinitions)
		{
			var title = widget.Get<TextFieldWidget>("TITLE");
			title.Text = map.Title;

			var author = widget.Get<TextFieldWidget>("AUTHOR");
			author.Text = map.Author;

			// TODO: This should use a multi-selection dropdown once they exist
			var visibilityDropdown = widget.Get<DropDownButtonWidget>("VISIBILITY_DROPDOWN");
			{
				var mapVisibility = new List<string>(Enum.GetNames(typeof(MapVisibility)));
				Func<string, ScrollItemWidget, ScrollItemWidget> setupItem = (option, template) =>
				{
					var item = ScrollItemWidget.Setup(template,
						() => visibilityDropdown.Text == option,
						() => { visibilityDropdown.Text = option; });
					item.Get<LabelWidget>("LABEL").GetText = () => option;
					return item;
				};

				visibilityDropdown.Text = Enum.GetName(typeof(MapVisibility), map.Visibility);
				visibilityDropdown.OnClick = () =>
					visibilityDropdown.ShowDropDown("LABEL_DROPDOWN_TEMPLATE", 210, mapVisibility, setupItem);
			}

			var writableDirectories = new List<SaveDirectory>();
			SaveDirectory selectedDirectory = null;

			var directoryDropdown = widget.Get<DropDownButtonWidget>("DIRECTORY_DROPDOWN");
			{
				Func<SaveDirectory, ScrollItemWidget, ScrollItemWidget> setupItem = (option, template) =>
				{
					var item = ScrollItemWidget.Setup(template,
						() => selectedDirectory == option,
						() => selectedDirectory = option);
					item.Get<LabelWidget>("LABEL").GetText = () => option.DisplayName;
					return item;
				};

				foreach (var kv in modData.MapCache.GetWriteableMapLocations())
				{
					writableDirectories.Add(new SaveDirectory(kv.Key as Folder, kv.Value));
				}

				if (map.Package != null)
					selectedDirectory = writableDirectories.FirstOrDefault(k => k.Folder.Contains(map.Package.Name));

				// Prioritize MapClassification.User directories over system directories
				if (selectedDirectory == null)
					selectedDirectory = writableDirectories.OrderByDescending(kv => kv.Classification).First();

				directoryDropdown.GetText = () => selectedDirectory == null ? "" : selectedDirectory.DisplayName;
				directoryDropdown.OnClick = () =>
					directoryDropdown.ShowDropDown("LABEL_DROPDOWN_TEMPLATE", 210, writableDirectories, setupItem);
			}

			var mapIsUnpacked = map.Package != null && (map.Package is Folder || map.Package is ZipFolder);

			var filename = widget.Get<TextFieldWidget>("FILENAME");
			filename.Text = map.Package == null ? "" : mapIsUnpacked ? Path.GetFileName(map.Package.Name) : Path.GetFileNameWithoutExtension(map.Package.Name);
			var fileType = mapIsUnpacked ? MapFileType.Unpacked : MapFileType.OraMap;

			var fileTypes = new Dictionary<MapFileType, MapFileTypeInfo>()
			{
				{ MapFileType.OraMap, new MapFileTypeInfo { Extension = ".oramap", UiLabel = ".oramap" } },
				{ MapFileType.Unpacked, new MapFileTypeInfo { Extension = "", UiLabel = "(unpacked)" } }
			};

			var typeDropdown = widget.Get<DropDownButtonWidget>("TYPE_DROPDOWN");
			{
				Func<KeyValuePair<MapFileType, MapFileTypeInfo>, ScrollItemWidget, ScrollItemWidget> setupItem = (option, template) =>
				{
					var item = ScrollItemWidget.Setup(template,
						() => fileType == option.Key,
						() => { typeDropdown.Text = option.Value.UiLabel; fileType = option.Key; });
					item.Get<LabelWidget>("LABEL").GetText = () => option.Value.UiLabel;
					return item;
				};

				typeDropdown.Text = fileTypes[fileType].UiLabel;

				typeDropdown.OnClick = () =>
					typeDropdown.ShowDropDown("LABEL_DROPDOWN_TEMPLATE", 210, fileTypes, setupItem);
			}

			var close = widget.Get<ButtonWidget>("BACK_BUTTON");
			close.OnClick = () => { Ui.CloseWindow(); onExit(); };

			var save = widget.Get<ButtonWidget>("SAVE_BUTTON");
			save.OnClick = () =>
			{
				if (string.IsNullOrEmpty(filename.Text))
					return;

				map.Title = title.Text;
				map.Author = author.Text;
				map.Visibility = (MapVisibility)Enum.Parse(typeof(MapVisibility), visibilityDropdown.Text);

				if (actorDefinitions != null)
					map.ActorDefinitions = actorDefinitions;

				if (playerDefinitions != null)
					map.PlayerDefinitions = playerDefinitions;

				map.RequiresMod = modData.Manifest.Mod.Id;

				var combinedPath = Platform.ResolvePath(Path.Combine(selectedDirectory.Folder.Name, filename.Text + fileTypes[fileType].Extension));

				// Invalidate the old map metadata
				if (map.Uid != null && map.Package != null && map.Package.Name == combinedPath)
					modData.MapCache[map.Uid].Invalidate();

				try
				{
					var package = map.Package as IReadWritePackage;
					if (package == null || package.Name != combinedPath)
					{
						selectedDirectory.Folder.Delete(combinedPath);
						if (fileType == MapFileType.OraMap)
							package = new ZipFile(modData.DefaultFileSystem, combinedPath, true);
						else
							package = new Folder(combinedPath);
					}

					map.Save(package);
				}
				catch
				{
					Console.WriteLine("Failed to save map at {0}", combinedPath);
				}

				// Update the map cache so it can be loaded without restarting the game
				modData.MapCache[map.Uid].UpdateFromMap(map.Package, selectedDirectory.Folder, selectedDirectory.Classification, null, map.Grid.Type);

				Console.WriteLine("Saved current map at {0}", combinedPath);
				Ui.CloseWindow();

				onSave(map.Uid);
			};
		}
	}
}
