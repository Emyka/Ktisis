using System;

using Dalamud.Game.Network;
using Dalamud.Logging;

namespace Ktisis.Interop {
	internal class NetworkPacket {
		public unsafe static void Log(IntPtr dataPtr, ushort opCode, uint sourceActorId, uint targetActorId, NetworkMessageDirection direction) {
			if (direction == NetworkMessageDirection.ZoneUp) {
				PluginLog.Verbose("Network Packet Intercepted ------------------");
				PluginLog.Verbose($"DataPointer: {dataPtr:X8}");
				PluginLog.Verbose($"opCode: 0x{opCode:X4}");
				PluginLog.Verbose($"sourceActorId: {sourceActorId}");
				PluginLog.Verbose($"targetActorId: {targetActorId}");
				PluginLog.Verbose($"Direction: {direction}");
				PluginLog.Verbose("End -----------------------------------------");
				// Check against known opCode list at
				// https://github.com/karashiiro/FFXIVOpcodes/blob/master/FFXIVOpcodes/Ipcs.cs
				// TODO: find a way to get opCode names programatically
			}
		}
	}
}
