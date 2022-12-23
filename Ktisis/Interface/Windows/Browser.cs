﻿using System;
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
using Ktisis.Interface.Components;
using Ktisis.Structs.Actor.State;
using Ktisis.Structs.Poses;
using Ktisis.Util;

namespace Ktisis.Interface.Windows.PoseBrowser {
	internal class BrowserWindow {

		// configurations variables
		private static int Columns = 0;
		private static bool FilterImagesOnly = false;
		private static bool CropImages = true;
		internal static bool StreamImageLoading = false;
		internal static bool UseAsync = true;
		internal static Regex PosesExts = new(@"^\.(pose)$", RegexOptions.IgnoreCase | RegexOptions.Compiled); // TODO: Add .cmp when supported
		internal static Regex ImagesExts = new(@"^\.(jpg|jpeg|png|gif)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

		// Temp variables
		private static bool Visible = false;
		private static float ThumbSize = ImGui.GetFontSize() * 0.4f;
		private static Vector2 ThumbSize2D = new(ImGui.GetFontSize() * ThumbSize);
		private static List<BrowserPoseFile> BrowserPoseFiles = new();
		private static BrowserPoseFile? FileInFocus = null;
		private static BrowserPoseFile? FileInPreview = null;
		private static BrowserPoseFile? OpenedImageModal = null;
		private static PoseContainer _TempPose = new();
		private static bool IsHolding = false;
		private static string Search = "";
		private static Regex ShortPath = new(@"^$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
		private static Vector2 ImageModalSize = default;


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
			if (gposeState == ActorGposeState.OFF) {
				ClearImageCache();
			}
		}

		// Draw window
		public static void Draw() {
			if (!Visible || !Ktisis.IsInGPose)
				return;

			if (!ImGui.Begin("Pose Browser", ref Visible)) {
				ImGui.End();
				if (BrowserPoseFiles.Any())
					ClearImageCache();
				return;
			}

			DrawImageModal();

			if (!BrowserPoseFiles.Any()) Sync();

			var files = BrowserPoseFiles;
			if (!string.IsNullOrWhiteSpace(Search)) files = files.Where(f => f.Path.Contains(Search, StringComparison.OrdinalIgnoreCase)).ToList();


			DrawToolBar(files.Count);
			ImGui.Spacing();

			ImGui.BeginChildFrame(76, ImGui.GetContentRegionAvail());
			bool anyHovered = false;
			int col = 1;
			foreach (var file in files) {
				// Free up ImageTask memory when image is fully loaded
				if (file.ImageTask != null && file.ImageTask.IsCompleted) {
					file.ImageTask.Dispose();
					file.ImageTask = null;
				}

				if (StreamImageLoading) {
					if (file.IsImageLoadable)
						file.LoadImage();
					if (file.IsImageUnloadable)
						file.DisposeImage();
				}

				if (FilterImagesOnly && file.ImagePath == null) continue;

				var ishovering = FileInFocus == file;
				float borderSize = ImGui.GetStyle().FramePadding.X;
				ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, borderSize);
				var hasImage = (file.ImageTask == null || file.ImageTask.IsCompleted) && file.Image != null;
				if (hasImage) {
					ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.FrameBg]);
					ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(borderSize));

					if (CropImages) {
						(var uv0, var uv1) = CropRatioImage(file.Image!);
						ImGui.ImageButton(file.Image!.ImGuiHandle, ThumbSize2D, uv0, uv1);
					} else
						ImGui.ImageButton(file.Image!.ImGuiHandle, ScaleThumbImage(file.Image));
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

					if (hasImage) ImGui.Text($"Image Size: {file.Image!.Width}*{file.Image.Height}");


					if (ImGui.Selectable($"Apply to target"))
						ImportPose(file.Path, ImportPoseFlags.SaveTempAfter | ImportPoseFlags.ResetPreview | ImportPoseFlags.Face | ImportPoseFlags.Body);
					if (ImGui.Selectable($"Apply body to target"))
						ImportPose(file.Path, ImportPoseFlags.SaveTempAfter | ImportPoseFlags.ResetPreview | ImportPoseFlags.Body);
					if (ImGui.Selectable($"Apply expression to target"))
						ImportPose(file.Path, ImportPoseFlags.SaveTempAfter | ImportPoseFlags.ResetPreview | ImportPoseFlags.Face);

					ImGui.EndPopup();
				}
				if (hasImage && ImGui.IsItemClicked(ImGuiMouseButton.Left))
					OpenedImageModal = file;

				// TODO: display discreet name in the image instead of tooltip

