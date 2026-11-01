using PCL.Extensions.Avalonia;
using PCL.Extensions.Contracts;

namespace PCL.Extensions.ScreenshotManager;

public sealed class ScreenshotManagerExtension : IExtension
{
    private IExtensionUi? _ui;
    private IDisposable? _viewRegistration;
    private IDisposable? _actionRegistration;
    private IDisposable? _searchRegistration;

    public ValueTask ActivateAsync(IExtensionContext context, CancellationToken cancellationToken)
    {
        _ui = context.GetService<IExtensionUi>();
        _viewRegistration = _ui.RegisterView(
            new ExtensionView
            {
                Id = "pcl.screenshot-manager.page",
                Title = "Screenshot Manager",
                TitleKey = "title",
                Location = ExtensionViewLocation.Page
            },
            _ => new ScreenshotView(context, _ui));

        var command = context.Manifest.Commands.Single();
        context.Commands.Register(command, token =>
            _ui.ShowViewAsync("pcl.screenshot-manager.page", cancellationToken: token));
        _actionRegistration = _ui.RegisterAction(new ExtensionAction(
            "pcl.screenshot-manager.open",
            command.Title,
            command.Id,
            ExtensionActionLocation.Toolbar,
            command.TitleKey,
            ScreenshotIcons.Camera.Data));

        _searchRegistration = context.Search.Register("pcl.screenshot-manager.search", (query, _) =>
        {
            var text = query.Text.Trim();
            var matches = text.Contains("screenshot", StringComparison.OrdinalIgnoreCase)
                          || text.Contains("截图", StringComparison.Ordinal)
                          || text.Contains("截圖", StringComparison.Ordinal);
            return ValueTask.FromResult<IReadOnlyList<ExtensionSearchResult>>(matches
                ?
                [
                    new ExtensionSearchResult
                    {
                        Id = "pcl.screenshot-manager",
                        Kind = ExtensionSearchKind.Page,
                        Score = 0.7,
                        Title = context.Text.Get("title", "Screenshot Manager"),
                        Description = context.Text.Get("search_description", "Browse and export screenshots."),
                        CommandId = command.Id
                    }
                ]
                : []);
        });

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _searchRegistration?.Dispose();
        _actionRegistration?.Dispose();
        _viewRegistration?.Dispose();
        _searchRegistration = null;
        _actionRegistration = null;
        _viewRegistration = null;
        _ui = null;
        return ValueTask.CompletedTask;
    }
}