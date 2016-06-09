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

		public SimpleCliffSetInfo(MiniYaml my)
		{
			FieldLoader.Load(this, my);
		}
	}

	public class GeneratorInfo
	{
		public readonly DebrisSetInfo Debris;
		public readonly SimpleCliffSetInfo SimpleCliffs;

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
		}
	}
}