				// Restore the cursor to the same line to be able to calculate available region
				if (Columns == 0 || col < Columns) {
					col++;
					ImGui.SameLine();
				} else
					col = 1;

				if (Columns == 0 && ImGui.GetContentRegionAvail().X < ThumbSize2D.X)
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

		private static void DrawImageModal() {
			if (OpenedImageModal == null) return;
			if (OpenedImageModal.Image == null) return;

			ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Always, new Vector2(0.5f));
			if (ImGui.BeginPopup($"##PoseBrowser##ImageDisplay##1", ImGuiWindowFlags.Modal | ImGuiWindowFlags.Popup | ImGuiWindowFlags.AlwaysAutoResize)) {

				if (ImageModalSize == default) ImageModalSize = ScaleImageIfBigger(OpenedImageModal.Image, ImGui.GetIO().DisplaySize * 0.75f);
				var mouseWheel = ImGui.GetIO().MouseWheel;

				if (ImGui.IsWindowHovered() && mouseWheel != 0 && ImGui.GetIO().KeyCtrl) {
					var resizeMult = mouseWheel > 0 ? 1.1f : 0.9f;
					ImageModalSize *= resizeMult;
				}

				ImGui.Image(OpenedImageModal.Image.ImGuiHandle, ImageModalSize);
				var isImageClickedLeft = ImGui.IsItemClicked(ImGuiMouseButton.Left);
				var isImageClickedRight = ImGui.IsItemClicked(ImGuiMouseButton.Right);

				if (ImGui.Button("Close", new(ImGui.CalcTextSize("Close").X+ImGui.GetStyle().FramePadding.X*2, ControlButtons.ButtonSize.Y)) || (!ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows) && (ImGui.IsMouseClicked(ImGuiMouseButton.Left) || ImGui.IsMouseClicked(ImGuiMouseButton.Right)))) {
					ImageModalSize = default;
					OpenedImageModal = null;
					ImGui.CloseCurrentPopup();
				}

				ImGui.SameLine();
				if (GuiHelpers.IconButton(Dalamud.Interface.FontAwesomeIcon.ArrowLeft, ControlButtons.ButtonSize) || isImageClickedLeft)
					PluginLog.Debug($"clicked left");
				ImGui.SameLine();
				if(OpenedImageModal != null) {
					ImGui.Text($"{OpenedImageModal.Name}");
					ImGui.SameLine();
				}
				if (GuiHelpers.IconButton(Dalamud.Interface.FontAwesomeIcon.ArrowRight, ControlButtons.ButtonSize) || isImageClickedRight)
					PluginLog.Debug($"clicked right");

