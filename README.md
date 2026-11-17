# Auto Name Sliders — Rhino 8 Grasshopper GHA source

This is a C# port of the tested GH_AutoName_Component.py script. It preserves All / Selected / DefaultsOnly, first connected recipient name, managed sliders, and a SolutionEnd watcher. It writes actual slider NickName values, so Draw Full Names is irrelevant.

## Build on Windows

1. Install Visual Studio 2022 with **.NET desktop development** and the .NET 7 SDK (Rhino 8 Windows uses .NET 7).
2. Open `AutoNameSlidersGHA.csproj` in Visual Studio.
3. Set project property `RhinoSystemDir` to the Rhino 8 System directory, normally `C:\Program Files\Rhino 8\System\` (include final backslash). One way: add `<RhinoSystemDir>C:\Program Files\Rhino 8\System\</RhinoSystemDir>` to the csproj PropertyGroup.
4. Build **Release**. Output should be `bin\Release\net7.0-windows\AutoNameSliders.gha`.
5. In Rhino, run `GrasshopperFolders` and open the **Components** folder. Copy `AutoNameSliders.gha` there.
6. Restart Rhino/Grasshopper. Look under **Utilities → Sliders**, or search `Auto Name Sliders`.

## Operation

- `Enable` Boolean input controls the watcher (default True).
- `Run Now` Boolean input triggers an immediate pass (use a Button; a latched True repeats on recompute).
- Right-click the component to select All sliders, Selected sliders, or Default-name sliders.
- Default-name mode leaves custom names untouched unless this component previously managed that slider during the current document session.
- The first recipient's Name (fallback NickName) becomes the slider NickName.
- Watcher unsubscribes when the component is removed.
- Each copy of the component maintains its own managed set. Avoid multiple active copies with conflicting modes.

## Limitations and testing

The source is provided for compilation on your Windows Rhino installation. It has not been compiled or runtime-tested here (the build environment lacks the .NET SDK and Rhino/Grasshopper assemblies). Grasshopper API signatures may need small adjustments in your Rhino 8 service release. If `InvalidateCanvas()` is unavailable, remove that line; the nickname change still works. SolutionEnd monitoring detects changes that trigger Grasshopper solutions; it is not a direct wire-event listener. Managed state is in-memory and is reset when the definition reopens.

For a persistent background plugin independent of a placed component, use a separate assembly-priority/document-server service; this implementation intentionally follows the Bifocals-style placed-component workflow.
