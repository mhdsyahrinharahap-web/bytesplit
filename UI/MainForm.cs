using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using ByteSplit.Core;
using ByteSplit.Models;

namespace ByteSplit.UI;

public sealed class MainForm : Form
{
    private const int WmClipboardUpdate = 0x031D;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    private static readonly Font UiFont = new("Segoe UI", 9.5f);
    private static readonly Font UiBoldFont = new("Segoe UI", 9.5f, FontStyle.Bold);
    private static readonly Font BrandTitleFont = new("Segoe UI", 11f, FontStyle.Bold);
    private static readonly Font BrandBadgeFont = new("Segoe UI", 8f, FontStyle.Bold);
    private static readonly Font SidebarGroupFont = new("Segoe UI", 8f, FontStyle.Bold);
    private static readonly Font SidebarItemFont = new("Segoe UI", 9.3f, FontStyle.Regular);
    private static readonly Font SidebarItemBoldFont = new("Segoe UI", 9.3f, FontStyle.Bold);
    private static readonly Font BadgeFont = new("Segoe UI", 8f, FontStyle.Bold);
    private static readonly Font GridHeaderFont = new("Segoe UI", 9f, FontStyle.Bold);
    private static readonly Font GridCellFont = new("Segoe UI", 9.2f, FontStyle.Regular);
    private static readonly Font GridStatusFont = new("Segoe UI", 8.5f, FontStyle.Bold);
    private static readonly Font DetailUrlFont = new("Segoe UI", 8.5f);
    private static readonly Font StatusBoldFont = new("Segoe UI", 8.8f, FontStyle.Bold);
    private static readonly Font StatusRegularFont = new("Segoe UI", 8.8f);
    private static readonly Font EmptyTitleFont = new("Segoe UI", 11f, FontStyle.Bold);

    private readonly DownloadManager _manager;
    private readonly Icon _appIcon;
    private readonly Bitmap? _appLogo;
    private readonly NotifyIcon _trayIcon;
    private readonly ToolTip _toolTip;
    private readonly System.Windows.Forms.Timer _uiThrottleTimer;

    private readonly Panel _topBar;
    private readonly Panel _toolbarDivider1;
    private readonly FluentButton _btnAdd;
    private readonly FluentButton _btnPause;
    private readonly FluentButton _btnResume;
    private readonly FluentButton _btnRefreshLink;
    private readonly FluentButton _btnRemove;
    private readonly FluentCheckBox _chkClipboard;
    private readonly Label _lblLimit;
    private readonly FluentComboBox _cmbLimit;
    private readonly FluentButton _btnBaseFolder;
    private readonly FluentButton _btnTheme;
    private readonly FluentButton _btnAbout;

    private readonly Panel _sidebarPanel;
    private readonly Panel _brandHeader;
    private readonly SidebarNavControl _sidebar;

    private readonly Panel _workspacePanel;
    private readonly DataGridView _grid;
    private readonly EmptyStateView _emptyStateView;

    private readonly Panel _bottomPanel;
    private readonly Label _lblDetailTitle;
    private readonly Label _lblDetailMeta;
    private readonly SegmentBarControl _segmentBar;
    private readonly Label _lblDetailUrl;

    private readonly Panel _statusBar;
    private readonly Label _lblStatusSummary;
    private readonly Label _lblStatusFolder;

    private ThemePalette _palette = ThemePalette.Light;
    private string? _selectedTaskId;
    private int _hoveredGridRow = -1;
    private string _lastClipboardUrl = string.Empty;
    private bool _lastStatusHasActive;
    private bool _forceExit;
    private bool _isDialogOpen;
    private bool _suppressSelectionChanged;
    private volatile bool _uiDirty;

    private static readonly (string Key, string Label, bool IsHeader, GlyphKind Glyph)[] SidebarItems =
    {
        ("header_status", "STATUS", true, GlyphKind.None),
        ("all", "Semua Unduhan", false, GlyphKind.AllItems),
        ("active", "Sedang Berjalan", false, GlyphKind.Downloading),
        ("completed", "Selesai", false, GlyphKind.CheckCircle),
        ("header_cat", "KATEGORI", true, GlyphKind.None),
        ("Compressed", "Compressed", false, GlyphKind.Archive),
        ("Documents", "Documents", false, GlyphKind.Document),
        ("Music", "Music", false, GlyphKind.Music),
        ("Programs", "Programs", false, GlyphKind.AppWindow),
        ("Video", "Video", false, GlyphKind.Video),
        ("Other", "Lainnya", false, GlyphKind.OtherFile)
    };

    private static readonly (string Label, long Bps)[] SpeedPresets =
    {
        ("Tanpa Batas", 0),
        ("512 KB/s", 512 * 1024),
        ("1 MB/s", 1024 * 1024),
        ("2 MB/s", 2 * 1024 * 1024),
        ("5 MB/s", 5 * 1024 * 1024),
        ("10 MB/s", 10 * 1024 * 1024)
    };

