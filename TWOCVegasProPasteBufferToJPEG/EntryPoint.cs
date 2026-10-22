// ============================================================================
// TWOC-VegasPro-Paste-Buffer-To-JPEG
// https://github.com/TheWayOfCoding/TWOC-VegasPro-Paste-Buffer-To-JPEG
//
// Copyright (C) 2026 TheWayOfCoding
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.
//
// You should have received a copy of the GNU Affero General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.
// ============================================================================

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using ScriptPortal.Vegas;

namespace TWOCVegasProPasteBufferToJPEG
{
    /// <summary>
    /// EntryPoint serves as the main execution class invoked by Vegas Pro scripting engine.
    /// It extracts an image currently stored in the Windows clipboard buffer, saves it as a high-quality
    /// JPEG file in a dedicated subfolder alongside the project file, adds the image to the Vegas project
    /// Media Pool, and places it as a new video event on the timeline at the current playhead cursor position.
    /// </summary>
    public class EntryPoint
    {
        /// <summary>
        /// Default duration (in seconds) assigned to the newly created still image timeline event.
        /// Matches standard Vegas Pro default still image duration (5.0 seconds).
        /// </summary>
        private const double DefaultEventDurationSeconds = 5.0;

        /// <summary>
        /// JPEG compression quality setting (0L - 100L). 95L provides near-lossless visual quality
        /// while maintaining manageable file sizes.
        /// </summary>
        private const long JpegQualitySetting = 95L;

        /// <summary>
        /// Subfolder name created inside the project directory to house saved clipboard images.
        /// </summary>
        private const string ClipboardFolderRelativeName = "Clipboard-Images";

