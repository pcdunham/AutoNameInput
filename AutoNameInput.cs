using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using GH_IO.Serialization;

namespace AutoNameInput;

public sealed class PluginInfo : GH_AssemblyInfo
{
    public override string Name => "Auto Name Input";
    public override string Description => "Rename Grasshopper input objects from their connected input names.";
    public override Guid Id => new("41D1D1BB-79A7-45C2-8F7D-A9A459EAE763");
    public override string AuthorName => "pcdunham";
    public override string Version => "0.1.0";
    public override string AuthorContact => "";
}

public sealed class AutoNameComponent : GH_Component
{
    private enum NamingMode { All, Selected, DefaultsOnly }

    [Flags]
    private enum InputKind
    {
        None = 0,
        NumberSlider = 1 << 0,
        BooleanToggle = 1 << 1,
        Button = 1 << 2,
        Panel = 1 << 3,
        ValueList = 1 << 4,
        ControlKnob = 1 << 5,
        DigitScroller = 1 << 6,
        ValuePicker = 1 << 7,
        MDSlider = 1 << 8,
        All = (1 << 9) - 1,
    }

    // Menu order and labels for the per-type toggles.
    private static readonly (InputKind Kind, string Label)[] KindLabels =
    {
        (InputKind.NumberSlider, "Number Slider"),
        (InputKind.BooleanToggle, "Boolean Toggle"),
        (InputKind.Button, "Button"),
        (InputKind.Panel, "Panel"),
        (InputKind.ValueList, "Value List"),
        (InputKind.ControlKnob, "Control Knob"),
        (InputKind.DigitScroller, "Digit Scroller"),
        (InputKind.ValuePicker, "Value Picker"),
        (InputKind.MDSlider, "MD Slider"),
    };

    private NamingMode _mode = NamingMode.DefaultsOnly;
    private InputKind _kinds = InputKind.All;
    private bool _enabled = false;
    private bool _busy;
    private int _lastChanged;
    // Input object InstanceGuid -> nickname this component last assigned. A slider stays managed only while its
    // nickname still equals that value, so manual renames are respected. Persisted with the document.
    private readonly Dictionary<Guid, string> _assigned = new();
    // Input object InstanceGuid -> recipient InstanceGuids seen on the last pass, used to detect newly plugged wires.
    private readonly Dictionary<Guid, HashSet<Guid>> _knownRecipients = new();
    private GH_Document? _watchedDocument;

    public AutoNameComponent() : base("Auto Name Input", "AutoName",
        "Automatically set input object nicknames (sliders, toggles, buttons, panels, value lists, knobs, scrollers, pickers, MD sliders) to the names of connected inputs. Right-click to configure.",
        "Params", "Util")
    { }

