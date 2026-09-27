using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using ImageDupeFinder.App.Decoding;
using ImageDupeFinder.Core;

namespace ImageDupeFinder.App;

internal sealed class MainForm : Form
{
    private const int ThumbSize = 64;

    private readonly IImageDecoder _decoder = new WindowsImageDecoder();
    private readonly AppSettings _settings = AppSettings.Load();

    private readonly TextBox _folder1 = new() { Dock = DockStyle.Fill, AllowDrop = true };
    private readonly TextBox _folder2 = new() { Dock = DockStyle.Fill, AllowDrop = true };
    private readonly CheckBox _subfolders = new() { Text = "Include subfolders", AutoSize = true, Margin = new Padding(3, 6, 12, 3) };
    private readonly NumericUpDown _tolerance = new() { Minimum = 0, Maximum = 24, Width = 50, Margin = new Padding(3, 3, 12, 3) };
    private readonly CheckBox _sameName = new() { Text = "Flag same file name as possible duplicate", AutoSize = true, Margin = new Padding(3, 6, 12, 3) };
    private readonly Button _scan = new() { Text = "Scan", AutoSize = true, Padding = new Padding(12, 0, 12, 0) };
    private readonly Button _cancel = new() { Text = "Cancel", AutoSize = true, Enabled = false };
    private readonly Button _export = new() { Text = "Export HTML report…", AutoSize = true, Enabled = false };

    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly Dictionary<MatchCategory, (TabPage Page, ListView List, string Title)> _lists = new();
    private readonly ImageList _thumbs = new() { ImageSize = new Size(ThumbSize, ThumbSize), ColorDepth = ColorDepth.Depth32Bit };
    private readonly ComparisonView _compare;
    private readonly SplitContainer _split = new() { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };

