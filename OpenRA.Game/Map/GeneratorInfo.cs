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

using System.Collections.Generic;
using System;

namespace OpenRA
{
	public class DebrisSetInfo
	{
		public readonly ushort[] LandDebris;
		public readonly ushort[] WaterDebris;

		public DebrisSetInfo(MiniYaml my)
		{
			FieldLoader.Load(this, my);
		}
	}

	public class SimpleCliffSetInfo
	{
		// straight
		public readonly ushort[] NS;
		public readonly ushort[] SN;
		public readonly ushort[] WE;
		public readonly ushort[] EW;

		// corners
		public readonly ushort[] NE;
		public readonly ushort[] EN;
		public readonly ushort[] NW;
		public readonly ushort[] WN;
		public readonly ushort[] SE;
		public readonly ushort[] ES;
		public readonly ushort[] SW;
		public readonly ushort[] WS;

		// edges
		public readonly ushort[] _N;
		public readonly ushort[] _E;
		public readonly ushort[] _S;
		public readonly ushort[] _W;
		public readonly ushort[] N_;
		public readonly ushort[] E_;
		public readonly ushort[] S_;
		public readonly ushort[] W_;

		public SimpleCliffSetInfo(MiniYaml my)
		{
			FieldLoader.Load(this, my);
		}
	}

	public class CliffConnection
	{
		public string Type;
		public int2 Position;

		public CliffConnection(string val)
		{
			var s = FieldLoader.GetValue<string[]>("", val);
			Type = s[0];
			Position = new int2(Exts.ParseIntegerInvariant(s[1]), Exts.ParseIntegerInvariant(s[2]));
		}
	}

	public class CliffTemplateInfo
	{
		public readonly ushort[] Tiles;
		[FieldLoader.Ignore]
		public readonly IReadOnlyList<CliffConnection> Connections;

		public CliffTemplateInfo(MiniYaml my)
		{
			FieldLoader.Load(this, my);
			var md = my.ToDictionary();
			var connections = md["Connections"];
			var l = new List<CliffConnection>(connections.Nodes.Count);
			foreach (var node in connections.Nodes)
			{
				l.Add(new CliffConnection(node.Key));
			}
			Connections = new ReadOnlyList<CliffConnection>(l);
		}
	}

	public class CliffSetInfo
	{
		public readonly IReadOnlyDictionary<string, string> Connections;
		public readonly IReadOnlyList<CliffTemplateInfo> Templates;

		public CliffSetInfo(MiniYaml my)
		{
			var md = my.ToDictionary();
			// TODO: better error when missing
			var connections = md["Connections"];
			var d = new Dictionary<string, string>();
			foreach (var node in connections.Nodes)
			{
				var s = FieldLoader.GetValue<string[]>("", node.Key);
				d.Add(s[0], s[1]);
				d.Add(s[1], s[0]);
			}
			Connections = new ReadOnlyDictionary<string, string>(d);

			var templates = md["Templates"];
			var l = new List<CliffTemplateInfo>(templates.Nodes.Count);
			foreach (var node in templates.Nodes)
			{
				l.Add(new CliffTemplateInfo(node.Value));
			}
			Templates = new ReadOnlyList<CliffTemplateInfo>(l);
		}
	}

	public class GeneratorInfo
	{
		public readonly DebrisSetInfo Debris;
		public readonly SimpleCliffSetInfo SimpleCliffs;
		public readonly CliffSetInfo Cliffs;

		public GeneratorInfo(MiniYaml my)
		{
			var md = my.ToDictionary();

			if (md.ContainsKey("Debris"))
			{
				Debris = new DebrisSetInfo(md["Debris"]);
			}
			if (md.ContainsKey("SimpleCliffs"))
			{
				SimpleCliffs = new SimpleCliffSetInfo(md["SimpleCliffs"]);
			}
			else if (md.ContainsKey("Cliffs"))
			{
				Cliffs = new CliffSetInfo(md["Cliffs"]);
			}
		}
	}
}
