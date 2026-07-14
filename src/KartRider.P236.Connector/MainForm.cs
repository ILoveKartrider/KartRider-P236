using System.Drawing;

namespace KartRider.P236.Connector;

internal sealed class MainForm : Form
{
    private readonly LauncherService _launcherService = new LauncherService();
    private readonly HashSet<string> _knownInstanceExecutables =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private readonly ComboBox _instanceCombo = new ComboBox();
    private readonly TextBox _pathTextBox = new TextBox();
    private readonly TextBox _usernameTextBox = new TextBox();
    private readonly TextBox _serverAddressTextBox = new TextBox();
    private readonly NumericUpDown _loginPortNumeric = new NumericUpDown();
    private readonly TextBox _storageRootTextBox = new TextBox();
    private readonly Button _browseFolderButton = new Button();
    private readonly Button _browseExecutableButton = new Button();
    private readonly Button _saveSettingsButton = new Button();
    private readonly Button _launchButton = new Button();
    private readonly Label _statusLabel = new Label();
    private readonly RichTextBox _logTextBox = new RichTextBox();

    internal MainForm()
    {
        Text = "KartRider 2005 접속기";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 610);
        ClientSize = new Size(840, 680);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("맑은 고딕", 9F, FontStyle.Regular, GraphicsUnit.Point);

