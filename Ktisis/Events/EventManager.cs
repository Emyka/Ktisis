using Ktisis.Structs.Actor.State;

namespace Ktisis.Events {
	public static class EventManager {
		public delegate void GPoseChange(ActorGposeState state);
		public static GPoseChange? OnGPoseChange = null;

		public static void FireOnGposeChangeEvent(ActorGposeState state) {
			if (state == ActorGposeState.ON)
				EnterGposeOrLoadInGpose();
			else
				LeaveGposeOrUnloadInGpose();

			OnGPoseChange?.Invoke(state);
		}


		// Converniency functions
		private static void EnterGposeOrLoadInGpose() {

		}
		private static void LeaveGposeOrUnloadInGpose() {

		}

	}
}
