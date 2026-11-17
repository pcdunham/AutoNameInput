# Auto Name Sliders — Rhino 8 Grasshopper GHA source

This is a C# port of the tested GH_AutoName_Component.py script. It preserves All / Selected / DefaultsOnly, first connected recipient name, managed sliders, and a SolutionEnd watcher. It writes actual slider NickName values, so Draw Full Names is irrelevant.

## Build on Windows

1. Install [Visual Studio Code](https://code.visualstudio.com/) and the [.NET SDK](https://dotnet.microsoft.com/download) (7.0 or later; Rhino 8 on Windows uses .NET 7). Check with `dotnet --version`.
2. In VS Code, install the **C# Dev Kit** extension (`ms-dotnettools.csdevkit`).
3. Open the repository folder in VS Code (**File → Open Folder…**).
4. The project references `RhinoCommon.dll`, `Grasshopper.dll`, and `GH_IO.dll` from the default Rhino 8 install location (`C:\Program Files\Rhino 8\`). If Rhino is installed elsewhere, edit the `HintPath` entries in `AutoNameSlidersGHA.csproj`.
5. Open the integrated terminal (**Terminal → New Terminal**) and build Release:

   ```powershell
   dotnet build AutoNameSlidersGHA.csproj -c Release
   ```

   Output should be `bin\Release\net7.0-windows\AutoNameSliders.gha`.
6. In Rhino, run `GrasshopperFolders` and open the **Components** folder. Copy `AutoNameSliders.gha` there.
7. Restart Rhino/Grasshopper. Look under **Utilities → Sliders**, or search `Auto Name Sliders`.

## Operation

- `Enable` Boolean input controls the watcher (default False).
- `Run Now` Boolean input triggers an immediate pass (use a Button; a latched True repeats on recompute).
- Right-click the component to select All sliders, Selected sliders, or Default-name sliders.
- Default-name mode leaves custom names untouched unless this component previously managed that slider during the current document session.
- The first recipient's Name (fallback NickName) becomes the slider NickName.
- Watcher unsubscribes when the component is removed.
- Each copy of the component maintains its own managed set. Avoid multiple active copies with conflicting modes.
