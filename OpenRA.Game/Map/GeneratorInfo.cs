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
		public readonly ushort[] NS_E;
		public readonly ushort[] NS_W;
		public readonly ushort[] WE_N;
		public readonly ushort[] WE_S;

		// corners
		public readonly ushort[] NE_I;
		public readonly ushort[] NE_O;
		public readonly ushort[] NW_I;
		public readonly ushort[] NW_O;
		public readonly ushort[] SE_I;
		public readonly ushort[] SE_O;
		public readonly ushort[] SW_I;
		public readonly ushort[] SW_O;

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
