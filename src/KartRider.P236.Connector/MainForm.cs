using System.Drawing;
using KartRider.P236.ItemProbabilities;

namespace KartRider.P236.Connector;

internal sealed class MainForm : Form
{
    private readonly LauncherService _launcherService = new LauncherService();
    private readonly HashSet<string> _knownInstanceExecutables =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _l1HookPreferences =
        new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

    private readonly ComboBox _instanceCombo = new ComboBox();
    private readonly TextBox _pathTextBox = new TextBox();
    private readonly TextBox _usernameTextBox = new TextBox();
    private readonly TextBox _serverAddressTextBox = new TextBox();
    private readonly NumericUpDown _loginPortNumeric = new NumericUpDown();
    private readonly TextBox _storageRootTextBox = new TextBox();
    private readonly CheckBox _applyL1CompatibilityHooksCheckBox = new CheckBox();
    private readonly Button _prepareL1DataButton = new Button();
    private readonly Button _restoreL1DataButton = new Button();
    private readonly Button _browseFolderButton = new Button();
    private readonly Button _browseExecutableButton = new Button();
    private readonly Button _saveSettingsButton = new Button();
    private readonly Button _launchButton = new Button();
    private readonly Label _statusLabel = new Label();
    private readonly RichTextBox _logTextBox = new RichTextBox();
    private readonly TabControl _mainTabs = new TabControl();
    private readonly TabPage _launchTabPage = new TabPage();
    private readonly TabPage _l1PatchTabPage = new TabPage();
    private bool _suppressInstanceRemember;
    private bool _busy;

    internal MainForm(bool loadPreparedInstances = true)
    {
        Text = "KartRider 2005 접속기";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 640);
        ClientSize = new Size(840, 720);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("맑은 고딕", 9F, FontStyle.Regular, GraphicsUnit.Point);
        FormClosing += MainForm_FormClosing;