				ImGui.EndPopup();
			}
			ImGui.OpenPopup($"##PoseBrowser##ImageDisplay##1");
		}
		private static void DrawToolBar(int hits) {

			if (GuiHelpers.IconButtonTooltip(Dalamud.Interface.FontAwesomeIcon.Sync, "Refresh poses and images", default, $"SyncButton##PoseBrowser"))
				Sync();

			ImGui.SameLine(0, ImGui.GetFontSize());
			ImGui.Text($"({hits})");

			ImGui.SameLine();
			ImGui.SetNextItemWidth(ImGui.GetFontSize() * 7);
			ImGui.InputTextWithHint("##Browser##Search", "Search", ref Search, 100, ImGuiInputTextFlags.AutoSelectAll);

			// images
			ImGui.SameLine(0, ImGui.GetFontSize());
			GuiHelpers.IconButtonToggle(Dalamud.Interface.FontAwesomeIcon.Image, ref FilterImagesOnly, "Images Only", default, $"Images Only##PoseBrowser");


			// Temporary disable stream image loading as loading seems more resource hungry than keep them all loaded.
			//ImGui.SameLine();
			//if (GuiHelpers.IconButtonToggle(Dalamud.Interface.FontAwesomeIcon.Stream, ref StreamImageLoading, "Stream image load (No image preload)", default, $"Preload Images##PoseBrowser"))
			//	Sync();

			// size/columns
			ImGui.SameLine(0, ImGui.GetFontSize());
			GuiHelpers.IconButtonToggle(Dalamud.Interface.FontAwesomeIcon.CropAlt, ref CropImages, "Crop Images", default, $"CropImages##PoseBrowser");
			if (!CropImages) {
				ImGui.SameLine();
				ImGui.SetNextItemWidth(ImGui.GetFontSize() * 5);
				ImGui.InputInt($"##Columns##PoseBrowser", ref Columns, 1, 2);
				GuiHelpers.Tooltip("Number of Images before a linebreak\n0: Auto");
			}

			ImGui.SameLine();
			ImGui.SetNextItemWidth(ImGui.GetFontSize() * 5);
			if (ImGui.SliderFloat("##Browser##ThumbSize", ref ThumbSize, 2, 100))
				ThumbSize2D = new(ImGui.GetFontSize() * ThumbSize);
			GuiHelpers.Tooltip("Thumb size");
			var mouseWheel = ImGui.GetIO().MouseWheel;
			if (ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows | ImGuiHoveredFlags.NoPopupHierarchy) && mouseWheel != 0 && ImGui.GetIO().KeyCtrl) {
				ThumbSize += mouseWheel * 0.5f;
				ThumbSize2D = new(ImGui.GetFontSize() * ThumbSize);
			}

			// add/clear library
			ImGui.SameLine(0, ImGui.GetFontSize());
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

		}


		private static void Sync() {
			if (!Ktisis.Configuration.BrowserLibraryPaths.Any(p => Directory.Exists(p))) return;

			ClearImageCache();
			ShortPath = new("^(" + String.Join("|", Ktisis.Configuration.BrowserLibraryPaths.Select(p => Regex.Escape(p))) + ")", RegexOptions.IgnoreCase | RegexOptions.Compiled);


			List<FileInfo> tempPosesFound = new();
			foreach (var path in Ktisis.Configuration.BrowserLibraryPaths) {
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
				if (!StreamImageLoading)
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


		private static (Vector2, Vector2) CropRatioImage(TextureWrap image) {

			float left = 0, top = 0, right = 1, bottom = 1;

			float sourceAspectRatio = (float)image.Width / image.Height;
			float targetAspectRatio = (float)ThumbSize2D.X / ThumbSize2D.Y;

			if (sourceAspectRatio > targetAspectRatio) {
				float excedingRatioH = Math.Abs(targetAspectRatio - sourceAspectRatio) / sourceAspectRatio;
				float excedingRatioHHalf = excedingRatioH / 2;

				left = excedingRatioHHalf;
				right = 1 - excedingRatioHHalf;
			} else if (sourceAspectRatio < targetAspectRatio) {
				float excedingRatioW = Math.Abs(targetAspectRatio - sourceAspectRatio) * sourceAspectRatio;
				float excedingRatioWHalf = excedingRatioW / 2;

				top = excedingRatioWHalf;
				bottom = 1 - excedingRatioWHalf;
			}

			var uv0 = new Vector2(left, top);
			var uv1 = new Vector2(right, bottom);
			return (uv0, uv1);
		}
		private static Vector2 ScaleThumbImage(TextureWrap image) =>
			ScaleImage(image, ThumbSize2D, true, false);
		private static Vector2 ScaleImage(TextureWrap image, Vector2 targetSize, bool resizeWidth = true, bool resizeHeight = true) {
			var ratioX = targetSize.X / image.Width;
			var ratioY = targetSize.Y / image.Height;
			float ratio = default;
			if (resizeWidth && resizeHeight)
				ratio = (float)Math.Min((double)ratioX, (double)ratioY);
			else if (resizeWidth && !resizeHeight)
				ratio = ratioY;
			else if (!resizeWidth && resizeHeight)
				ratio = ratioX;
			else
				return new(image.Width, image.Height);

			return new(
				image.Width * ratio,
				image.Height * ratio
			);
		}
		private static Vector2 ScaleImageIfBigger(TextureWrap image, Vector2 maxSize) {
			if (image.Width > maxSize.X || image.Height > maxSize.Y)
				return ScaleImage(image, maxSize);
			else
				return new Vector2(image.Width, image.Height);
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

			if (File.Exists(this.ImagePath)) {

				// Always use Async if preloaded
				if (BrowserWindow.UseAsync || !BrowserWindow.StreamImageLoading)
					Ktisis.UiBuilder.LoadImageAsync(this.ImagePath).ContinueWith(t => this.Image = t.Result);
				else
					this.Image = Ktisis.UiBuilder.LoadImage(this.ImagePath);

			} else {
				var bytes = Convert.FromBase64String(this.ImagePath);

				if (BrowserWindow.UseAsync || !BrowserWindow.StreamImageLoading)
					Ktisis.UiBuilder.LoadImageAsync(bytes).ContinueWith(t => this.Image = t.Result);
				else
					this.Image = Ktisis.UiBuilder.LoadImage(bytes);
			}
		}
		public void DisposeImage() {
			this.Image?.Dispose();
			this.Image = null;
			this.ImageTask?.Dispose();
			this.ImageTask = null;
		}
	}
}
