using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using GH_IO.Serialization;

namespace AutoNameSliders;

public sealed class PluginInfo : GH_AssemblyInfo
{
    public override string Name => "Auto Name Sliders";
    public override string Description => "Rename Grasshopper number sliders from their connected input names.";
    public override Guid Id => new("41D1D1BB-79A7-45C2-8F7D-A9A459EAE763");
    public override string AuthorName => "Auto Name Sliders";
    public override string AuthorContact => "";
}

public sealed class AutoNameComponent : GH_Component
{
    private enum NamingMode { All, Selected, DefaultsOnly }
    private NamingMode _mode = NamingMode.DefaultsOnly;
    private bool _enabled = false;
    private bool _busy;
    private int _lastChanged;
    private readonly HashSet<Guid> _managed = new();
    private GH_Document? _watchedDocument;

    public AutoNameComponent() : base("Auto Name Sliders", "AutoName",
        "Automatically set number slider nicknames to the names of connected inputs. Right-click to configure.",
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
        p.AddIntegerParameter("Changed", "C", "Number of sliders renamed by the latest pass.", GH_ParamAccess.item);
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

    private static bool DefaultName(string? nickname)
    {
        var name = (nickname ?? "").Trim();
        return name.Length == 0 || name.Equals("Number Slider", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Slider", StringComparison.OrdinalIgnoreCase);
    }

    private static string? DestinationName(GH_NumberSlider slider)
    {
        foreach (var recipient in slider.Recipients)
        {
            var name = recipient.Name?.Trim();
            if (!string.IsNullOrEmpty(name)) return name;
            name = recipient.NickName?.Trim();
            if (!string.IsNullOrEmpty(name)) return name;
        }
        return null;
    }

    private int Rename(GH_Document document)
    {
        int changed = 0;
        var sliders = document.Objects.OfType<GH_NumberSlider>().ToList();
        foreach (var slider in sliders)
        {
            if (_mode == NamingMode.Selected && slider.Attributes?.Selected != true) continue;
            if (_mode == NamingMode.DefaultsOnly && !_managed.Contains(slider.InstanceGuid)
                && !DefaultName(slider.NickName)) continue;
            var target = DestinationName(slider);
            if (string.IsNullOrWhiteSpace(target)) continue;
            if (!string.Equals(slider.NickName, target, StringComparison.Ordinal))
            {
                slider.NickName = target;
                changed++;
            }
            _managed.Add(slider.InstanceGuid);
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
        da.SetData(0, _enabled ? $"ON | {_mode} | watching solutions" : "OFF");
        da.SetData(1, _lastChanged);
    }

    protected override void AppendAdditionalComponentMenuItems(System.Windows.Forms.ToolStripDropDown menu)
    {
        base.AppendAdditionalComponentMenuItems(menu);
        Menu_AppendSeparator(menu);
        Menu_AppendItem(menu, "All sliders", (s, e) => SetMode(NamingMode.All), true, _mode == NamingMode.All);
        Menu_AppendItem(menu, "Selected sliders", (s, e) => SetMode(NamingMode.Selected), true, _mode == NamingMode.Selected);
        Menu_AppendItem(menu, "Default-name sliders", (s, e) => SetMode(NamingMode.DefaultsOnly), true, _mode == NamingMode.DefaultsOnly);
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
        return base.Write(writer);
    }

    public override bool Read(GH_IReader reader)
    {
        int mode = (int)NamingMode.DefaultsOnly;
        if (reader.ItemExists("NamingMode")) mode = reader.GetInt32("NamingMode");
        _mode = Enum.IsDefined(typeof(NamingMode), mode) ? (NamingMode)mode : NamingMode.DefaultsOnly;
        return base.Read(reader);
    }
}
