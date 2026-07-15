using System.ComponentModel;
using System.Drawing;
using KartRider.P236.ItemProbabilities;

namespace KartRider.P236.Server.Launcher;

internal sealed partial class MainForm
{
    private TabPage BuildProbabilityPage()
    {
        TabPage page = new("아이템 확률");
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 1,
            RowCount = 7,
        };
        for (int index = 0; index < 5; index++)
        {
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _clientDataDirectoryTextBox.PlaceholderText = "클라이언트 설치 폴더 안의 Data 폴더";
        _browseClientDataButton.Click += BrowseClientDataButton_Click;
        _itemConfigurationPathTextBox.Dock = DockStyle.Fill;
        _itemConfigurationPathTextBox.PlaceholderText = "item-probabilities.json 경로";

        FlowLayoutPanel actions = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(150, 7, 0, 5),
        };
        ConfigureActionButton(_importProbabilitiesButton, "Data에서 가져오기", ImportProbabilitiesButton_Click);
        ConfigureActionButton(_loadProbabilitiesButton, "JSON 불러오기...", LoadProbabilitiesButton_Click);
        ConfigureActionButton(_saveProbabilitiesButton, "JSON 저장", SaveProbabilitiesButton_Click);
        ConfigureActionButton(_saveProbabilitiesAsButton, "다른 이름으로 저장...", SaveProbabilitiesAsButton_Click);
        ConfigureActionButton(_applyProbabilitiesButton, "선택한 Data에 적용", ApplyProbabilitiesButton_Click);
        _applyProbabilitiesButton.Font = new Font(Font, FontStyle.Bold);
        actions.Controls.Add(_importProbabilitiesButton);
        actions.Controls.Add(_loadProbabilitiesButton);
        actions.Controls.Add(_saveProbabilitiesButton);
        actions.Controls.Add(_saveProbabilitiesAsButton);
        actions.Controls.Add(_applyProbabilitiesButton);

        Label applyWarning = new()
        {
            Text = "주의: 멀티플레이에 참가하는 모든 클라이언트는 동일한 확률 설정이 적용된 Data를 사용해야 합니다. " +
                   "적용 전 게임을 종료하고, 적용 후 모든 클라이언트를 다시 시작하세요.",
            AutoSize = true,
            ForeColor = Color.DarkRed,
            BackColor = Color.FromArgb(255, 245, 230),
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(8),
            MaximumSize = new Size(900, 0),
            Margin = new Padding(150, 4, 0, 6),
        };
        Label bonusWarning = new()
        {
            Text = "개인/팀 보너스 표는 P236 클라이언트 자료에 존재하지만 활성 조건은 정적 분석만으로 확인되지 않았습니다. " +
                   "값은 보존·편집할 수 있으나 실제 선택 조건은 별도 검증이 필요합니다.",
            AutoSize = true,
            ForeColor = Color.DarkGoldenrod,
            Margin = new Padding(150, 2, 0, 6),
            MaximumSize = new Size(900, 0),
        };

        TabControl tables = new()
        {
            Dock = DockStyle.Fill,
        };
        tables.TabPages.Add(CreateRankTablePage("개인전", _individualGrid, _individualSource));
        tables.TabPages.Add(CreateRankTablePage("팀전", _teamGrid, _teamSource));
        tables.TabPages.Add(CreateRankTablePage("플래그전", _flagGrid, _flagSource));
        tables.TabPages.Add(CreateBonusTablePage(
            "개인 보너스(조건 미확인)",
            _individualBonusGrid,
            _individualBonusSource));
        tables.TabPages.Add(CreateBonusTablePage(
            "팀 보너스(조건 미확인)",
            _teamBonusGrid,
            _teamBonusSource));

        _probabilityStatusLabel.Text = "확률 설정이 아직 없습니다.";
        _probabilityStatusLabel.AutoSize = true;
        _probabilityStatusLabel.ForeColor = Color.DimGray;
        _probabilityStatusLabel.Margin = new Padding(150, 6, 0, 0);