    private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft, Text = "Choose two folders and press Scan." };
    private readonly ToolStripProgressBar _progress = new() { Visible = false, Width = 220 };

    private CancellationTokenSource? _cts;
    private ScanResult? _result;

    public MainForm()
    {
        Text = "Image Duplicate Finder";
        Font = SystemFonts.MessageBoxFont ?? Font;
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1400, 860);
        MinimumSize = new Size(900, 560);
        StartPosition = FormStartPosition.CenterScreen;

        _compare = new ComparisonView(_decoder) { Dock = DockStyle.Fill };

        // --- Top: folders and options -------------------------------------------------------
        var top = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Padding = new Padding(8, 8, 8, 4) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        AddFolderRow(top, 0, "Folder 1 (reference):", _folder1, "Select the reference folder (folder 1)");
        AddFolderRow(top, 1, "Folder 2 (to check):", _folder2, "Select the folder to check (folder 2)");

        var options = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, 4, 0, 0) };
        var tolLabel = new Label { Text = "Similarity tolerance:", AutoSize = true, Margin = new Padding(3, 7, 0, 3) };
        options.Controls.AddRange(new Control[] { _subfolders, tolLabel, _tolerance, _sameName, _scan, _cancel, _export });
        top.Controls.Add(options, 0, 2);
        top.SetColumnSpan(options, 3);

        var tips = new ToolTip { AutoPopDelay = 20000 };
        const string tolTip =
            "How different two images may look (0–24) and still be flagged as a possible duplicate.\n" +
            "0–4: only near-identical copies.  8–10 (default 10): resized and re-compressed copies.\n" +
            "12–16: also light crops and edits, with more false alarms.\n" +
            "Changing this after a scan re-sorts the results instantly without rescanning.";
        tips.SetToolTip(_tolerance, tolTip);
        tips.SetToolTip(tolLabel, tolTip);
        tips.SetToolTip(_sameName, "Images in folder 2 whose file name also exists in folder 1, even though the pictures differ.");

        // --- Left: category tabs --------------------------------------------------------------
        AddTab(MatchCategory.New, "New");
        AddTab(MatchCategory.Duplicate, "Duplicate");
        AddTab(MatchCategory.PossibleDuplicate, "Possible Duplicate");
        _tabs.SelectedIndexChanged += (_, _) => ShowSelected();

        _split.Panel1.Controls.Add(_tabs);
        _split.Panel2.Controls.Add(_compare);

        var statusStrip = new StatusStrip();
        statusStrip.Items.Add(_status);
        statusStrip.Items.Add(_progress);

        // Fill first, then edges (WinForms docks in reverse z-order).
        Controls.Add(_split);
        Controls.Add(top);
        Controls.Add(statusStrip);

        // --- Wiring ---------------------------------------------------------------------------
        _scan.Click += (_, _) => StartScan();
        _cancel.Click += (_, _) => _cts?.Cancel();
        _export.Click += (_, _) => ExportReport();
        _tolerance.ValueChanged += (_, _) => Reclassify();
        _sameName.CheckedChanged += (_, _) => Reclassify();
        _subfolders.CheckedChanged += (_, _) =>
        {
            if (_result is not null) _status.Text = "Subfolder setting changed — press Scan to apply it.";
        };
        AcceptButton = _scan;

        _folder1.Text = _settings.Folder1;
        _folder2.Text = _settings.Folder2;
        _subfolders.Checked = _settings.IncludeSubfolders;
        _tolerance.Value = Math.Clamp(_settings.SimilarityTolerance, (int)_tolerance.Minimum, (int)_tolerance.Maximum);
        _sameName.Checked = _settings.FlagSameFileName;

        Load += (_, _) => _split.SplitterDistance = (int)(_split.Width * 0.46);
        FormClosing += (_, _) =>
        {
            _cts?.Cancel();
            _settings.Folder1 = _folder1.Text.Trim();
            _settings.Folder2 = _folder2.Text.Trim();
            _settings.IncludeSubfolders = _subfolders.Checked;
            _settings.SimilarityTolerance = (int)_tolerance.Value;
            _settings.FlagSameFileName = _sameName.Checked;
            _settings.Save();
        };
    }

    // ------------------------------------------------------------------------------------------
    // Layout helpers
    // ------------------------------------------------------------------------------------------

    private void AddFolderRow(TableLayoutPanel t, int row, string label, TextBox box, string dialogTitle)
    {
        var browse = new Button { Text = "Browse…", AutoSize = true };
        browse.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { Description = dialogTitle, UseDescriptionForTitle = true, ShowNewFolderButton = false };
            if (Directory.Exists(box.Text)) dlg.SelectedPath = box.Text;
            if (dlg.ShowDialog(this) == DialogResult.OK) box.Text = dlg.SelectedPath;
        };

        // Drag a folder from Explorer onto the box.
        box.DragEnter += (_, e) =>
        {
            e.Effect = e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } p && Directory.Exists(p[0])
                ? DragDropEffects.Copy : DragDropEffects.None;
        };
        box.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } p && Directory.Exists(p[0])) box.Text = p[0];
        };

        t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 6, 3) }, 0, row);
        t.Controls.Add(box, 1, row);
        t.Controls.Add(browse, 2, row);
    }

    private void AddTab(MatchCategory category, string title)
    {
        var list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false,
            SmallImageList = _thumbs,
            ShowItemToolTips = true,
        };
        list.Columns.Add("File", 230);
        list.Columns.Add("Dimensions", 95);
        list.Columns.Add("Size", 75);
        list.Columns.Add(category == MatchCategory.New ? "Notes" : "Match in folder 1", 420);

        list.SelectedIndexChanged += (_, _) => ShowSelected();
        list.DoubleClick += (_, _) => { if (SelectedItem(list) is { } it) Shell.OpenFile(it.Image.FullPath); };
        list.ColumnClick += (_, e) => SortList(list, e.Column);
        list.ContextMenuStrip = BuildContextMenu(list);

        var page = new TabPage(title) { Padding = new Padding(2) };
        page.Controls.Add(list);
        _tabs.TabPages.Add(page);
        _lists[category] = (page, list, title);
    }

    private ContextMenuStrip BuildContextMenu(ListView list)
    {
        var menu = new ContextMenuStrip();
        var open = menu.Items.Add("Open image", null, (_, _) => { if (SelectedItem(list) is { } it) Shell.OpenFile(it.Image.FullPath); });
        var reveal = menu.Items.Add("Show image in folder", null, (_, _) => { if (SelectedItem(list) is { } it) Shell.ShowInFolder(it.Image.FullPath); });
        menu.Items.Add(new ToolStripSeparator());
        var openMatch = menu.Items.Add("Open matching image in folder 1", null, (_, _) => { if (SelectedItem(list)?.BestMatch is { } m) Shell.OpenFile(m.Reference.FullPath); });
        var revealMatch = menu.Items.Add("Show matching image in folder", null, (_, _) => { if (SelectedItem(list)?.BestMatch is { } m) Shell.ShowInFolder(m.Reference.FullPath); });
        menu.Items.Add(new ToolStripSeparator());
        var copy = menu.Items.Add("Copy path(s)", null, (_, _) =>
        {
            if (SelectedItem(list) is not { } it) return;
            var text = it.BestMatch is { } m ? $"{it.Image.FullPath}\r\n{m.Reference.FullPath}" : it.Image.FullPath;
            Clipboard.SetText(text);
        });
        menu.Opening += (_, e) =>
        {
            var it = SelectedItem(list);
            if (it is null) { e.Cancel = true; return; }
            openMatch.Enabled = revealMatch.Enabled = it.BestMatch is not null;
        };
        return menu;
    }

    private static ScanResultItem? SelectedItem(ListView list) =>
        list.SelectedItems.Count > 0 ? list.SelectedItems[0].Tag as ScanResultItem : null;

    private void ShowSelected()
    {
        var page = _tabs.SelectedTab;
        var entry = _lists.Values.FirstOrDefault(v => v.Page == page);
        _compare.ShowItem(entry.List is null ? null : SelectedItem(entry.List));
    }

    // ------------------------------------------------------------------------------------------
    // Scanning
    // ------------------------------------------------------------------------------------------

    private async void StartScan()
    {
        string f1 = _folder1.Text.Trim(), f2 = _folder2.Text.Trim();
        if (!Directory.Exists(f1)) { Warn("Folder 1 doesn't exist. Choose the reference folder first."); return; }
        if (!Directory.Exists(f2)) { Warn("Folder 2 doesn't exist. Choose the folder to check first."); return; }
        if (string.Equals(Path.GetFullPath(f1).TrimEnd('\\', '/'), Path.GetFullPath(f2).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
        {
            Warn("Folder 1 and folder 2 are the same folder. Pick two different folders.");
            return;
        }

        var options = BuildOptions(f1, f2);
        _cts = new CancellationTokenSource();
        SetBusy(true);
        ClearResults();

        var progress = new Progress<ScanProgress>(p =>
        {
            if (p.Total > 0)
            {
                _progress.Maximum = p.Total;
                _progress.Value = Math.Min(p.Done, p.Total);
                _status.Text = $"{p.Stage}… {p.Done:N0} of {p.Total:N0}" +
                               (p.CurrentFile is null ? "" : $"   {Path.GetFileName(p.CurrentFile)}");
            }
            else
            {
                _status.Text = p.Stage + "…";
            }
        });

        try
        {
            _result = await new DuplicateScanner(_decoder).ScanAsync(options, progress, _cts.Token);
            ShowResults();
            int unreadable = _result.Items.Count(i => i.Image.DecodeError is not null) +
                             _result.ReferenceImages.Count(r => r.DecodeError is not null);
            _status.Text =
                $"Scanned {_result.ReferenceImages.Count:N0} reference and {_result.Items.Count:N0} checked images in {_result.Elapsed.TotalSeconds:0.0}s." +
                (unreadable > 0 ? $"  {unreadable} file(s) couldn't be decoded and were compared by contents and name only." : "");
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            _status.Text = "Scan failed.";
            MessageBox.Show(this, ex.Message, "Scan failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            SetBusy(false);
        }
    }

    private ScanOptions BuildOptions(string f1, string f2) => new()
    {
        ReferenceFolder = f1,
        CheckFolder = f2,
        IncludeSubfolders = _subfolders.Checked,
        SimilarityTolerance = (int)_tolerance.Value,
        FlagSameFileName = _sameName.Checked,
    };

    /// <summary>Tolerance / same-name changes only affect matching, so re-run that on the existing hashes.</summary>
    private void Reclassify()
    {
        if (_result is null || _cts is not null) return;
        var options = BuildOptions(_result.Options.ReferenceFolder, _result.Options.CheckFolder);
        var items = DuplicateScanner.Classify(_result.Items.Select(i => i.Image).ToList(), _result.ReferenceImages, options);
        _result = new ScanResult
        {
            Options = options,
            ReferenceImages = _result.ReferenceImages,
            Items = items,
            Elapsed = _result.Elapsed,
            CompletedAt = _result.CompletedAt,
        };
        ShowResults();
        _status.Text = $"Re-sorted results with similarity tolerance {options.SimilarityTolerance}.";
    }

    private void SetBusy(bool busy)
    {
        _scan.Enabled = !busy;
        _cancel.Enabled = busy;
        _export.Enabled = !busy && _result is not null;
        _folder1.ReadOnly = _folder2.ReadOnly = busy;
        _subfolders.Enabled = _tolerance.Enabled = _sameName.Enabled = !busy;
        _progress.Visible = busy;
        _progress.Value = 0;
        UseWaitCursor = false;
    }

    private void ClearResults()
    {
        _result = null;
        foreach (var (page, list, title) in _lists.Values)
        {
            list.Items.Clear();
            page.Text = title;
        }
        DisposeThumbs();
        _compare.ShowItem(null);
    }

    private void ShowResults()
    {
        if (_result is null) return;

        // Remember selection so re-sorting (tolerance change) doesn't lose the user's place.
        string? selectedPath = null;
        foreach (var (_, list, _) in _lists.Values)
            if (SelectedItem(list) is { } s && list.Parent == _tabs.SelectedTab) selectedPath = s.Image.FullPath;
        TabPage? selectedPage = null;

        foreach (var (_, list, _) in _lists.Values) list.Items.Clear();
        DisposeThumbs();

        foreach (var (category, (page, list, title)) in _lists)
        {
            var items = _result.InCategory(category).ToList();
            page.Text = $"{title} ({items.Count:N0})";

            list.BeginUpdate();
            foreach (var item in items)
            {
                string key = item.Image.FullPath;
                if (!_thumbs.Images.ContainsKey(key))
                {
                    using var t = BitmapHelper.FromPng(item.Image.ThumbnailPng);
                    _thumbs.Images.Add(key, BitmapHelper.Square(t, ThumbSize));
                }

                var lvi = new ListViewItem(item.Image.RelativePath, key) { Tag = item, ToolTipText = item.Image.FullPath };
                lvi.SubItems.Add(item.Image.DimensionsText);
                lvi.SubItems.Add(Comparison.FormatSize(item.Image.FileSize));
                lvi.SubItems.Add(item.Summary);
                list.Items.Add(lvi);
                if (key == selectedPath)
                {
                    lvi.Selected = true;
                    selectedPage = page;
                }
            }
            list.EndUpdate();
        }

        _export.Enabled = true;

        // First time through, jump to the tab most worth reviewing.
        if (selectedPath is null)
        {
            var first = _result.Count(MatchCategory.PossibleDuplicate) > 0 ? MatchCategory.PossibleDuplicate
                      : _result.Count(MatchCategory.Duplicate) > 0 ? MatchCategory.Duplicate
                      : MatchCategory.New;
            _tabs.SelectedTab = _lists[first].Page;
            var l = _lists[first].List;
            if (l.Items.Count > 0) l.Items[0].Selected = true;
        }
        else if (selectedPage is not null)
        {
            // The image may have moved category; follow it.
            _tabs.SelectedTab = selectedPage;
        }
        ShowSelected();
    }

    private void DisposeThumbs() => _thumbs.Images.Clear();

    private static void SortList(ListView list, int column)
    {
        bool desc = list.Tag is int prev && prev == column;
        list.Tag = desc ? -1 : column;
        list.ListViewItemSorter = new ItemComparer(column, desc);
        list.Sort();
    }

    private sealed class ItemComparer(int column, bool descending) : System.Collections.IComparer
    {
        public int Compare(object? x, object? y)
        {
            var a = (x as ListViewItem)?.Tag as ScanResultItem;
            var b = (y as ListViewItem)?.Tag as ScanResultItem;
            if (a is null || b is null) return 0;
            int r = column switch
            {
                1 => ((long)a.Image.Width * a.Image.Height).CompareTo((long)b.Image.Width * b.Image.Height),
                2 => a.Image.FileSize.CompareTo(b.Image.FileSize),
                3 => (b.BestMatch?.Similarity ?? -1).CompareTo(a.BestMatch?.Similarity ?? -1),
                _ => string.Compare(a.Image.RelativePath, b.Image.RelativePath, StringComparison.OrdinalIgnoreCase),
            };
            return descending ? -r : r;
        }
    }

    // ------------------------------------------------------------------------------------------
    // Export
    // ------------------------------------------------------------------------------------------

    private void ExportReport()
    {
        if (_result is null) return;
        using var dlg = new SaveFileDialog
        {
            Filter = "HTML report (*.html)|*.html",
            FileName = $"Image duplicate report {DateTime.Now:yyyy-MM-dd HHmm}.html",
            Title = "Save report",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            HtmlReportWriter.Write(_result, dlg.FileName);
            _status.Text = $"Report saved to {dlg.FileName}";
            Shell.OpenFile(dlg.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Couldn't save report", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Warn(string message) =>
        MessageBox.Show(this, message, "Image Duplicate Finder", MessageBoxButtons.OK, MessageBoxIcon.Information);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeThumbs();
            _thumbs.Dispose();
        }
        base.Dispose(disposing);
    }
}