        /// <summary>
        /// Main entry point method called directly by Vegas Pro when this script/extension is triggered.
        /// </summary>
        /// <param name="vegas">The active instance of the Vegas Pro application context.</param>
        public void FromVegas(Vegas vegas)
        {
            try
            {
                // ---------------------------------------------------------------------------------
                // Step 1: Validate Vegas Pro application instance and active project state
                // ---------------------------------------------------------------------------------
                if (vegas == null || vegas.Project == null)
                {
                    MessageBox.Show(
                        "No active Vegas Pro application or project instance was found.\n\n" +
                        "Please open or create a Vegas Pro project before running this script.",
                        "Paste Image - No Active Project",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                // ---------------------------------------------------------------------------------
                // Step 2: Verify clipboard content availability (lightweight presence check)
                // ---------------------------------------------------------------------------------
                // We perform a fast boolean check first to ensure the clipboard actually contains
                // an image before asking the user to save an untitled project.
                // NOTE: We do NOT extract or allocate the image bitmap buffer yet; actual extraction
                // is deferred to Step 6 once the destination file path is verified.
                if (!HasClipboardImage(out string clipboardNotice))
                {
                    MessageBox.Show(
                        clipboardNotice,
                        "Paste Image - Clipboard Notice",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                // ---------------------------------------------------------------------------------
                // Step 3: Ensure the Vegas project is saved so we have a valid destination directory
                // ---------------------------------------------------------------------------------
                // An unsaved project has an empty FilePath. The image must be saved in a subfolder
                // relative to the project file to keep project assets organized and portable.
                if (string.IsNullOrEmpty(vegas.Project.FilePath))
                {
                    DialogResult promptResult = MessageBox.Show(
                        "The current Vegas Pro project has not been saved yet.\n\n" +
                        "The project must be saved to determine where to store the clipboard image.\n\n" +
                        "Would you like to save the project now?",
                        "Save Project Required",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);

                    if (promptResult != DialogResult.Yes)
                    {
                        MessageBox.Show(
                            "Operation cancelled. The project was not saved, so the image cannot be stored.",
                            "Paste Image Cancelled",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                        return;
                    }

                    // Prompt the user with the native Vegas Pro Save Project dialog
                    bool savedSuccessfully = vegas.SaveProjectDialog(null);
                    if (!savedSuccessfully || string.IsNullOrEmpty(vegas.Project.FilePath))
                    {
                        MessageBox.Show(
                            "The project was not saved. Unable to determine the storage destination for the image.",
                            "Paste Image Cancelled",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        return;
                    }
                }

                // ---------------------------------------------------------------------------------
                // Step 4: Resolve and prepare the target directory ("Clipboard Images")
                // ---------------------------------------------------------------------------------
                string projectDirectory = Path.GetDirectoryName(vegas.Project.FilePath);
                if (string.IsNullOrEmpty(projectDirectory) || !Directory.Exists(projectDirectory))
                {
                    MessageBox.Show(
                        $"The directory containing the project file could not be accessed:\n\n" +
                        $"{vegas.Project.FilePath}",
                        "Project Directory Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                string saveDirectory = Path.Combine(projectDirectory, ClipboardFolderRelativeName);
                try
                {
                    if (!Directory.Exists(saveDirectory))
                    {
                        Directory.CreateDirectory(saveDirectory);
                    }
                }
                catch (Exception dirEx)
                {
                    MessageBox.Show(
                        $"Failed to create the clipboard images storage folder:\n\n" +
                        $"{saveDirectory}\n\n" +
                        $"System Message: {dirEx.Message}",
                        "Folder Creation Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                // ---------------------------------------------------------------------------------
                // Step 5: Generate a unique, timestamped filename for the image
                // ---------------------------------------------------------------------------------
                // Uses timestamp down to the second, with collision avoidance counter if multiple
                // pastes occur within the same second.
                string baseFileName = $"PastedImage_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";
                string fileName = $"{baseFileName}.jpg";
                string fullPath = Path.Combine(saveDirectory, fileName);
                int collisionCounter = 1;

                while (File.Exists(fullPath))
                {
                    fileName = $"{baseFileName}_{collisionCounter}.jpg";
                    fullPath = Path.Combine(saveDirectory, fileName);
                    collisionCounter++;
                }

                // ---------------------------------------------------------------------------------
                // Step 6: Extract the image buffer from clipboard and save to disk
                // ---------------------------------------------------------------------------------
                // Now that the destination directory is verified and ready, extract the image data.
                // This avoids holding unmanaged GDI+ image resources in memory during interactive dialogs.
                Image clipboardImage;
                if (!TryExtractClipboardImage(out clipboardImage, out string extractErrorMessage))
                {
                    MessageBox.Show(
                        extractErrorMessage,
                        "Clipboard Extraction Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                using (clipboardImage)
                {
                    bool saveSucceeded = SaveImageToDisk(clipboardImage, fullPath, JpegQualitySetting, out string saveError);
                    if (!saveSucceeded)
                    {
                        MessageBox.Show(
                            $"Failed to save the clipboard image to disk:\n\n" +
                            $"{fullPath}\n\n" +
                            $"Details: {saveError}",
                            "Image Save Error",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                        return;
                    }
                }

                // Double check that the file physically exists on disk
                if (!File.Exists(fullPath))
                {
                    MessageBox.Show(
                        $"The image was reported as saved, but the file could not be verified on disk:\n\n{fullPath}",
                        "File Verification Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                // ---------------------------------------------------------------------------------
                // Step 7: Add the saved image to the Vegas Project Media Pool
                // ---------------------------------------------------------------------------------
                if (vegas.Project.MediaPool == null)
                {
                    MessageBox.Show(
                        "Could not access the Vegas Pro Media Pool to register the saved image.",
                        "Media Pool Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                Media newMedia;
                try
                {
                    // Parameter 'true' instructs Vegas to place the media into the currently active media bin
                    newMedia = vegas.Project.MediaPool.AddMedia(fullPath, true);
                }
                catch (Exception mediaEx)
                {
                    MessageBox.Show(
                        $"An unexpected error occurred while adding the image to the Vegas Media Pool:\n\n" +
                        $"{fullPath}\n\n" +
                        $"Error: {mediaEx.Message}",
                        "Media Pool Import Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                if (newMedia == null)
                {
                    MessageBox.Show(
                        $"Vegas Pro was unable to load the saved image into the Media Pool:\n\n{fullPath}",
                        "Media Import Failure",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                // ---------------------------------------------------------------------------------
                // Step 8: Find or create the target video track on the timeline
                // ---------------------------------------------------------------------------------
                // Prioritize a currently selected video track; if none is selected, use the first
                // existing video track; if no video track exists in the project, create a new one.
                VideoTrack targetTrack = FindTargetVideoTrack(vegas);
                if (targetTrack == null)
                {
                    try
                    {
                        targetTrack = new VideoTrack(vegas.Project, -1, "Video");
                        vegas.Project.Tracks.Add(targetTrack);
                    }
                    catch (Exception trackEx)
                    {
                        MessageBox.Show(
                            $"Failed to create a new video track on the timeline:\n\n{trackEx.Message}",
                            "Timeline Track Error",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                        return;
                    }
                }

                // ---------------------------------------------------------------------------------
                // Step 9: Determine timeline insertion position and duration
                // ---------------------------------------------------------------------------------
                Timecode cursorPosition;
                try
                {
                    cursorPosition = (vegas.Transport != null)
                        ? vegas.Transport.CursorPosition
                        : Timecode.FromSeconds(0);
                }
                catch
                {
                    cursorPosition = Timecode.FromSeconds(0);
                }

                Timecode eventDuration = Timecode.FromSeconds(DefaultEventDurationSeconds);

                // ---------------------------------------------------------------------------------
                // Step 10: Create the timeline video event and assign the media take
                // ---------------------------------------------------------------------------------
                VideoEvent newEvent;
                try
                {
                    newEvent = targetTrack.AddVideoEvent(cursorPosition, eventDuration);
                }
                catch (Exception eventEx)
                {
                    MessageBox.Show(
                        $"Failed to create a video event at position {cursorPosition} on track '{targetTrack.Name}':\n\n" +
                        $"{eventEx.Message}",
                        "Timeline Event Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                if (newEvent == null)
                {
                    MessageBox.Show(
                        $"Vegas Pro failed to create a video event at cursor position {cursorPosition} on track '{targetTrack.Name}'.",
                        "Timeline Event Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                try
                {
                    VideoStream videoStream = newMedia.GetVideoStreamByIndex(0);
                    if (videoStream == null)
                    {
                        MessageBox.Show(
                            $"Could not locate a video stream within the imported media file:\n\n{fullPath}",
                            "Media Stream Error",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                        return;
                    }

                    Take take = newEvent.AddTake(videoStream);
                    if (take == null)
                    {
                        MessageBox.Show(
                            "The timeline event was created, but Vegas Pro failed to attach the image stream take.",
                            "Timeline Take Warning",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        return;
                    }
                }
                catch (Exception takeEx)
                {
                    MessageBox.Show(
                        $"Failed to assign the image media take to the timeline event:\n\n{takeEx.Message}",
                        "Take Assignment Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }
            }
            catch (Exception ex)
            {
                // Top-level fallback error handler catching any unforeseen exceptions
                MessageBox.Show(
                    $"An unexpected error occurred while executing the script:\n\n" +
                    $"{ex.Message}\n\n" +
                    $"Stack Trace:\n{ex.StackTrace}",
                    "Script Error - TWOC Vegas Pro Paste Buffer To JPEG",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Checks whether the Windows clipboard currently contains image data without extracting the bitmap.
        /// Provides informative guidance if text or file drops are found instead.
        /// </summary>
        /// <param name="notice">Outputs a friendly message describing what was found if no image is present.</param>
        /// <returns>True if the clipboard contains an image; otherwise false.</returns>
        private bool HasClipboardImage(out string notice)
        {
            notice = string.Empty;
            try
            {
                if (Clipboard.ContainsImage())
                {
                    return true;
                }

                if (Clipboard.ContainsText())
                {
                    notice =
                        "The clipboard currently contains text, not an image.\n\n" +
                        "Please copy an image or take a screenshot to the clipboard and try again.";
                }
                else if (Clipboard.ContainsFileDropList())
                {
                    notice =
                        "The clipboard contains file references rather than image data in the memory buffer.\n\n" +
                        "To use an image file, either drag it directly into Vegas Pro or open it, " +
                        "copy the image content to the clipboard, and run this script again.";
                }
                else
                {
                    notice =
                        "The clipboard does not contain any image data.\n\n" +
                        "Please copy an image or capture a screenshot to the clipboard and try again.";
                }
                return false;
            }
            catch (ExternalException)
            {
                // Clipboard temporarily locked; allow flow to proceed to extraction which has retry logic
                return true;
            }
            catch (Exception ex)
            {
                notice = $"Error inspecting clipboard contents:\n\n{ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Extracts the image from the Windows clipboard into an Image instance.
        /// Retries on transient clipboard locks (e.g. CLIPBRD_E_CANT_OPEN).
        /// </summary>
        /// <param name="image">Outputs the retrieved Image if successful, or null on failure.</param>
        /// <param name="errorMessage">Outputs error details if retrieval fails.</param>
        /// <returns>True if an image was retrieved successfully; otherwise false.</returns>
        private bool TryExtractClipboardImage(out Image image, out string errorMessage)
        {
            image = null;
            errorMessage = string.Empty;

            const int maxRetries = 5;
            const int retryDelayMilliseconds = 80;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    if (Clipboard.ContainsImage())
                    {
                        image = Clipboard.GetImage();
                        if (image != null)
                        {
                            return true;
                        }
                    }
                    else
                    {
                        errorMessage = "No image data was found on the clipboard.";
                        return false;
                    }
                }
                catch (ExternalException)
                {
                    // The clipboard is locked by another process (CLIPBRD_E_CANT_OPEN: 0x800401D0)
                    if (attempt < maxRetries)
                    {
                        Thread.Sleep(retryDelayMilliseconds);
                        continue;
                    }

                    errorMessage =
                        "The Windows clipboard could not be accessed because it is currently locked by another application.\n\n" +
                        "Please close any conflicting clipboard managers or screenshot utilities and try again.";
                    return false;
                }
                catch (Exception ex)
                {
                    errorMessage = $"An unexpected error occurred while reading the clipboard:\n\n{ex.Message}";
                    return false;
                }
            }

            if (image == null)
            {
                errorMessage =
                    "The clipboard indicated that an image was present, but it could not be retrieved.\n\n" +
                    "This may be caused by an unsupported image format or insufficient memory.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Saves an Image instance to disk as a high-quality JPEG file.
        /// Includes a robust fallback mechanism that converts the image onto a fresh standard Bitmap
        /// if GDI+ encounters stream locking or pixel format encoding constraints.
        /// </summary>
        /// <param name="image">The Image to save.</param>
        /// <param name="outputPath">The target file path on disk.</param>
        /// <param name="quality">JPEG compression quality (0-100).</param>
        /// <param name="errorMessage">Outputs error details if saving fails.</param>
        /// <returns>True if the file was written successfully; otherwise false.</returns>
        private bool SaveImageToDisk(Image image, string outputPath, long quality, out string errorMessage)
        {
            errorMessage = string.Empty;

            try
            {
                // Primary save attempt using the system JPEG codec with quality parameter
                SaveWithJpegCodec(image, outputPath, quality);
                return true;
            }
            catch (Exception primaryEx)
            {
                // GDI+ can fail when saving images directly from clipboard streams if the pixel format
                // or underlying memory stream is restricted. Fall back by rendering onto a fresh Bitmap.
                try
                {
                    using (Bitmap cleanBitmap = new Bitmap(image.Width, image.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb))
                    {
                        using (Graphics g = Graphics.FromImage(cleanBitmap))
                        {
                            g.Clear(Color.Black);
                            g.DrawImage(image, 0, 0, image.Width, image.Height);
                        }
                        SaveWithJpegCodec(cleanBitmap, outputPath, quality);
                    }
                    return true;
                }
                catch (Exception fallbackEx)
                {
                    errorMessage = $"Primary save failed: {primaryEx.Message}\nFallback save failed: {fallbackEx.Message}";
                    return false;
                }
            }
        }

        /// <summary>
        /// Encodes an image using the GDI+ JPEG encoder and the specified quality setting.
        /// </summary>
        /// <param name="image">Image to encode.</param>
        /// <param name="outputPath">Destination file path.</param>
        /// <param name="quality">JPEG quality value between 0 and 100.</param>
        private void SaveWithJpegCodec(Image image, string outputPath, long quality)
        {
            ImageCodecInfo jpegCodec = GetEncoder(ImageFormat.Jpeg);
            if (jpegCodec != null)
            {
                using (EncoderParameters encoderParams = new EncoderParameters(1))
                using (EncoderParameter qualityParam = new EncoderParameter(Encoder.Quality, quality))
                {
                    encoderParams.Param[0] = qualityParam;
                    image.Save(outputPath, jpegCodec, encoderParams);
                }
            }
            else
            {
                // Fallback to default GDI+ JPEG serializer if codec lookup returns null
                image.Save(outputPath, ImageFormat.Jpeg);
            }
        }

        /// <summary>
        /// Finds the most appropriate video track for placing the newly imported media.
        /// Prioritizes a user-selected video track; falls back to the first video track in the project.
        /// </summary>
        /// <param name="vegas">The active Vegas Pro instance.</param>
        /// <returns>The target VideoTrack if found; otherwise null.</returns>
        private VideoTrack FindTargetVideoTrack(Vegas vegas)
        {
            if (vegas?.Project?.Tracks == null)
            {
                return null;
            }

            // Priority 1: Check if the user has specifically selected a video track
            foreach (Track track in vegas.Project.Tracks)
            {
                if (track.Selected && track.IsVideo())
                {
                    return (VideoTrack)track;
                }
            }

            // Priority 2: Use the first available video track on the timeline
            foreach (Track track in vegas.Project.Tracks)
            {
                if (track.IsVideo())
                {
                    return (VideoTrack)track;
                }
            }

            return null;
        }

        /// <summary>
        /// Locates the GDI+ ImageCodecInfo encoder for the specified image format.
        /// </summary>
        /// <param name="format">The target ImageFormat (e.g., ImageFormat.Jpeg).</param>
        /// <returns>The matching ImageCodecInfo instance, or null if not found.</returns>
        private ImageCodecInfo GetEncoder(ImageFormat format)
        {
            try
            {
                ImageCodecInfo[] codecs = ImageCodecInfo.GetImageEncoders();
                foreach (ImageCodecInfo codec in codecs)
                {
                    if (codec.FormatID == format.Guid)
                    {
                        return codec;
                    }
                }
            }
            catch
            {
                // Return null on codec enumeration failure; callers handle null fallback
            }

            return null;
        }
    }
}