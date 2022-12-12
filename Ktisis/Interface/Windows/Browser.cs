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
		private static string Search = "";

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
			var files = BrowserPoseFiles;
			if (!string.IsNullOrWhiteSpace(Search)) files = files.Where(f => f.Path.Contains(Search, StringComparison.OrdinalIgnoreCase)).ToList();

			foreach (var file in files) {
				if (!file.Images.Any()) continue; // TODO: Handle files without images

				var image = file.Images.First();
				ImGui.Image(image.ImGuiHandle, ScaleImage(image));
				if (ImGui.IsItemHovered()) {
					FileInFocus = file;
					anyHovered |= true;
				}

				var fileExt = Path.GetExtension(file.Path);
				string fileType;
				if (fileExt == ".pose")
					fileType = "Anamnesis pose";
				else if (fileExt == ".cmp")
					fileType = "Concept Matrix pose";
				else
					fileType = "Unknown file type";

				if (ImGui.BeginPopupContextItem($"{file.Path}", ImGuiPopupFlags.MouseButtonRight | ImGuiPopupFlags.AnyPopupId)) {
					ImGui.Text(file.Name);
					ImGui.Separator();
					ImGui.Text(fileType);
					ImGui.Text($"Path:\n{file.Path}");

					ImGui.TextDisabled($"Apply to target");
					ImGui.TextDisabled($"Apply body to target");
					ImGui.TextDisabled($"Apply expression to target");

					ImGui.EndPopup();
				}

				// TODO: display discreet name in the image instead of tooltip
				GuiHelpers.Tooltip($"{file.Name}\n{Path.GetExtension(file.Path)}");
				ImGui.SameLine();

				// Hacky line wrap
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
			if (ImGui.SliderFloat("##Browser##ThumbSize", ref ThumbSize, 2, 100))
				ThumbSize2D = new(ImGui.GetFontSize() * ThumbSize);
			GuiHelpers.Tooltip("Thumb size");
			var mouseWheel = ImGui.GetIO().MouseWheel;
			if (mouseWheel != 0 && ImGui.GetIO().KeyCtrl) {
				ThumbSize += mouseWheel * 0.5f;
				ThumbSize2D = new(ImGui.GetFontSize() * ThumbSize);
			}
			ImGui.SameLine();

			ImGui.SetNextItemWidth(ImGui.GetFontSize() * 10);
			ImGui.InputTextWithHint("##Browser##Search","Search", ref Search, 100, ImGuiInputTextFlags.AutoSelectAll);

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


			// TODO: Once CMP files are supported, change ^\.(pose)$ to ^\.(pose|cmp)$
			Regex poseExts = new(@"^\.(pose)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
			Regex imgExts = new(@"^\.(jpg|jpeg|png|gif)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
				} else {

					// Try finding related images close to the pose file
					// TODO: improve algo for better relevance
					var dir = Path.GetDirectoryName(item.FullName);
					if (dir != null) {
						var imageFile = new DirectoryInfo(dir)
							.EnumerateFiles("*", SearchOption.TopDirectoryOnly)
							.FirstOrDefault(file => imgExts.IsMatch(file.Extension));
						if( imageFile != null)
							Ktisis.UiBuilder.LoadImageAsync(imageFile.FullName).ContinueWith(t=> entry.Images.Add(t.Result));
					}
				}

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
