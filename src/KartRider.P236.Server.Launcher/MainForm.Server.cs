using System.Net;
using System.Net.Sockets;
using KartRider.P236.Server;

namespace KartRider.P236.Server.Launcher;

internal sealed partial class MainForm
{
    private TabPage BuildServerPage()
    {
        TabPage page = new("서버 실행");
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 1,
            RowCount = 9,
        };
        for (int index = 0; index < 8; index++)
        {
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        _bindAddressTextBox.Dock = DockStyle.Fill;
        _bindAddressTextBox.PlaceholderText = "예: 0.0.0.0 또는 127.0.0.1";
        _advertisedAddressTextBox.Dock = DockStyle.Fill;
        _advertisedAddressTextBox.PlaceholderText = "클라이언트에 전달할 IPv4";

        ConfigurePortControl(_tcpPortNumeric);
        ConfigurePortControl(_udpPortNumeric);
        TableLayoutPanel portControls = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 4,
        };
        portControls.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        portControls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        portControls.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        portControls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        portControls.Controls.Add(CreateInlineLabel("TCP"), 0, 0);
        portControls.Controls.Add(_tcpPortNumeric, 1, 0);
        portControls.Controls.Add(CreateInlineLabel("UDP"), 2, 0);
        portControls.Controls.Add(_udpPortNumeric, 3, 0);

        _serverDataDirectoryTextBox.PlaceholderText = "profiles.json, observers.json 저장 폴더";
        _browseServerDataButton.Click += BrowseServerDataButton_Click;
        _logDirectoryTextBox.PlaceholderText = "패킷 trace 로그 저장 폴더";
        _browseLogDirectoryButton.Click += BrowseLogDirectoryButton_Click;

        _packetTraceCheckBox.Text = "전체 패킷 hex trace 기록 (로그 크기가 빠르게 증가할 수 있음)";
        _packetTraceCheckBox.AutoSize = true;
        _packetTraceCheckBox.Margin = new Padding(0, 5, 0, 5);

