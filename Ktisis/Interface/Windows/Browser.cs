using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;

using ImGuiNET;
using ImGuiScene;

using Ktisis.Data.Files;
using Ktisis.Data.Serialization;
using Ktisis.Util;

namespace Ktisis.Interface.Windows.Browser {
	internal class BrowserWindow {
		private static bool Visible = true;

		private static List<string> Paths = new();
		private static List<BrowserPoseFile> BrowserPoseFiles = new();
		private static float ThumbSize = 15;
		private static Vector2 ThumbSize2D = new(ImGui.GetFontSize() * ThumbSize);
		private static BrowserPoseFile? FileInFocus = null;

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
			foreach(var file in BrowserPoseFiles.Where(f => f.Images.Any())){

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
						Paths.Add(path);
						Sync();
					},
					Paths.Any() ? Paths.Last() : null
					);
			}
			var libList = string.Join("\n", Paths);
			GuiHelpers.Tooltip($"{Paths.Count} saved pose librarie(s):\n{libList}");

			ImGui.SameLine();
			if (GuiHelpers.IconButtonHoldConfirm(Dalamud.Interface.FontAwesomeIcon.FolderMinus, $"Delete all {Paths.Count} saved pose librarie(s):\n{libList}")) {
				Paths.Clear();
				BrowserPoseFiles.Clear();
			}


		}


		private static void Sync() {
			if (!Paths.Any(p => Directory.Exists(p))) return;

			BrowserPoseFiles.Clear();


			Regex poseExts = new(@"^\.(pose|cmp)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
			List<FileInfo> tempPosesFound = new();
			foreach(var path in Paths) {
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


				BrowserPoseFile entry = new(item.FullName, item.Name);

				// Add embedded image if exists
				if (item.Extension == ".pose" && File.ReadLines(item.FullName).Any(line => line.Contains("\"Base64Image\""))) {

					var content = File.ReadAllText(item.FullName);
					var pose = JsonParser.Deserialize<PoseFile>(content);
					if (pose?.Base64Image != null) {
						var bytes = Convert.FromBase64String(pose.Base64Image);
						entry.Images.Add(Ktisis.UiBuilder.LoadImage(bytes));
					}
				}


				// TODO: Add find potential images coupled with the pose file

				BrowserPoseFiles.Add(entry);
			}

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

		public BrowserPoseFile(string path, string name) {
			Path = path;
			Name = name;
		}
	}
}
