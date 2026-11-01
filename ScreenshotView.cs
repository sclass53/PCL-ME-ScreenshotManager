using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PCL.Extensions.Avalonia;
using PCL.Extensions.Contracts;
using PCL.UI.Animation;
using PCL.UI.Commands;
using PCL.UI.Controls;

namespace PCL.Extensions.ScreenshotManager;

internal sealed class ScreenshotView : UserControl, IDisposable
{
    private const int PageSize = 120;

    private readonly IExtensionContext _context;
    private readonly IExtensionUi _ui;
    private readonly ScreenshotThumbnailCache _thumbnails;
    private readonly List<ScreenshotItem> _items = [];
    private readonly List<ScreenshotGroup> _groups = [];
    private readonly HashSet<string> _selectedVersions = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _selection = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ScreenshotTile> _tiles = new(StringComparer.OrdinalIgnoreCase);

    private readonly PclSearchBox _search;
    private readonly StackPanel _versionsPanel;
    private readonly CheckBox _allVersions;
    private readonly ComboBox _yearCombo;
    private readonly ComboBox _monthCombo;
    private readonly ComboBox _dayCombo;
    private readonly PclButton _todayButton;
    private readonly TextBlock _headerCount;
    private readonly TextBlock _status;
    private readonly TextBlock _empty;
    private readonly WrapPanel _grid;
    private readonly Grid _right;
    private readonly Grid _overlayHost;
    private Border _backdrop = null!;
    private Border _filterPanel = null!;
    private readonly PclButton _filterButton;
    private readonly Border _selectionBar;
    private readonly ProgressBar _exportProgressBar;
    private readonly TextBlock _selectionText;
    private readonly PclButton _exportButton;
    private readonly PclButton _loadMore;
    private readonly PclButton _selectButton;
    private readonly PclButton _selectAllVisible;
    private readonly PclButton _refreshButton;
    private readonly PclButton _openFolderButton;

    private readonly List<int?> _yearValues = [];
    private readonly List<int?> _monthValues = [];
    private readonly List<int?> _dayValues = [];

    private CancellationTokenSource? _scanCancellation;
    private CancellationTokenSource? _searchDebounce;
    private int _renderVersion;
    private int _renderLimit = PageSize;
    private bool _isScanning;
    private bool _selectMode;
    private bool _suppressDateEvents;
    private bool _suppressVersionEvents;
    private bool _entered;
    private bool _filterOpen;
    private int _filterVersion;
    private int? _year;
    private int? _month;
    private int? _day;
    private bool _includeShared = true;
    private string _searchText = string.Empty;
    private List<ScreenshotItem> _visible = [];

