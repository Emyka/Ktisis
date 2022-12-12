using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using ImGuiNET;
using ImGuiScene;

using Ktisis.Data.Files;
using Ktisis.Data.Serialization;
using Ktisis.Structs.Poses;
using Ktisis.Util;

namespace Ktisis.Interface.Windows.Browser {
	internal class BrowserWindow {
		private static bool Visible = true;
		private static List<BrowserPoseFile> BrowserPoseFiles = new();
		private static float ThumbSize = 15;
		private static Vector2 ThumbSize2D = new(ImGui.GetFontSize() * ThumbSize);
		private static BrowserPoseFile? FileInFocus = null;
		private static BrowserPoseFile? FileInPreview = null;
		private static bool IsHolding = false;

		// Toggle visibility
		public static void Toggle() => Visible = !Visible;

		// Draw window
		public static void Draw() {
			if (!Visible || !Ktisis.IsInGPose)
				return;

			if (!ImGui.Begin("Browser", ref Visible)) {
				ImGui.End();
				return;
			}
			if (!BrowserPoseFiles.Any()) Sync();

			DrawToolBar();
			ImGui.Spacing();

			ImGui.BeginChildFrame(76,ImGui.GetContentRegionAvail());
			bool anyHovered = false;
			foreach (var file in BrowserPoseFiles){
				if (!file.Images.Any()) continue; // TODO: Handle files without images

				var image = file.Images.First();
				ImGui.Image(image.ImGuiHandle, ScaleImage(image));
				if (ImGui.IsItemHovered()) {
					FileInFocus = file;
					anyHovered |= true;
				}
				ImGui.SameLine();
				if (ImGui.GetContentRegionAvail().X < ThumbSize2D.X * 0.66f)
					ImGui.Text("");
			}
			if (!anyHovered)
				FileInFocus = null;

			if (FileInFocus != FileInPreview)
				RestoreTempPose();
			if (IsHolding && FileInFocus != null && FileInPreview == null)
				PressPreview();
			ImGui.EndChildFrame();

			ImGui.End();
		}


		private static void DrawToolBar() {

			ImGui.Text($" Hits: {BrowserPoseFiles.Count}");

			ImGui.SameLine();
			ImGui.SetNextItemWidth(ImGui.GetFontSize() * 10);
			if (ImGui.SliderFloat("Thumb size", ref ThumbSize, 2, 100))
				ThumbSize2D = new(ImGui.GetFontSize() * ThumbSize);

			ImGui.SameLine();
			if (GuiHelpers.IconButton(Dalamud.Interface.FontAwesomeIcon.FolderPlus)) {
				KtisisGui.FileDialogManager.OpenFolderDialog(
					"Add pose library path",
					(selected, path) => {
						Ktisis.Configuration.BrowserLibraryPaths.Add(path);
						Sync();
					},
					Ktisis.Configuration.BrowserLibraryPaths.Any() ? Ktisis.Configuration.BrowserLibraryPaths.Last() : null
					);
			}
			var libList = string.Join("\n", Ktisis.Configuration.BrowserLibraryPaths);
			GuiHelpers.Tooltip($"{Ktisis.Configuration.BrowserLibraryPaths.Count} saved pose librarie(s):\n{libList}");

			ImGui.SameLine();
			if (GuiHelpers.IconButtonHoldConfirm(Dalamud.Interface.FontAwesomeIcon.FolderMinus, $"Delete all {Ktisis.Configuration.BrowserLibraryPaths.Count} saved pose librarie(s):\n{libList}")) {
				Ktisis.Configuration.BrowserLibraryPaths.Clear();
				BrowserPoseFiles.Clear();
			}


		}


		private static void Sync() {
			if (!Ktisis.Configuration.BrowserLibraryPaths.Any(p => Directory.Exists(p))) return;

			BrowserPoseFiles.Clear();


			Regex poseExts = new(@"^\.(pose|cmp)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
			List<FileInfo> tempPosesFound = new();
			foreach(var path in Ktisis.Configuration.BrowserLibraryPaths) {
				var pathItems = from d in new DirectoryInfo(path)
						.EnumerateFiles("*", SearchOption.AllDirectories)
						.Where(file => poseExts.IsMatch(file.Extension))
					select d;
				tempPosesFound = tempPosesFound.Concat(pathItems).ToList();
			}
			var posesFound = tempPosesFound.OrderBy(f => f.FullName);

			foreach (var item in posesFound) {

				if (string.IsNullOrEmpty(item.Name) || (item.Name[0] == '.')) continue;
				// TODO: verify if the file is valid


				BrowserPoseFile entry = new(item.FullName, Path.GetFileNameWithoutExtension(item.Name));

				// Add embedded image if exists
				if (item.Extension == ".pose" && File.ReadLines(item.FullName).Any(line => line.Contains("\"Base64Image\""))) {

					var content = File.ReadAllText(item.FullName);
					var pose = JsonParser.Deserialize<PoseFile>(content);
					if (pose?.Base64Image != null) {
						var bytes = Convert.FromBase64String(pose.Base64Image);
						Ktisis.UiBuilder.LoadImageAsync(bytes).ContinueWith(t => entry.Images.Add(t.Result));
					}
				}


				// TODO: Add find potential images coupled with the pose file

				BrowserPoseFiles.Add(entry);
			}

		}

		private static PoseContainer _TempPose = new();
		public unsafe static bool PressPreview() {
			if (!Visible || FileInFocus == null) return false;

			var actor = Ktisis.Target;
			if (actor->Model == null) return false;
			_TempPose.Store(actor->Model->Skeleton);

			IsHolding = true;
			FileInPreview = FileInFocus;
			var trans = Ktisis.Configuration.PoseTransforms;
			Workspace.Workspace.ImportPath(FileInFocus.Path, actor, true, true, trans);
			return true;
		}

		public static bool ReleasePreview() {
			IsHolding = false;
			FileInPreview = null;
			if (!Visible || FileInFocus == null) return false;

			return RestoreTempPose();
		}
		public unsafe static bool RestoreTempPose() {
			FileInPreview = null;

			var actor = Ktisis.Target;
			if (actor->Model == null) return false;

			_TempPose.Apply(actor->Model->Skeleton);
			return true;
		}

		private static Vector2 ScaleImage(TextureWrap image) {
			var ratioX = ThumbSize2D.X / image.Width;
			var ratioY = ThumbSize2D.Y / image.Height;
			var ratio = (float)Math.Min((double)ratioX, (double)ratioY);

			return new(
				image.Width * ratio,
				image.Height * ratio
			);
		}
	}
	internal class BrowserPoseFile {
		public string Path { get; set; }
		public string Name { get; set; }
		public List<TextureWrap> Images { get; set; } = new();
		public Task<TextureWrap>? ImageTask;

		public BrowserPoseFile(string path, string name) {
			Path = path;
			Name = name;
		}
	}
}
