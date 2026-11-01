using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Extensions.Avalonia;
using PCL.Extensions.Contracts;
using PCL.Extensions.ScreenshotManager;

[assembly: AvaloniaTestApplication(typeof(ScreenshotManager.Tests.ScreenshotTestApplication))]
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerTest)]

namespace ScreenshotManager.Tests;

public sealed class ScreenshotTestApplication : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<ScreenshotTestApplication>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

[TestClass]
public sealed class ScreenshotViewSmokeTest
{
    [TestMethod]
    public async Task ExtensionActivates()
    {
        var extension = new ScreenshotManagerExtension();
        await extension.ActivateAsync(new FakeContext(), CancellationToken.None);
        await extension.DisposeAsync();
    }

    [TestMethod]
    public async Task ViewConstructs()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ScreenshotViewSmokeTest).Assembly);
        await session.Dispatch(() =>
        {
            using var view = new ScreenshotView(new FakeContext(), new FakeUi());
            Assert.IsNotNull(view);
            return true;
        }, CancellationToken.None);
    }
}

internal sealed class FakeContext : IExtensionContext
{
    public ExtensionManifest Manifest { get; } = new()
    {
        Id = "pcl.screenshot-manager",
        Name = "Screenshot Manager",
        Version = "0.1.0",
        EntryAssembly = "test.dll",
        EntryType = "Test",
        Commands = [new ExtensionCommand { Id = "pcl.screenshot-manager.open", Title = "Screenshot Manager" }]
    };
    public string PackageDirectory => Path.GetTempPath();
    public string DataDirectory => Path.GetTempPath();
    public CancellationToken Lifetime => CancellationToken.None;
    public IExtensionSettings Settings { get; } = new FakeSettings();
    public IExtensionLocalizer Text { get; } = new FakeText();
    public IExtensionCommands Commands { get; } = new FakeCommands();
    public IExtensionSearch Search { get; } = new FakeSearch();
    public IExtensionEvents Events { get; } = new FakeEvents();
    public ILauncherServices Launcher { get; } = new FakeLauncher();
    public T GetService<T>() where T : class => (T)(object)new FakeUi();
    public T Own<T>(T resource) where T : IDisposable => resource;
    public void RunBackground(Func<CancellationToken, Task> operation) { }
    public void Log(ExtensionLogLevel level, string message, Exception? exception = null) { }
    public ValueTask DisableAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public IExtensionWorker StartWorker(ExtensionWorkerOptions options) => throw new NotSupportedException();
}

internal sealed class FakeSettings : IExtensionSettings
{
    public event Action<string>? Changed;
    public T Get<T>(string key, T defaultValue) => defaultValue;
    public void Set<T>(string key, T value) => Changed?.Invoke(key);
}

internal sealed class FakeText : IExtensionLocalizer
{
    public string Locale => "en-US";
    public string Get(string key, string? fallback = null) => fallback ?? key;
}

internal sealed class FakeCommands : IExtensionCommands
{
    public IDisposable Register(ExtensionCommand command, Func<CancellationToken, ValueTask> execute, Func<bool>? canExecute = null) => new Noop();
    public ValueTask ExecuteAsync(string commandId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
}

internal sealed class FakeSearch : IExtensionSearch
{
    public IDisposable Register(string providerId, Func<ExtensionSearchQuery, CancellationToken, ValueTask<IReadOnlyList<ExtensionSearchResult>>> search) => new Noop();
    public ValueTask<IReadOnlyList<ExtensionSearchResult>> QueryAsync(ExtensionSearchQuery query, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<ExtensionSearchResult>>([]);
    public ValueTask ExecuteAsync(ExtensionSearchResult result, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
}

internal sealed class FakeEvents : IExtensionEvents
{
    public IDisposable Subscribe(string topic, Action<ExtensionEvent> handler) => new Noop();
    public void Publish(string topic, System.Text.Json.JsonElement data) { }
}

internal sealed class FakeLauncher : ILauncherServices
{
    public ValueTask<IReadOnlyList<LauncherInstance>> GetInstancesAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<LauncherInstance>>([]);
    public IReadOnlyList<LauncherLocation> GetLocations() => [];
    public ValueTask NavigateAsync(string route, IReadOnlyDictionary<string, string>? arguments = null, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public ValueTask LaunchAsync(string instanceId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public void ShowLauncher() { }
    public void Notify(string message, bool isError = false) { }
    public ValueTask<string?> PickFileAsync(string title, IReadOnlyList<string> patterns, CancellationToken cancellationToken = default) => ValueTask.FromResult<string?>(null);
}

internal sealed class FakeUi : IExtensionUi
{
    public IDisposable RegisterView(ExtensionView view, Func<ExtensionViewContext, Control> create) => new Noop();
    public IDisposable RegisterAction(ExtensionAction action) => new Noop();
    public ValueTask ShowViewAsync(string viewId, ExtensionViewContext? context = null, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public IExtensionOverlay ShowOverlay(Control content, ExtensionOverlayOptions? options = null) => new FakeOverlay();
    public IDisposable ShowWindow(Window window, ExtensionWindowOptions? options = null) => new Noop();
    public ValueTask ShowDialogAsync(Window dialog, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public void Dispatch(Action action) => action();
}

internal sealed class FakeOverlay : IExtensionOverlay
{
    public bool IsOpen { get; private set; } = true;
    public event Action? Closed;
    public void Dispose() { IsOpen = false; Closed?.Invoke(); }
}

internal sealed class Noop : IDisposable
{
    public void Dispose() { }
}