    public override Guid ComponentGuid => new("ADCA4DA5-91C8-4BE9-93B2-7536BC2D6F49");
    protected override Bitmap? Icon => null;
    public override GH_Exposure Exposure => GH_Exposure.primary;

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddBooleanParameter("Enable", "E", "Enable automatic naming.", GH_ParamAccess.item, false);
        p.AddBooleanParameter("Run Now", "R", "Run a naming pass on demand (use a button).", GH_ParamAccess.item, false);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddTextParameter("Status", "S", "Current naming mode and watcher status.", GH_ParamAccess.item);
        p.AddIntegerParameter("Changed", "C", "Number of input objects renamed by the latest pass.", GH_ParamAccess.item);
    }

    public override void AddedToDocument(GH_Document document)
    {
        base.AddedToDocument(document);
        Attach(document);
    }

    public override void RemovedFromDocument(GH_Document document)
    {
        Detach();
        base.RemovedFromDocument(document);
    }

    private void Attach(GH_Document document)
    {
        if (ReferenceEquals(_watchedDocument, document)) return;
        Detach();
        _watchedDocument = document;
        document.SolutionEnd += OnSolutionEnd;
    }

    private void Detach()
    {
        if (_watchedDocument != null) _watchedDocument.SolutionEnd -= OnSolutionEnd;
        _watchedDocument = null;
    }

    private static InputKind KindOf(IGH_Param param) => param switch
    {
        GH_NumberSlider => InputKind.NumberSlider,
        GH_BooleanToggle => InputKind.BooleanToggle,
        GH_ButtonObject => InputKind.Button,
        GH_Panel => InputKind.Panel,
        GH_ValueList => InputKind.ValueList,
        GH_DialKnob => InputKind.ControlKnob,
        GH_DigitScroller => InputKind.DigitScroller,
        GH_MultiDimensionalSlider => InputKind.MDSlider,
        // Value Picker is not part of core Grasshopper, so match it by name rather than type.
        _ when string.Equals(param.Name?.Trim(), "Value Picker", StringComparison.OrdinalIgnoreCase)
            || param.GetType().Name.Contains("ValuePicker", StringComparison.OrdinalIgnoreCase) => InputKind.ValuePicker,
        _ => InputKind.None,
    };

    // Default nicknames Grasshopper gives each input type when it is placed on the canvas.
    private static readonly HashSet<string> DefaultNicknames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Slider", "Toggle", "Button", "Panel", "List", "Knob", "Scroller", "Picker", "MD Slider",
    };

    private static bool DefaultName(IGH_Param param)
    {
        var name = (param.NickName ?? "").Trim();
        return name.Length == 0 || DefaultNicknames.Contains(name)
            || name.Equals(param.Name?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string? RecipientName(IGH_Param recipient)
    {
        var name = recipient.Name?.Trim();
        if (!string.IsNullOrEmpty(name)) return name;
        name = recipient.NickName?.Trim();
        return string.IsNullOrEmpty(name) ? null : name;
    }

    private bool IsManaged(IGH_Param slider) =>
        _assigned.TryGetValue(slider.InstanceGuid, out var name)
        && string.Equals(name, slider.NickName, StringComparison.Ordinal);

    // Picks the name for an input object. A newly connected recipient wins, so re-plugging a slider into another
    // component renames it even if the old wire is still attached; otherwise keep the current name if it
    // still matches a recipient, else fall back to the first recipient.
    private string? DestinationName(IGH_Param slider)
    {
        var recipients = slider.Recipients.ToList();
        _knownRecipients.TryGetValue(slider.InstanceGuid, out var known);
        _knownRecipients[slider.InstanceGuid] = new HashSet<Guid>(recipients.Select(r => r.InstanceGuid));

        if (known != null)
        {
            for (int i = recipients.Count - 1; i >= 0; i--)
            {
                if (known.Contains(recipients[i].InstanceGuid)) continue;
                var name = RecipientName(recipients[i]);
                if (name != null) return name;
            }
        }
        foreach (var recipient in recipients)
        {
            var name = RecipientName(recipient);
            if (name != null && string.Equals(name, slider.NickName, StringComparison.Ordinal)) return name;
        }
        foreach (var recipient in recipients)
        {
            var name = RecipientName(recipient);
            if (name != null) return name;
        }
        return null;
    }

    private int Rename(GH_Document document)
    {
        int changed = 0;
        var sliders = document.Objects.OfType<IGH_Param>().Where(p => KindOf(p) != InputKind.None).ToList();
        var present = new HashSet<Guid>(sliders.Select(s => s.InstanceGuid));
        foreach (var id in _assigned.Keys.Where(id => !present.Contains(id)).ToList()) _assigned.Remove(id);
        foreach (var id in _knownRecipients.Keys.Where(id => !present.Contains(id)).ToList()) _knownRecipients.Remove(id);

        foreach (var slider in sliders)
        {
            if ((_kinds & KindOf(slider)) == 0) continue;
            if (_mode == NamingMode.Selected && slider.Attributes?.Selected != true) continue;
            if (_mode == NamingMode.DefaultsOnly && !IsManaged(slider) && !DefaultName(slider))
            {
                _assigned.Remove(slider.InstanceGuid);
                continue;
            }
            var target = DestinationName(slider);
            if (string.IsNullOrWhiteSpace(target)) continue;
            if (!string.Equals(slider.NickName, target, StringComparison.Ordinal))
            {
                slider.NickName = target;
                slider.Attributes?.ExpireLayout();
                changed++;
            }
            _assigned[slider.InstanceGuid] = target!;
        }
        return changed;
    }

    private void OnSolutionEnd(object sender, GH_SolutionEventArgs e)
    {
        if (!_enabled || _busy || _watchedDocument == null) return;
        _busy = true;
        try
        {
            _lastChanged = Rename(_watchedDocument);
            if (_lastChanged > 0)
            {
                Grasshopper.Instances.ActiveCanvas?.Invalidate();
            }
        }
        catch (Exception ex)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, ex.Message);
        }
        finally { _busy = false; }
    }

    protected override void SolveInstance(IGH_DataAccess da)
    {
        bool enabled = false, runNow = false;
        da.GetData(0, ref enabled);
        da.GetData(1, ref runNow);
        _enabled = enabled;
        var doc = OnPingDocument();
        if (doc != null && !ReferenceEquals(_watchedDocument, doc)) Attach(doc);
        if (_enabled && runNow && doc != null && !_busy)
        {
            _busy = true;
            try { _lastChanged = Rename(doc); }
            finally { _busy = false; }
        }
        Message = _enabled ? _mode.ToString() : "OFF";
        da.SetData(0, _enabled ? $"ON | {_mode} | {KindSummary()} | watching solutions" : "OFF");
        da.SetData(1, _lastChanged);
    }

    protected override void AppendAdditionalComponentMenuItems(System.Windows.Forms.ToolStripDropDown menu)
    {
        base.AppendAdditionalComponentMenuItems(menu);
        Menu_AppendSeparator(menu);
        Menu_AppendItem(menu, "All inputs", (s, e) => SetMode(NamingMode.All), true, _mode == NamingMode.All);
        Menu_AppendItem(menu, "Selected inputs", (s, e) => SetMode(NamingMode.Selected), true, _mode == NamingMode.Selected);
        Menu_AppendItem(menu, "Default-name inputs", (s, e) => SetMode(NamingMode.DefaultsOnly), true, _mode == NamingMode.DefaultsOnly);
        Menu_AppendSeparator(menu);

        var types = Menu_AppendItem(menu, "Input types to rename");
        foreach (var (kind, label) in KindLabels)
        {
            var k = kind;
            Menu_AppendItem(types.DropDown, label, (s, e) => SetKinds(_kinds ^ k), true, (_kinds & k) != 0);
        }
        Menu_AppendSeparator(types.DropDown);
        Menu_AppendItem(types.DropDown, "Enable all", (s, e) => SetKinds(InputKind.All), _kinds != InputKind.All);
        Menu_AppendItem(types.DropDown, "Disable all", (s, e) => SetKinds(InputKind.None), _kinds != InputKind.None);
    }

    private string KindSummary()
    {
        if (_kinds == InputKind.All) return "all types";
        if (_kinds == InputKind.None) return "no types";
        return string.Join(", ", KindLabels.Where(k => (_kinds & k.Kind) != 0).Select(k => k.Label));
    }

    private void SetKinds(InputKind kinds)
    {
        kinds &= InputKind.All;
        if (_kinds == kinds) return;
        RecordUndoEvent("Change auto-name input types");
        _kinds = kinds;
        ExpireSolution(true);
    }

    private void SetMode(NamingMode mode)
    {
        if (_mode == mode) return;
        RecordUndoEvent("Change auto-name mode");
        _mode = mode;
        ExpireSolution(true);
    }

    public override bool Write(GH_IWriter writer)
    {
        writer.SetInt32("NamingMode", (int)_mode);
        writer.SetInt32("InputKinds", (int)_kinds);
        var assigned = _assigned.ToList();
        writer.SetInt32("AssignedCount", assigned.Count);
        for (int i = 0; i < assigned.Count; i++)
        {
            writer.SetGuid("AssignedId", i, assigned[i].Key);
            writer.SetString("AssignedName", i, assigned[i].Value);
        }
        return base.Write(writer);
    }

    public override bool Read(GH_IReader reader)
    {
        int mode = (int)NamingMode.DefaultsOnly;
        if (reader.ItemExists("NamingMode")) mode = reader.GetInt32("NamingMode");
        _mode = Enum.IsDefined(typeof(NamingMode), mode) ? (NamingMode)mode : NamingMode.DefaultsOnly;
        _kinds = reader.ItemExists("InputKinds") ? (InputKind)reader.GetInt32("InputKinds") & InputKind.All : InputKind.All;
        _assigned.Clear();
        _knownRecipients.Clear();
        if (reader.ItemExists("AssignedCount"))
        {
            int count = reader.GetInt32("AssignedCount");
            for (int i = 0; i < count; i++)
            {
                if (!reader.ItemExists("AssignedId", i) || !reader.ItemExists("AssignedName", i)) continue;
                _assigned[reader.GetGuid("AssignedId", i)] = reader.GetString("AssignedName", i);
            }
        }
        return base.Read(reader);
    }
}
