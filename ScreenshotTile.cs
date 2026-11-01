using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace PCL.Extensions.ScreenshotManager;

internal sealed class ScreenshotTile : UserControl
{
    public const double TileWidth = 236;
    public const double ThumbnailWidth = 220;
    public const double ThumbnailHeight = 132;

    private readonly ScreenshotItem _item;
    private readonly ScreenshotView _owner;
    private readonly Image _image;
    private readonly Border _frame;
    private readonly Border _checkHost;
    private readonly TextBlock _check;
    private bool _loaded;
    private bool _selected;
    private bool _selectMode;

    public ScreenshotTile(ScreenshotItem item, ScreenshotView owner)
    {
        _item = item;
        _owner = owner;
        _image = new Image { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _check = new TextBlock
        {
            Text = "✓",
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = false
        };
        _checkHost = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(10),
            Background = Brushes.Black,
            Opacity = 0.72,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(8),
            Child = _check,
            IsVisible = false
        };
        _frame = new Border
        {
            BorderThickness = new Thickness(2),
            BorderBrush = Brushes.Transparent,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(0)
        };
        Content = Build();
        AttachedToVisualTree += (_, _) => LoadThumbnail();
        PointerPressed += OnPointerPressed;
    }

    private Control Build()
    {
        var imageHost = new Panel
        {
            Width = ThumbnailWidth,
            Height = ThumbnailHeight,
            ClipToBounds = true
        };
        var placeholder = new Border();
        placeholder.Bind(Border.BackgroundProperty, new DynamicResourceExtension("Pcl.SubtleSurface"));
        imageHost.Children.Add(placeholder);
        imageHost.Children.Add(_image);
        imageHost.Children.Add(_checkHost);

        var name = new TextBlock
        {
            Text = _item.DisplayName,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(2, 8, 2, 0)
        };
        var meta = new TextBlock
        {
            Text = $"{_item.TakenAt:yyyy-MM-dd HH:mm}  ·  {_owner.VersionLabel(_item)}",
            FontSize = 11,
            Opacity = 0.65,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(2, 3, 2, 0)
        };

        var stack = new StackPanel { Spacing = 0 };
        stack.Children.Add(imageHost);
        stack.Children.Add(name);
        stack.Children.Add(meta);

        var card = new Border
        {
            Width = TileWidth,
            Margin = new Thickness(0, 0, 10, 10),
            Padding = new Thickness(8),
            CornerRadius = new CornerRadius(6),
            Child = stack
        };
        card.Bind(Border.BackgroundProperty, new DynamicResourceExtension("Pcl.Surface"));

        _frame.Child = card;
        return _frame;
    }

    private async void LoadThumbnail()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        try
        {
            var bitmap = await _owner.GetThumbnailAsync(_item);
            if (bitmap is not null)
            {
                _image.Source = bitmap;
            }
        }
        catch
        {
            // Keep the placeholder for unreadable images.
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        e.Handled = true;
        _owner.HandleTileClick(_item);
    }

    public void SetSelectMode(bool selectMode)
    {
        _selectMode = selectMode;
        _checkHost.IsVisible = selectMode;
        UpdateSelectionVisual();
    }

    public void SetSelected(bool selected)
    {
        _selected = selected;
        UpdateSelectionVisual();
    }

    private void UpdateSelectionVisual()
    {
        _check.IsVisible = _selectMode && _selected;
        _checkHost.Background = _selected ? Brushes.MediumSeaGreen : Brushes.Black;
        if (_selected)
        {
            _frame.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("Pcl.Accent"));
        }
        else
        {
            _frame.BorderBrush = Brushes.Transparent;
        }
    }
}