    public MainForm()
    {
        string dataDir = Path.Combine(AppContext.BaseDirectory, "data");
        _manager = new DownloadManager(dataDir);
        _appIcon = IconHelper.LoadAppIcon();
        _appLogo = IconHelper.LoadAppBitmap();
        _toolTip = new ToolTip { InitialDelay = 300, ReshowDelay = 100 };

        Text = "ByteSplit";
        Icon = _appIcon;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(940, 620);
        Size = new Size(1240, 700);
        StartPosition = FormStartPosition.CenterScreen;
        Font = UiFont;
        DoubleBuffered = true;

        _uiThrottleTimer = new System.Windows.Forms.Timer { Interval = 125 };
        _uiThrottleTimer.Tick += (_, _) =>
        {
            if (_uiDirty)
            {
                _uiDirty = false;
                RefreshUi();
            }
        };

        // 1. Toolbar Atas
        _topBar = new Panel { Dock = DockStyle.Top, Height = 54 };
        _topBar.Paint += (_, e) =>
        {
            using var p = new Pen(_palette.Border, 1f);
            e.Graphics.DrawLine(p, 0, _topBar.Height - 1, _topBar.Width, _topBar.Height - 1);
        };

        _btnAdd = new FluentButton
        {
            Text = "Tambah URL",
            Glyph = GlyphKind.Add,
            Variant = FluentButtonVariant.Primary,
            Font = UiBoldFont,
            Height = 34
        };

        _toolbarDivider1 = new Panel { Width = 1, Height = 22 };

        _btnPause = new FluentButton { Text = "Jeda", Glyph = GlyphKind.Pause, Variant = FluentButtonVariant.Secondary, Height = 32, Enabled = false };
        _btnResume = new FluentButton { Text = "Lanjut", Glyph = GlyphKind.Play, Variant = FluentButtonVariant.Secondary, Height = 32, Enabled = false };
        _btnRefreshLink = new FluentButton { Text = "Perbarui Link", Glyph = GlyphKind.Refresh, Variant = FluentButtonVariant.Secondary, Height = 32, Enabled = false };
        _btnRemove = new FluentButton { Text = "Hapus", Glyph = GlyphKind.Trash, Variant = FluentButtonVariant.DangerGhost, Height = 32, Enabled = false };

        _chkClipboard = new FluentCheckBox { Text = "Pantau Clipboard", Checked = true, Height = 32 };
        _lblLimit = new Label { Text = "Batas:", AutoSize = true, TextAlign = ContentAlignment.MiddleRight };
        _cmbLimit = new FluentComboBox { Size = new Size(122, 32) };
        _cmbLimit.SetItems(SpeedPresets.Select(p => p.Label));
        _cmbLimit.SelectedIndex = 0;

        _btnBaseFolder = new FluentButton { Text = "Folder Dasar", Glyph = GlyphKind.Folder, Variant = FluentButtonVariant.Ghost, Height = 32 };
        _btnTheme = new FluentButton { Text = "Tema", Glyph = GlyphKind.Moon, Variant = FluentButtonVariant.Ghost, Height = 32 };
        _btnAbout = new FluentButton { Text = "Tentang", Glyph = GlyphKind.Info, Variant = FluentButtonVariant.Ghost, Height = 32 };

        _toolTip.SetToolTip(_btnBaseFolder, "Ubah Folder Dasar Penyimpanan");
        _toolTip.SetToolTip(_btnTheme, "Ganti Tema (Terang / Gelap)");
        _toolTip.SetToolTip(_btnAbout, "Tentang ByteSplit");

        _topBar.Controls.AddRange(new Control[]
        {
            _btnAdd, _toolbarDivider1, _btnPause, _btnResume, _btnRefreshLink, _btnRemove,
            _chkClipboard, _lblLimit, _cmbLimit, _btnBaseFolder, _btnTheme, _btnAbout
        });
        _topBar.Resize += (_, _) => LayoutToolbar();

        // 2. Sidebar Kiri (100% Managed Double-Buffered Control — Bebas Kedip Saat Hover)
        _sidebarPanel = new Panel { Dock = DockStyle.Left, Width = 224 };
        _sidebarPanel.Paint += (_, e) =>
        {
            using var p = new Pen(_palette.Border, 1f);
            e.Graphics.DrawLine(p, _sidebarPanel.Width - 1, 0, _sidebarPanel.Width - 1, _sidebarPanel.Height);
        };

        _brandHeader = new Panel { Dock = DockStyle.Top, Height = 68 };
        _brandHeader.Paint += BrandHeader_Paint;

        _sidebar = new SidebarNavControl { Dock = DockStyle.Fill };
        _sidebar.SelectionChanged += (_, _) => RefreshUi();

        _sidebarPanel.Controls.Add(_sidebar);
        _sidebarPanel.Controls.Add(_brandHeader);

        // 3. Area Utama
        _workspacePanel = new Panel { Dock = DockStyle.Fill };

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            ReadOnly = true,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
            ColumnHeadersHeight = 38,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            RowTemplate = { Height = 40 },
            EnableHeadersVisualStyles = false,
            Font = GridCellFont
        };
        typeof(DataGridView).InvokeMember(
            "DoubleBuffered",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.SetProperty,
            null,
            _grid,
            new object[] { true });

        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colName", HeaderText = "Nama", FillWeight = 33, SortMode = DataGridViewColumnSortMode.NotSortable });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colSize", HeaderText = "Ukuran", FillWeight = 11, SortMode = DataGridViewColumnSortMode.NotSortable });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colProgress", HeaderText = "Progres", FillWeight = 17, SortMode = DataGridViewColumnSortMode.NotSortable });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colSpeed", HeaderText = "Kecepatan", FillWeight = 12, SortMode = DataGridViewColumnSortMode.NotSortable });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colEta", HeaderText = "Sisa Waktu", FillWeight = 11, SortMode = DataGridViewColumnSortMode.NotSortable });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "colStatus", HeaderText = "Status", FillWeight = 16, SortMode = DataGridViewColumnSortMode.NotSortable });
        _grid.CellPainting += Grid_CellPainting;

        _emptyStateView = new EmptyStateView();
        _emptyStateView.AddClicked += (_, _) => OpenAddDialog(null, false);

        _workspacePanel.Controls.Add(_grid);
        _workspacePanel.Controls.Add(_emptyStateView);
        _emptyStateView.BringToFront();
        _workspacePanel.Resize += (_, _) => LayoutEmptyState();

        // 4. Panel Bawah (Rincian Segmen)
        _bottomPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 84,
            Padding = new Padding(16, 8, 16, 8),
            Visible = false
        };
        _bottomPanel.Paint += (_, e) =>
        {
            using var p = new Pen(_palette.Border, 1f);
            e.Graphics.DrawLine(p, 0, 0, _bottomPanel.Width, 0);
        };

        var bottomLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55f));
        bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45f));
        bottomLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
        bottomLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
        bottomLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20f));

        _lblDetailTitle = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UiBoldFont,
            AutoEllipsis = true
        };

        _lblDetailMeta = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            Font = GridCellFont,
            AutoEllipsis = true
        };

        _segmentBar = new SegmentBarControl
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 0, 4)
        };

        _lblDetailUrl = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = DetailUrlFont,
            AutoEllipsis = true
        };

        bottomLayout.Controls.Add(_lblDetailTitle, 0, 0);
        bottomLayout.Controls.Add(_lblDetailMeta, 1, 0);
        bottomLayout.Controls.Add(_segmentBar, 0, 1);
        bottomLayout.SetColumnSpan(_segmentBar, 2);
        bottomLayout.Controls.Add(_lblDetailUrl, 0, 2);
        bottomLayout.SetColumnSpan(_lblDetailUrl, 2);
        _bottomPanel.Controls.Add(bottomLayout);

        // 5. Status Bar
        _statusBar = new Panel { Dock = DockStyle.Bottom, Height = 30, Padding = new Padding(16, 4, 16, 4) };
        _statusBar.Paint += StatusBar_Paint;

        _lblStatusSummary = new Label
        {
            Text = "0 aktif  |  0,00 MB/s",
            Location = new Point(28, 5),
            AutoSize = true,
            Font = StatusBoldFont
        };
        _lblStatusFolder = new Label
        {
            Text = "Folder dasar: -",
            Dock = DockStyle.Right,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleRight,
            Font = StatusRegularFont
        };
        _statusBar.Controls.Add(_lblStatusFolder);
        _statusBar.Controls.Add(_lblStatusSummary);

        Controls.Add(_workspacePanel);
        Controls.Add(_sidebarPanel);
        Controls.Add(_bottomPanel);
        Controls.Add(_statusBar);
        Controls.Add(_topBar);

        // Context Menu
        var ctxMenu = new ContextMenuStrip();
        ctxMenu.Items.Add("Buka File", null, (_, _) => OpenSelectedFile(false));
        ctxMenu.Items.Add("Buka Folder", null, (_, _) => OpenSelectedFile(true));
        ctxMenu.Items.Add(new ToolStripSeparator());
        ctxMenu.Items.Add("Jeda", null, async (_, _) => await PauseSelectedAsync());
        ctxMenu.Items.Add("Lanjut", null, (_, _) => ResumeSelected());
        ctxMenu.Items.Add("Perbarui Link", null, (_, _) => OpenRefreshDialog());
        ctxMenu.Items.Add("Salin URL", null, (_, _) => CopySelectedUrl());
        ctxMenu.Items.Add(new ToolStripSeparator());
        ctxMenu.Items.Add("Hapus dari Daftar", null, async (_, _) => await RemoveSelectedAsync());
        _grid.ContextMenuStrip = ctxMenu;

        // System Tray
        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Buka ByteSplit", null, (_, _) => RestoreFromTray());
        trayMenu.Items.Add("Jeda Semua", null, async (_, _) => await _manager.PauseAllAsync());
        trayMenu.Items.Add("Keluar", null, async (_, _) => await ExitApplicationAsync());

        _trayIcon = new NotifyIcon
        {
            Icon = _appIcon,
            Text = "ByteSplit",
            Visible = true,
            ContextMenuStrip = trayMenu
        };
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();

        WireEvents();
        ApplyTheme(false);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        LayoutToolbar();
        LayoutEmptyState();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        LayoutToolbar();
        LayoutEmptyState();
    }

    private void LayoutToolbar()
    {
        if (_topBar == null) return;
        int w = _topBar.ClientSize.Width;
        if (w <= 0) return;

        _btnBaseFolder.CompactText = false;
        _btnTheme.CompactText = false;
        _btnAbout.CompactText = false;

        Size addSize = _btnAdd.GetPreferredSize(Size.Empty);
        Size pauseSize = _btnPause.GetPreferredSize(Size.Empty);
        Size resumeSize = _btnResume.GetPreferredSize(Size.Empty);
        Size refreshSize = _btnRefreshLink.GetPreferredSize(Size.Empty);
        Size removeSize = _btnRemove.GetPreferredSize(Size.Empty);

        Size folderFull = _btnBaseFolder.GetPreferredSize(Size.Empty);
        Size themeFull = _btnTheme.GetPreferredSize(Size.Empty);
        Size aboutFull = _btnAbout.GetPreferredSize(Size.Empty);

        int leftTotal = 14 + addSize.Width + 10 + _toolbarDivider1.Width + 10
                        + pauseSize.Width + 6 + resumeSize.Width + 6 + refreshSize.Width + 6 + removeSize.Width;
        int rightFullTotal = _chkClipboard.Width + 12 + _lblLimit.Width + 4 + _cmbLimit.Width + 12
                             + folderFull.Width + 6 + themeFull.Width + 6 + aboutFull.Width + 14;

        bool compactUtils = w < (leftTotal + rightFullTotal + 24);
        _btnBaseFolder.CompactText = compactUtils;
        _btnTheme.CompactText = compactUtils;
        _btnAbout.CompactText = compactUtils;

        _btnAdd.Size = addSize;
        _btnPause.Size = pauseSize;
        _btnResume.Size = resumeSize;
        _btnRefreshLink.Size = refreshSize;
        _btnRemove.Size = removeSize;
        _btnBaseFolder.Size = _btnBaseFolder.GetPreferredSize(Size.Empty);
        _btnTheme.Size = _btnTheme.GetPreferredSize(Size.Empty);
        _btnAbout.Size = _btnAbout.GetPreferredSize(Size.Empty);

        int yCenter(Control c) => (_topBar.Height - c.Height) / 2;

        int x = 14;
        _btnAdd.Location = new Point(x, yCenter(_btnAdd));
        x = _btnAdd.Right + 10;

        _toolbarDivider1.Location = new Point(x, (_topBar.Height - _toolbarDivider1.Height) / 2);
        x = _toolbarDivider1.Right + 10;

        _btnPause.Location = new Point(x, yCenter(_btnPause));
        x = _btnPause.Right + 6;

        _btnResume.Location = new Point(x, yCenter(_btnResume));
        x = _btnResume.Right + 6;

        _btnRefreshLink.Location = new Point(x, yCenter(_btnRefreshLink));
        x = _btnRefreshLink.Right + 6;

        _btnRemove.Location = new Point(x, yCenter(_btnRemove));

        int rx = w - 14;

        rx -= _btnAbout.Width;
        _btnAbout.Location = new Point(rx, yCenter(_btnAbout));
        rx -= 6;

        rx -= _btnTheme.Width;
        _btnTheme.Location = new Point(rx, yCenter(_btnTheme));
        rx -= 6;

        rx -= _btnBaseFolder.Width;
        _btnBaseFolder.Location = new Point(rx, yCenter(_btnBaseFolder));
        rx -= 12;

        rx -= _cmbLimit.Width;
        _cmbLimit.Location = new Point(rx, yCenter(_cmbLimit));
        rx -= 4;

        rx -= _lblLimit.Width;
        _lblLimit.Location = new Point(rx, (_topBar.Height - _lblLimit.Height) / 2);
        rx -= 12;

        rx -= _chkClipboard.Width;
        _chkClipboard.Location = new Point(rx, yCenter(_chkClipboard));
    }

    private void LayoutEmptyState()
    {
        if (_workspacePanel == null || _emptyStateView == null || _grid == null) return;
        int headerH = _grid.ColumnHeadersHeight;
        int w = _workspacePanel.ClientSize.Width;
        int h = Math.Max(0, _workspacePanel.ClientSize.Height - headerH);
        _emptyStateView.Bounds = new Rectangle(0, headerH, w, h);
    }

    private void WireEvents()
    {
        Load += async (_, _) =>
        {
            await _manager.InitAsync();
            _chkClipboard.Checked = _manager.Settings.ClipboardMonitor;
            SelectSpeedPreset(_manager.Settings.SpeedLimitBps);
            ApplyTheme(_manager.Settings.Theme == "dark");
            LayoutToolbar();
            LayoutEmptyState();
            RefreshUi();
            _uiThrottleTimer.Start();
        };

        _manager.TaskUpdated += _ => _uiDirty = true;
        _manager.ListChanged += () => _uiDirty = true;
        _manager.TaskCompleted += task => SafeUiInvoke(() =>
        {
            _uiDirty = true;
            _trayIcon.ShowBalloonTip(
                3000,
                "Unduhan Selesai",
                task.FileName ?? "Berkas selesai diunduh.",
                ToolTipIcon.Info);
        });

        _btnAdd.Click += (_, _) => OpenAddDialog(null, false);
        _btnPause.Click += async (_, _) => await PauseSelectedAsync();
        _btnResume.Click += (_, _) => ResumeSelected();
        _btnRefreshLink.Click += (_, _) => OpenRefreshDialog();
        _btnRemove.Click += async (_, _) => await RemoveSelectedAsync();

        _chkClipboard.CheckedChanged += async (_, _) =>
        {
            _manager.Settings.ClipboardMonitor = _chkClipboard.Checked;
            await _manager.SaveSettingsAsync();
        };

        _cmbLimit.SelectedIndexChanged += async (_, _) =>
        {
            int idx = Math.Clamp(_cmbLimit.SelectedIndex, 0, SpeedPresets.Length - 1);
            long bps = SpeedPresets[idx].Bps;
            _manager.Limiter.SetLimit(bps);
            _manager.Settings.SpeedLimitBps = bps;
            await _manager.SaveSettingsAsync();
        };

        _btnBaseFolder.Click += async (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { SelectedPath = _manager.Settings.DefaultOutputDir };
            if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
            {
                Directory.CreateDirectory(dlg.SelectedPath);
                _manager.Settings.DefaultOutputDir = dlg.SelectedPath;
                await _manager.SaveSettingsAsync();
                RefreshUi();
            }
        };

        _btnTheme.Click += async (_, _) =>
        {
            bool nextDark = _manager.Settings.Theme != "dark";
            _manager.Settings.Theme = nextDark ? "dark" : "light";
            ApplyTheme(nextDark);
            await _manager.SaveSettingsAsync();
            RefreshUi();
        };

        _btnAbout.Click += (_, _) =>
        {
            using var about = new AboutForm(_palette, _appIcon, _appLogo);
            about.ShowDialog(this);
        };

        _grid.SelectionChanged += (_, _) =>
        {
            if (_suppressSelectionChanged) return;
            if (_grid.SelectedRows.Count > 0 && _grid.SelectedRows[0].Tag is RowMeta meta)
            {
                _selectedTaskId = meta.TaskId;
            }
            else
            {
                _selectedTaskId = null;
            }
            UpdateDetailAndButtons();
        };

        _grid.CellMouseEnter += (_, e) =>
        {
            if (e.RowIndex != _hoveredGridRow)
            {
                int prev = _hoveredGridRow;
                _hoveredGridRow = e.RowIndex;
                if (prev >= 0 && prev < _grid.Rows.Count) _grid.InvalidateRow(prev);
                if (_hoveredGridRow >= 0 && _hoveredGridRow < _grid.Rows.Count) _grid.InvalidateRow(_hoveredGridRow);
            }
        };

        _grid.MouseLeave += (_, _) =>
        {
            if (_hoveredGridRow >= 0)
            {
                int prev = _hoveredGridRow;
                _hoveredGridRow = -1;
                if (prev < _grid.Rows.Count) _grid.InvalidateRow(prev);
            }
        };

        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0) OpenSelectedFile(false);
        };

        _grid.CellMouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0)
            {
                _grid.ClearSelection();
                _grid.Rows[e.RowIndex].Selected = true;
            }
            else if (e.RowIndex < 0)
            {
                _grid.ClearSelection();
                _selectedTaskId = null;
                UpdateDetailAndButtons();
            }
        };

        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.N)
            {
                e.SuppressKeyPress = true;
                OpenAddDialog(null, false);
            }
            else if (e.KeyCode == Keys.Escape && _selectedTaskId != null)
            {
                _grid.ClearSelection();
                _selectedTaskId = null;
                UpdateDetailAndButtons();
            }
        };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        AddClipboardFormatListener(Handle);
        ThemePalette.ApplyWindowChrome(Handle, _palette.IsDark);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        RemoveClipboardFormatListener(Handle);
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmClipboardUpdate && _chkClipboard.Checked && !_isDialogOpen)
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    string text = Clipboard.GetText().Trim();
                    if (!string.Equals(text, _lastClipboardUrl, StringComparison.Ordinal) &&
                        CategoryHelper.IsDirectDownloadUrl(text))
                    {
                        _lastClipboardUrl = text;
                        RestoreFromTray();
                        OpenAddDialog(text, true);
                    }
                }
            }
            catch { }
        }
        base.WndProc(ref m);
    }

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_forceExit && e.CloseReason == CloseReason.UserClosing)
        {
            bool hasActive = _manager.GetTasks().Any(t => t.Status is "downloading" or "probing" or "queued");
            if (hasActive)
            {
                e.Cancel = true;
                Hide();
                _trayIcon.ShowBalloonTip(2500, "ByteSplit", "Unduhan masih berjalan di latar belakang.", ToolTipIcon.Info);
                return;
            }
        }

        _uiThrottleTimer.Stop();
        _trayIcon.Visible = false;
        _toolTip.Dispose();
        _appLogo?.Dispose();
        await _manager.PauseAllAsync();
        base.OnFormClosing(e);
    }

    private void SafeUiInvoke(Action action)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(action);
        else action();
    }

    private void RestoreFromTray()
    {
        Show();
        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;
        Activate();
    }

    private async Task ExitApplicationAsync()
    {
        _forceExit = true;
        _uiThrottleTimer.Stop();
        _trayIcon.Visible = false;
        await _manager.PauseAllAsync();
        Application.Exit();
    }

    private void OpenAddDialog(string? prefillUrl, bool fromClipboard)
    {
        if (_isDialogOpen) return;
        _isDialogOpen = true;
        try
        {
            using var dlg = new AddDownloadForm(
                _manager.Settings.DefaultOutputDir,
                prefillUrl,
                fromClipboard,
                _palette,
                _appIcon);

            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                try
                {
                    var task = _manager.AddDownload(
                        dlg.ResultUrl,
                        dlg.ResultCustomName,
                        dlg.ResultFolder,
                        dlg.ResultConnections,
                        dlg.ResultAutoCategorize);

                    _selectedTaskId = task.Id;
                    RefreshUi();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Gagal Menambah Unduhan", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
        finally
        {
            _isDialogOpen = false;
        }
    }

    private void OpenRefreshDialog()
    {
        DownloadTaskItem? task = GetSelectedTask();
        if (task == null || task.Status is not ("paused" or "error"))
            return;

        _isDialogOpen = true;
        try
        {
            using var dlg = new RefreshLinkForm(
                task.FileName ?? task.Url,
                _palette,
                _appIcon);

            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                try
                {
                    _manager.RefreshLink(task.Id, dlg.ResultNewUrl);
                    RefreshUi();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Gagal Memperbarui Link", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
        finally
        {
            _isDialogOpen = false;
        }
    }

    private DownloadTaskItem? GetSelectedTask()
    {
        return string.IsNullOrEmpty(_selectedTaskId) ? null : _manager.GetTask(_selectedTaskId);
    }

    private async Task PauseSelectedAsync()
    {
        var task = GetSelectedTask();
        if (task != null) await _manager.PauseDownloadAsync(task.Id);
    }

    private void ResumeSelected()
    {
        var task = GetSelectedTask();
        if (task != null) _manager.ResumeDownload(task.Id);
    }

    private async Task RemoveSelectedAsync()
    {
        var task = GetSelectedTask();
        if (task == null) return;
        await _manager.RemoveDownloadAsync(task.Id, false);
        _selectedTaskId = null;
        RefreshUi();
    }

    private void CopySelectedUrl()
    {
        var task = GetSelectedTask();
        if (task != null && !string.IsNullOrEmpty(task.Url))
        {
            Clipboard.SetText(task.Url);
        }
    }

    private void OpenSelectedFile(bool folderOnly)
    {
        var task = GetSelectedTask();
        if (task == null || task.Status != "completed" || string.IsNullOrEmpty(task.DestPath))
            return;

        string target = folderOnly ? (Path.GetDirectoryName(task.DestPath) ?? task.OutputDir) : task.DestPath;
        if (!File.Exists(target) && !Directory.Exists(target))
        {
            MessageBox.Show(this, "Berkas sudah dipindahkan atau dihapus dari disk.", "Tidak Ditemukan", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }

    private void RefreshUi()
    {
        List<DownloadTaskItem> allTasks = _manager.GetTasks();
        string filterKey = _sidebar.SelectedKey;

        List<DownloadTaskItem> filtered = allTasks.Where(t => filterKey switch
        {
            "all" => true,
            "active" => t.Status is "downloading" or "probing" or "queued",
            "completed" => t.Status == "completed",
            _ => CategoryHelper.NormalizeCategory(t.Category) == filterKey
        }).ToList();

        bool sameStructure = _grid.Rows.Count == filtered.Count;
        if (sameStructure)
        {
            for (int i = 0; i < filtered.Count; i++)
            {
                if (_grid.Rows[i].Tag is not RowMeta m || m.TaskId != filtered[i].Id)
                {
                    sameStructure = false;
                    break;
                }
            }
        }

        if (sameStructure && filtered.Count > 0)
        {
            for (int i = 0; i < filtered.Count; i++)
            {
                DownloadTaskItem t = filtered[i];
                DataGridViewRow row = _grid.Rows[i];
                int pct = GetProgressPercent(t);
                string normalizedCat = CategoryHelper.NormalizeCategory(t.Category);

                var prevMeta = row.Tag as RowMeta;
                bool statusOrCatChanged = prevMeta == null || prevMeta.Status != t.Status || prevMeta.Category != normalizedCat;
                bool progressChanged = prevMeta == null || prevMeta.Percent != pct;

                string newName = t.FileName ?? t.Url;
                if (!Equals(row.Cells[0].Value, newName))
                    row.Cells[0].Value = newName;

                string newSize = FormatBytes(t.TotalSize);
                if (!Equals(row.Cells[1].Value, newSize))
                    row.Cells[1].Value = newSize;

                row.Cells[2].Value = $"{pct}%";
                row.Cells[3].Value = t.Status == "downloading" ? FormatSpeed(t.SpeedBps) : "-";
                row.Cells[4].Value = (t.Status == "downloading" && t.EtaSec is > 0) ? FormatEta(t.EtaSec.Value) : "-";
                row.Cells[5].Value = FormatStatus(t.Status);

                if (statusOrCatChanged)
                {
                    row.Tag = new RowMeta(t.Id, t.Status, pct, normalizedCat);
                    _grid.InvalidateRow(i);
                }
                else
                {
                    row.Tag = new RowMeta(t.Id, t.Status, pct, normalizedCat);
                    if (progressChanged) _grid.InvalidateCell(2, i);
                    if (t.Status == "downloading" || progressChanged)
                    {
                        _grid.InvalidateCell(3, i);
                        _grid.InvalidateCell(4, i);
                    }
                }
            }
        }
        else
        {
            string? preservedId = _selectedTaskId;
            _suppressSelectionChanged = true;
            _grid.SuspendLayout();
            try
            {
                _grid.Rows.Clear();
                int selectedRowIndex = -1;

                foreach (var t in filtered)
                {
                    int pct = GetProgressPercent(t);
                    string normalizedCat = CategoryHelper.NormalizeCategory(t.Category);
                    string etaText = (t.Status == "downloading" && t.EtaSec is > 0) ? FormatEta(t.EtaSec.Value) : "-";

                    int idx = _grid.Rows.Add(
                        t.FileName ?? t.Url,
                        FormatBytes(t.TotalSize),
                        $"{pct}%",
                        t.Status == "downloading" ? FormatSpeed(t.SpeedBps) : "-",
                        etaText,
                        FormatStatus(t.Status));

                    DataGridViewRow row = _grid.Rows[idx];
                    row.Tag = new RowMeta(t.Id, t.Status, pct, normalizedCat);
                    if (t.Id == preservedId)
                        selectedRowIndex = idx;
                }

                _grid.ClearSelection();
                if (selectedRowIndex >= 0)
                {
                    _grid.Rows[selectedRowIndex].Selected = true;
                }
                else
                {
                    _selectedTaskId = null;
                }
            }
            finally
            {
                _grid.ResumeLayout();
                _suppressSelectionChanged = false;
            }
        }

        bool showEmpty = filtered.Count == 0;
        if (_emptyStateView.Visible != showEmpty)
        {
            _emptyStateView.Visible = showEmpty;
            if (showEmpty)
            {
                LayoutEmptyState();
                _emptyStateView.Invalidate();
            }
        }

        _sidebar.UpdateCounts(allTasks);
        UpdateDetailAndButtons();

        int activeCount = allTasks.Count(t => t.Status is "downloading" or "probing" or "queued");
        double totalSpeed = allTasks.Where(t => t.Status == "downloading").Sum(t => t.SpeedBps);
        _lblStatusSummary.Text = $"{activeCount} aktif  |  {(totalSpeed / (1024.0 * 1024.0)):F2} MB/s";
        _lblStatusFolder.Text = $"Folder dasar: {_manager.Settings.DefaultOutputDir}";

        bool hasActive = activeCount > 0;
        if (hasActive != _lastStatusHasActive)
        {
            _lastStatusHasActive = hasActive;
            _statusBar.Invalidate();
        }
    }

    private void UpdateDetailAndButtons()
    {
        DownloadTaskItem? task = GetSelectedTask();

        if (task == null)
        {
            _btnPause.Enabled = false;
            _btnResume.Enabled = false;
            _btnRefreshLink.Enabled = false;
            _btnRemove.Enabled = false;
            _bottomPanel.Visible = false;
            return;
        }

        bool isRunning = task.Status is "downloading" or "probing" or "queued";
        bool canResume = task.Status is "paused" or "error";

        _btnPause.Enabled = isRunning;
        _btnResume.Enabled = canResume;
        _btnRefreshLink.Enabled = canResume;
        _btnRemove.Enabled = true;

        _bottomPanel.Visible = true;
        int pct = GetProgressPercent(task);
        int activeChunks = task.Chunks.Count(c => c.Status == "downloading");
        int totalChunks = task.Chunks.Count > 0 ? task.Chunks.Count : task.Connections;

        string connPart = task.Status == "downloading"
            ? $"{activeChunks}/{totalChunks} koneksi"
            : $"{totalChunks} koneksi";
        string etaPart = (task.Status == "downloading" && task.EtaSec is > 0)
            ? $"  •  Sisa {FormatEta(task.EtaSec.Value)}"
            : string.Empty;
        string errPart = (task.Status == "error" && !string.IsNullOrEmpty(task.Error))
            ? $"  •  {task.Error}"
            : string.Empty;

        _lblDetailTitle.Text = task.FileName ?? task.Url;
        _lblDetailMeta.Text = $"{FormatStatus(task.Status)} ({pct}%)  •  {connPart}{etaPart}{errPart}";
        _lblDetailUrl.Text = task.Url;
        _segmentBar.SetData(task.Chunks, pct, _palette);
    }

    private void BrandHeader_Paint(object? sender, PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int textLeft = 16;
        if (_appLogo != null)
        {
            g.DrawImage(_appLogo, new Rectangle(16, 17, 32, 32));
            textLeft = 56;
        }

        TextRenderer.DrawText(g, "ByteSplit", BrandTitleFont, new Point(textLeft, 13), _palette.Text, TextFormatFlags.NoPadding);

        string verText = "v2.0.0-beta.1";
        Size verSize = TextRenderer.MeasureText(verText, BrandBadgeFont, Size.Empty, TextFormatFlags.NoPadding);
        var verRect = new Rectangle(textLeft, 36, verSize.Width + 10, 18);
        using (GraphicsPath badgePath = ThemePalette.CreateRoundedRect(verRect, 5))
        using (var badgeBrush = new SolidBrush(_palette.PrimarySoft))
        {
            g.FillPath(badgeBrush, badgePath);
        }
        TextRenderer.DrawText(g, verText, BrandBadgeFont, verRect, _palette.Primary, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        using var pen = new Pen(_palette.Border, 1f);
        g.DrawLine(pen, 14, _brandHeader.Height - 1, _brandHeader.Width - 14, _brandHeader.Height - 1);
    }

    private void Grid_CellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (_grid.Rows[e.RowIndex].Tag is not RowMeta meta) return;

        Graphics g = e.Graphics!;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        bool isSelected = _grid.Rows[e.RowIndex].Selected;
        bool isHovered = e.RowIndex == _hoveredGridRow && !isSelected;

        Color rowBg = isSelected
            ? _palette.SurfaceSelected
            : (isHovered ? _palette.SurfaceHover : (e.RowIndex % 2 == 1 ? _palette.SurfaceSubtle : _palette.Surface));

        using (var bgBrush = new SolidBrush(rowBg))
            g.FillRectangle(bgBrush, e.CellBounds);
        using (var gridPen = new Pen(_palette.Border, 1f))
            g.DrawLine(gridPen, e.CellBounds.Left, e.CellBounds.Bottom - 1, e.CellBounds.Right, e.CellBounds.Bottom - 1);

        string colName = _grid.Columns[e.ColumnIndex].Name;

        if (colName == "colName")
        {
            var iconRect = new Rectangle(e.CellBounds.X + 12, e.CellBounds.Y + (e.CellBounds.Height - 16) / 2, 16, 16);
            FluentGlyphs.Draw(g, FluentGlyphs.ForCategory(meta.Category), iconRect, _palette.Primary, 1.5f);

            var textRect = new Rectangle(iconRect.Right + 9, e.CellBounds.Y, e.CellBounds.Width - 44, e.CellBounds.Height);
            TextRenderer.DrawText(g, e.FormattedValue?.ToString() ?? string.Empty, GridCellFont, textRect, _palette.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            e.Handled = true;
        }
        else if (colName == "colProgress")
        {
            int barH = 16;
            int padX = 10;
            var trackRect = new Rectangle(
                e.CellBounds.X + padX,
                e.CellBounds.Y + (e.CellBounds.Height - barH) / 2,
                Math.Max(28, e.CellBounds.Width - padX * 2),
                barH);

            Color fillCol = meta.Status switch
            {
                "completed" => _palette.Success,
                "error" => _palette.Danger,
                "paused" => _palette.Warning,
                _ => _palette.Primary
            };

            using GraphicsPath trackPath = ThemePalette.CreateRoundedRect(trackRect, 4);
            using (var trackBrush = new SolidBrush(_palette.SurfaceSubtle))
                g.FillPath(trackBrush, trackPath);

            int fillW = (int)Math.Round(trackRect.Width * (meta.Percent / 100.0));
            if (fillW > 0)
            {
                g.SetClip(trackPath);
                using var fillBrush = new SolidBrush(fillCol);
                g.FillRectangle(fillBrush, trackRect.X, trackRect.Y, fillW, trackRect.Height);
                g.ResetClip();
            }

            using (var borderPen = new Pen(_palette.BorderStrong, 1f))
                g.DrawPath(borderPen, trackPath);

            Color pctColor = (_palette.IsDark || meta.Percent >= 52) ? Color.White : _palette.Text;
            TextRenderer.DrawText(g, $"{meta.Percent}%", BadgeFont, trackRect, pctColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            e.Handled = true;
        }
        else if (colName == "colStatus")
        {
            string statusText = e.FormattedValue?.ToString() ?? string.Empty;
            var (fg, bg) = meta.Status switch
            {
                "downloading" or "probing" => (_palette.Primary, _palette.PrimarySoft),
                "completed" => (_palette.Success, _palette.SuccessSoft),
                "paused" or "queued" => (_palette.Warning, _palette.WarningSoft),
                "error" => (_palette.Danger, _palette.DangerSoft),
                _ => (_palette.TextMuted, _palette.SurfaceSubtle)
            };

            Size txtSize = TextRenderer.MeasureText(statusText, GridStatusFont, Size.Empty, TextFormatFlags.NoPadding);
            int pillW = Math.Min(e.CellBounds.Width - 16, txtSize.Width + 22);
            int pillH = 22;
            var pillRect = new Rectangle(e.CellBounds.X + 8, e.CellBounds.Y + (e.CellBounds.Height - pillH) / 2, pillW, pillH);

            using GraphicsPath pillPath = ThemePalette.CreateRoundedRect(pillRect, 6);
            using (var pillBrush = new SolidBrush(bg))
                g.FillPath(pillBrush, pillPath);

            using (var dotBrush = new SolidBrush(fg))
                g.FillEllipse(dotBrush, pillRect.X + 8, pillRect.Y + (pillH - 6) / 2, 6, 6);

            var lblRect = new Rectangle(pillRect.X + 18, pillRect.Y, pillRect.Width - 22, pillRect.Height);
            TextRenderer.DrawText(g, statusText, GridStatusFont, lblRect, fg, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            e.Handled = true;
        }
        else
        {
            var textRect = new Rectangle(e.CellBounds.X + 8, e.CellBounds.Y, e.CellBounds.Width - 16, e.CellBounds.Height);
            TextRenderer.DrawText(g, e.FormattedValue?.ToString() ?? string.Empty, GridCellFont, textRect, _palette.TextSecondary, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            e.Handled = true;
        }
    }

    private void StatusBar_Paint(object? sender, PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var borderPen = new Pen(_palette.Border, 1f);
        g.DrawLine(borderPen, 0, 0, _statusBar.Width, 0);

        bool hasActive = _manager.GetTasks().Any(t => t.Status is "downloading" or "probing" or "queued");
        Color dotColor = hasActive ? _palette.Success : _palette.TextMuted;
        using var dotBrush = new SolidBrush(dotColor);
        g.FillEllipse(dotBrush, 16, (_statusBar.Height - 7) / 2, 7, 7);
    }

    private void ApplyTheme(bool dark)
    {
        _palette = ThemePalette.FromMode(dark);
        ThemePalette.ApplyWindowChrome(Handle, dark);

        BackColor = _palette.Background;
        ForeColor = _palette.Text;

        _topBar.BackColor = _palette.Surface;
        _toolbarDivider1.BackColor = _palette.Border;

        _btnAdd.ApplyPalette(_palette);
        _btnPause.ApplyPalette(_palette);
        _btnResume.ApplyPalette(_palette);
        _btnRefreshLink.ApplyPalette(_palette);
        _btnRemove.ApplyPalette(_palette);
        _chkClipboard.ApplyPalette(_palette);
        _lblLimit.ForeColor = _palette.TextSecondary;
        _cmbLimit.ApplyPalette(_palette);
        _btnBaseFolder.ApplyPalette(_palette);
        _btnTheme.Glyph = dark ? GlyphKind.Sun : GlyphKind.Moon;
        _btnTheme.ApplyPalette(_palette);
        _btnAbout.ApplyPalette(_palette);

        _sidebarPanel.BackColor = _palette.SurfaceSubtle;
        _brandHeader.BackColor = _palette.SurfaceSubtle;
        _sidebar.ApplyPalette(_palette);

        _workspacePanel.BackColor = _palette.Surface;
        _emptyStateView.ApplyPalette(_palette);

        _grid.BackgroundColor = _palette.Surface;
        _grid.GridColor = _palette.Border;
        _grid.DefaultCellStyle.BackColor = _palette.Surface;
        _grid.DefaultCellStyle.ForeColor = _palette.Text;
        _grid.DefaultCellStyle.SelectionBackColor = _palette.SurfaceSelected;
        _grid.DefaultCellStyle.SelectionForeColor = _palette.Text;
        _grid.AlternatingRowsDefaultCellStyle.BackColor = _palette.SurfaceSubtle;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = _palette.SurfaceSubtle;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = _palette.TextSecondary;
        _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = _palette.SurfaceSubtle;
        _grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = _palette.TextSecondary;
        _grid.ColumnHeadersDefaultCellStyle.Font = GridHeaderFont;
        _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(8, 0, 8, 0);

        _bottomPanel.BackColor = _palette.Surface;
        _lblDetailTitle.ForeColor = _palette.Text;
        _lblDetailMeta.ForeColor = _palette.TextSecondary;
        _lblDetailUrl.ForeColor = _palette.TextMuted;

        _statusBar.BackColor = _palette.SurfaceSubtle;
        _lblStatusSummary.ForeColor = _palette.Text;
        _lblStatusFolder.ForeColor = _palette.TextSecondary;

        Invalidate(true);
    }

    private void SelectSpeedPreset(long bps)
    {
        for (int i = 0; i < SpeedPresets.Length; i++)
        {
            if (SpeedPresets[i].Bps == bps)
            {
                _cmbLimit.SelectedIndex = i;
                return;
            }
        }
        _cmbLimit.SelectedIndex = 0;
    }

    private static int GetProgressPercent(DownloadTaskItem t)
    {
        if (t.Status == "completed") return 100;
        if (t.TotalSize <= 0) return 0;
        return (int)Math.Clamp(Math.Round((double)t.Downloaded / t.TotalSize * 100.0), 0, 100);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "-";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        int idx = 0;
        double val = bytes;
        while (val >= 1024 && idx < units.Length - 1)
        {
            val /= 1024;
            idx++;
        }
        return $"{val.ToString(idx == 0 ? "F0" : "F2")} {units[idx]}";
    }

    private static string FormatSpeed(double bps)
    {
        if (bps <= 0) return "0,00 MB/s";
        double mb = bps / (1024.0 * 1024.0);
        return mb >= 1.0 ? $"{mb:F2} MB/s" : $"{(bps / 1024.0):F1} KB/s";
    }

    private static string FormatEta(int sec)
    {
        if (sec < 60) return $"{sec}d";
        int m = sec / 60;
        int rem = sec % 60;
        if (m < 60) return $"{m}m {rem}d";
        return $"{m / 60}j {m % 60}m";
    }

    private static string FormatStatus(string status) => status switch
    {
        "probing" => "Memeriksa",
        "queued" => "Antrean",
        "downloading" => "Mengunduh",
        "paused" => "Dijeda",
        "completed" => "Selesai",
        "error" => "Gagal",
        "canceled" => "Dibatalkan",
        _ => status
    };

    private sealed record RowMeta(string TaskId, string Status, int Percent, string Category);

    private sealed class SidebarNavControl : Control
    {
        private ThemePalette _palette = ThemePalette.Light;
        private readonly int[] _counts = new int[SidebarItems.Length];
        private int _selectedIndex = 1;
        private int _hoveredIndex = -1;

        public event EventHandler? SelectionChanged;

        public string SelectedKey => SidebarItems[_selectedIndex].Key;

        public SidebarNavControl()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
        }

        public void ApplyPalette(ThemePalette palette)
        {
            _palette = palette;
            BackColor = palette.SurfaceSubtle;
            Invalidate();
        }

        public void UpdateCounts(List<DownloadTaskItem> all)
        {
            int active = 0, completed = 0, comp = 0, doc = 0, mus = 0, prog = 0, vid = 0, other = 0;
            foreach (var t in all)
            {
                if (t.Status is "downloading" or "probing" or "queued") active++;
                else if (t.Status == "completed") completed++;

                switch (CategoryHelper.NormalizeCategory(t.Category))
                {
                    case "Compressed": comp++; break;
                    case "Documents": doc++; break;
                    case "Music": mus++; break;
                    case "Programs": prog++; break;
                    case "Video": vid++; break;
                    default: other++; break;
                }
            }

            bool changed = false;
            SetCount(1, all.Count, ref changed);
            SetCount(2, active, ref changed);
            SetCount(3, completed, ref changed);
            SetCount(5, comp, ref changed);
            SetCount(6, doc, ref changed);
            SetCount(7, mus, ref changed);
            SetCount(8, prog, ref changed);
            SetCount(9, vid, ref changed);
            SetCount(10, other, ref changed);

            if (changed) Invalidate();
        }

        private void SetCount(int index, int value, ref bool changed)
        {
            if (_counts[index] != value)
            {
                _counts[index] = value;
                changed = true;
            }
        }

        private Rectangle GetItemBounds(int index)
        {
            int y = 4;
            for (int i = 0; i < index; i++)
            {
                y += SidebarItems[i].IsHeader ? 26 : 32;
            }
            int h = SidebarItems[index].IsHeader ? 26 : 32;
            return new Rectangle(0, y, Width, h);
        }

        private int HitTest(Point pt)
        {
            for (int i = 0; i < SidebarItems.Length; i++)
            {
                if (GetItemBounds(i).Contains(pt))
                    return i;
            }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int idx = HitTest(e.Location);
            int nextHover = (idx >= 0 && !SidebarItems[idx].IsHeader) ? idx : -1;
            Cursor = nextHover >= 0 ? Cursors.Hand : Cursors.Default;

            if (nextHover != _hoveredIndex)
            {
                _hoveredIndex = nextHover;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            Cursor = Cursors.Default;
            if (_hoveredIndex != -1)
            {
                _hoveredIndex = -1;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            int idx = HitTest(e.Location);
            if (idx >= 0 && !SidebarItems[idx].IsHeader && idx != _selectedIndex)
            {
                _selectedIndex = idx;
                Invalidate();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (var bgBrush = new SolidBrush(_palette.SurfaceSubtle))
                g.FillRectangle(bgBrush, ClientRectangle);

            for (int i = 0; i < SidebarItems.Length; i++)
            {
                Rectangle bounds = GetItemBounds(i);
                var (_, label, isHeader, glyph) = SidebarItems[i];

                if (isHeader)
                {
                    var hRect = new Rectangle(bounds.X + 16, bounds.Y + 6, bounds.Width - 24, bounds.Height - 6);
                    TextRenderer.DrawText(g, label, SidebarGroupFont, hRect, _palette.TextMuted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
                    continue;
                }

                bool selected = i == _selectedIndex;
                bool hovered = i == _hoveredIndex && !selected;

                var pillRect = new Rectangle(bounds.X + 10, bounds.Y + 2, bounds.Width - 20, bounds.Height - 4);
                if (selected || hovered)
                {
                    Color pillBg = selected ? _palette.SurfaceSelected : _palette.SurfaceHover;
                    using GraphicsPath pillPath = ThemePalette.CreateRoundedRect(pillRect, 6);
                    using var pillBrush = new SolidBrush(pillBg);
                    g.FillPath(pillBrush, pillPath);

                    if (selected)
                    {
                        using var accentBrush = new SolidBrush(_palette.Primary);
                        g.FillRectangle(accentBrush, pillRect.X, pillRect.Y + 5, 3, pillRect.Height - 10);
                    }
                }

                Color fg = selected ? _palette.Primary : _palette.TextSecondary;
                var iconRect = new Rectangle(pillRect.X + 10, pillRect.Y + (pillRect.Height - 15) / 2, 15, 15);
                FluentGlyphs.Draw(g, glyph, iconRect, fg, 1.6f);

                string countStr = _counts[i].ToString();
                Size badgeTextSize = TextRenderer.MeasureText(countStr, BadgeFont, Size.Empty, TextFormatFlags.NoPadding);
                int badgeW = Math.Max(22, badgeTextSize.Width + 10);
                int badgeH = 18;
                var badgeRect = new Rectangle(pillRect.Right - badgeW - 8, pillRect.Y + (pillRect.Height - badgeH) / 2, badgeW, badgeH);

                Color badgeBg = selected ? _palette.Primary : _palette.Surface;
                Color badgeFg = selected ? _palette.PrimaryText : _palette.TextMuted;
                using (GraphicsPath badgePath = ThemePalette.CreateRoundedRect(badgeRect, 5))
                using (var badgeBrush = new SolidBrush(badgeBg))
                {
                    g.FillPath(badgeBrush, badgePath);
                    if (!selected)
                    {
                        using var borderPen = new Pen(_palette.Border, 1f);
                        g.DrawPath(borderPen, badgePath);
                    }
                }
                TextRenderer.DrawText(g, countStr, BadgeFont, badgeRect, badgeFg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

                Font itemFont = selected ? SidebarItemBoldFont : SidebarItemFont;
                var textRect = new Rectangle(iconRect.Right + 9, pillRect.Y, badgeRect.Left - iconRect.Right - 14, pillRect.Height);
                TextRenderer.DrawText(g, label, itemFont, textRect, fg, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }
        }
    }

    private sealed class EmptyStateView : Control
    {
        private ThemePalette _palette = ThemePalette.Light;
        private readonly FluentButton _btnAdd;

        public event EventHandler? AddClicked;

        public EmptyStateView()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);

            _btnAdd = new FluentButton
            {
                Text = "Tambah URL",
                Glyph = GlyphKind.Add,
                Variant = FluentButtonVariant.Primary,
                Font = UiBoldFont,
                Height = 36
            };
            _btnAdd.Click += (s, e) => AddClicked?.Invoke(s, e);
            Controls.Add(_btnAdd);
        }

        public void ApplyPalette(ThemePalette palette)
        {
            _palette = palette;
            BackColor = palette.Surface;
            _btnAdd.ApplyPalette(palette);
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            PositionButton();
        }

        private void PositionButton()
        {
            _btnAdd.Size = _btnAdd.GetPreferredSize(Size.Empty);
            int cx = (Width - _btnAdd.Width) / 2;
            int cy = (Height / 2) + 36;
            _btnAdd.Location = new Point(Math.Max(12, cx), Math.Max(12, cy));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (var bgBrush = new SolidBrush(_palette.Surface))
                g.FillRectangle(bgBrush, ClientRectangle);

            int cx = Width / 2;
            int cy = Height / 2;

            var badgeCircle = new Rectangle(cx - 28, cy - 86, 56, 56);
            using (var circleBrush = new SolidBrush(_palette.PrimarySoft))
                g.FillEllipse(circleBrush, badgeCircle);

            var iconRect = new Rectangle(cx - 14, cy - 72, 28, 28);
            FluentGlyphs.Draw(g, GlyphKind.CloudDownload, iconRect, _palette.Primary, 1.8f);

            var titleRect = new Rectangle(24, cy - 20, Math.Max(40, Width - 48), 24);
            TextRenderer.DrawText(g, "Belum ada unduhan di daftar ini", EmptyTitleFont, titleRect, _palette.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            var subRect = new Rectangle(24, cy + 5, Math.Max(40, Width - 48), 22);
            TextRenderer.DrawText(g, "Klik '+ Tambah URL' untuk memulai atau salin tautan unduhan dari browser.", GridCellFont, subRect, _palette.TextMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}