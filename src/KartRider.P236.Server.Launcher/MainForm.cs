using System.ComponentModel;
using System.Drawing;
using KartRider.P236.ItemProbabilities;
using KartRider.P236.Server;

namespace KartRider.P236.Server.Launcher;

internal sealed partial class MainForm : Form
{
    private const int MaximumLogCharacters = 1_000_000;

    private readonly TextBox _bindAddressTextBox = new();
    private readonly TextBox _advertisedAddressTextBox = new();
    private readonly NumericUpDown _tcpPortNumeric = new();
    private readonly NumericUpDown _udpPortNumeric = new();
    private readonly TextBox _serverDataDirectoryTextBox = new();
    private readonly TextBox _logDirectoryTextBox = new();
    private readonly CheckBox _packetTraceCheckBox = new();
    private readonly Button _browseServerDataButton = new();
    private readonly Button _browseLogDirectoryButton = new();
    private readonly Button _saveServerSettingsButton = new();
    private readonly Button _startServerButton = new();
    private readonly Button _stopServerButton = new();
    private readonly Label _serverStatusLabel = new();

    private readonly TextBox _clientDataDirectoryTextBox = new();
    private readonly TextBox _itemConfigurationPathTextBox = new();
    private readonly Button _browseClientDataButton = new();
    private readonly Button _importProbabilitiesButton = new();
    private readonly Button _loadProbabilitiesButton = new();
    private readonly Button _saveProbabilitiesButton = new();
    private readonly Button _saveProbabilitiesAsButton = new();
    private readonly Button _applyProbabilitiesButton = new();
    private readonly Label _probabilityStatusLabel = new();

    private readonly DataGridView _individualGrid = new();
    private readonly DataGridView _teamGrid = new();
    private readonly DataGridView _flagGrid = new();
    private readonly DataGridView _individualBonusGrid = new();
    private readonly DataGridView _teamBonusGrid = new();
    private readonly BindingSource _individualSource = new();
    private readonly BindingSource _teamSource = new();
    private readonly BindingSource _flagSource = new();
    private readonly BindingSource _individualBonusSource = new();
    private readonly BindingSource _teamBonusSource = new();
    private readonly RichTextBox _logTextBox = new();

    private readonly TextWriter _originalOut;
    private readonly TextWriter _originalError;
    private readonly UiLogTextWriter _uiOut;
    private readonly UiLogTextWriter _uiError;

    private ServerLauncherSettings _settings = new();
    private P236Server? _server;
    private bool _serverBusy;
    private bool _probabilityBusy;
    private bool _hasProbabilityConfiguration;
    private bool _shutdownStarted;
    private bool _allowClose;
    private Task _activeServerOperation = Task.CompletedTask;
    private Task _activeProbabilityOperation = Task.CompletedTask;

    internal MainForm()
    {
        Text = "KartRider P236 서버 런처";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 700);
        ClientSize = new Size(1080, 820);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("맑은 고딕", 9F, FontStyle.Regular, GraphicsUnit.Point);

        BuildLayout();
        LoadLauncherSettings();

        _originalOut = Console.Out;
        _originalError = Console.Error;
        _uiOut = new UiLogTextWriter(AppendLog);
        _uiError = new UiLogTextWriter(AppendLog, "[오류] ");
        Console.SetOut(new TeeTextWriter(_originalOut, _uiOut));
        Console.SetError(new TeeTextWriter(_originalError, _uiError));