    public ScreenshotView(IExtensionContext context, IExtensionUi ui)
    {
        _context = context;
        _ui = ui;
        _thumbnails = new ScreenshotThumbnailCache(ReadThumbnailWidth(context));

        _search = new PclSearchBox
        {
            Watermark = T("search_watermark"),
            ShowSearchButton = true,
            SearchButtonText = T("search"),
            SearchCommand = new ActionCommand(() => _ = ApplySearchNowAsync())
        };
        _search.PropertyChanged += OnSearchPropertyChanged;

        _allVersions = new CheckBox { Content = T("all_versions"), Margin = new Thickness(0, 2, 0, 2) };
        _allVersions.IsCheckedChanged += (_, _) => OnAllVersionsChanged();
        _versionsPanel = new StackPanel { Spacing = 2 };

        _yearCombo = CreateCombo();
        _monthCombo = CreateCombo();
        _dayCombo = CreateCombo();
        _yearCombo.SelectionChanged += (_, _) => OnDateSelectionChanged(DatePart.Year);
        _monthCombo.SelectionChanged += (_, _) => OnDateSelectionChanged(DatePart.Month);
        _dayCombo.SelectionChanged += (_, _) => OnDateSelectionChanged(DatePart.Day);

        _todayButton = new PclButton
        {
            Text = T("today"),
            Command = new ActionCommand(SelectToday),
            HorizontalAlignment = HorizontalAlignment.Left
        };

        _headerCount = new TextBlock { FontSize = 13, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        _status = new TextBlock { FontSize = 11, Opacity = 0.65, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        _empty = new TextBlock
        {
            Text = T("empty"),
            FontSize = 14,
            Opacity = 0.6,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = false
        };
        _grid = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        Motion.SetStaggerChildren(_grid, true);
        Motion.SetStaggerStep(_grid, 18);
        Motion.SetOffsetX(_grid, 0);
        Motion.SetOffsetY(_grid, 8);
        Motion.SetInitialOpacity(_grid, 0);

        _selectButton = new PclButton { Text = T("select"), Command = new ActionCommand(ToggleSelectMode) };
        _selectAllVisible = new PclButton { Text = T("select_all_visible"), IsVisible = false, Command = new ActionCommand(SelectAllVisible) };
        _refreshButton = new PclButton { Text = T("refresh"), Command = new ActionCommand(() => _ = LoadAsync()) };
        _openFolderButton = new PclButton { Text = T("open_folder"), Command = new ActionCommand(OpenCurrentFolder) };
        _loadMore = new PclButton
        {
            Text = T("load_more"),
            IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0)
        };
        _loadMore.Command = new ActionCommand(LoadMore);

        _selectionText = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        _exportButton = new PclButton
        {
            Text = T("export"),
            ColorType = PclButtonColorState.Highlight,
            Command = new ActionCommand(() => _ = ExportAsync())
        };
        var clearSelection = new PclButton { Text = T("clear"), Command = new ActionCommand(ClearSelection) };
        _selectionBar = new Border
        {
            IsVisible = false,
            Padding = new Thickness(10, 6),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 6, 0, 0)
        };
        _selectionBar.Bind(Border.BackgroundProperty, new DynamicResourceExtension("Pcl.SubtleSurface"));
        var selectionContent = new DockPanel();
        DockPanel.SetDock(_selectionText, Dock.Left);
        selectionContent.Children.Add(_selectionText);
        var selectionButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        selectionButtons.Children.Add(clearSelection);
        selectionButtons.Children.Add(_exportButton);
        selectionContent.Children.Add(selectionButtons);
        _exportProgressBar = new ProgressBar
        {
            IsIndeterminate = true,
            Height = 3,
            IsVisible = false,
            Margin = new Thickness(0, 4, 0, 0)
        };
        var selectionRoot = new StackPanel { Spacing = 0 };
        selectionRoot.Children.Add(selectionContent);
        selectionRoot.Children.Add(_exportProgressBar);
        _selectionBar.Child = selectionRoot;
        Motion.SetOffsetY(_selectionBar, 8);
        Motion.SetInitialOpacity(_selectionBar, 0);

        _filterButton = new PclButton { Text = T("filters"), Command = new ActionCommand(OpenFilters) };
        _right = BuildRightPanel();
        _overlayHost = BuildFilterOverlay();
        Motion.SetOffsetX(_right, 14);
        Motion.SetInitialOpacity(_right, 0);

        var root = new Grid { Margin = new Thickness(16, 6, 16, 12) };
        root.Children.Add(_right);
        root.Children.Add(_overlayHost);
        Content = root;

        AttachedToVisualTree += OnAttached;
    }

    internal string T(string key, string? fallback = null) => _context.Text.Get(key, fallback);

    internal string VersionLabel(ScreenshotItem item) => item.IsShared ? T("shared") : item.VersionName ?? string.Empty;

    internal Task<Bitmap?> GetThumbnailAsync(ScreenshotItem item) => _thumbnails.GetAsync(item);

