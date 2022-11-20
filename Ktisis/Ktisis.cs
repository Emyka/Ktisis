﻿using System;

using Dalamud.Plugin;
using Dalamud.Game.Command;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Network;
using Dalamud.Logging;

using Ktisis.Interface;
using Ktisis.Interface.Windows.ActorEdit;
using Ktisis.Interface.Windows.Workspace;
using Ktisis.Structs.Actor;

namespace Ktisis {
	public sealed class Ktisis : IDalamudPlugin {
		public string Name => "Ktisis";
		public string CommandName = "/ktisis";

		public static Configuration Configuration { get; private set; } = null!;

		public static bool IsInGPose => Services.PluginInterface.UiBuilder.GposeActive;

		public unsafe static GameObject? GPoseTarget
			=> IsInGPose ? Services.ObjectTable.CreateObjectReference((IntPtr)Services.Targets->GPoseTarget) : null;
		public unsafe static Actor* Target => GPoseTarget != null ? (Actor*)GPoseTarget.Address : null;
		public Ktisis(DalamudPluginInterface pluginInterface) {
			Services.Init(pluginInterface);
			Configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

			Configuration.Validate();

			// Init interop stuff

			Interop.Alloc.Init();
			Interop.Methods.Init();
			Interop.StaticOffsets.Init();
			Interop.Hooks.ActorHooks.Init();
			Interop.Hooks.PoseHooks.Init();
			Interop.Hooks.GuiHooks.Init();
			Interop.Hooks.EventsHooks.Init();
			Input.Init();
			Services.GameNetwork.NetworkMessage += NetworkPacket;

			// Register command

			Services.CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand) {
				HelpMessage = "/ktisis - Show the Ktisis interface."
			});

			// Overlays & UI

			if (Configuration.AutoOpenCtor)
				Workspace.Show();

			pluginInterface.UiBuilder.DisableGposeUiHide = true;
			pluginInterface.UiBuilder.Draw += KtisisGui.Draw;
		}

		public void Dispose() {
			Services.CommandManager.RemoveHandler(CommandName);
			Services.PluginInterface.SavePluginConfig(Configuration);

			Services.GameNetwork.NetworkMessage -= NetworkPacket;
			Interop.Alloc.Dispose();
			Interop.Hooks.ActorHooks.Dispose();
			Interop.Hooks.PoseHooks.Dispose();
			Interop.Hooks.GuiHooks.Dispose();
			Interop.Hooks.EventsHooks.Dispose();
			Input.Instance.Dispose();

			GameData.Sheets.Cache.Clear();
			if (EditEquip.Items != null)
				EditEquip.Items = null;
		}

		private void OnCommand(string command, string arguments) {
			Workspace.Show();
		}

		public static unsafe void NetworkPacket(IntPtr dataPtr, ushort opCode, uint sourceActorId, uint targetActorId, NetworkMessageDirection direction) {

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
			}
		}
	}
}