        BuildLayout();
        if (loadPreparedInstances)
        {
            LoadPreparedInstances();
        }
    }

    private void BuildLayout()
    {
        TableLayoutPanel root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 1,
            RowCount = 7
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 58F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 42F));

        Label title = new Label
        {
            Text = "KartRider 2005-12-14 전용 접속기",
            AutoSize = true,
            Font = new Font(Font.FontFamily, 16F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 4)
        };
        Label description = new Label
        {
            Text = "대상 클라이언트를 선택한 뒤 실행 설정 또는 L1 데이터 패치 탭을 사용하세요.",
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

        TableLayoutPanel launchLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 6
        };
        launchLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        launchLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        launchLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        launchLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        launchLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        launchLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        _usernameTextBox.Dock = DockStyle.Fill;
        _usernameTextBox.MaxLength = 32;
        launchLayout.Controls.Add(CreateFieldRow("username", _usernameTextBox), 0, 0);

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
        launchLayout.Controls.Add(CreateFieldRow("서버 IPv4", endpointControls), 0, 1);

        _storageRootTextBox.Dock = DockStyle.Fill;
        _storageRootTextBox.MaxLength = 120;
        _storageRootTextBox.PlaceholderText = "예: 카트라이더_236_client3";
        launchLayout.Controls.Add(CreateFieldRow("문서 저장 폴더", _storageRootTextBox), 0, 2);

        _applyL1CompatibilityHooksCheckBox.Name = "applyL1CompatibilityHooksCheckBox";
        _applyL1CompatibilityHooksCheckBox.Text = "실행 시 L1 호환 훅 적용 (0x41, 0x44, 0x45)";
        _applyL1CompatibilityHooksCheckBox.Checked = true;
        _applyL1CompatibilityHooksCheckBox.AutoSize = true;
        _applyL1CompatibilityHooksCheckBox.Margin = new Padding(0, 5, 0, 5);
        _applyL1CompatibilityHooksCheckBox.CheckedChanged += (_, _) =>
            RememberCurrentHookPreferenceInMemory();
        launchLayout.Controls.Add(
            CreateFieldRow("L1 미션", _applyL1CompatibilityHooksCheckBox),
            0,
            3);

        _prepareL1DataButton.Name = "l1PatchApplyButton";
        _prepareL1DataButton.Text = "L1 패치 적용…";
        _prepareL1DataButton.AutoSize = true;
        _prepareL1DataButton.Padding = new Padding(18, 7, 18, 7);
        _prepareL1DataButton.Font = new Font(Font, FontStyle.Bold);
        _prepareL1DataButton.Click += PrepareL1DataButton_Click;
        _restoreL1DataButton.Name = "l1PatchRestoreButton";
        _restoreL1DataButton.Text = "원본 복원";
        _restoreL1DataButton.AutoSize = true;
        _restoreL1DataButton.Padding = new Padding(12, 7, 12, 7);
        _restoreL1DataButton.Click += RestoreL1DataButton_Click;

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
        _launchButton.Name = "launchButton";
        _launchButton.Text = "설정 저장 후 실행";
        _launchButton.AutoSize = true;
        _launchButton.Padding = new Padding(22, 6, 22, 6);
        _launchButton.Font = new Font(Font, FontStyle.Bold);
        _launchButton.Click += LaunchButton_Click;
        actions.Controls.Add(_saveSettingsButton);
        actions.Controls.Add(_launchButton);
        launchLayout.Controls.Add(actions, 0, 4);

        TableLayoutPanel l1PatchLayout = new TableLayoutPanel
        {
            Name = "l1PatchLayout",
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 1,
            RowCount = 4
        };
        l1PatchLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        l1PatchLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        l1PatchLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        l1PatchLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        l1PatchLayout.Controls.Add(new Label
        {
            Text = "L1 라이센스 클라이언트 데이터 패치",
            AutoSize = true,
            Font = new Font(Font.FontFamily, 12F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 8)
        }, 0, 0);
        Label l1PatchDescription = new Label
        {
            Name = "l1PatchDescriptionLabel",
            Text =
                "위에서 선택한 P236 클라이언트에 L1 미션과 트랙 데이터를 생성합니다. " +
                "버튼을 누르면 지원 donor 클라이언트 폴더를 별도로 선택합니다.\n" +
                "원본 게임 데이터는 접속기에 포함되지 않으며, 최초 적용 상태는 복원용으로 보존됩니다.",
            AutoSize = true,
            MaximumSize = new Size(620, 0),
            ForeColor = Color.DimGray,
            Margin = new Padding(0, 0, 0, 14)
        };
        l1PatchLayout.SizeChanged += (_, _) =>
        {
            int availableWidth =
                l1PatchLayout.ClientSize.Width -
                l1PatchLayout.Padding.Horizontal -
                l1PatchDescription.Margin.Horizontal;
            if (availableWidth >= 200 && l1PatchDescription.MaximumSize.Width != availableWidth)
            {
                l1PatchDescription.MaximumSize = new Size(availableWidth, 0);
            }
        };
        l1PatchLayout.Controls.Add(l1PatchDescription, 0, 1);
        FlowLayoutPanel l1PatchActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        l1PatchActions.Controls.Add(_prepareL1DataButton);
        l1PatchActions.Controls.Add(_restoreL1DataButton);
        l1PatchLayout.Controls.Add(l1PatchActions, 0, 2);

        _launchTabPage.Name = "launchTabPage";
        _launchTabPage.Text = "접속 및 실행";
        _launchTabPage.UseVisualStyleBackColor = true;
        _launchTabPage.Controls.Add(launchLayout);
        _l1PatchTabPage.Name = "l1PatchTabPage";
        _l1PatchTabPage.Text = "L1 데이터 패치";
        _l1PatchTabPage.UseVisualStyleBackColor = true;
        _l1PatchTabPage.Controls.Add(l1PatchLayout);
        _mainTabs.Name = "mainTabs";
        _mainTabs.Dock = DockStyle.Fill;
        _mainTabs.Controls.Add(_launchTabPage);
        _mainTabs.Controls.Add(_l1PatchTabPage);
        _mainTabs.SelectedIndexChanged += (_, _) => RefreshDefaultButtonForSelectedTab();
        root.Controls.Add(_mainTabs, 0, 4);

        _statusLabel.Text = "대기 중";
        _statusLabel.AutoSize = true;
        _statusLabel.Margin = new Padding(0, 5, 0, 5);
        root.Controls.Add(CreateFieldRow("상태", _statusLabel), 0, 5);

        _logTextBox.Dock = DockStyle.Fill;
        _logTextBox.ReadOnly = true;
        _logTextBox.BackColor = Color.FromArgb(248, 248, 248);
        _logTextBox.BorderStyle = BorderStyle.FixedSingle;
        _logTextBox.Font = new Font("Consolas", 9F);
        _logTextBox.DetectUrls = false;
        root.Controls.Add(_logTextBox, 0, 6);

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
        ClientInstanceDiscoveryResult discovery =
            ClientInstanceDiscovery.DiscoverPreparedInstances();
        foreach (string warning in discovery.Warnings)
        {
            AppendLog($"인스턴스 검색 경고: {warning}");
        }

        foreach (ClientInstanceOption instance in discovery.Instances)
        {
            _instanceCombo.Items.Add(instance);
            _knownInstanceExecutables.Add(instance.ExecutablePath);
            _l1HookPreferences[instance.RootDirectory] = instance.ApplyL1CompatibilityHooks;
        }

        if (_instanceCombo.Items.Count > 0)
        {
            int preferredIndex = -1;
            if (!string.IsNullOrWhiteSpace(discovery.PreferredExecutablePath))
            {
                for (int index = 0; index < _instanceCombo.Items.Count; index++)
                {
                    if (_instanceCombo.Items[index] is ClientInstanceOption option &&
                        string.Equals(
                            option.ExecutablePath,
                            discovery.PreferredExecutablePath,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        preferredIndex = index;
                        break;
                    }
                }
            }
            _suppressInstanceRemember = true;
            try
            {
                _instanceCombo.SelectedIndex = preferredIndex >= 0 ? preferredIndex : 0;
            }
            finally
            {
                _suppressInstanceRemember = false;
            }
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
        _applyL1CompatibilityHooksCheckBox.Checked =
            _l1HookPreferences.TryGetValue(instance.RootDirectory, out bool applyHooks)
                ? applyHooks
                : instance.ApplyL1CompatibilityHooks;
        _knownInstanceExecutables.Add(instance.ExecutablePath);
        LoadPinSettings(instance.RootDirectory);
        _statusLabel.Text = instance.Name;
        if (!_suppressInstanceRemember)
        {
            RememberInstanceForNextRun(
                instance.RootDirectory,
                instance.Name,
                logSuccess: false);
        }
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
            _ = OriginalClientValidator.ValidateConfigurableClient(selection);
            _knownInstanceExecutables.Add(selection.ExecutablePath);
            LoadSettingsFromCurrentPath();
            string displayName = Path.GetFileName(
                selection.RootDirectory.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = "KartRider";
            }
            RememberInstanceForNextRun(selection.RootDirectory, displayName);

            int matchingIndex = -1;
            for (int index = 0; index < _instanceCombo.Items.Count; index++)
            {
                if (_instanceCombo.Items[index] is ClientInstanceOption option &&
                    string.Equals(
                        option.ExecutablePath,
                        selection.ExecutablePath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    matchingIndex = index;
                    break;
                }
            }
            if (matchingIndex < 0)
            {
                _applyL1CompatibilityHooksCheckBox.Checked = true;
                _l1HookPreferences[selection.RootDirectory] = true;
                matchingIndex = _instanceCombo.Items.Add(new ClientInstanceOption(
                    displayName,
                    selection.RootDirectory,
                    selection.ExecutablePath,
                    LegacyAccountProfile.TryReadUsername(selection.RootDirectory),
                    ApplyL1CompatibilityHooks: true));
            }
            else if (_instanceCombo.Items[matchingIndex] is ClientInstanceOption existing)
            {
                _applyL1CompatibilityHooksCheckBox.Checked =
                    _l1HookPreferences.TryGetValue(existing.RootDirectory, out bool applyHooks)
                        ? applyHooks
                        : existing.ApplyL1CompatibilityHooks;
            }

            _suppressInstanceRemember = true;
            try
            {
                _instanceCombo.SelectedIndex = matchingIndex;
            }
            finally
            {
                _suppressInstanceRemember = false;
            }
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
            LoadHookPreference(selection.RootDirectory);
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
                $"server={_serverAddressTextBox.Text}:{loginPort}, storage={storageRoot}, " +
                $"l1Hook={(_applyL1CompatibilityHooksCheckBox.Checked ? "on" : "off")}");

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
                _applyL1CompatibilityHooksCheckBox.Checked,
                _knownInstanceExecutables.ToArray(),
                progress);

            _knownInstanceExecutables.Add(selection.ExecutablePath);
            RememberInstanceForNextRun(
                selection.RootDirectory,
                applyL1CompatibilityHooks: _applyL1CompatibilityHooksCheckBox.Checked);
            UpdateRememberedHookPreference(
                selection.RootDirectory,
                _applyL1CompatibilityHooksCheckBox.Checked);
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
            RememberInstanceForNextRun(
                selection.RootDirectory,
                applyL1CompatibilityHooks: _applyL1CompatibilityHooksCheckBox.Checked);
            UpdateRememberedHookPreference(
                selection.RootDirectory,
                _applyL1CompatibilityHooksCheckBox.Checked);
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

    private async void PrepareL1DataButton_Click(object? sender, EventArgs e)
    {
        try
        {
            ClientSelection selection = ClientSelection.FromPath(_pathTextBox.Text);
            _ = OriginalClientValidator.ValidateConfigurableClient(selection);
            using FolderBrowserDialog dialog = new FolderBrowserDialog
            {
                Description =
                    "L1 데이터가 들어 있는 donor 클라이언트 루트 또는 Data 폴더를 선택하세요. " +
                    "RHO 자체는 접속기에 포함되지 않으며 선택한 로컬 파일에서 생성합니다.",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            DialogResult confirmation = MessageBox.Show(
                this,
                "선택한 donor RHO에서 필요한 트랙·텍스처·L1 정보를 읽어 현재 P236 Data를 생성합니다.\n\n" +
                $"donor: {dialog.SelectedPath}\n" +
                $"target: {selection.RootDirectory}\n\n" +
                "원본 12개 RHO와 신규 트랙 존재 여부는 영구 백업에 기록됩니다. 계속할까요?",
                "L1 클라이언트 데이터 패치",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Information);
            if (confirmation != DialogResult.OK)
            {
                return;
            }

            SetBusy(true);
            AppendLog(
                $"L1 패치 요청: donor={dialog.SelectedPath}, target={selection.RootDirectory}");
            Progress<string> progress = new Progress<string>(message =>
            {
                _statusLabel.Text = message;
                AppendLog(message);
            });
            P236L1DataPatchResult result = await Task.Run(() =>
                P236L1DataPatcher.Apply(
                    dialog.SelectedPath,
                    selection.RootDirectory,
                    progress));
            _statusLabel.Text = result.Changed
                ? $"L1 패치 완료 ({result.ChangedFileCount}개 파일)"
                : "L1 패치 변경 없음";
            AppendLog(
                $"{_statusLabel.Text}: RHO {result.GeneratedArchiveCount}개, " +
                $"신규 트랙 {result.AddedTrackArchiveCount}개, backup={result.BackupDirectory ?? "없음"}");
            MessageBox.Show(
                this,
                result.Changed
                    ? "L1 호환 클라이언트 데이터 생성과 검증을 완료했습니다."
                    : "선택한 클라이언트 데이터가 이미 같은 상태입니다.",
                "KartRider 2005 접속기",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
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

    private async void RestoreL1DataButton_Click(object? sender, EventArgs e)
    {
        try
        {
            ClientSelection selection = ClientSelection.FromPath(_pathTextBox.Text);
            DialogResult confirmation = MessageBox.Show(
                this,
                "접속기가 관리하는 L1 데이터 설치 기록을 검증한 뒤 원본 RHO 상태로 복원합니다.\n\n" +
                $"target: {selection.RootDirectory}\n\n계속할까요?",
                "L1 클라이언트 데이터 복원",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning);
            if (confirmation != DialogResult.OK)
            {
                return;
            }

            SetBusy(true);
            AppendLog($"L1 데이터 원본 복원 요청: {selection.RootDirectory}");
            Progress<string> progress = new Progress<string>(message =>
            {
                _statusLabel.Text = message;
                AppendLog(message);
            });
            P236L1DataPatchResult result = await Task.Run(() =>
                P236L1DataPatcher.Restore(selection.RootDirectory, progress));
            _statusLabel.Text = $"L1 데이터 원본 복원 완료 ({result.ChangedFileCount}개 파일)";
            AppendLog($"{_statusLabel.Text}: backup={result.BackupDirectory}");
            MessageBox.Show(
                this,
                "관리되는 원본 데이터로 복원했습니다.",
                "KartRider 2005 접속기",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
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

    private void RememberInstanceForNextRun(
        string rootDirectory,
        string? displayName = null,
        bool logSuccess = true,
        bool? applyL1CompatibilityHooks = null)
    {
        try
        {
            string catalogPath = ClientInstanceDiscovery.RememberInstance(
                rootDirectory,
                displayName,
                applyL1CompatibilityHooks);
            if (logSuccess)
            {
                AppendLog($"휴대형 인스턴스 목록 저장: {catalogPath}");
            }
        }
        catch (Exception exception)
        {
            // Catalog persistence is a convenience. A successful settings
            // update or game launch must not be turned into a failure here.
            AppendLog($"인스턴스 목록 저장 경고: {exception.Message}");
        }
    }

    private void LoadHookPreference(string rootDirectory)
    {
        if (_l1HookPreferences.TryGetValue(rootDirectory, out bool applyHooks))
        {
            _applyL1CompatibilityHooksCheckBox.Checked = applyHooks;
            return;
        }

        foreach (object item in _instanceCombo.Items)
        {
            if (item is ClientInstanceOption option &&
                string.Equals(
                    option.RootDirectory,
                    rootDirectory,
                    StringComparison.OrdinalIgnoreCase))
            {
                _applyL1CompatibilityHooksCheckBox.Checked = option.ApplyL1CompatibilityHooks;
                return;
            }
        }

        _applyL1CompatibilityHooksCheckBox.Checked = true;
    }

    private void UpdateRememberedHookPreference(string rootDirectory, bool enabled)
    {
        _l1HookPreferences[rootDirectory] = enabled;
    }

    private void RememberCurrentHookPreferenceInMemory()
    {
        try
        {
            ClientSelection selection = ClientSelection.FromPath(_pathTextBox.Text);
            _l1HookPreferences[selection.RootDirectory] =
                _applyL1CompatibilityHooksCheckBox.Checked;
        }
        catch
        {
            // An incomplete path may be edited before an instance is selected.
        }
    }

    internal void RefreshDefaultButtonForSelectedTab()
    {
        AcceptButton = _mainTabs.SelectedTab == _launchTabPage ? _launchButton : null;
    }

    internal void SetBusy(bool busy)
    {
        _busy = busy;
        UseWaitCursor = busy;
        _mainTabs.Enabled = !busy;
        _launchButton.Enabled = !busy;
        _instanceCombo.Enabled = !busy;
        _pathTextBox.Enabled = !busy;
        _usernameTextBox.Enabled = !busy;
        _serverAddressTextBox.Enabled = !busy;
        _loginPortNumeric.Enabled = !busy;
        _storageRootTextBox.Enabled = !busy;
        _applyL1CompatibilityHooksCheckBox.Enabled = !busy;
        _prepareL1DataButton.Enabled = !busy;
        _restoreL1DataButton.Enabled = !busy;
        _saveSettingsButton.Enabled = !busy;
        _browseFolderButton.Enabled = !busy;
        _browseExecutableButton.Enabled = !busy;
    }

    internal void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!ShouldCancelClose(e.CloseReason))
        {
            return;
        }

        e.Cancel = true;
        _statusLabel.Text = "작업 중에는 접속기를 종료할 수 없습니다.";
        AppendLog("진행 중인 작업이 끝날 때까지 종료 요청을 보류했습니다.");
    }

    internal bool ShouldCancelClose(CloseReason reason) =>
        _busy && reason == CloseReason.UserClosing;

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
