using ImGuiNET;

using Ktisis.Interface.Components;
using Ktisis.Interface.Windows.ActorEdit;
using Ktisis.Interface.Windows.Workspace;

namespace Ktisis.Interface.Modular.ItemTypes.Panel {
	public class ActorList : IModularItem {
		public void Draw() => ActorsList.Draw();
	}
	public class ControlButtonsExtra : IModularItem {
		public void Draw() => ControlButtons.DrawExtra();
	}
	public class HandleEmpty : IModularItem {
		public void Draw() => ImGui.Text("       ");
	}
	public class GizmoOperations : IModularItem {
		public void Draw() => ControlButtons.DrawGizmoOperations();
	}
	public class GposeTextIndicator : IModularItem {
		public void Draw() => Workspace.DrawGposeIndicator();
	}
	public class PoseSwitch : IModularItem {
		public void Draw() => ControlButtons.DrawPoseSwitch();
	}
	public class SelectInfo : IModularItem {
		public void Draw() => Workspace.SelectInfo();
	}
	public class EditActorButton : IModularItem {
		public void Draw() => EditActor.DrawButton();
	}
	public class AnimationControls : IModularItem {
		public void Draw() => Components.AnimationControls.Draw();
	}

}