        BuildLayout();
        LoadPreparedInstances();
    }

    private void BuildLayout()
    {
        TableLayoutPanel root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 1,
            RowCount = 9
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        Label title = new Label
        {
            Text = "KartRider 2005-12-14 전용 접속기",
            AutoSize = true,
            Font = new Font(Font.FontFamily, 16F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 4)
        };
        Label description = new Label
        {
            Text = "실행 전에 PIN과 KartRider.xml의 서버 및 문서 저장 설정을 함께 적용합니다.",
            AutoSize = true,
            ForeColor = Color.DimGray,
            Margin = new Padding(0, 0, 0, 14)
        };

        _instanceCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _instanceCombo.Dock = DockStyle.Fill;
        _instanceCombo.SelectedIndexChanged += InstanceCombo_SelectedIndexChanged;
        root.Controls.Add(title, 0, 0);
        root.Controls.Add(description, 0, 1);
        root.Controls.Add(CreateFieldRow("클라이언트 인스턴스", _instanceCombo), 0, 2);

        _pathTextBox.Dock = DockStyle.Fill;
        _pathTextBox.PlaceholderText = "설치 폴더를 선택하세요";
        _pathTextBox.Leave += (_, _) => LoadSettingsFromCurrentPath();
        _browseFolderButton.Text = "폴더 선택";
        _browseFolderButton.AutoSize = true;
        _browseFolderButton.Click += BrowseFolderButton_Click;
        _browseExecutableButton.Text = "EXE 선택";
        _browseExecutableButton.AutoSize = true;
        _browseExecutableButton.Click += BrowseExecutableButton_Click;

        TableLayoutPanel pathControls = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 3
        };
        pathControls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        pathControls.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathControls.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathControls.Controls.Add(_pathTextBox, 0, 0);
        pathControls.Controls.Add(_browseFolderButton, 1, 0);
        pathControls.Controls.Add(_browseExecutableButton, 2, 0);
        root.Controls.Add(CreateFieldRow("클라이언트 경로", pathControls), 0, 3);

        _usernameTextBox.Dock = DockStyle.Fill;
        _usernameTextBox.MaxLength = 32;
        root.Controls.Add(CreateFieldRow("username", _usernameTextBox), 0, 4);

        _serverAddressTextBox.Dock = DockStyle.Fill;
        _serverAddressTextBox.Text = "127.0.0.1";
        _serverAddressTextBox.PlaceholderText = "예: 127.0.0.1 또는 LAN 서버 IPv4";
        _loginPortNumeric.Minimum = 1;
        _loginPortNumeric.Maximum = ushort.MaxValue;
        _loginPortNumeric.Value = 39312;
        _loginPortNumeric.Width = 92;
        _loginPortNumeric.TextAlign = HorizontalAlignment.Right;

        TableLayoutPanel endpointControls = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 3
        };
        endpointControls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        endpointControls.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        endpointControls.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        endpointControls.Controls.Add(_serverAddressTextBox, 0, 0);
        endpointControls.Controls.Add(new Label
        {
            Text = "로그인 TCP 포트",
            AutoSize = true,
            Margin = new Padding(12, 5, 8, 0)
        }, 1, 0);
        endpointControls.Controls.Add(_loginPortNumeric, 2, 0);
        root.Controls.Add(CreateFieldRow("서버 IPv4", endpointControls), 0, 5);

        _storageRootTextBox.Dock = DockStyle.Fill;
        _storageRootTextBox.MaxLength = 120;
        _storageRootTextBox.PlaceholderText = "예: 카트라이더_236_client3";
        root.Controls.Add(CreateFieldRow("문서 저장 폴더", _storageRootTextBox), 0, 6);

        FlowLayoutPanel actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(142, 8, 0, 8)
        };
        _saveSettingsButton.Text = "설정만 저장";
        _saveSettingsButton.AutoSize = true;
        _saveSettingsButton.Padding = new Padding(12, 6, 12, 6);
        _saveSettingsButton.Click += SaveSettingsButton_Click;
        _launchButton.Text = "설정 저장 후 실행";
        _launchButton.AutoSize = true;
        _launchButton.Padding = new Padding(22, 6, 22, 6);
        _launchButton.Font = new Font(Font, FontStyle.Bold);
        _launchButton.Click += LaunchButton_Click;
        _statusLabel.Text = "대기 중";
        _statusLabel.AutoSize = true;
        _statusLabel.Margin = new Padding(16, 11, 0, 0);
        actions.Controls.Add(_saveSettingsButton);
        actions.Controls.Add(_launchButton);
        actions.Controls.Add(_statusLabel);
        root.Controls.Add(actions, 0, 7);

        _logTextBox.Dock = DockStyle.Fill;
        _logTextBox.ReadOnly = true;
        _logTextBox.BackColor = Color.FromArgb(248, 248, 248);
        _logTextBox.BorderStyle = BorderStyle.FixedSingle;
        _logTextBox.Font = new Font("Consolas", 9F);
        _logTextBox.DetectUrls = false;
        root.Controls.Add(_logTextBox, 0, 8);

        Controls.Add(root);
        AcceptButton = _launchButton;
    }

    private static Control CreateFieldRow(string labelText, Control control)
    {
        TableLayoutPanel row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            Margin = new Padding(0, 4, 0, 4)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140F));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        Label label = new Label
        {
            Text = labelText,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 3, 8, 3)
        };
        row.Controls.Add(label, 0, 0);
        row.Controls.Add(control, 1, 0);
        return row;
    }

    private void LoadPreparedInstances()
    {
        IReadOnlyList<ClientInstanceOption> instances = ClientInstanceDiscovery.FindPreparedInstances();
        foreach (ClientInstanceOption instance in instances)
        {
            _instanceCombo.Items.Add(instance);
            _knownInstanceExecutables.Add(instance.ExecutablePath);
        }

        if (_instanceCombo.Items.Count > 0)
        {
            _instanceCombo.SelectedIndex = 0;
            AppendLog($"준비된 인스턴스 {_instanceCombo.Items.Count}개를 찾았습니다.");
        }
        else
        {
            AppendLog("자동으로 찾은 인스턴스가 없습니다. 폴더 또는 KartRider.exe를 선택하세요.");
        }
    }

    private void InstanceCombo_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_instanceCombo.SelectedItem is not ClientInstanceOption instance)
        {
            return;
        }

        _pathTextBox.Text = instance.RootDirectory;
        _usernameTextBox.Text = instance.Username ?? string.Empty;
        _knownInstanceExecutables.Add(instance.ExecutablePath);
        LoadPinSettings(instance.RootDirectory);
        _statusLabel.Text = instance.Name;
    }

    private void BrowseFolderButton_Click(object? sender, EventArgs e)
    {
        using FolderBrowserDialog dialog = new FolderBrowserDialog
        {
            Description = "KartRider.exe가 있는 2005 클라이언트 폴더를 선택하세요.",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        if (Directory.Exists(_pathTextBox.Text))
        {
            dialog.InitialDirectory = _pathTextBox.Text;
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            SetClientPath(dialog.SelectedPath);
        }
    }

    private void BrowseExecutableButton_Click(object? sender, EventArgs e)
    {
        using OpenFileDialog dialog = new OpenFileDialog
        {
            Title = "2005 KartRider.exe 선택",
            Filter = "KartRider.exe|KartRider.exe|실행 파일 (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = false
        };
        try
        {
            ClientSelection current = ClientSelection.FromPath(_pathTextBox.Text);
            dialog.InitialDirectory = current.RootDirectory;
        }
        catch
        {
            // No valid current path yet.
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            SetClientPath(dialog.FileName);
        }
    }

    private void SetClientPath(string path)
    {
        try
        {
            ClientSelection selection = ClientSelection.FromPath(path);
            _pathTextBox.Text = selection.RootDirectory;
            if (OriginalClientValidator.IsKnownOriginal(selection.ExecutablePath))
            {
                _knownInstanceExecutables.Add(selection.ExecutablePath);
            }
            LoadSettingsFromCurrentPath();
            _statusLabel.Text = "선택됨";
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private void LoadUsernameFromCurrentPath()
    {
        try
        {
            ClientSelection selection = ClientSelection.FromPath(_pathTextBox.Text);
            string? username = LegacyAccountProfile.TryReadUsername(selection.RootDirectory);
            if (!string.IsNullOrWhiteSpace(username))
            {
                _usernameTextBox.Text = username;
            }
        }
        catch
        {
            // Path editing is allowed; launch-time validation supplies the error.
        }
    }

    private void LoadSettingsFromCurrentPath()
    {
        LoadUsernameFromCurrentPath();
        try
        {
            ClientSelection selection = ClientSelection.FromPath(_pathTextBox.Text);
            LoadPinSettings(selection.RootDirectory);
        }
        catch
        {
            // Path editing is allowed; launch-time validation supplies the error.
        }
    }

    private void LoadPinSettings(string rootDirectory)
    {
        PreparedClientSettings.RecoverIfNeeded(rootDirectory);
        PreparedPinInfo pinInfo = PreparedPinValidator.InspectConfigurable(rootDirectory);
        _serverAddressTextBox.Text = pinInfo.LoginHost;
        _loginPortNumeric.Value = pinInfo.LoginPort;
        _storageRootTextBox.Text = PreparedClientSettings.GetRecommendedStorageRoot(
            rootDirectory,
            pinInfo.StorageRoot);
    }

    private async void LaunchButton_Click(object? sender, EventArgs e)
    {
        try
        {
            ClientSelection selection = ClientSelection.FromPath(_pathTextBox.Text);
            string username = LegacyAccountProfile.NormalizeUsername(_usernameTextBox.Text);
            string serverAddress = _serverAddressTextBox.Text.Trim();
            ushort loginPort = checked((ushort)_loginPortNumeric.Value);
            string storageRoot = PreparedClientSettings.NormalizeStorageRoot(_storageRootTextBox.Text);
            _usernameTextBox.Text = username;
            _serverAddressTextBox.Text = PreparedClientSettings.NormalizeServerAddress(serverAddress).ToString();
            _storageRootTextBox.Text = storageRoot;
            SetBusy(true);
            AppendLog(
                $"실행 요청: {selection.RootDirectory}, username={username}, " +
                $"server={_serverAddressTextBox.Text}:{loginPort}, storage={storageRoot}");

            Progress<string> progress = new Progress<string>(message =>
            {
                _statusLabel.Text = message;
                AppendLog(message);
            });
            LaunchResult result = await _launcherService.LaunchAsync(
                selection,
                username,
                _serverAddressTextBox.Text,
                loginPort,
                storageRoot,
                _knownInstanceExecutables.ToArray(),
                progress);

            _knownInstanceExecutables.Add(selection.ExecutablePath);
            _statusLabel.Text = $"실행됨 (PID {result.ProcessId})";
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void SaveSettingsButton_Click(object? sender, EventArgs e)
    {
        try
        {
            ClientSelection selection = ClientSelection.FromPath(_pathTextBox.Text);
            PreparedClientSettings.RecoverIfNeeded(selection.RootDirectory);
            _ = OriginalClientValidator.ValidateConfigurableClient(selection);
            string serverAddress = PreparedClientSettings.NormalizeServerAddress(
                _serverAddressTextBox.Text).ToString();
            ushort loginPort = checked((ushort)_loginPortNumeric.Value);
            string storageRoot = PreparedClientSettings.NormalizeStorageRoot(_storageRootTextBox.Text);

            _serverAddressTextBox.Text = serverAddress;
            _storageRootTextBox.Text = storageRoot;
            SetBusy(true);
            AppendLog(
                $"설정 저장 요청: {selection.RootDirectory}, " +
                $"server={serverAddress}:{loginPort}, storage={storageRoot}");

            PreparedClientSettingsResult result = await Task.Run(() =>
                PreparedClientSettings.Apply(
                    selection.RootDirectory,
                    serverAddress,
                    loginPort,
                    storageRoot));
            _statusLabel.Text = result.Changed ? "PIN/XML 저장 완료" : "설정 변경 없음";
            AppendLog(
                $"{_statusLabel.Text}: {result.PinInfo.LoginEndpoint}, " +
                $"storage={result.PinInfo.StorageRoot}");
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        UseWaitCursor = busy;
        _launchButton.Enabled = !busy;
        _instanceCombo.Enabled = !busy;
        _pathTextBox.Enabled = !busy;
        _usernameTextBox.Enabled = !busy;
        _serverAddressTextBox.Enabled = !busy;
        _loginPortNumeric.Enabled = !busy;
        _storageRootTextBox.Enabled = !busy;
        _saveSettingsButton.Enabled = !busy;
        _browseFolderButton.Enabled = !busy;
        _browseExecutableButton.Enabled = !busy;
    }

    private void AppendLog(string message)
    {
        _logTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        _logTextBox.SelectionStart = _logTextBox.TextLength;
        _logTextBox.ScrollToCaret();
    }

    private void ShowError(Exception exception)
    {
        string message = exception.Message;
        AppendLog("오류: " + message);
        _statusLabel.Text = "오류";
        MessageBox.Show(this, message, "KartRider 2005 접속기", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