        FlowLayoutPanel actions = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(150, 8, 0, 8),
        };
        _saveServerSettingsButton.Text = "설정 저장";
        _saveServerSettingsButton.AutoSize = true;
        _saveServerSettingsButton.Padding = new Padding(10, 5, 10, 5);
        _saveServerSettingsButton.Click += SaveServerSettingsButton_Click;
        _startServerButton.Text = "서버 시작";
        _startServerButton.AutoSize = true;
        _startServerButton.Padding = new Padding(18, 5, 18, 5);
        _startServerButton.Font = new Font(Font, FontStyle.Bold);
        _startServerButton.Click += StartServerButton_Click;
        _stopServerButton.Text = "서버 중지";
        _stopServerButton.AutoSize = true;
        _stopServerButton.Padding = new Padding(18, 5, 18, 5);
        _stopServerButton.Click += StopServerButton_Click;
        _serverStatusLabel.Text = "중지됨";
        _serverStatusLabel.AutoSize = true;
        _serverStatusLabel.Margin = new Padding(16, 10, 0, 0);
        actions.Controls.Add(_saveServerSettingsButton);
        actions.Controls.Add(_startServerButton);
        actions.Controls.Add(_stopServerButton);
        actions.Controls.Add(_serverStatusLabel);

        Label note = new()
        {
            Text = "서버 시작은 클라이언트 item.rho 또는 아이템 확률 JSON을 수정하지 않습니다. " +
                   "확률 적용은 ‘아이템 확률’ 탭에서 별도로 실행해야 합니다.",
            AutoSize = true,
            ForeColor = Color.DimGray,
            MaximumSize = new Size(820, 0),
            Margin = new Padding(150, 8, 0, 0),
        };

        root.Controls.Add(CreateFieldRow("바인드 IPv4", _bindAddressTextBox), 0, 0);
        root.Controls.Add(CreateFieldRow("광고 IPv4", _advertisedAddressTextBox), 0, 1);
        root.Controls.Add(CreateFieldRow("서버 포트", portControls), 0, 2);
        root.Controls.Add(CreateFieldRow(
            "서버 데이터 폴더",
            CreatePathControls(_serverDataDirectoryTextBox, _browseServerDataButton)), 0, 3);
        root.Controls.Add(CreateFieldRow(
            "로그 폴더",
            CreatePathControls(_logDirectoryTextBox, _browseLogDirectoryButton)), 0, 4);
        root.Controls.Add(CreateFieldRow("패킷 trace", _packetTraceCheckBox), 0, 5);
        root.Controls.Add(actions, 0, 6);
        root.Controls.Add(note, 0, 7);
        page.Controls.Add(root);
        return page;
    }

    private static void ConfigurePortControl(NumericUpDown control)
    {
        control.Minimum = 0;
        control.Maximum = ushort.MaxValue;
        control.Width = 110;
        control.Dock = DockStyle.Fill;
        control.TextAlign = HorizontalAlignment.Right;
        control.ThousandsSeparator = false;
    }

    private static Label CreateInlineLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Margin = new Padding(0, 5, 8, 0),
        };
    }

    private void BrowseServerDataButton_Click(object? sender, EventArgs e)
    {
        BrowseForDirectory(
            _serverDataDirectoryTextBox,
            "서버 profiles.json과 observers.json을 저장할 폴더를 선택하세요.");
    }

    private void BrowseLogDirectoryButton_Click(object? sender, EventArgs e)
    {
        BrowseForDirectory(
            _logDirectoryTextBox,
            "패킷 trace 로그를 저장할 폴더를 선택하세요.");
    }

    private void BrowseForDirectory(TextBox destination, string description)
    {
        using FolderBrowserDialog dialog = new()
        {
            Description = description,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = Directory.Exists(destination.Text) ? destination.Text : string.Empty,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            destination.Text = Path.GetFullPath(dialog.SelectedPath);
        }
    }

    private void SaveServerSettingsButton_Click(object? sender, EventArgs e)
    {
        TrySaveSettings(showSuccess: true, showErrors: true);
    }

    private async void StartServerButton_Click(object? sender, EventArgs e)
    {
        if (_serverBusy || _server is not null)
        {
            return;
        }

        Task operation = StartServerFromUiAsync();
        _activeServerOperation = operation;
        await operation;
    }

    private async Task StartServerFromUiAsync()
    {

        ServerLauncherSettings settings;
        try
        {
            settings = CaptureSettings();
            ServerLauncherSettingsStore.Save(settings);
            _settings = settings;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or
                ArgumentException or NotSupportedException)
        {
            ShowError("서버 설정 오류", exception);
            return;
        }

        _serverBusy = true;
        _serverStatusLabel.Text = "시작 중...";
        UpdateServerControls();
        P236Server candidate = new();
        try
        {
            P236ServerOptions options = CreateServerOptions(settings);
            await candidate.StartAsync(options);
            _server = candidate;
            _serverStatusLabel.Text = $"실행 중 (TCP {_server.TcpPort}, UDP {_server.UdpPort})";
            _serverStatusLabel.ForeColor = Color.DarkGreen;
            AppendLog(
                $"P236 서버가 시작되었습니다: bind={settings.BindAddress}, " +
                $"advertise={settings.AdvertisedAddress}, TCP={_server.TcpPort}, UDP={_server.UdpPort}");
            AppendLog("서버 시작 과정에서는 아이템 확률 파일을 자동 적용하지 않았습니다.");
        }
        catch (Exception exception)
        {
            try
            {
                await candidate.DisposeAsync();
            }
            catch (Exception disposeException)
            {
                AppendLog($"시작 실패 후 서버 정리 중 추가 오류: {disposeException.Message}");
            }
            _serverStatusLabel.Text = "시작 실패";
            _serverStatusLabel.ForeColor = Color.DarkRed;
            ShowError("서버 시작 실패", exception);
        }
        finally
        {
            _serverBusy = false;
            UpdateServerControls();
        }
    }

    private async void StopServerButton_Click(object? sender, EventArgs e)
    {
        if (_serverBusy || _server is null)
        {
            return;
        }

        Task operation = StopServerFromUiAsync();
        _activeServerOperation = operation;
        await operation;
    }

    private async Task StopServerFromUiAsync()
    {

        _serverBusy = true;
        _serverStatusLabel.Text = "중지 중...";
        UpdateServerControls();
        try
        {
            await StopServerCoreAsync();
            _serverStatusLabel.Text = "중지됨";
            _serverStatusLabel.ForeColor = SystemColors.ControlText;
            AppendLog("P236 서버가 중지되었습니다.");
        }
        catch (Exception exception)
        {
            _serverStatusLabel.Text = "중지 오류";
            _serverStatusLabel.ForeColor = Color.DarkRed;
            ShowError("서버 중지 실패", exception);
        }
        finally
        {
            _serverBusy = false;
            UpdateServerControls();
        }
    }

    private async Task StopServerCoreAsync()
    {
        P236Server? server = _server;
        _server = null;
        if (server is null)
        {
            return;
        }

        try
        {
            await server.StopAsync();
        }
        finally
        {
            await server.DisposeAsync();
        }
    }

    private bool TrySaveSettings(bool showSuccess, bool showErrors)
    {
        try
        {
            ServerLauncherSettings settings = CaptureSettings();
            ServerLauncherSettingsStore.Save(settings);
            _settings = settings;
            AppendLog($"서버 런처 설정을 저장했습니다: {ServerLauncherSettingsStore.SettingsPath}");
            if (showSuccess)
            {
                _serverStatusLabel.Text = "설정 저장됨";
            }
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or
                ArgumentException or NotSupportedException)
        {
            if (showErrors)
            {
                ShowError("서버 런처 설정 저장 실패", exception);
            }
            else
            {
                AppendLog($"서버 런처 설정을 저장하지 못했습니다: {exception.Message}");
            }
            return false;
        }
    }

    private ServerLauncherSettings CaptureSettings()
    {
        ServerLauncherSettings settings = new()
        {
            BindAddress = _bindAddressTextBox.Text.Trim(),
            AdvertisedAddress = _advertisedAddressTextBox.Text.Trim(),
            TcpPort = decimal.ToInt32(_tcpPortNumeric.Value),
            UdpPort = decimal.ToInt32(_udpPortNumeric.Value),
            ServerDataDirectory = NormalizeRequiredPath(
                _serverDataDirectoryTextBox.Text,
                "서버 데이터 폴더"),
            LogDirectory = NormalizeRequiredPath(_logDirectoryTextBox.Text, "로그 폴더"),
            EnablePacketTrace = _packetTraceCheckBox.Checked,
            ClientDataDirectory = NormalizeOptionalPath(_clientDataDirectoryTextBox.Text),
            ItemProbabilityConfigurationPath = NormalizeRequiredPath(
                _itemConfigurationPathTextBox.Text,
                "아이템 확률 JSON"),
        };
        settings.Validate();
        return settings;
    }

    private static string NormalizeRequiredPath(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"{label} 값이 비어 있습니다.");
        }

        return Path.GetFullPath(value.Trim());
    }

    private static string NormalizeOptionalPath(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : Path.GetFullPath(value.Trim());
    }

    private static P236ServerOptions CreateServerOptions(ServerLauncherSettings settings)
    {
        if (!IPAddress.TryParse(settings.BindAddress, out IPAddress? bindAddress) ||
            bindAddress.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new InvalidDataException("바인드 주소는 IPv4여야 합니다.");
        }
        if (!IPAddress.TryParse(settings.AdvertisedAddress, out IPAddress? advertisedAddress) ||
            advertisedAddress.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new InvalidDataException("광고 주소는 IPv4여야 합니다.");
        }

        return new P236ServerOptions
        {
            BindAddress = bindAddress,
            AdvertisedAddress = advertisedAddress,
            TcpPort = settings.TcpPort,
            UdpPort = settings.UdpPort,
            DataDirectory = settings.ServerDataDirectory,
            LogDirectory = settings.LogDirectory,
            EnablePacketTrace = settings.EnablePacketTrace,
        };
    }

    private void UpdateServerControls()
    {
        bool running = _server is not null;
        bool canEdit = !_serverBusy && !running && !_shutdownStarted;
        _bindAddressTextBox.Enabled = canEdit;
        _advertisedAddressTextBox.Enabled = canEdit;
        _tcpPortNumeric.Enabled = canEdit;
        _udpPortNumeric.Enabled = canEdit;
        _serverDataDirectoryTextBox.Enabled = canEdit;
        _logDirectoryTextBox.Enabled = canEdit;
        _packetTraceCheckBox.Enabled = canEdit;
        _browseServerDataButton.Enabled = canEdit;
        _browseLogDirectoryButton.Enabled = canEdit;
        _saveServerSettingsButton.Enabled = canEdit;
        _startServerButton.Enabled = canEdit;
        _stopServerButton.Enabled = !_serverBusy && running && !_shutdownStarted;
    }
}
