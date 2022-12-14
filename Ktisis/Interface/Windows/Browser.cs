using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Dalamud.Logging;

using ImGuiNET;
using ImGuiScene;

using Ktisis.Data.Files;
using Ktisis.Data.Serialization;
using Ktisis.Structs.Actor.State;
using Ktisis.Structs.Poses;
using Ktisis.Util;

namespace Ktisis.Interface.Windows.PoseBrowser {
	internal class BrowserWindow {
		private static bool Visible = false;
		private static List<BrowserPoseFile> BrowserPoseFiles = new();
		private static float ThumbSize = ImGui.GetFontSize() * 0.4f;
		private static Vector2 ThumbSize2D = new(ImGui.GetFontSize() * ThumbSize);
		private static BrowserPoseFile? FileInFocus = null;
		private static BrowserPoseFile? FileInPreview = null;
		private static bool IsHolding = false;
		private static string Search = "";
		private static bool ShowImages = true;
		private static PoseContainer _TempPose = new();
		private static bool PreloadImages = true;
		private static int Columns = 0;

		// TODO: Once CMP files are supported, change ^\.(pose)$ to ^\.(pose|cmp)$
		internal static Regex PosesExts = new(@"^\.(pose)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
		internal static Regex ImagesExts = new(@"^\.(jpg|jpeg|png|gif)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
		private static Regex ShortPath = new(@"^$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

		// Toggle visibility
		public static void Toggle() {
			if (Visible) ClearImageCache();
			Visible = !Visible;
		}

		public static void ClearImageCache() {
			PluginLog.Verbose($"Clear Pose Browser images");
			BrowserPoseFiles.ForEach(f => f.DisposeImage());
			BrowserPoseFiles.Clear();
			FileInFocus = null;
			FileInPreview = null;
		}
		public static void OnGposeToggle(ActorGposeState gposeState) {
			if(gposeState == ActorGposeState.OFF) {
				ClearImageCache();
			}
		}

		// Draw window
		public static void Draw() {
			if (!Visible || !Ktisis.IsInGPose)
				return;

			if (!ImGui.Begin("Pose Browser", ref Visible)) {
				ImGui.End();
				return;
			}
			if (!BrowserPoseFiles.Any()) Sync();

			var files = BrowserPoseFiles;
			if (!string.IsNullOrWhiteSpace(Search)) files = files.Where(f => f.Path.Contains(Search, StringComparison.OrdinalIgnoreCase)).ToList();


			DrawToolBar(files.Count);
			ImGui.Spacing();

			ImGui.BeginChildFrame(76,ImGui.GetContentRegionAvail());
			bool anyHovered = false;
			int col = 1;
			foreach (var file in files) {
				// Free up ImageTask memory when image is fully loaded
				if (file.ImageTask != null && file.ImageTask.IsCompleted) {
					file.ImageTask.Dispose();
					file.ImageTask = null;
				}

				if (!PreloadImages) {
					if (file.IsImageLoadable)
						file.LoadImage();
					if (file.IsImageUnloadable)
						file.DisposeImage();
				}

				if (ShowImages && file.ImagePath == null) continue;

				var ishovering = FileInFocus == file;
				float borderSize = ImGui.GetStyle().FramePadding.X;
				ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, borderSize);

				if ((file.ImageTask == null || file.ImageTask.IsCompleted) && file.Image != null) {
					ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.FrameBg]);
					ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(borderSize));

					ImGui.ImageButton(file.Image.ImGuiHandle, ScaleImage(file.Image));
					ImGui.PopStyleVar();
					ImGui.PopStyleColor();
				} else {
					ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, borderSize);
					ImGui.PushStyleColor(ImGuiCol.Border, ImGui.GetStyle().Colors[ishovering ? (int)ImGuiCol.ButtonHovered : (int)ImGuiCol.WindowBg]); ;
					ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGui.GetStyle().Colors[(int)ImGuiCol.FrameBg]);
					ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.FrameBg]);

					ImGui.Button($"{file.Name}##ContextMenu##{file.Path}", ThumbSize2D + (new Vector2(borderSize * 2)));

					ImGui.PopStyleColor(3);
					ImGui.PopStyleVar();
				}

				//ImGui.PopStyleColor();
				ImGui.PopStyleVar(1);

				file.IsVisible = ImGui.IsItemVisible();

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

				if (ImGui.BeginPopupContextItem($"PoseBrowser##ContextMenu##{file.Path}", ImGuiPopupFlags.MouseButtonRight | ImGuiPopupFlags.AnyPopupId)) {
					ImGui.Text(file.Name);
					ImGui.Separator();
					ImGui.Text(fileType);

					if (ImGui.Selectable($"{ShortPath.Replace(file.Path, "").TrimStart(new char[] { '\\', '/' })}"))
						ImGui.SetClipboardText(Path.GetDirectoryName(file.Path));



					if (ImGui.Selectable($"Apply to target"))
						ImportPose(file.Path, ImportPoseFlags.SaveTempAfter | ImportPoseFlags.ResetPreview | ImportPoseFlags.Face | ImportPoseFlags.Body);
					if (ImGui.Selectable($"Apply body to target"))
						ImportPose(file.Path, ImportPoseFlags.SaveTempAfter | ImportPoseFlags.ResetPreview | ImportPoseFlags.Body);
					if (ImGui.Selectable($"Apply expression to target"))
						ImportPose(file.Path, ImportPoseFlags.SaveTempAfter | ImportPoseFlags.ResetPreview | ImportPoseFlags.Face);

					ImGui.EndPopup();
				}

				// TODO: display discreet name in the image instead of tooltip

				// Restore the cursor to the same line to be able to calculate available region
				if(Columns == 0 || col < Columns) {
					col++;
					ImGui.SameLine();
				} else
					col = 1;

				if (Columns == 0 && ImGui.GetContentRegionAvail().X < ThumbSize2D.X * 0.66f)
					ImGui.Text(""); // Newline() seems buggy, so wrap with Text's natural line break
			}
			if (!anyHovered)
				FileInFocus = null;

			if (FileInFocus != FileInPreview && FileInPreview != null)
				RestoreTempPose();
			if (IsHolding && FileInFocus != null && FileInPreview == null)
				PressPreview();
			ImGui.EndChildFrame();

			ImGui.End();
		}


		private static void DrawToolBar(int hits) {

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
			ImGui.Text($"({hits})");

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
				ClearImageCache();
			}
			ImGui.SameLine();

			ImGui.Checkbox($"Images Only##PoseBrowser", ref ShowImages);
			ImGui.SameLine();
			ImGui.SameLine();
			ImGui.SetNextItemWidth(ImGui.GetFontSize() * 4);
			ImGui.InputInt($"Columns##PoseBrowser", ref Columns, 1, 2);


		}


		private static void Sync() {
			if (!Ktisis.Configuration.BrowserLibraryPaths.Any(p => Directory.Exists(p))) return;

			ClearImageCache();
			ShortPath = new("^(" + String.Join("|", Ktisis.Configuration.BrowserLibraryPaths.Select(p => Regex.Escape(p))) + ")", RegexOptions.IgnoreCase | RegexOptions.Compiled);


			List<FileInfo> tempPosesFound = new();
			foreach(var path in Ktisis.Configuration.BrowserLibraryPaths) {
				var pathItems = from d in new DirectoryInfo(path)
						.EnumerateFiles("*", SearchOption.AllDirectories)
						.Where(file => PosesExts.IsMatch(file.Extension))
					select d;
				tempPosesFound = tempPosesFound.Concat(pathItems).ToList();
			}
			var posesFound = tempPosesFound.OrderBy(f => f.FullName);

			foreach (var item in posesFound) {

				if (string.IsNullOrEmpty(item.Name) || (item.Name[0] == '.')) continue;
				// TODO: verify if the file is valid

				BrowserPoseFile entry = new(item.FullName, Path.GetFileNameWithoutExtension(item.Name));
				if (PreloadImages)
					entry.LoadImage();
				BrowserPoseFiles.Add(entry);
			}
		}

		[Flags]
		enum ImportPoseFlags {
			None = 0,
			Face = 1,
			Body = 2,
			SaveTempBefore = 4,
			SaveTempAfter = 8,
			ResetPreview = 16,
		}
		private unsafe static void ImportPose(string path, ImportPoseFlags flags) {
			var actor = Ktisis.Target;
			if (actor->Model == null) return;
			var trans = Ktisis.Configuration.PoseTransforms;

			if (flags.HasFlag(ImportPoseFlags.ResetPreview))
				FileInPreview = null;

			if (flags.HasFlag(ImportPoseFlags.SaveTempBefore)) _TempPose.Store(actor->Model->Skeleton);

			Workspace.Workspace.ImportPath(path, actor, flags.HasFlag(ImportPoseFlags.Body), flags.HasFlag(ImportPoseFlags.Face), trans);
			if (flags.HasFlag(ImportPoseFlags.SaveTempAfter)) _TempPose.Store(actor->Model->Skeleton);
		}

		public static bool PressPreview() {
			if (!Visible || FileInFocus == null) return false;

			IsHolding = true;
			FileInPreview = FileInFocus;
			var flags = ImportPoseFlags.SaveTempBefore;
			if (!ImGui.GetIO().KeyShift) flags |= ImportPoseFlags.Body;
			if (!ImGui.GetIO().KeyCtrl) flags |= ImportPoseFlags.Face;
			ImportPose(FileInFocus.Path, flags);
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
				image.Width * ratioY,
				image.Height * ratioY
			);
		}
	}
	internal class BrowserPoseFile {
		public string Path { get; set; }
		public string Name { get; set; }
		public TextureWrap? Image { get; set; } = null;
		public string? ImagePath { get; set; } = null;
		public Task<TextureWrap>? ImageTask { get; set; } = null;
		public bool IsVisible { get; set; } = false;

		public BrowserPoseFile(string path, string name) {
			Path = path;
			Name = name;
			this.FindImages();
		}

		public bool IsImageLoadable => this.IsVisible && this.Image == null && this.ImageTask == null && this.ImagePath != null;
		public bool IsImageUnloadable => !this.IsVisible && (this.Image != null || this.ImageTask != null);

		public void FindImages() {
			// Add embedded image if exists
			if (System.IO.Path.GetFileNameWithoutExtension(this.Path) == ".pose" && File.ReadLines(this.Path).Any(line => line.Contains("\"Base64Image\""))) {

				var content = File.ReadAllText(this.Path);
				var pose = JsonParser.Deserialize<PoseFile>(content);
				if (pose?.Base64Image != null) {
					this.ImagePath = pose.Base64Image;
				}
			} else {

				// Try finding related images close to the pose file
				// TODO: improve algo for better relevance
				var dir = System.IO.Path.GetDirectoryName(this.Path);
				if (dir != null) {
					var imageFile = new DirectoryInfo(dir)
						.EnumerateFiles("*", SearchOption.TopDirectoryOnly)
						.FirstOrDefault(file => BrowserWindow.ImagesExts.IsMatch(file.Extension));
					if (imageFile != null)
						this.ImagePath = imageFile.FullName;
				}
			}
		}
		public void LoadImage() {
			if (this.ImagePath == null) return;

			if (File.Exists(this.ImagePath))
				Ktisis.UiBuilder.LoadImageAsync(this.ImagePath).ContinueWith(t => this.Image = t.Result);
			else {
				var bytes = Convert.FromBase64String(this.ImagePath);
				Ktisis.UiBuilder.LoadImageAsync(bytes).ContinueWith(t => this.Image = t.Result);
			}
		}
		public void DisposeImage() {
			this.ImageTask?.Dispose();
			this.ImageTask = null;
			this.Image?.Dispose();
			this.Image = null;
		}
	}
}
