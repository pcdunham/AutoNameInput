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
- Right-click the component to select All inputs, Selected inputs, or Default-name inputs.
- Right-click → **Input types to rename** to choose which input objects are renamed (each is a checkbox, all on by default, saved with the .gh file): Number Slider, Boolean Toggle, Button, Panel, Value List, Control Knob, Digit Scroller, Value Picker, MD Slider. Value Picker is not part of core Grasshopper; it is matched by its display name if a plugin provides it.
- Default-name mode leaves custom names untouched. A name counts as default if it is empty, the type's default nickname (e.g. Toggle, Button, List, Knob, MD Slider), or the type's full name. Objects this component has named stay managed (saved with the .gh file), so re-plugging one into a different component renames it automatically. Manually renaming an object takes it out of management.
- The recipient's Name (fallback NickName) becomes the object's NickName. When an object gets a new wire, the newly connected input wins, even if the old wire is still attached.
- Watcher unsubscribes when the component is removed.
- Each copy of the component maintains its own managed set. Avoid multiple active copies with conflicting modes.
