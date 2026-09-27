using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using ImageDupeFinder.Core;

namespace ImageDupeFinder.App;

/// <summary>
/// Right-hand pane: the selected folder-2 image next to its folder-1 match, with a property table
/// that highlights what differs, and buttons to open either file or show it in Explorer.
/// </summary>
internal sealed class ComparisonView : UserControl
{
    private const int PreviewMax = 1400;
    private static readonly Color DiffBack = Color.FromArgb(255, 236, 204);

    private readonly IImageDecoder _decoder;
    private readonly Label _verdict = new() { Dock = DockStyle.Fill, AutoSize = false, AutoEllipsis = true, Height = 30, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ComboBox _candidates = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label _leftTitle = NewTitle();
    private readonly Label _rightTitle = NewTitle();
    private readonly PictureBox _leftPic = NewPicture();
    private readonly PictureBox _rightPic = NewPicture();
    private readonly Button _leftOpen = new() { Text = "Open", AutoSize = true };
    private readonly Button _leftReveal = new() { Text = "Show in folder", AutoSize = true };
    private readonly Button _rightOpen = new() { Text = "Open", AutoSize = true };
    private readonly Button _rightReveal = new() { Text = "Show in folder", AutoSize = true };
    private readonly FlowLayoutPanel _rightButtons;
    private readonly ListView _table = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        GridLines = true,
        HeaderStyle = ColumnHeaderStyle.Nonclickable,
        MultiSelect = false,
    };
    private readonly Label _empty = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = SystemColors.GrayText,
        Text = "Run a scan, then select an image on the left to compare it with its match in folder 1.",
    };
    private readonly TableLayoutPanel _layout;

    private ScanResultItem? _item;
    private int _leftVersion, _rightVersion;
    private Font? _boldRowFont;

    public ComparisonView(IImageDecoder decoder)
    {
        _decoder = decoder;

        _table.Columns.Add("Property", 120);
        _table.Columns.Add("Folder 2 (checking)", 240);
        _table.Columns.Add("Folder 1 (match)", 240);

        _layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6, Padding = new Padding(6) };
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // verdict
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // candidate picker
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // titles
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 62));   // pictures
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // buttons
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 38));   // property table

        _layout.Controls.Add(_verdict, 0, 0);
        _layout.SetColumnSpan(_verdict, 2);
        _layout.Controls.Add(_candidates, 0, 1);
        _layout.SetColumnSpan(_candidates, 2);
        _layout.Controls.Add(_leftTitle, 0, 2);
        _layout.Controls.Add(_rightTitle, 1, 2);
        _layout.Controls.Add(_leftPic, 0, 3);
        _layout.Controls.Add(_rightPic, 1, 3);
        _layout.Controls.Add(Buttons(_leftOpen, _leftReveal), 0, 4);
        _rightButtons = Buttons(_rightOpen, _rightReveal);
        _layout.Controls.Add(_rightButtons, 1, 4);
        _layout.Controls.Add(_table, 0, 5);
        _layout.SetColumnSpan(_table, 2);

        Controls.Add(_layout);
        Controls.Add(_empty);
        _layout.Visible = false;
        _verdict.Font = new Font(Font, FontStyle.Bold);

        _candidates.SelectedIndexChanged += (_, _) => ShowCandidate();
        _leftOpen.Click += (_, _) => { if (_item is not null) Shell.OpenFile(_item.Image.FullPath); };
        _leftReveal.Click += (_, _) => { if (_item is not null) Shell.ShowInFolder(_item.Image.FullPath); };
        _rightOpen.Click += (_, _) => { if (Selected is { } m) Shell.OpenFile(m.Reference.FullPath); };
        _rightReveal.Click += (_, _) => { if (Selected is { } m) Shell.ShowInFolder(m.Reference.FullPath); };
        _leftPic.DoubleClick += (_, _) => _leftOpen.PerformClick();
        _rightPic.DoubleClick += (_, _) => _rightOpen.PerformClick();
        Resize += (_, _) => SizeColumns();
    }

    private MatchCandidate? Selected =>
        _item is not null && _candidates.SelectedIndex >= 0 && _candidates.SelectedIndex < _item.Matches.Count
            ? _item.Matches[_candidates.SelectedIndex]
            : null;

    public void ShowItem(ScanResultItem? item)
    {
        _item = item;
        if (item is null)
        {
            _layout.Visible = false;
            _empty.Visible = true;
            _leftVersion++;
            _rightVersion++;
            SetPicture(_leftPic, null);
            SetPicture(_rightPic, null);
            return;
        }

        _empty.Visible = false;
        _layout.Visible = true;

        (string label, Color color) = item.Category switch
        {
            MatchCategory.Duplicate => ("DUPLICATE", Palette.Duplicate),
            MatchCategory.PossibleDuplicate => ("POSSIBLE DUPLICATE", Palette.Possible),
            _ => ("NEW", Palette.New),
        };
        _verdict.ForeColor = color;
        _verdict.Text = item.BestMatch is null
            ? $"{label} — {item.Summary}"
            : $"{label} — {item.BestMatch.Reason}";

        _leftTitle.Text = $"Folder 2:  {item.Image.RelativePath}";
        LoadPreviewAsync(_leftPic, item.Image.FullPath);

        _candidates.BeginUpdate();
        _candidates.Items.Clear();
        for (int i = 0; i < item.Matches.Count; i++)
        {
            var m = item.Matches[i];
            _candidates.Items.Add($"Match {i + 1} of {item.Matches.Count}:  {m.Reference.RelativePath}  —  {m.Reason}");
        }
        _candidates.EndUpdate();

        _candidates.Visible = item.Matches.Count > 0;
        _candidates.Enabled = item.Matches.Count > 1;
        if (item.Matches.Count > 0)
        {
            _candidates.SelectedIndex = 0; // triggers ShowCandidate
        }
        else
        {
            ShowCandidate();
        }
    }

    private void ShowCandidate()
    {
        if (_item is null) return;
        var m = Selected;

        _rightButtons.Enabled = m is not null;
        _table.BeginUpdate();
        _table.Items.Clear();

        if (m is null)
        {
            _rightTitle.Text = "Folder 1:  no match";
            _rightVersion++;
            SetPicture(_rightPic, BitmapHelper.Placeholder("No matching image in folder 1"));
            foreach (var (name, value) in Describe(_item.Image))
                _table.Items.Add(new ListViewItem(new[] { name, value, "" }));
        }
        else
        {
            _rightTitle.Text = $"Folder 1:  {m.Reference.RelativePath}" + (m.Similarity is int s ? $"   ({s}% similar)" : "");
            LoadPreviewAsync(_rightPic, m.Reference.FullPath);
            foreach (var row in Comparison.Compare(_item.Image, m.Reference))
            {
                var lvi = new ListViewItem(new[] { row.Property, row.CheckValue, row.ReferenceValue });
                if (row.Differs)
                {
                    lvi.BackColor = DiffBack;
                    lvi.Font = _boldRowFont ??= new Font(_table.Font, FontStyle.Bold);
                }
                _table.Items.Add(lvi);
            }
        }
        _table.EndUpdate();
        SizeColumns();
    }

    private static (string, string)[] Describe(ImageRecord r) => new[]
    {
        ("File name", r.FileName),
        ("Location", System.IO.Path.GetDirectoryName(r.FullPath) ?? ""),
        ("Dimensions", r.DimensionsText),
        ("File size", Comparison.FormatSize(r.FileSize)),
        ("Format", r.Format ?? "—"),
        ("Date taken", r.DateTaken?.ToString("yyyy-MM-dd HH:mm:ss") ?? "—"),
        ("Camera", r.Camera ?? "—"),
        ("Last modified", r.LastModified.ToString("yyyy-MM-dd HH:mm:ss")),
        ("Note", r.DecodeError ?? ""),
    }.Where(p => p.Item1 != "Note" || p.Item2.Length > 0).ToArray();

    private async void LoadPreviewAsync(PictureBox box, string path)
    {
        bool left = box == _leftPic;
        int version = left ? ++_leftVersion : ++_rightVersion;
        SetPicture(box, null);
        Bitmap bmp;
        try
        {
            bmp = await Task.Run(() => BitmapHelper.LoadPreview(path, _decoder, PreviewMax));
        }
        catch (Exception ex)
        {
            bmp = BitmapHelper.Placeholder("Preview failed\n" + ex.Message);
        }

        // The user may have moved on to another image while this was decoding.
        if (version != (left ? _leftVersion : _rightVersion) || IsDisposed)
        {
            bmp.Dispose();
            return;
        }
        SetPicture(box, bmp);
    }

    private static void SetPicture(PictureBox box, Image? img)
    {
        var old = box.Image;
        box.Image = img;
        old?.Dispose();
    }

    private void SizeColumns()
    {
        if (_table.Columns.Count < 3) return;
        int w = _table.ClientSize.Width - 4;
        _table.Columns[0].Width = Math.Max(100, w / 5);
        int rest = Math.Max(160, (w - _table.Columns[0].Width) / 2);
        _table.Columns[1].Width = rest;
        _table.Columns[2].Width = rest;
    }

    private static FlowLayoutPanel Buttons(params Control[] buttons)
    {
        var p = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        p.Controls.AddRange(buttons);
        return p;
    }

    private static Label NewTitle() => new()
    {
        Dock = DockStyle.Fill,
        AutoEllipsis = true,
        Height = 22,
        TextAlign = ContentAlignment.MiddleLeft,
    };

    private static PictureBox NewPicture() => new()
    {
        Dock = DockStyle.Fill,
        SizeMode = PictureBoxSizeMode.Zoom,
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = Color.FromArgb(40, 40, 40),
        Cursor = Cursors.Hand,
        Margin = new Padding(3),
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _leftPic.Image?.Dispose();
            _rightPic.Image?.Dispose();
            _boldRowFont?.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal static class Palette
{
    public static readonly Color New = Color.FromArgb(31, 122, 77);
    public static readonly Color Duplicate = Color.FromArgb(180, 35, 24);
    public static readonly Color Possible = Color.FromArgb(181, 71, 8);
}