        root.Controls.Add(CreateFieldRow(
            "클라이언트 Data",
            CreatePathControls(_clientDataDirectoryTextBox, _browseClientDataButton)), 0, 0);
        root.Controls.Add(CreateFieldRow("확률 JSON", _itemConfigurationPathTextBox), 0, 1);
        root.Controls.Add(actions, 0, 2);
        root.Controls.Add(applyWarning, 0, 3);
        root.Controls.Add(bonusWarning, 0, 4);
        root.Controls.Add(tables, 0, 5);
        root.Controls.Add(_probabilityStatusLabel, 0, 6);
        page.Controls.Add(root);
        return page;
    }

    private static void ConfigureActionButton(
        Button button,
        string text,
        EventHandler clickHandler)
    {
        button.Text = text;
        button.AutoSize = true;
        button.Padding = new Padding(8, 4, 8, 4);
        button.Click += clickHandler;
    }

    private TabPage CreateRankTablePage(
        string title,
        DataGridView grid,
        BindingSource source)
    {
        ConfigureGrid(grid);
        grid.Columns.Add(CreateTextColumn("Name", "Name", "이름", 180, readOnly: true));
        grid.Columns.Add(CreateTextColumn("ItemId", "ItemId", "아이템 ID", 90, readOnly: true));
        grid.Columns.Add(CreateWeightColumn("HighRank", "HighRank", "상위권 (highrank)"));
        grid.Columns.Add(CreateWeightColumn("MidRank", "MidRank", "중위권 (midrank)"));
        grid.Columns.Add(CreateWeightColumn("LowRank", "LowRank", "하위권 (lowrank)"));
        grid.DataSource = source;
        return new TabPage(title)
        {
            Controls = { grid },
        };
    }

    private TabPage CreateBonusTablePage(
        string title,
        DataGridView grid,
        BindingSource source)
    {
        ConfigureGrid(grid);
        grid.Columns.Add(CreateTextColumn("Name", "Name", "이름", 220, readOnly: true));
        grid.Columns.Add(CreateTextColumn("ItemId", "ItemId", "아이템 ID", 100, readOnly: true));
        grid.Columns.Add(CreateWeightColumn("Weight", "Weight", "가중치 (prob)"));
        grid.DataSource = source;
        return new TabPage(title)
        {
            Controls = { grid },
        };
    }

    private void ConfigureGrid(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill;
        grid.AutoGenerateColumns = false;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToOrderColumns = false;
        grid.AllowUserToResizeRows = false;
        grid.RowHeadersVisible = false;
        grid.MultiSelect = false;
        grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.BackgroundColor = Color.White;
        grid.BorderStyle = BorderStyle.FixedSingle;
        grid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
        grid.CellValidating += ProbabilityGrid_CellValidating;
        grid.CellEndEdit += (_, eventArgs) => grid.Rows[eventArgs.RowIndex].ErrorText = string.Empty;
        grid.DataError += ProbabilityGrid_DataError;
    }

    private static DataGridViewTextBoxColumn CreateTextColumn(
        string name,
        string propertyName,
        string headerText,
        int minimumWidth,
        bool readOnly)
    {
        return new DataGridViewTextBoxColumn
        {
            Name = name,
            DataPropertyName = propertyName,
            HeaderText = headerText,
            MinimumWidth = minimumWidth,
            ReadOnly = readOnly,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        };
    }

    private static DataGridViewTextBoxColumn CreateWeightColumn(
        string name,
        string propertyName,
        string headerText)
    {
        return new DataGridViewTextBoxColumn
        {
            Name = name,
            DataPropertyName = propertyName,
            HeaderText = headerText,
            MinimumWidth = 110,
            ReadOnly = false,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            ValueType = typeof(int),
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Alignment = DataGridViewContentAlignment.MiddleRight,
                Format = "N0",
                NullValue = 0,
            },
        };
    }

    private void ProbabilityGrid_CellValidating(
        object? sender,
        DataGridViewCellValidatingEventArgs e)
    {
        if (sender is not DataGridView grid ||
            grid.Columns[e.ColumnIndex].DataPropertyName is not (
                "HighRank" or "MidRank" or "LowRank" or "Weight"))
        {
            return;
        }

        string value = Convert.ToString(e.FormattedValue)?.Replace(",", string.Empty).Trim()
            ?? string.Empty;
        if (!int.TryParse(value, out int weight) ||
            weight is < ItemProbabilityEntry.MinimumWeight or > ItemProbabilityEntry.MaximumWeight)
        {
            e.Cancel = true;
            grid.Rows[e.RowIndex].ErrorText =
                $"가중치는 {ItemProbabilityEntry.MinimumWeight:N0}~" +
                $"{ItemProbabilityEntry.MaximumWeight:N0} 범위의 정수여야 합니다.";
        }
    }

    private void ProbabilityGrid_DataError(object? sender, DataGridViewDataErrorEventArgs e)
    {
        e.ThrowException = false;
        _probabilityStatusLabel.Text = "가중치 셀에는 0~1,000,000 범위의 정수를 입력하세요.";
        _probabilityStatusLabel.ForeColor = Color.DarkRed;
    }

    private void BrowseClientDataButton_Click(object? sender, EventArgs e)
    {
        BrowseForDirectory(
            _clientDataDirectoryTextBox,
            "P236 클라이언트의 item.rho와 aaa.pk가 있는 Data 폴더를 선택하세요.");
    }

    private async void ImportProbabilitiesButton_Click(object? sender, EventArgs e)
    {
        string dataDirectory;
        try
        {
            dataDirectory = GetClientDataDirectory();
        }
        catch (Exception exception)
        {
            ShowError("클라이언트 Data 폴더 오류", exception);
            return;
        }

        await RunProbabilityOperationAsync(
            "클라이언트 Data 확률 가져오기",
            async () =>
            {
                ItemProbabilityConfiguration configuration = await Task.Run(
                    () => P236ItemProbabilityArchive.Import(dataDirectory));
                SetProbabilityConfiguration(configuration);
                _probabilityStatusLabel.Text = "클라이언트 Data에서 가져옴";
                AppendLog($"아이템 확률을 가져왔습니다: {dataDirectory}");
                TrySaveSettings(showSuccess: false, showErrors: false);
            });
    }

    private async void LoadProbabilitiesButton_Click(object? sender, EventArgs e)
    {
        using OpenFileDialog dialog = new()
        {
            Title = "아이템 확률 JSON 불러오기",
            Filter = "아이템 확률 JSON (item-probabilities.json)|item-probabilities.json|JSON 파일 (*.json)|*.json|모든 파일 (*.*)|*.*",
            FileName = "item-probabilities.json",
            CheckFileExists = true,
            Multiselect = false,
            InitialDirectory = GetExistingParentDirectory(_itemConfigurationPathTextBox.Text),
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        string path = Path.GetFullPath(dialog.FileName);
        await LoadProbabilityConfigurationFromPathAsync(path);
    }

    private async Task LoadProbabilityConfigurationFromPathAsync(string path)
    {
        await RunProbabilityOperationAsync(
            "아이템 확률 JSON 불러오기",
            async () =>
            {
                ItemProbabilityConfiguration configuration =
                    await Task.Run(() => ItemProbabilityConfigurationStore.Load(path));
                SetProbabilityConfiguration(configuration);
                _itemConfigurationPathTextBox.Text = path;
                _probabilityStatusLabel.Text = "JSON 불러옴";
                AppendLog($"아이템 확률 JSON을 불러왔습니다: {path}");
                TrySaveSettings(showSuccess: false, showErrors: false);
            });
    }

    private async void SaveProbabilitiesButton_Click(object? sender, EventArgs e)
    {
        string path;
        try
        {
            path = NormalizeRequiredPath(
                _itemConfigurationPathTextBox.Text,
                "아이템 확률 JSON");
        }
        catch (Exception exception)
        {
            ShowError("아이템 확률 JSON 경로 오류", exception);
            return;
        }

        await SaveProbabilityConfigurationToPathAsync(path);
    }

    private async void SaveProbabilitiesAsButton_Click(object? sender, EventArgs e)
    {
        using SaveFileDialog dialog = new()
        {
            Title = "아이템 확률 JSON 저장",
            Filter = "JSON 파일 (*.json)|*.json|모든 파일 (*.*)|*.*",
            FileName = "item-probabilities.json",
            AddExtension = true,
            DefaultExt = "json",
            OverwritePrompt = true,
            InitialDirectory = GetExistingParentDirectory(_itemConfigurationPathTextBox.Text),
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        await SaveProbabilityConfigurationToPathAsync(Path.GetFullPath(dialog.FileName));
    }

    private async Task SaveProbabilityConfigurationToPathAsync(string path)
    {
        ItemProbabilityConfiguration configuration;
        try
        {
            configuration = CaptureProbabilityConfiguration();
        }
        catch (Exception exception)
        {
            ShowError("아이템 확률 설정 오류", exception);
            return;
        }

        await RunProbabilityOperationAsync(
            "아이템 확률 JSON 저장",
            async () =>
            {
                await Task.Run(() => ItemProbabilityConfigurationStore.Save(path, configuration));
                _itemConfigurationPathTextBox.Text = path;
                _probabilityStatusLabel.Text = "JSON 저장됨";
                AppendLog($"아이템 확률 JSON을 원자적으로 저장했습니다: {path}");
                TrySaveSettings(showSuccess: false, showErrors: false);
            });
    }

    private async void ApplyProbabilitiesButton_Click(object? sender, EventArgs e)
    {
        string dataDirectory;
        ItemProbabilityConfiguration configuration;
        try
        {
            dataDirectory = GetClientDataDirectory();
            configuration = CaptureProbabilityConfiguration();
        }
        catch (Exception exception)
        {
            ShowError("아이템 확률 적용 준비 실패", exception);
            return;
        }

        DialogResult confirmation = MessageBox.Show(
            this,
            "선택한 클라이언트 Data의 item.rho와 aaa.pk에 현재 확률을 적용합니다.\n\n" +
            "- 실행 중인 KartRider 클라이언트를 모두 종료하세요.\n" +
            "- 멀티플레이에 참가하는 모든 클라이언트에 동일한 설정을 적용하세요.\n" +
            "- 적용 후 모든 클라이언트를 다시 시작하세요.\n\n" +
            $"대상: {dataDirectory}\n\n계속하시겠습니까?",
            "클라이언트 Data에 아이템 확률 적용",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (confirmation != DialogResult.Yes)
        {
            return;
        }

        await RunProbabilityOperationAsync(
            "클라이언트 Data에 아이템 확률 적용",
            async () =>
            {
                ItemProbabilityApplyResult result = await Task.Run(
                    () => P236ItemProbabilityArchive.Apply(dataDirectory, configuration));
                _probabilityStatusLabel.Text = result.Changed
                    ? "클라이언트 Data 적용 완료 — 모든 클라이언트 재시작 필요"
                    : "클라이언트 Data가 이미 같은 설정임";
                AppendLog(result.Changed
                    ? $"아이템 확률을 클라이언트 Data에 적용했습니다: {dataDirectory}"
                    : $"클라이언트 Data가 이미 현재 아이템 확률과 같습니다: {dataDirectory}");
                if (result.RecoveredInterruptedApply)
                {
                    AppendLog("이전의 중단된 아이템 확률 적용 transaction을 먼저 복구했습니다.");
                }
                if (result.Changed)
                {
                    AppendLog(
                        $"원본 1회 백업: {result.ItemBackupPath}; {result.MetadataBackupPath}");
                }
                AppendLog("멀티플레이 참가 클라이언트 전체에 같은 설정을 적용하고 모두 다시 시작하세요.");
                TrySaveSettings(showSuccess: false, showErrors: false);
            });
    }

    private string GetClientDataDirectory()
    {
        string directory = NormalizeRequiredPath(
            _clientDataDirectoryTextBox.Text,
            "클라이언트 Data 폴더");
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"클라이언트 Data 폴더가 없습니다: {directory}");
        }
        if (!File.Exists(Path.Combine(directory, "item.rho")) ||
            !File.Exists(Path.Combine(directory, "aaa.pk")))
        {
            throw new InvalidDataException(
                "선택한 폴더에 P236 item.rho와 aaa.pk가 모두 있어야 합니다.");
        }

        _clientDataDirectoryTextBox.Text = directory;
        return directory;
    }

    private static string GetExistingParentDirectory(string path)
    {
        try
        {
            string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (directory is not null && Directory.Exists(directory))
            {
                return directory;
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // The dialog will fall back to its normal initial folder.
        }

        return AppContext.BaseDirectory;
    }

    private void SetProbabilityConfiguration(ItemProbabilityConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.Validate();
        _individualSource.DataSource = new BindingList<ItemProbabilityEntry>(
            configuration.Individual.Select(CloneEntry).ToList());
        _teamSource.DataSource = new BindingList<ItemProbabilityEntry>(
            configuration.Team.Select(CloneEntry).ToList());
        _flagSource.DataSource = new BindingList<ItemProbabilityEntry>(
            configuration.Flag.Select(CloneEntry).ToList());
        _individualBonusSource.DataSource = new BindingList<ItemProbabilityBonusEntry>(
            configuration.IndividualBonus.Select(CloneBonusEntry).ToList());
        _teamBonusSource.DataSource = new BindingList<ItemProbabilityBonusEntry>(
            configuration.TeamBonus.Select(CloneBonusEntry).ToList());
        _hasProbabilityConfiguration = true;
        _probabilityStatusLabel.ForeColor = SystemColors.ControlText;
        UpdateProbabilityControls();
    }

    private ItemProbabilityConfiguration CaptureProbabilityConfiguration()
    {
        if (!_hasProbabilityConfiguration)
        {
            throw new InvalidOperationException(
                "먼저 클라이언트 Data에서 확률을 가져오거나 JSON을 불러오세요.");
        }

        CommitGridEdits(_individualGrid, _individualSource);
        CommitGridEdits(_teamGrid, _teamSource);
        CommitGridEdits(_flagGrid, _flagSource);
        CommitGridEdits(_individualBonusGrid, _individualBonusSource);
        CommitGridEdits(_teamBonusGrid, _teamBonusSource);

        ItemProbabilityConfiguration configuration = new()
        {
            Individual = GetBoundEntries<ItemProbabilityEntry>(_individualSource)
                .Select(CloneEntry)
                .ToList(),
            Team = GetBoundEntries<ItemProbabilityEntry>(_teamSource)
                .Select(CloneEntry)
                .ToList(),
            Flag = GetBoundEntries<ItemProbabilityEntry>(_flagSource)
                .Select(CloneEntry)
                .ToList(),
            IndividualBonus = GetBoundEntries<ItemProbabilityBonusEntry>(_individualBonusSource)
                .Select(CloneBonusEntry)
                .ToList(),
            TeamBonus = GetBoundEntries<ItemProbabilityBonusEntry>(_teamBonusSource)
                .Select(CloneBonusEntry)
                .ToList(),
        };
        configuration.Validate();
        return configuration;
    }

    private static void CommitGridEdits(DataGridView grid, BindingSource source)
    {
        if (!grid.EndEdit())
        {
            throw new InvalidDataException("편집 중인 확률 셀 값을 확정할 수 없습니다.");
        }
        source.EndEdit();
    }

    private static IEnumerable<T> GetBoundEntries<T>(BindingSource source)
    {
        return source.List.Cast<T>();
    }

    private static ItemProbabilityEntry CloneEntry(ItemProbabilityEntry entry)
    {
        return new ItemProbabilityEntry
        {
            ItemId = entry.ItemId,
            Name = entry.Name,
            HighRank = entry.HighRank,
            MidRank = entry.MidRank,
            LowRank = entry.LowRank,
        };
    }

    private static ItemProbabilityBonusEntry CloneBonusEntry(ItemProbabilityBonusEntry entry)
    {
        return new ItemProbabilityBonusEntry
        {
            ItemId = entry.ItemId,
            Name = entry.Name,
            Weight = entry.Weight,
        };
    }

    private async Task RunProbabilityOperationAsync(
        string operationName,
        Func<Task> operation)
    {
        if (_probabilityBusy || _shutdownStarted)
        {
            return;
        }

        _probabilityBusy = true;
        _probabilityStatusLabel.Text = $"{operationName} 중...";
        _probabilityStatusLabel.ForeColor = Color.DarkBlue;
        UpdateProbabilityControls();

        Task activeOperation = RunProbabilityOperationCoreAsync(operationName, operation);
        _activeProbabilityOperation = activeOperation;
        await activeOperation;
    }

    private async Task RunProbabilityOperationCoreAsync(
        string operationName,
        Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (Exception exception)
        {
            _probabilityStatusLabel.Text = $"{operationName} 실패";
            _probabilityStatusLabel.ForeColor = Color.DarkRed;
            ShowError($"{operationName} 실패", exception);
        }
        finally
        {
            _probabilityBusy = false;
            UpdateProbabilityControls();
        }
    }

    private void UpdateProbabilityControls()
    {
        bool canOperate = !_probabilityBusy && !_shutdownStarted;
        _clientDataDirectoryTextBox.Enabled = canOperate;
        _itemConfigurationPathTextBox.Enabled = canOperate;
        _browseClientDataButton.Enabled = canOperate;
        _importProbabilitiesButton.Enabled = canOperate;
        _loadProbabilitiesButton.Enabled = canOperate;
        _saveProbabilitiesButton.Enabled = canOperate && _hasProbabilityConfiguration;
        _saveProbabilitiesAsButton.Enabled = canOperate && _hasProbabilityConfiguration;
        _applyProbabilitiesButton.Enabled = canOperate && _hasProbabilityConfiguration;
        _individualGrid.ReadOnly = !canOperate;
        _teamGrid.ReadOnly = !canOperate;
        _flagGrid.ReadOnly = !canOperate;
        _individualBonusGrid.ReadOnly = !canOperate;
        _teamBonusGrid.ReadOnly = !canOperate;
    }
}
