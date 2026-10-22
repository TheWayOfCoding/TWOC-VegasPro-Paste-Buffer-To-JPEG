# TWOC VegasPro Paste Buffer To JPEG

A Vegas Pro extension script that captures image data directly from the Windows clipboard buffer, saves it as a high-quality JPEG image in a dedicated project subfolder, imports it into the Project Media Pool, and inserts it onto the timeline at the current playhead cursor position.

Developed by [TheWayOfCoding](https://github.com/TheWayOfCoding).

---

## Features

- **Direct Clipboard Image Extraction**: Paste screenshots or copied images directly into Vegas Pro without needing to manually save them to disk first.
- **Project-Centric Organization**: Saves images to a dedicated `Clipboard Images` subfolder within your project's directory, keeping project assets portable and organized.
- **Smart Timeline Placement**:
  - Automatically targets the currently selected video track; if none is selected, uses the first video track (or creates one if none exists).
  - Inserts the image event at the current playhead cursor position with a standard 5-second duration.
- **High-Quality JPEG Encoding**: Encodes with a 95% quality parameter and includes robust GDI+ stream fallbacks to ensure images save reliably.
- **Comprehensive Error Handling & User Feedback**:
  - Clear message boxes for all scenarios (clipboard locked by another program, non-image clipboard content such as text or file drops, unsaved project prompt, disk errors, etc.).
  - Distinguishes between text clipboard data, file references, and true image bitmaps so you always know why an operation didn't proceed.
- **Collision Avoidance**: Automatic timestamping down to the second with sequential fallback counters prevents overwriting images when pasting repeatedly.

---

## Requirements

- **Vegas Pro** (with .NET scripting support, e.g. Vegas Pro 14+)
- **.NET Framework 4.8**
- Reference to `ScriptPortal.Vegas.dll` (located in your Vegas Pro installation directory)

---

## Installation

1. Build the solution in Visual Studio or via CLI:
   ```cmd
   dotnet msbuild TWOCVegasProPasteBufferToJPEG\TWOCVegasProPasteBufferToJPEG.csproj -p:Configuration=Release
   ```
2. The build automatically copies the compiled `TWOCVegasProPasteBufferToJPEG.dll` (and `.pdb`) via post-build event to:
   ```
   %USERPROFILE%\Documents\Vegas Script Menu\TWOC-VegasPro-Paste-Buffer-To-JPEG\
   ```
3. Alternatively, manually copy `TWOCVegasProPasteBufferToJPEG.dll` into:
   ```
   C:\Users\<YourUsername>\Documents\Vegas Script Menu\TWOC-VegasPro-Paste-Buffer-To-JPEG\
   ```
4. Restart Vegas Pro or rescan the Script Menu (`Tools` > `Scripting` > `Rescan Script Menu Folders`).

---

## How to Use

1. Copy an image or take a screenshot to the clipboard (e.g. `Win + Shift + S`, `PrtScn`, or right-click > Copy Image in a browser).
2. In Vegas Pro, place the playhead cursor at the desired insertion time on the timeline.
3. (Optional) Select the video track where you want the image placed.
4. Run the script via **Tools** > **Scripting** > **TWOC-VegasPro-Paste-Buffer-To-JPEG** (or bind it to a toolbar button or keyboard shortcut).
5. The image is saved into `<ProjectFolder>\Clipboard Images\`, added to the Media Pool, and placed on your timeline.

---

## License

This project is licensed under the **GNU Affero General Public License v3.0** (AGPL-3.0). See the [LICENSE](LICENSE) file for the full license text.