        Shown += MainForm_Shown;
    }

    private void BuildLayout()
    {
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 1,
            RowCount = 5,
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 72F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 28F));

        Label title = new()
        {
            Text = "KartRider 2005-12-14 (P236) 서버 런처",
            AutoSize = true,
            Font = new Font(Font.FontFamily, 16F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 4),
        };
        Label description = new()
        {
            Text = "서버 실행 설정과 클라이언트 아이템 확률 파일을 한 곳에서 관리합니다.",
            AutoSize = true,
            ForeColor = Color.DimGray,
            Margin = new Padding(0, 0, 0, 12),
        };
        TabControl sections = new()
        {
            Dock = DockStyle.Fill,
        };
        sections.TabPages.Add(BuildServerPage());
        sections.TabPages.Add(BuildProbabilityPage());

        Label logLabel = new()
        {
            Text = "실행 로그",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 10, 0, 5),
        };
        _logTextBox.Dock = DockStyle.Fill;
        _logTextBox.ReadOnly = true;
        _logTextBox.BackColor = Color.FromArgb(248, 248, 248);
        _logTextBox.BorderStyle = BorderStyle.FixedSingle;
        _logTextBox.Font = new Font("Consolas", 9F);
        _logTextBox.DetectUrls = false;

        root.Controls.Add(title, 0, 0);
        root.Controls.Add(description, 0, 1);
        root.Controls.Add(sections, 0, 2);
        root.Controls.Add(logLabel, 0, 3);
        root.Controls.Add(_logTextBox, 0, 4);
        Controls.Add(root);
    }

    private void LoadLauncherSettings()
    {
        try
        {
            if (File.Exists(ServerLauncherSettingsStore.SettingsPath))
            {
                _settings = ServerLauncherSettingsStore.Load();
                AppendLog($"서버 런처 설정을 불러왔습니다: {ServerLauncherSettingsStore.SettingsPath}");
            }
            else
            {
                _settings = new ServerLauncherSettings();
                AppendLog("저장된 서버 런처 설정이 없어 기본값을 사용합니다.");
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _settings = new ServerLauncherSettings();
            AppendLog($"서버 런처 설정을 읽지 못해 기본값을 사용합니다: {exception.Message}");
            MessageBox.Show(
                this,
                exception.Message,
                "서버 런처 설정 읽기 실패",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        _bindAddressTextBox.Text = _settings.BindAddress;
        _advertisedAddressTextBox.Text = _settings.AdvertisedAddress;
        _tcpPortNumeric.Value = _settings.TcpPort;
        _udpPortNumeric.Value = _settings.UdpPort;
        _serverDataDirectoryTextBox.Text = _settings.ServerDataDirectory;
        _logDirectoryTextBox.Text = _settings.LogDirectory;
        _packetTraceCheckBox.Checked = _settings.EnablePacketTrace;
        _clientDataDirectoryTextBox.Text = _settings.ClientDataDirectory;
        _itemConfigurationPathTextBox.Text = _settings.ItemProbabilityConfigurationPath;
        UpdateServerControls();
        UpdateProbabilityControls();
    }

    private async void MainForm_Shown(object? sender, EventArgs e)
    {
        string path = _itemConfigurationPathTextBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            await RunProbabilityOperationAsync(
                "저장된 아이템 확률 JSON 불러오기",
                async () =>
                {
                    ItemProbabilityConfiguration configuration =
                        await Task.Run(() => ItemProbabilityConfigurationStore.Load(path));
                    SetProbabilityConfiguration(configuration);
                    _probabilityStatusLabel.Text = "JSON 불러옴";
                    AppendLog($"아이템 확률 JSON을 불러왔습니다: {path}");
                });
        }
        else
        {
            _probabilityStatusLabel.Text = "클라이언트 Data에서 가져오거나 JSON을 불러오세요.";
        }
    }

    private static Control CreateFieldRow(string labelText, Control control)
    {
        TableLayoutPanel row = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            Margin = new Padding(0, 4, 0, 4),
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        row.Controls.Add(new Label
        {
            Text = labelText,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 3, 8, 3),
        }, 0, 0);
        row.Controls.Add(control, 1, 0);
        return row;
    }

    private static TableLayoutPanel CreatePathControls(TextBox textBox, Button browseButton)
    {
        textBox.Dock = DockStyle.Fill;
        browseButton.Text = "폴더 선택";
        browseButton.AutoSize = true;

        TableLayoutPanel controls = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
        };
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        controls.Controls.Add(textBox, 0, 0);
        controls.Controls.Add(browseButton, 1, 0);
        return controls;
    }

    private void AppendLog(string message)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(new Action<string>(AppendLog), message);
            }
            catch (InvalidOperationException)
            {
                // The native window can disappear while a server worker exits.
            }
            return;
        }

        if (_logTextBox.TextLength > MaximumLogCharacters)
        {
            _logTextBox.Select(0, _logTextBox.TextLength - MaximumLogCharacters / 2);
            _logTextBox.SelectedText = string.Empty;
        }

        _logTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        _logTextBox.SelectionStart = _logTextBox.TextLength;
        _logTextBox.ScrollToCaret();
    }

    private void ShowError(string title, Exception exception)
    {
        AppendLog($"{title}: {exception.Message}");
        MessageBox.Show(
            this,
            exception.Message,
            title,
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            if (!_shutdownStarted)
            {
                _shutdownStarted = true;
                Enabled = false;
                _ = ShutdownAndCloseAsync();
            }
        }

        base.OnFormClosing(e);
    }

    private async Task ShutdownAndCloseAsync()
    {
        try
        {
            await _activeServerOperation;
            await _activeProbabilityOperation;
            await StopServerCoreAsync();
            TrySaveSettings(showSuccess: false, showErrors: false);
        }
        catch (Exception exception)
        {
            AppendLog($"종료 처리 중 오류: {exception.Message}");
        }
        finally
        {
            Console.SetOut(_originalOut);
            Console.SetError(_originalError);
            _uiOut.Dispose();
            _uiError.Dispose();
            _allowClose = true;
            Enabled = true;
            Close();
        }
    }
}
