using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows.Input;
using DesktopDlnaCast.App.Commands;
using DesktopDlnaCast.Core.Abstractions;
using DesktopDlnaCast.Core.Casting;
using DesktopDlnaCast.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Windows.ApplicationModel.Resources;

namespace DesktopDlnaCast.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly Action<ILogger, Exception?> LogUiOperationFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(400, nameof(LogUiOperationFailed)),
            "A DesktopDlnaCast UI operation failed");

    private static readonly Action<ILogger, Exception?> LogVolumeOperationFailed =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(401, nameof(LogVolumeOperationFailed)),
            "A renderer volume operation failed");

    private static readonly Action<ILogger, long, Exception?> LogDisplayPreviewFailed =
        LoggerMessage.Define<long>(
            LogLevel.Warning,
            new EventId(402, nameof(LogDisplayPreviewFailed)),
            "Display preview capture failed for monitor handle {MonitorHandle}");

    private readonly IDlnaDiscoveryService discoveryService;
    private readonly IDlnaRendererClient rendererClient;
    private readonly IStaticMediaTestSession testSession;
    private readonly ICastSession liveSession;
    private readonly IDisplayCaptureSourceProvider displaySourceProvider;
    private readonly IDisplayPreviewProvider displayPreviewProvider;
    private readonly IUserSettingsStore userSettingsStore;
    private readonly CastSessionStateMachine stateMachine;
    private readonly ILogger<MainViewModel> logger;
    private readonly ResourceLoader resources = new();
    private readonly DispatcherQueue dispatcherQueue;
    private readonly AsyncCommand refreshCommand;
    private readonly AsyncCommand testRendererCommand;
    private readonly AsyncCommand startCastCommand;
    private readonly AsyncCommand stopCommand;
    private readonly DispatcherQueueTimer volumeSendTimer;
    private CancellationTokenSource? currentOperation;
    private CancellationTokenSource? volumeLoad;
    private CancellationTokenSource? displayPreviewRefresh;
    private RendererDeviceViewModel? selectedDevice;
    private double volume = 50;
    private bool isVolumeAvailable;
    private bool suppressVolumeSend;
    private CaptureDisplayViewModel? selectedDisplay;
    private VideoOutputProfileViewModel? selectedOutputProfile;
    private GopOptionViewModel? selectedGopOption;
    private QualityOptionViewModel? selectedQualityOption;
    private string statusText;
    private string encoderText = string.Empty;
    private bool includeCursor = true;
    private bool includeAudio = true;
    private bool audioOnly;
    private bool muteLocalPlayback;
    private bool startAtLiveEdge;
    private bool isBusy;
    private bool isDiscoveringDevices;
    private bool disposed;
    private int initialized;
    private ActiveSession activeSession;
    private UserSettings restoredSettings = new();

    public MainViewModel(
        IDlnaDiscoveryService discoveryService,
        IDlnaRendererClient rendererClient,
        IStaticMediaTestSession testSession,
        ICastSession liveSession,
        IDisplayCaptureSourceProvider displaySourceProvider,
        IDisplayPreviewProvider displayPreviewProvider,
        IUserSettingsStore userSettingsStore,
        CastSessionStateMachine stateMachine,
        ILogger<MainViewModel> logger)
    {
        this.discoveryService = discoveryService;
        this.rendererClient = rendererClient;
        this.testSession = testSession;
        this.liveSession = liveSession;
        this.displaySourceProvider = displaySourceProvider;
        this.displayPreviewProvider = displayPreviewProvider;
        this.userSettingsStore = userSettingsStore;
        this.stateMachine = stateMachine;
        this.logger = logger;
        dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        statusText = GetStateText(stateMachine.State);
        refreshCommand = new(
            RefreshDevicesAsync,
            () => !IsBusy && stateMachine.State == CastSessionState.Idle,
            HandleOperationException);
        testRendererCommand = new(
            TestRendererAsync,
            () => !IsBusy && SelectedDevice is not null && stateMachine.State == CastSessionState.Idle,
            HandleOperationException);
        startCastCommand = new(
            StartCastAsync,
            () => !IsBusy && SelectedDevice is not null && SelectedDisplay is not null &&
                SelectedOutputProfile is not null && stateMachine.State == CastSessionState.Idle,
            HandleOperationException);
        stopCommand = new(
            StopAsync,
            () => IsBusy || stateMachine.State != CastSessionState.Idle,
            HandleOperationException);
        volumeSendTimer = dispatcherQueue.CreateTimer();
        volumeSendTimer.Interval = TimeSpan.FromMilliseconds(300);
        volumeSendTimer.IsRepeating = false;
        volumeSendTimer.Tick += (_, _) => SendVolume();
        stateMachine.StateChanged += OnStateChanged;
        OutputProfiles.Add(new(resources.GetString("Resolution720p"), 1280, 720, 3_000_000, 128_000));
        OutputProfiles.Add(new(resources.GetString("Resolution1080p"), 1920, 1080, 6_000_000, 160_000));
        SelectedOutputProfile = OutputProfiles[0];
        GopOptions.Add(new(resources.GetString("GopHalfSecond"), 15));
        GopOptions.Add(new(resources.GetString("GopOneSecond"), 30));
        GopOptions.Add(new(resources.GetString("GopTwoSeconds"), 60));
        SelectedGopOption = GopOptions[1];
        RefreshDisplays();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<RendererDeviceViewModel> Devices { get; } = [];

    public ObservableCollection<CaptureDisplayViewModel> Displays { get; } = [];

    public ObservableCollection<VideoOutputProfileViewModel> OutputProfiles { get; } = [];

    public ObservableCollection<GopOptionViewModel> GopOptions { get; } = [];

    public ObservableCollection<QualityOptionViewModel> QualityOptions { get; } = [];

    public ICommand RefreshCommand => refreshCommand;

    public ICommand TestRendererCommand => testRendererCommand;

    public ICommand StartCastCommand => startCastCommand;

    public ICommand StopCommand => stopCommand;

    public CaptureDisplayViewModel? SelectedDisplay
    {
        get => selectedDisplay;
        set
        {
            if (ReferenceEquals(selectedDisplay, value))
            {
                return;
            }

            selectedDisplay = value;
            OnPropertyChanged();
            RaiseCommandStates();
        }
    }

    public VideoOutputProfileViewModel? SelectedOutputProfile
    {
        get => selectedOutputProfile;
        set
        {
            if (ReferenceEquals(selectedOutputProfile, value))
            {
                return;
            }

            selectedOutputProfile = value;
            OnPropertyChanged();
            RaiseCommandStates();
            RebuildQualityOptions();
        }
    }

    public GopOptionViewModel? SelectedGopOption
    {
        get => selectedGopOption;
        set
        {
            if (ReferenceEquals(selectedGopOption, value))
            {
                return;
            }

            selectedGopOption = value;
            OnPropertyChanged();
        }
    }

    public QualityOptionViewModel? SelectedQualityOption
    {
        get => selectedQualityOption;
        set
        {
            if (ReferenceEquals(selectedQualityOption, value))
            {
                return;
            }

            selectedQualityOption = value;
            OnPropertyChanged();
        }
    }

    public bool StartAtLiveEdge
    {
        get => startAtLiveEdge;
        set
        {
            if (startAtLiveEdge == value)
            {
                return;
            }

            startAtLiveEdge = value;
            OnPropertyChanged();
        }
    }

    public bool IncludeCursor
    {
        get => includeCursor;
        set
        {
            if (includeCursor == value)
            {
                return;
            }

            includeCursor = value;
            OnPropertyChanged();
        }
    }

    public bool IncludeAudio
    {
        get => includeAudio;
        set
        {
            if (includeAudio == value)
            {
                return;
            }

            includeAudio = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsAudioOnlyEnabled));
            if (!value)
            {
                AudioOnly = false;
                MuteLocalPlayback = false;
            }
        }
    }

    public bool IsAudioOnlyEnabled => IncludeAudio;

    public bool IsVideoOptionsEnabled => !AudioOnly;

    public bool AudioOnly
    {
        get => audioOnly;
        set
        {
            bool effectiveValue = IncludeAudio && value;
            if (audioOnly == effectiveValue)
            {
                return;
            }

            audioOnly = effectiveValue;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsVideoOptionsEnabled));
        }
    }

    public bool MuteLocalPlayback
    {
        get => muteLocalPlayback;
        set
        {
            bool effectiveValue = IncludeAudio && value;
            if (muteLocalPlayback == effectiveValue)
            {
                return;
            }

            muteLocalPlayback = effectiveValue;
            OnPropertyChanged();
        }
    }

    public string EncoderText
    {
        get => encoderText;
        private set
        {
            if (encoderText == value)
            {
                return;
            }

            encoderText = value;
            OnPropertyChanged();
        }
    }

    public RendererDeviceViewModel? SelectedDevice
    {
        get => selectedDevice;
        set
        {
            if (ReferenceEquals(selectedDevice, value))
            {
                return;
            }

            selectedDevice = value;
            OnPropertyChanged();
            RaiseCommandStates();
            _ = LoadVolumeAsync(value?.Device);
        }
    }

    public double Volume
    {
        get => volume;
        set
        {
            if (volume == value)
            {
                return;
            }

            volume = value;
            OnPropertyChanged();
            if (!suppressVolumeSend && IsVolumeAvailable)
            {
                volumeSendTimer.Stop();
                volumeSendTimer.Start();
            }
        }
    }

    public bool IsVolumeAvailable
    {
        get => isVolumeAvailable;
        private set
        {
            if (isVolumeAvailable == value)
            {
                return;
            }

            isVolumeAvailable = value;
            OnPropertyChanged();
        }
    }

    public string StatusText
    {
        get => statusText;
        private set
        {
            if (statusText == value)
            {
                return;
            }

            statusText = value;
            OnPropertyChanged();
        }
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (isBusy == value)
            {
                return;
            }

            isBusy = value;
            OnPropertyChanged();
            RaiseCommandStates();
        }
    }

    public string DevicePlaceholderText =>
        resources.GetString(isDiscoveringDevices ? "DeviceListSearching" : "DeviceListEmpty");

    public async Task InitializeAsync()
    {
        if (Interlocked.Exchange(ref initialized, 1) != 0)
        {
            return;
        }

        restoredSettings = await userSettingsStore.LoadAsync(CancellationToken.None).ConfigureAwait(true);
        ApplySettings(restoredSettings);
        try
        {
            await RefreshDevicesAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            HandleOperationException(exception);
        }
    }

    public void CancelCurrentOperation() => currentOperation?.Cancel();

    public async Task RefreshDisplayPreviewsAsync()
    {
        displayPreviewRefresh?.Cancel();
        displayPreviewRefresh?.Dispose();
        displayPreviewRefresh = new CancellationTokenSource();
        CancellationToken cancellationToken = displayPreviewRefresh.Token;

        foreach (CaptureDisplayViewModel display in Displays)
        {
            try
            {
                DisplayPreviewFrame frame =
                    await displayPreviewProvider.CaptureAsync(display.Source, cancellationToken)
                        .ConfigureAwait(true);
                cancellationToken.ThrowIfCancellationRequested();
                WriteableBitmap bitmap = new(frame.Width, frame.Height);
                using Stream pixelStream = bitmap.PixelBuffer.AsStream();
                await pixelStream.WriteAsync(frame.BgraPixels, cancellationToken).ConfigureAwait(true);
                display.Preview = bitmap;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                LogDisplayPreviewFailed(logger, display.Source.Handle, exception);
            }
        }
    }

    public Task SaveSettingsAsync(CancellationToken cancellationToken)
    {
        DisplayCaptureSource? display = SelectedDisplay?.Source;
        VideoOutputProfileViewModel? profile = SelectedOutputProfile;
        UserSettings settings = new()
        {
            OutputWidth = profile?.Width ?? restoredSettings.OutputWidth,
            OutputHeight = profile?.Height ?? restoredSettings.OutputHeight,
            IncludeCursor = IncludeCursor,
            IncludeAudio = IncludeAudio,
            AudioOnly = AudioOnly,
            MuteLocalPlayback = MuteLocalPlayback,
            GopFrames = SelectedGopOption?.Frames ?? restoredSettings.GopFrames,
            VideoBitratePercent = SelectedQualityOption?.BitratePercent ?? restoredSettings.VideoBitratePercent,
            StartAtLiveEdge = StartAtLiveEdge,
            RendererUdn = SelectedDevice?.Device.Udn ?? restoredSettings.RendererUdn,
            Display = display is null
                ? restoredSettings.Display
                : new(
                    display.Index,
                    display.Left,
                    display.Top,
                    display.Width,
                    display.Height,
                    display.IsPrimary),
        };
        restoredSettings = settings;
        return userSettingsStore.SaveAsync(settings, cancellationToken);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        stateMachine.StateChanged -= OnStateChanged;
        volumeSendTimer.Stop();
        volumeLoad?.Cancel();
        volumeLoad?.Dispose();
        displayPreviewRefresh?.Cancel();
        displayPreviewRefresh?.Dispose();
        currentOperation?.Cancel();
        currentOperation?.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task RefreshDevicesAsync()
    {
        string? preferredRendererUdn = SelectedDevice?.Device.Udn ?? restoredSettings.RendererUdn;
        using CancellationTokenSource operation = BeginOperation();
        try
        {
            IsBusy = true;
            SetDeviceDiscovering(true);
            StatusText = resources.GetString("DiscoveryStarted");
            RefreshDisplays();
            Devices.Clear();
            SelectedDevice = null;
            await foreach (RendererDevice renderer in discoveryService.DiscoverAsync(operation.Token))
            {
                Devices.Add(new(renderer));
            }

            SelectedDevice = Devices.FirstOrDefault(device =>
                string.Equals(device.Device.Udn, preferredRendererUdn, StringComparison.OrdinalIgnoreCase)) ??
                Devices.FirstOrDefault();
            StatusText = string.Format(
                CultureInfo.CurrentCulture,
                resources.GetString("DiscoveryCompleted"),
                Devices.Count);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            StatusText = resources.GetString("OperationCanceled");
        }
        finally
        {
            CompleteOperation(operation);
            IsBusy = false;
            SetDeviceDiscovering(false);
        }
    }

    private void SetDeviceDiscovering(bool value)
    {
        if (isDiscoveringDevices == value)
        {
            return;
        }

        isDiscoveringDevices = value;
        OnPropertyChanged(nameof(DevicePlaceholderText));
    }

    private async Task TestRendererAsync()
    {
        RendererDevice renderer = SelectedDevice?.Device ??
            throw new InvalidOperationException("No renderer is selected.");
        using CancellationTokenSource operation = BeginOperation();
        try
        {
            IsBusy = true;
            activeSession = ActiveSession.Test;
            await testSession.StartAsync(renderer, operation.Token).ConfigureAwait(true);
            StatusText = resources.GetString("RendererTestSucceeded");
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            activeSession = ActiveSession.None;
            StatusText = resources.GetString("OperationCanceled");
        }
        finally
        {
            CompleteOperation(operation);
            IsBusy = false;
        }
    }

    private async Task StartCastAsync()
    {
        RendererDevice renderer = SelectedDevice?.Device ??
            throw new InvalidOperationException("No renderer is selected.");
        DisplayCaptureSource display = SelectedDisplay?.Source ??
            throw new InvalidOperationException("No display is selected.");
        VideoOutputProfileViewModel profile = SelectedOutputProfile ??
            throw new InvalidOperationException("No output resolution is selected.");
        int bitratePercent = SelectedQualityOption?.BitratePercent ?? 100;
        MediaCaptureConfiguration configuration = new(
            CaptureSourceKind.Display,
            display.Handle,
            IncludeCursor,
            profile.Width,
            profile.Height,
            FrameRate: 30,
            VideoBitrate: (int)((long)profile.VideoBitrate * bitratePercent / 100),
            GopFrames: SelectedGopOption?.Frames ?? 30,
            IncludeAudio: IncludeAudio,
            AudioBitrate: profile.AudioBitrate,
            MuteLocalPlayback: MuteLocalPlayback,
            StartAtLiveEdge: StartAtLiveEdge,
            AudioOnly: AudioOnly);

        using CancellationTokenSource operation = BeginOperation();
        try
        {
            IsBusy = true;
            activeSession = ActiveSession.Live;
            EncoderText = string.Empty;
            await liveSession.StartAsync(renderer, configuration, operation.Token).ConfigureAwait(true);
            MediaEncoderDiagnostics? diagnostics = liveSession.EncoderDiagnostics;
            if (diagnostics is not null)
            {
                EncoderText = string.Format(
                    CultureInfo.CurrentCulture,
                    resources.GetString("EncoderSummary"),
                    diagnostics.EncoderName,
                    diagnostics.AcceptedWidth,
                    diagnostics.AcceptedHeight,
                    diagnostics.AudioEnabled ? resources.GetString("AudioEnabled") : resources.GetString("AudioDisabled"));
            }

            StatusText = resources.GetString("LiveCastStarted");
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            activeSession = ActiveSession.None;
            StatusText = resources.GetString("OperationCanceled");
        }
        finally
        {
            CompleteOperation(operation);
            IsBusy = false;
        }
    }

    private async Task StopAsync()
    {
        currentOperation?.Cancel();
        if (activeSession == ActiveSession.Live)
        {
            await liveSession.StopAsync(CancellationToken.None).ConfigureAwait(true);
        }
        else if (activeSession == ActiveSession.Test)
        {
            await testSession.StopAsync(CancellationToken.None).ConfigureAwait(true);
        }

        activeSession = ActiveSession.None;
        StatusText = GetStateText(stateMachine.State);
        IsBusy = false;
    }

    private async Task LoadVolumeAsync(RendererDevice? renderer)
    {
        volumeLoad?.Cancel();
        volumeLoad?.Dispose();
        volumeLoad = null;
        volumeSendTimer.Stop();
        IsVolumeAvailable = false;
        if (renderer is null || disposed)
        {
            return;
        }

        CancellationTokenSource load = volumeLoad = new();
        try
        {
            int? current = await rendererClient.GetVolumeAsync(renderer, load.Token).ConfigureAwait(true);
            if (load.IsCancellationRequested || current is null)
            {
                return;
            }

            suppressVolumeSend = true;
            try
            {
                Volume = Math.Clamp(current.Value, 0, 100);
            }
            finally
            {
                suppressVolumeSend = false;
            }

            IsVolumeAvailable = true;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogVolumeOperationFailed(logger, exception);
        }
    }

    private async void SendVolume()
    {
        RendererDevice? renderer = SelectedDevice?.Device;
        if (renderer is null || !IsVolumeAvailable)
        {
            return;
        }

        try
        {
            await rendererClient.SetVolumeAsync(
                renderer,
                (int)Math.Round(Volume),
                CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogVolumeOperationFailed(logger, exception);
        }
    }

    private CancellationTokenSource BeginOperation()
    {
        currentOperation?.Cancel();
        currentOperation?.Dispose();
        currentOperation = new();
        return currentOperation;
    }

    private void CompleteOperation(CancellationTokenSource operation)
    {
        if (ReferenceEquals(currentOperation, operation))
        {
            currentOperation = null;
        }
    }

    private void HandleOperationException(Exception exception)
    {
        LogUiOperationFailed(logger, exception);
        StatusText = exception is DllNotFoundException
            ? resources.GetString("NativeRuntimeMissing")
            : (activeSession == ActiveSession.Live ? liveSession.Failure : testSession.Failure)?.UserMessage ??
                resources.GetString("OperationFailed");
        if (stateMachine.State == CastSessionState.Idle)
        {
            activeSession = ActiveSession.None;
        }

        IsBusy = false;
    }

    private void RebuildQualityOptions()
    {
        int baseBitrate = SelectedOutputProfile?.VideoBitrate ?? 3_000_000;
        int preservedPercent = SelectedQualityOption?.BitratePercent ?? 100;
        QualityOptions.Clear();
        QualityOptions.Add(CreateQualityOption("QualityHigh", 150, baseBitrate));
        QualityOptions.Add(CreateQualityOption("QualityMedium", 100, baseBitrate));
        QualityOptions.Add(CreateQualityOption("QualityLow", 50, baseBitrate));
        SelectedQualityOption = QualityOptions.FirstOrDefault(option =>
            option.BitratePercent == preservedPercent) ??
            QualityOptions[1];
    }

    private QualityOptionViewModel CreateQualityOption(string resourceKey, int bitratePercent, int baseBitrate)
    {
        double megabits = (long)baseBitrate * bitratePercent / 100 / 1_000_000.0;
        return new(
            string.Format(
                CultureInfo.CurrentCulture,
                resources.GetString(resourceKey),
                megabits.ToString("0.#", CultureInfo.CurrentCulture)),
            bitratePercent);
    }

    private void RefreshDisplays()
    {
        long? selectedHandle = SelectedDisplay?.Source.Handle;
        Displays.Clear();
        foreach (DisplayCaptureSource display in displaySourceProvider.GetDisplays())
        {
            string name = string.Format(
                CultureInfo.CurrentCulture,
                display.IsPrimary
                    ? resources.GetString("PrimaryDisplayName")
                    : resources.GetString("DisplayName"),
                display.Index,
                display.Width,
                display.Height);
            Displays.Add(new(display, name));
        }

        SelectedDisplay = Displays.FirstOrDefault(item => item.Source.Handle == selectedHandle) ??
            Displays.FirstOrDefault();
    }

    private void ApplySettings(UserSettings settings)
    {
        IncludeCursor = settings.IncludeCursor;
        IncludeAudio = settings.IncludeAudio;
        AudioOnly = settings.AudioOnly;
        MuteLocalPlayback = settings.MuteLocalPlayback;
        StartAtLiveEdge = settings.StartAtLiveEdge;
        SelectedOutputProfile = OutputProfiles.FirstOrDefault(profile =>
            profile.Width == settings.OutputWidth && profile.Height == settings.OutputHeight) ??
            OutputProfiles[0];
        SelectedGopOption = GopOptions.FirstOrDefault(option => option.Frames == settings.GopFrames) ??
            GopOptions[1];
        SelectedQualityOption = QualityOptions.FirstOrDefault(option =>
            option.BitratePercent == settings.VideoBitratePercent) ??
            QualityOptions[1];

        DisplayUserSettings? preferredDisplay = settings.Display;
        if (preferredDisplay is null)
        {
            return;
        }

        SelectedDisplay = Displays.FirstOrDefault(display =>
            display.Source.Left == preferredDisplay.Left &&
            display.Source.Top == preferredDisplay.Top &&
            display.Source.Width == preferredDisplay.Width &&
            display.Source.Height == preferredDisplay.Height) ??
            Displays.FirstOrDefault(display =>
                display.Source.Index == preferredDisplay.Index &&
                display.Source.Width == preferredDisplay.Width &&
                display.Source.Height == preferredDisplay.Height) ??
            (preferredDisplay.IsPrimary
                ? Displays.FirstOrDefault(display => display.Source.IsPrimary)
                : null) ??
            Displays.FirstOrDefault();
    }

    private void OnStateChanged(object? sender, CastSessionStateChangedEventArgs args)
    {
        if (dispatcherQueue.HasThreadAccess)
        {
            ApplyState(args.CurrentState);
            return;
        }

        dispatcherQueue.TryEnqueue(() => ApplyState(args.CurrentState));
    }

    private void ApplyState(CastSessionState state)
    {
        if (state == CastSessionState.Idle &&
            activeSession == ActiveSession.Live &&
            liveSession.StopReason is CastStopReason.RendererReportedStopped or CastStopReason.RendererUnreachable)
        {
            activeSession = ActiveSession.None;
            EncoderText = string.Empty;
            StatusText = resources.GetString(
                liveSession.StopReason == CastStopReason.RendererReportedStopped
                    ? "RendererStoppedPlayback"
                    : "RendererUnreachable");
            RaiseCommandStates();
            return;
        }

        StatusText = GetStateText(state);
        RaiseCommandStates();
    }

    private void RaiseCommandStates()
    {
        refreshCommand.RaiseCanExecuteChanged();
        testRendererCommand.RaiseCanExecuteChanged();
        startCastCommand.RaiseCanExecuteChanged();
        stopCommand.RaiseCanExecuteChanged();
    }

    private string GetStateText(CastSessionState state) =>
        resources.GetString($"CastState_{state}");

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new(propertyName));

    private enum ActiveSession
    {
        None,
        Test,
        Live,
    }
}
