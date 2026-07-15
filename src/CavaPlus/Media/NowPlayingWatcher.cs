using Windows.Media.Control;

namespace CavaPlus.Media;

public sealed class NowPlayingWatcher
{
    private readonly object _lock = new();
    private NowPlayingInfo? _current;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _activeSession;

    public NowPlayingInfo? Current
    {
        get
        {
            lock (_lock)
            {
                return _current;
            }
        }
    }

    public async Task StartAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += (_, _) => HandleSessionChanged();
            HandleSessionChanged();
        }
        catch
        {
            // API unavailable on this system (unsupported Windows version, WinRT activation
            // failure, etc.) - Current stays null forever and the caller just keeps showing
            // its configured placeholder; the rest of the app is unaffected.
        }
    }

    private void HandleSessionChanged()
    {
        if (_activeSession is not null)
        {
            _activeSession.MediaPropertiesChanged -= HandleMediaPropertiesChanged;
        }

        _activeSession = _manager!.GetCurrentSession();

        if (_activeSession is null)
        {
            SetCurrent(null);
            return;
        }

        _activeSession.MediaPropertiesChanged += HandleMediaPropertiesChanged;
        _ = RefreshCurrentAsync(_activeSession);
    }

    private void HandleMediaPropertiesChanged(
        GlobalSystemMediaTransportControlsSession session,
        MediaPropertiesChangedEventArgs args)
    {
        _ = RefreshCurrentAsync(session);
    }

    private async Task RefreshCurrentAsync(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            var properties = await session.TryGetMediaPropertiesAsync();
            SetCurrent(string.IsNullOrWhiteSpace(properties.Title)
                ? null
                : new NowPlayingInfo(properties.Title, properties.Artist ?? string.Empty));
        }
        catch
        {
            SetCurrent(null);
        }
    }

    private void SetCurrent(NowPlayingInfo? info)
    {
        lock (_lock)
        {
            _current = info;
        }
    }
}