    internal void HandleTileClick(ScreenshotItem item)
    {
        if (_selectMode)
        {
            ToggleSelection(item);
            return;
        }

        ShowPreview(item);
    }

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_entered)
        {
            return;
        }

        _entered = true;
        _ = AnimateAndLoadAsync();
    }

    private async Task AnimateAndLoadAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        await Motion.PlayEnterAsync(_right);
        await LoadAsync();
    }
    private async void OpenFilters()
    {
        if (_filterOpen)
        {
            return;
        }

        _filterOpen = true;
        _filterVersion++;
        _overlayHost.IsVisible = true;
        await Task.WhenAll(Motion.PlayEnterAsync(_backdrop), Motion.PlayEnterAsync(_filterPanel));
    }

    private async void CloseFilters()
    {
        if (!_filterOpen)
        {
            return;
        }

        _filterOpen = false;
        var version = ++_filterVersion;
        await Task.WhenAll(Motion.PlayExitAsync(_filterPanel), Motion.PlayExitAsync(_backdrop));
        if (version == _filterVersion)
        {
            _overlayHost.IsVisible = false;
        }
    }


    private Border BuildFilterPanel()
    {
        var panel = new Border
        {
            Width = 430,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Stretch,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16)
        };
        panel.Bind(Border.BackgroundProperty, new DynamicResourceExtension("Pcl.Surface"));

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var title = new TextBlock
        {
            Text = T("filters"),
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        header.Children.Add(title);
        var done = new PclButton { Text = T("done"), HorizontalAlignment = HorizontalAlignment.Right, Command = new ActionCommand(CloseFilters) };
        var doneHost = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        doneHost.Children.Add(done);
        header.Children.Add(doneHost);

        var stack = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 12, 0) };
        stack.Children.Add(header);
        stack.Children.Add(_search);
        stack.Children.Add(SectionTitle(T("versions")));
        stack.Children.Add(_allVersions);
        stack.Children.Add(_versionsPanel);
        stack.Children.Add(SectionTitle(T("dates")));
        stack.Children.Add(LabeledCombo(T("year"), _yearCombo));
        stack.Children.Add(LabeledCombo(T("month"), _monthCombo));
        stack.Children.Add(LabeledCombo(T("day"), _dayCombo));
        stack.Children.Add(_todayButton);
        panel.Child = new ScrollViewer { Content = stack };
        return panel;
    }

    private Grid BuildFilterOverlay()
    {
        var overlay = new Grid { IsVisible = false, ZIndex = 20 };
        _backdrop = new Border();
        _backdrop.Bind(Border.BackgroundProperty, new DynamicResourceExtension("Pcl.Overlay"));
        _backdrop.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            CloseFilters();
        };
        _filterPanel = BuildFilterPanel();
        overlay.Children.Add(_backdrop);
        overlay.Children.Add(_filterPanel);
        Motion.SetOffsetX(_backdrop, 0);
        Motion.SetOffsetY(_backdrop, 0);
        Motion.SetExitOffsetX(_backdrop, 0);
        Motion.SetExitOffsetY(_backdrop, 0);
        Motion.SetInitialOpacity(_backdrop, 0);
        Motion.SetOffsetX(_filterPanel, -24);
        Motion.SetOffsetY(_filterPanel, 0);
        Motion.SetExitOffsetX(_filterPanel, -24);
        Motion.SetExitOffsetY(_filterPanel, 0);
        Motion.SetInitialOpacity(_filterPanel, 0);
        return overlay;
    }


    private Grid BuildRightPanel()
    {
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var headerText = new StackPanel();
        headerText.Children.Add(_headerCount);
        headerText.Children.Add(_status);
        DockPanel.SetDock(headerText, Dock.Left);
        header.Children.Add(headerText);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        buttons.Children.Add(_filterButton);
        buttons.Children.Add(_selectAllVisible);
        buttons.Children.Add(_selectButton);
        buttons.Children.Add(_refreshButton);
        buttons.Children.Add(_openFolderButton);
        header.Children.Add(new Border { Child = buttons });

        var gridHost = new Panel();
        var scroll = new ScrollViewer
        {
            Content = _grid,
            Padding = new Thickness(0, 0, 6, 0)
        };
        gridHost.Children.Add(scroll);
        gridHost.Children.Add(_empty);

        var footer = new StackPanel { Spacing = 4 };
        footer.Children.Add(_loadMore);
        footer.Children.Add(_selectionBar);

        var right = new Grid();
        right.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        right.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        right.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        right.Children.Add(header);
        right.Children.Add(gridHost);
        right.Children.Add(footer);
        Grid.SetRow(header, 0);
        Grid.SetRow(gridHost, 1);
        Grid.SetRow(footer, 2);
        return right;
    }

    private static ComboBox CreateCombo() => new()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        MinHeight = 28
    };

    private static Control SectionTitle(string text) => new TextBlock
    {
        Text = text,
        FontSize = 12,
        FontWeight = FontWeight.SemiBold,
        Margin = new Thickness(2, 6, 2, 2)
    };

    private static Control LabeledCombo(string label, ComboBox combo)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(48)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        var text = new TextBlock { Text = label, FontSize = 12, Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(text);
        grid.Children.Add(combo);
        Grid.SetColumn(text, 0);
        Grid.SetColumn(combo, 1);
        return grid;
    }

    private void OnSearchPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == PclSearchBox.TextProperty)
        {
            _searchDebounce?.Cancel();
            var cancellation = _searchDebounce = new CancellationTokenSource();
            _ = DebounceSearchAsync(cancellation.Token);
        }
    }

    private async Task DebounceSearchAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(220, cancellationToken);
            if (!cancellationToken.IsCancellationRequested)
            {
                UpdateSearchText();
                ApplyFilters();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private Task ApplySearchNowAsync()
    {
        _searchDebounce?.Cancel();
        UpdateSearchText();
        ApplyFilters();
        return Task.CompletedTask;
    }

    private void UpdateSearchText() => _searchText = _search.Text.Trim();

    private async Task LoadAsync()
    {
        _scanCancellation?.Cancel();
        var cancellation = _scanCancellation = new CancellationTokenSource();
        _isScanning = true;
        SetStatus(T("loading"));
        _empty.IsVisible = false;
        try
        {
            var result = await ScreenshotScanner.ScanAsync(_context, cancellation.Token);
            if (cancellation.IsCancellationRequested)
            {
                return;
            }

            _items.Clear();
            _items.AddRange(result.Items);
            _groups.Clear();
            _groups.AddRange(result.Groups);
            _includeShared = result.Groups.Any(group => group.IsShared);

            var knownVersions = _groups.Where(group => !group.IsShared && group.Id is not null).Select(group => group.Id!).ToArray();
            _selectedVersions.RemoveWhere(id => !knownVersions.Contains(id, StringComparer.OrdinalIgnoreCase));
            if (_selectedVersions.Count == 0)
            {
                foreach (var id in knownVersions)
                {
                    _selectedVersions.Add(id);
                }
            }

            _selection.RemoveWhere(path => !_items.Any(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase)));
            _isScanning = false;
            RebuildVersionList();
            RebuildDateOptions();
            ApplyFilters();
            if (result.Errors.Count > 0)
            {
                SetStatus(string.Format(T("read_errors"), result.Errors.Count));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_scanCancellation, cancellation))
            {
                _scanCancellation = null;
            }

            _isScanning = false;
            cancellation.Dispose();
        }
    }
    private void RebuildVersionList()
    {
        _suppressVersionEvents = true;
        try
        {
            _versionsPanel.Children.Clear();
            foreach (var group in _groups)
            {
                if (group.IsShared)
                {
                    var shared = new CheckBox
                    {
                        Content = $"{T("shared")} ({group.Count})",
                        IsChecked = _includeShared,
                        Margin = new Thickness(0, 1, 0, 1)
                    };
                    shared.IsCheckedChanged += (_, _) => SetShared(shared.IsChecked == true);
                    _versionsPanel.Children.Add(shared);
                    continue;
                }

                if (group.Id is null)
                {
                    continue;
                }

                var id = group.Id;
                var checkBox = new CheckBox
                {
                    Content = $"{group.Name} ({group.Count})",
                    IsChecked = _selectedVersions.Contains(id),
                    Margin = new Thickness(0, 1, 0, 1)
                };
                checkBox.IsCheckedChanged += (_, _) => SetVersion(id, checkBox.IsChecked == true);
                _versionsPanel.Children.Add(checkBox);
            }
        }
        finally
        {
            _suppressVersionEvents = false;
        }

        UpdateAllVersionsState();
    }

    private void UpdateAllVersionsState()
    {
        _suppressVersionEvents = true;
        try
        {
            var versions = _groups.Where(group => !group.IsShared && group.Id is not null).Select(group => group.Id!).ToArray();
            _allVersions.IsChecked = versions.Length > 0 && versions.All(id => _selectedVersions.Contains(id));
        }
        finally
        {
            _suppressVersionEvents = false;
        }
    }

    private void OnAllVersionsChanged()
    {
        if (_suppressVersionEvents)
        {
            return;
        }

        var selectAll = _allVersions.IsChecked == true;
        _selectedVersions.Clear();
        if (selectAll)
        {
            foreach (var group in _groups.Where(group => !group.IsShared && group.Id is not null))
            {
                _selectedVersions.Add(group.Id!);
            }
        }

        RebuildVersionList();
        ApplyFilters();
    }

    private void SetVersion(string id, bool selected)
    {
        if (_suppressVersionEvents)
        {
            return;
        }

        if (selected)
        {
            _selectedVersions.Add(id);
        }
        else
        {
            _selectedVersions.Remove(id);
        }

        UpdateAllVersionsState();
        ApplyFilters();
    }

    private void SetShared(bool selected)
    {
        if (_suppressVersionEvents)
        {
            return;
        }

        _includeShared = selected;
        ApplyFilters();
    }

    private void RebuildDateOptions()
    {
        _suppressDateEvents = true;
        try
        {
            var years = _items.Select(item => item.TakenAt.Year).Distinct().OrderByDescending(year => year).ToArray();
            _yearValues.Clear();
            _yearValues.Add(null);
            _yearValues.AddRange(years.Select(year => (int?)year));
            _yearCombo.ItemsSource = _yearValues.Select(year => year?.ToString() ?? T("all")).ToArray();
            _yearCombo.SelectedIndex = IndexOf(_yearValues, _year);

            var months = _items
                .Where(item => _year is null || item.TakenAt.Year == _year)
                .Select(item => item.TakenAt.Month)
                .Distinct()
                .OrderBy(month => month)
                .ToArray();
            _monthValues.Clear();
            _monthValues.Add(null);
            _monthValues.AddRange(months.Select(month => (int?)month));
            _monthCombo.ItemsSource = _monthValues.Select(month => month?.ToString("00") ?? T("all")).ToArray();
            _monthCombo.SelectedIndex = IndexOf(_monthValues, _month);

            var days = _items
                .Where(item => (_year is null || item.TakenAt.Year == _year) && (_month is null || item.TakenAt.Month == _month))
                .Select(item => item.TakenAt.Day)
                .Distinct()
                .OrderBy(day => day)
                .ToArray();
            _dayValues.Clear();
            _dayValues.Add(null);
            _dayValues.AddRange(days.Select(day => (int?)day));
            _dayCombo.ItemsSource = _dayValues.Select(day => day?.ToString("00") ?? T("all")).ToArray();
            _dayCombo.SelectedIndex = IndexOf(_dayValues, _day);

            _monthCombo.IsEnabled = _year is not null;
            _dayCombo.IsEnabled = _year is not null && _month is not null;
        }
        finally
        {
            _suppressDateEvents = false;
        }
    }

    private static int IndexOf(IReadOnlyList<int?> values, int? value)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (values[index] == value)
            {
                return index;
            }
        }

        return 0;
    }

    private void OnDateSelectionChanged(DatePart part)
    {
        if (_suppressDateEvents)
        {
            return;
        }

        switch (part)
        {
            case DatePart.Year:
                _year = _yearCombo.SelectedIndex > 0 ? _yearValues[_yearCombo.SelectedIndex] : null;
                _month = null;
                _day = null;
                break;
            case DatePart.Month:
                _month = _monthCombo.SelectedIndex > 0 ? _monthValues[_monthCombo.SelectedIndex] : null;
                _day = null;
                break;
            case DatePart.Day:
                _day = _dayCombo.SelectedIndex > 0 ? _dayValues[_dayCombo.SelectedIndex] : null;
                break;
        }

        RebuildDateOptions();
        ApplyFilters();
    }

    private void SelectToday()
    {
        var today = DateTime.Today;
        if (_items.All(item => item.TakenAt.Date != today))
        {
            return;
        }

        _year = today.Year;
        _month = today.Month;
        _day = today.Day;
        RebuildDateOptions();
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        _renderLimit = PageSize;
        _visible = _items
            .Where(MatchesVersion)
            .Where(MatchesDate)
            .Where(MatchesSearch)
            .OrderByDescending(item => item.TakenAt)
            .ToList();

        _empty.Text = _items.Count == 0 ? T("empty") : T("no_matches");
        _empty.IsVisible = _visible.Count == 0 && !_isScanning;
        _headerCount.Text = string.Format(T("status.results"), _visible.Count);
        _ = RenderAsync();
    }

    private bool MatchesVersion(ScreenshotItem item)
    {
        return item.IsShared ? _includeShared : item.VersionId is not null && _selectedVersions.Contains(item.VersionId);
    }

    private bool MatchesDate(ScreenshotItem item)
    {
        if (_year is not null && item.TakenAt.Year != _year)
        {
            return false;
        }

        if (_month is not null && item.TakenAt.Month != _month)
        {
            return false;
        }

        if (_day is not null && item.TakenAt.Day != _day)
        {
            return false;
        }

        return true;
    }

    private bool MatchesSearch(ScreenshotItem item)
    {
        if (_searchText.Length == 0)
        {
            return true;
        }

        foreach (var term in _searchText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!item.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private async Task RenderAsync()
    {
        var version = ++_renderVersion;
        _tiles.Clear();
        _grid.Children.Clear();
        var count = Math.Min(_renderLimit, _visible.Count);
        for (var index = 0; index < count; index++)
        {
            var item = _visible[index];
            var tile = new ScreenshotTile(item, this);
            tile.SetSelectMode(_selectMode);
            tile.SetSelected(_selection.Contains(item.Path));
            _tiles[item.Path] = tile;
            _grid.Children.Add(tile);
        }

        _loadMore.IsVisible = _visible.Count > count;
        if (count == 0)
        {
            return;
        }

        await Task.Yield();
        if (version == _renderVersion)
        {
            await Motion.PlayEnterAsync(_grid);
        }
    }

    private void LoadMore()
    {
        _renderLimit += PageSize;
        _ = RenderAsync();
    }

    private void ToggleSelectMode()
    {
        _selectMode = !_selectMode;
        _selectButton.Text = _selectMode ? T("cancel") : T("select");
        _selectAllVisible.IsVisible = _selectMode;
        foreach (var tile in _tiles.Values)
        {
            tile.SetSelectMode(_selectMode);
        }

        if (!_selectMode)
        {
            ClearSelection();
        }
    }

    private void SelectAllVisible()
    {
        foreach (var item in _visible)
        {
            _selection.Add(item.Path);
        }

        foreach (var tile in _tiles.Values)
        {
            tile.SetSelected(true);
        }

        UpdateSelectionBar();
    }

    private void ToggleSelection(ScreenshotItem item)
    {
        if (_selection.Contains(item.Path))
        {
            _selection.Remove(item.Path);
        }
        else
        {
            _selection.Add(item.Path);
        }

        if (_tiles.TryGetValue(item.Path, out var tile))
        {
            tile.SetSelected(_selection.Contains(item.Path));
        }

        UpdateSelectionBar();
    }

    private void ClearSelection()
    {
        _selection.Clear();
        foreach (var tile in _tiles.Values)
        {
            tile.SetSelected(false);
        }

        UpdateSelectionBar();
    }

    private void UpdateSelectionBar()
    {
        var selected = _selection.Count;
        var wasVisible = _selectionBar.IsVisible;
        _selectionBar.IsVisible = selected > 0;
        _selectionText.Text = string.Format(T("selected_count"), selected);
        if (selected > 0 && !wasVisible)
        {
            _ = Motion.PlayEnterAsync(_selectionBar);
        }
    }
    private async Task ExportAsync()
    {
        if (_selection.Count == 0)
        {
            return;
        }

        var top = TopLevel.GetTopLevel(this);
        if (top is null)
        {
            return;
        }

        var options = new FolderPickerOpenOptions
        {
            Title = T("export_choose_folder"),
            AllowMultiple = false
        };
        var lastFolder = _context.Settings.Get("export-folder", string.Empty);
        if (!string.IsNullOrWhiteSpace(lastFolder))
        {
            try
            {
                options.SuggestedStartLocation = await top.StorageProvider.TryGetFolderFromPathAsync(lastFolder);
            }
            catch
            {
            }
        }

        IReadOnlyList<IStorageFolder> folders;
        try
        {
            folders = await top.StorageProvider.OpenFolderPickerAsync(options);
        }
        catch (Exception ex)
        {
            SetStatus(string.Format(T("export_failed"), ex.Message));
            return;
        }

        if (folders.Count == 0)
        {
            return;
        }

        var destination = folders[0].Path.LocalPath;
        _context.Settings.Set("export-folder", destination);
        _exportButton.IsEnabled = false;
        _exportProgressBar.IsVisible = true;
        SetStatus(string.Format(T("exporting"), _selection.Count));
        try
        {
            var paths = _selection.ToArray();
            var (copied, failed) = await Task.Run(() => CopyFiles(paths, destination));
            var message = failed == 0
                ? T("export_success")
                : string.Format(T("exported_partial"), copied, failed);
            SetStatus(message);
            _context.Launcher.Notify(message);
        }
        catch (Exception ex)
        {
            var message = string.Format(T("export_failed"), ex.Message);
            SetStatus(message);
            _context.Launcher.Notify(message, true);
        }
        finally
        {
            _exportButton.IsEnabled = true;
            _exportProgressBar.IsVisible = false;
        }
    }


    private static (int Copied, int Failed) CopyFiles(IReadOnlyList<string> paths, string destination)
    {
        Directory.CreateDirectory(destination);
        var copied = 0;
        var failed = 0;
        foreach (var path in paths)
        {
            try
            {
                if (!File.Exists(path))
                {
                    failed++;
                    continue;
                }

                var target = BuildUniquePath(destination, Path.GetFileName(path));
                File.Copy(path, target, overwrite: false);
                copied++;
            }
            catch
            {
                failed++;
            }
        }

        return (copied, failed);
    }

    private static string BuildUniquePath(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path))
        {
            return path;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var index = 1; ; index++)
        {
            path = Path.Combine(directory, $"{stem} ({index}){extension}");
            if (!File.Exists(path))
            {
                return path;
            }
        }
    }

    private void OpenCurrentFolder()
    {
        var target = _selection.FirstOrDefault() ?? _visible.FirstOrDefault()?.Path;
        if (target is null)
        {
            return;
        }

        var folder = Path.GetDirectoryName(target);
        if (folder is null)
        {
            return;
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", folder);
            }
            else
            {
                Process.Start("xdg-open", folder);
            }
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private void ShowPreview(ScreenshotItem item)
    {
        _ = ShowPreviewAsync(item);
    }

    private async Task ShowPreviewAsync(ScreenshotItem item)
    {
        Bitmap? image = null;
        try
        {
            image = await Task.Run(() =>
            {
                using var stream = File.OpenRead(item.Path);
                return Bitmap.DecodeToWidth(stream, 1800, BitmapInterpolationMode.HighQuality);
            });
        }
        catch
        {
        }

        if (image is null)
        {
            return;
        }

        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new Image
        {
            Source = image,
            MaxWidth = 860,
            MaxHeight = 520,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"{item.FileName}\n{item.TakenAt:yyyy-MM-dd HH:mm:ss}  ·  {VersionLabel(item)}  ·  {FormatSize(item.Size)}",
            FontSize = 12,
            Opacity = 0.75,
            TextWrapping = TextWrapping.Wrap
        });

        IExtensionOverlay? overlay = null;
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(new PclButton
        {
            Text = T("open_file"),
            Command = new ActionCommand(() => OpenFile(item.Path))
        });
        buttons.Children.Add(new PclButton
        {
            Text = T("show_in_folder"),
            Command = new ActionCommand(() => ShowInFolder(item.Path))
        });
        buttons.Children.Add(new PclButton
        {
            Text = T("copy_path"),
            Command = new ActionCommand(() => _ = CopyPathAsync(item.Path))
        });
        buttons.Children.Add(new PclButton
        {
            Text = T("close"),
            Command = new ActionCommand(() => overlay?.Dispose())
        });
        panel.Children.Add(buttons);
        overlay = _ui.ShowOverlay(panel, new ExtensionOverlayOptions
        {
            MaxWidth = 920,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
    }

    private void OpenFile(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private void ShowInFolder(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            }
            else
            {
                var folder = Path.GetDirectoryName(path);
                if (folder is not null)
                {
                    Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
                }
            }
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private async Task CopyPathAsync(string path)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(path);
            _context.Launcher.Notify(T("copied"));
        }
    }

    private static string FormatSize(long size)
    {
        if (size <= 0)
        {
            return "—";
        }

        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)size;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }

    private static int ReadThumbnailWidth(IExtensionContext context)
    {
        var value = context.Settings.Get("thumbnail-size", 320d);
        return (int)Math.Clamp(value, 120, 640);
    }

    private void SetStatus(string text) => _status.Text = text;

    public void Dispose()
    {
        _scanCancellation?.Cancel();
        _scanCancellation?.Dispose();
        _searchDebounce?.Cancel();
        _searchDebounce?.Dispose();
        _thumbnails.Dispose();
    }

    private enum DatePart
    {
        Year,
        Month,
        Day
    }
}
