using System.Globalization;
using System.IO;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Rome2Explorer.Domain;
using Rome2Explorer.Features;
using Rome2Explorer.Memory;
using Rome2Explorer.Trace;

namespace Rome2Explorer.App;

public partial class MainWindow : Window
{
    private readonly MemoryMapExporter _exporter = new();
    private readonly CandidateScanExporter _candidateExporter = new();
    private readonly TreasuryDiscriminatorExporter _discriminatorExporter = new();
    private readonly TreasuryWriteExporter _treasuryWriteExporter = new();
    private readonly TreasuryPointerAnalysisExporter _treasuryPointerAnalysisExporter = new();
    private readonly TreasuryRestartValidationExporter _treasuryRestartValidationExporter = new();
    private readonly TreasuryStructureCaptureImporter _structureCaptureImporter = new();
    private readonly KnownValueScanner _treasuryScanner = new();
    private readonly TreasuryCandidateDiscriminator _treasuryDiscriminator = new();
    private readonly TreasuryMoneyWriter _treasuryMoneyWriter = new();
    private readonly TreasuryPointerAnalyzer _treasuryPointerAnalyzer = new();
    private readonly TreasuryRestartValidationService _treasuryRestartValidationService = new();
    private readonly Algorithme _algorithme = new();
    private readonly Rome2ProcessDiscovery _processDiscovery = new();
    private readonly SavedFindingStore _savedFindingStore = new();
    private ProcessMemoryReader? _reader;
    private MemoryMapSnapshot? _snapshot;
    private KnownValueScanSession? _treasurySession;
    private CancellationTokenSource? _scanCancellation;
    private CancellationTokenSource? _discriminatorCancellation;
    private CancellationTokenSource? _writeCancellation;
    private CancellationTokenSource? _pointerAnalysisCancellation;
    private bool _isWaitingForMoneyChangeConfirmation;
    private bool _isWritingTreasuryMoney;
    private bool _isAnalyzingTreasuryPointers;
    private bool _isValidatingTreasuryRestart;
    private IReadOnlyList<SavedMemoryFinding> _savedFindings = Array.Empty<SavedMemoryFinding>();
    private readonly ObservableCollection<DiscriminatorLogRow> _discriminatorLogRows = new();
    private readonly List<TreasuryDiscriminatorLogEntry> _discriminatorWorkflowLogEntries = new();
    private IReadOnlyDictionary<string, TreasuryDiscriminatorCandidateResult> _lastDiscriminatorCandidates =
        new Dictionary<string, TreasuryDiscriminatorCandidateResult>(StringComparer.Ordinal);
    private TreasuryDiscriminatorResult? _lastDiscriminatorResult;
    private readonly Dictionary<string, TreasuryPointerAnalysisResult> _lastPointerAnalysisCandidates = new(StringComparer.Ordinal);
    private TreasuryPointerAnalysisResult? _lastPointerAnalysisResult;
    private string? _lastPointerAnalysisExportPath;
    private IReadOnlyDictionary<string, TreasuryValidationCandidateResult> _lastTreasuryValidationCandidates =
        new Dictionary<string, TreasuryValidationCandidateResult>(StringComparer.Ordinal);
    private TreasuryRestartValidationResult? _lastTreasuryValidationResult;
    private string? _lastTreasuryValidationBundleDirectory;
    private IReadOnlyDictionary<string, StructureComparisonResult> _lastStructureComparisonCandidates =
        new Dictionary<string, StructureComparisonResult>(StringComparer.Ordinal);
    private StructureComparisonReport? _lastStructureComparisonReport;
    private string? _lastStructureComparisonBundleDirectory;

    public MainWindow()
    {
        InitializeComponent();
        DiscriminatorLogGrid.ItemsSource = _discriminatorLogRows;
        BindTreasuryGuidance(null);
        BindDiscriminatorResult(null);
        BindPointerAnalysisResult(null, null);
        BindTreasuryRestartValidationResult(null, null);
        BindStructureComparisonResult(null, null);
        LoadSavedFindings();
        RefreshRome2Processes(updateStatus: false);
        SetStatus("NotAttached", isError: false);
    }

    private void RefreshProcessesButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshRome2Processes(updateStatus: true);
    }

    private void AttachButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Rome2ProcessComboBox.SelectedItem is not Rome2ProcessRow selectedProcess)
            {
                throw new InvalidOperationException("Aucun processus Rome2 selectionne. Clique sur Refresh Rome2 puis choisis le bon PID.");
            }

            SetStatus("Attach en cours...", isError: false);
            _reader?.Dispose();
            _reader = new ProcessMemoryReader();
            _reader.Attach(selectedProcess.ProcessId);
            _snapshot = _reader.CaptureMemoryMap();

            BindSnapshot(_snapshot);
            _treasurySession = null;
            ResetDiscriminatorState();
            ResetPointerAnalysisState();
            ResetTreasuryRestartValidationState();
            ResetStructureComparisonState();
            TreasuryCandidatesGrid.ItemsSource = null;
            TreasuryCandidateDetailsText.Text = "Selectionne un candidat pour voir adresse, offset, architecture, hook, region et preuves.";
            SaveTreasuryFindingButton.IsEnabled = false;
            ResetTreasuryWriteState("Pret : lance un scan treasury, selectionne un candidat, puis confirme avant d'ecrire.");
            TreasurySummaryText.Text = "Aucun scan treasury lance.";
            BindTreasuryGuidance(null);
            ExportButton.IsEnabled = true;
            UpdateTreasuryButtons(isBusy: false);
            SetStatus($"Attached to Rome2 PID {selectedProcess.ProcessId}", isError: false);
        }
        catch (Exception ex)
        {
            ExportButton.IsEnabled = false;
            _snapshot = null;
            ModulesGrid.ItemsSource = null;
            RegionsGrid.ItemsSource = null;
            TreasuryCandidatesGrid.ItemsSource = null;
            _treasurySession = null;
            ResetDiscriminatorState();
            ResetPointerAnalysisState();
            ResetTreasuryRestartValidationState();
            ResetStructureComparisonState();
            ProcessInfoText.Text = "Process: aucun processus attache.";
            TreasurySummaryText.Text = "Aucun scan treasury lance.";
            TreasuryCandidateDetailsText.Text = "Selectionne un candidat pour voir adresse, offset, architecture, hook, region et preuves.";
            SaveTreasuryFindingButton.IsEnabled = false;
            ResetTreasuryWriteState("Pret : aucun processus attache.");
            BindTreasuryGuidance(null);
            UpdateTreasuryButtons(isBusy: false);
            SetStatus($"Error: {ex.Message}", isError: true);
        }
    }

    private void TreasuryCandidatesGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (TreasuryCandidatesGrid.SelectedItem is not TreasuryCandidateRow row)
        {
            TreasuryCandidateDetailsText.Text = "Selectionne un candidat pour voir adresse, offset, architecture, hook, region et preuves.";
            SaveTreasuryFindingButton.IsEnabled = false;
            UpdateTreasuryWritePreview();
            UpdateTreasuryButtons(isBusy: false);
            return;
        }

        TreasuryCandidateDetailsText.Text = row.Details;
        SaveTreasuryFindingButton.IsEnabled = _snapshot is not null && _treasurySession is not null;
        UpdateTreasuryWritePreview();
        UpdateTreasuryButtons(isBusy: false);
    }

    private void SaveTreasuryFindingButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_snapshot is null || _treasurySession is null)
            {
                throw new InvalidOperationException("Aucun scan actif a conserver.");
            }

            if (TreasuryCandidatesGrid.SelectedItem is not TreasuryCandidateRow row)
            {
                throw new InvalidOperationException("Selectionne un candidat avant de le garder en memoire.");
            }

            var finding = BuildSavedFinding(row, _snapshot, _treasurySession);
            var root = DonjHackPathResolver.ResolveRoot();
            var path = _savedFindingStore.Upsert(finding, root);
            LoadSavedFindings();

            SetStatus($"Candidate kept: {row.Address} -> {path}", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}", isError: true);
        }
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_reader is null)
            {
                throw new InvalidOperationException("Aucun processus attache. Clique d'abord sur Attach Rome2.");
            }

            SetStatus("Export en cours...", isError: false);
            _snapshot = _reader.CaptureMemoryMap();
            BindSnapshot(_snapshot);

            var root = DonjHackPathResolver.ResolveRoot();
            var exportPath = _exporter.Export(_snapshot, root);
            SetStatus($"Exported: {exportPath}", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}", isError: true);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _scanCancellation?.Cancel();
        _discriminatorCancellation?.Cancel();
        _writeCancellation?.Cancel();
        _pointerAnalysisCancellation?.Cancel();
        _reader?.Dispose();
        base.OnClosed(e);
    }

    private async void StartTreasuryScanButton_Click(object sender, RoutedEventArgs e)
    {
        await RunTreasuryScanAsync(isRefine: false);
    }

    private async void RefineTreasuryScanButton_Click(object sender, RoutedEventArgs e)
    {
        await RunTreasuryScanAsync(isRefine: true);
    }

    private void CancelTreasuryScanButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isWaitingForMoneyChangeConfirmation)
        {
            _isWaitingForMoneyChangeConfirmation = false;
            AppendDiscriminatorLog(
                new TreasuryDiscriminatorLogEntry(DateTimeOffset.Now, "Cancel", null, null, "Attente utilisateur annulee avant observation."),
                rememberForExport: true);
            BindDiscriminatorResult(_lastDiscriminatorResult, "Attente departage annulee.");
            UpdateTreasuryButtons(isBusy: false);
            SetStatus("Attente departage annulee.", isError: false);
            return;
        }

        _scanCancellation?.Cancel();
        _discriminatorCancellation?.Cancel();
        _writeCancellation?.Cancel();
        _pointerAnalysisCancellation?.Cancel();
    }

    private void ExportTreasuryCandidatesButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_snapshot is null || _treasurySession is null)
            {
                throw new InvalidOperationException("Aucun scan treasury a exporter.");
            }

            var root = DonjHackPathResolver.ResolveRoot();
            var exportPath = _candidateExporter.ExportTreasuryCandidates(
                _snapshot.Process,
                _snapshot.Summary,
                _treasurySession,
                root);

            SetStatus($"Treasury candidates exported: {exportPath}", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}", isError: true);
        }
    }

    private void ImportCeCaptureButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_treasurySession is null)
            {
                throw new InvalidOperationException("Lance un scan treasury avant d'importer une capture CE/Lua.");
            }

            var root = DonjHackPathResolver.ResolveRoot();
            var captureDirectory = DonjHackPathResolver.ResolveLuaCapturesDirectory(root);
            Directory.CreateDirectory(captureDirectory);
            var dialog = new OpenFileDialog
            {
                Title = "Importer une ou plusieurs captures CE/Lua",
                Filter = "Captures CE/Lua (*.json)|*.json|Tous les fichiers (*.*)|*.*",
                InitialDirectory = captureDirectory,
                Multiselect = true,
                CheckFileExists = true
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            var bundle = _structureCaptureImporter.ImportAndCompareTreasury(_treasurySession, dialog.FileNames, root);
            _lastStructureComparisonReport = bundle.Report;
            _lastStructureComparisonBundleDirectory = bundle.BundleDirectory;
            _lastStructureComparisonCandidates = bundle.Report.Results
                .GroupBy(result => result.CandidateId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

            BindStructureComparisonResult(bundle.Report, bundle.BundleDirectory);
            BindTreasurySession(_treasurySession);
            SetStatus($"Import CE/Lua termine: {bundle.Report.OverallVerdict}", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}", isError: true);
        }
        finally
        {
            UpdateTreasuryButtons(isBusy: false);
        }
    }

    private void DiscriminateTreasuryCandidatesButton_Click(object sender, RoutedEventArgs e)
    {
        BeginTreasuryDiscriminatorWait();
    }

    private async void MoneyChangeDoneButton_Click(object sender, RoutedEventArgs e)
    {
        await RunTreasuryDiscriminatorObservationAsync();
    }

    private async void WriteTreasuryMoneyButton_Click(object sender, RoutedEventArgs e)
    {
        await RunTreasuryWriteAsync();
    }

    private async void AnalyzeTreasuryPointersButton_Click(object sender, RoutedEventArgs e)
    {
        await RunTreasuryPointerAnalysisAsync();
    }

    private void ExportTreasuryPointersButton_Click(object sender, RoutedEventArgs e)
    {
        ExportLastTreasuryPointerAnalysis(updateStatus: true);
    }

    private async void ExportTreasuryValidationButton_Click(object sender, RoutedEventArgs e)
    {
        await RunTreasuryRestartValidationAsync("session-baseline", requirePreviousReferences: false);
    }

    private async void CompareTreasuryValidationButton_Click(object sender, RoutedEventArgs e)
    {
        await RunTreasuryRestartValidationAsync("reload-or-restart-compare", requirePreviousReferences: true);
    }

    private void ConfirmTreasuryWriteCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
    {
        UpdateTreasuryWritePreview();
        UpdateTreasuryButtons(isBusy: false);
    }

    private void BeginTreasuryDiscriminatorWait()
    {
        if (_reader is null || _snapshot is null)
        {
            SetStatus("Error: aucun processus attache. Clique d'abord sur Attach Rome2.", isError: true);
            return;
        }

        if (!TreasuryCandidateDiscriminator.CanRun(_treasurySession, TreasuryDiscriminatorOptions.Default.MaxCandidates))
        {
            SetStatus("Error: le departage demande 2 a 10 candidats apres au moins un refine.", isError: true);
            return;
        }

        _discriminatorLogRows.Clear();
        _discriminatorWorkflowLogEntries.Clear();
        _lastDiscriminatorResult = null;
        _lastDiscriminatorCandidates = new Dictionary<string, TreasuryDiscriminatorCandidateResult>(StringComparer.Ordinal);
        _isWaitingForMoneyChangeConfirmation = true;

        AppendDiscriminatorLog(
            new TreasuryDiscriminatorLogEntry(
                DateTimeOffset.Now,
                "WaitUser",
                null,
                null,
                "Retourne dans Rome2, change l'argent maintenant, reviens ici puis clique C'est fait. L'observation ne commence pas avant ce clic."),
            rememberForExport: true);

        BindDiscriminatorResult(null, "Changez l'argent en jeu maintenant. Reviens ensuite dans DonjHACK et clique C'est fait. L'observation read-only ne commence pas avant ce clic.");
        UpdateTreasuryButtons(isBusy: false);
        SetStatus("Attente utilisateur: change l'argent dans Rome2 puis clique C'est fait.", isError: false);
    }

    private async Task RunTreasuryDiscriminatorObservationAsync()
    {
        if (!_isWaitingForMoneyChangeConfirmation)
        {
            SetStatus("Error: clique d'abord sur Departager candidats.", isError: true);
            return;
        }

        if (_reader is null || _snapshot is null || _treasurySession is null)
        {
            SetStatus("Error: contexte departage perdu. Relance Attach puis le scan.", isError: true);
            return;
        }

        if (!TreasuryCandidateDiscriminator.CanRun(_treasurySession, TreasuryDiscriminatorOptions.Default.MaxCandidates))
        {
            SetStatus("Error: le departage demande 2 a 10 candidats apres au moins un refine.", isError: true);
            return;
        }

        _discriminatorCancellation?.Dispose();
        _discriminatorCancellation = new CancellationTokenSource();
        var token = _discriminatorCancellation.Token;

        try
        {
            _isWaitingForMoneyChangeConfirmation = false;
            AppendDiscriminatorLog(
                new TreasuryDiscriminatorLogEntry(DateTimeOffset.Now, "UserConfirmed", null, null, "Utilisateur confirme que l'argent a ete change en jeu. Observation read-only lancee."),
                rememberForExport: true);
            BindDiscriminatorResult(null, "Observation en cours : ne change plus l'argent pendant la lecture, attends le verdict.");
            UpdateTreasuryButtons(isBusy: true);
            SetStatus("Departage treasury en cours...", isError: false);

            var progress = new Progress<TreasuryDiscriminatorLogEntry>(entry =>
            {
                AppendDiscriminatorLog(entry, rememberForExport: false);
            });

            var result = await Task.Run(() =>
                _treasuryDiscriminator.RunAsync(
                    _treasurySession!,
                    _treasurySession!.Candidates,
                    _reader,
                    _snapshot,
                    TreasuryDiscriminatorOptions.Default,
                    progress,
                    token),
                token);

            var resultWithWorkflowLog = result with
            {
                Log = _discriminatorWorkflowLogEntries.Concat(result.Log).ToArray()
            };

            _lastDiscriminatorResult = resultWithWorkflowLog;
            _lastDiscriminatorCandidates = resultWithWorkflowLog.Candidates.ToDictionary(candidate => candidate.CandidateId, StringComparer.Ordinal);
            BindDiscriminatorResult(resultWithWorkflowLog);
            BindTreasurySession(_treasurySession!);

            var root = DonjHackPathResolver.ResolveRoot();
            var exportPath = _discriminatorExporter.Export(
                _snapshot.Process,
                _snapshot.Summary,
                _treasurySession!,
                resultWithWorkflowLog,
                root);

            DiscriminatorExportPathText.Text = $"Export auto: {exportPath}";
            SetStatus($"Departage termine: {resultWithWorkflowLog.OverallVerdict}", isError: false);
        }
        catch (OperationCanceledException)
        {
            _isWaitingForMoneyChangeConfirmation = false;
            BindDiscriminatorResult(_lastDiscriminatorResult, "Departage annule.");
            SetStatus("Departage treasury annule.", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}", isError: true);
        }
        finally
        {
            _isWaitingForMoneyChangeConfirmation = false;
            _discriminatorCancellation?.Dispose();
            _discriminatorCancellation = null;
            UpdateTreasuryButtons(isBusy: false);
        }
    }

    private async Task RunTreasuryWriteAsync()
    {
        if (_reader is null || _snapshot is null || _treasurySession is null)
        {
            SetStatus("Error: attache Rome2 et lance un scan treasury avant d'ecrire.", isError: true);
            return;
        }

        if (ConfirmTreasuryWriteCheckBox.IsChecked != true)
        {
            SetStatus("Error: coche la confirmation avant d'ecrire sur le candidat selectionne.", isError: true);
            return;
        }

        if (!TryParseNewTreasuryValue(out var desiredValue))
        {
            SetStatus("Error: nouvelle valeur d'argent invalide. Entre un entier Int32, par exemple 150000.", isError: true);
            return;
        }

        var selectedCandidate = GetSelectedTreasuryCandidate();
        if (selectedCandidate is null)
        {
            SetStatus("Error: selectionne une ligne candidate avant de modifier l'argent.", isError: true);
            return;
        }

        _writeCancellation?.Dispose();
        _writeCancellation = new CancellationTokenSource();
        var token = _writeCancellation.Token;

        try
        {
            _isWritingTreasuryMoney = true;
            UpdateTreasuryButtons(isBusy: true);
            TreasuryWriteStatusText.Text = "Ecriture en cours : verification avant ecriture, WriteProcessMemory 4 octets, puis relecture.";
            SetStatus("Modification treasury en cours...", isError: false);

            var result = await _treasuryMoneyWriter.WriteSelectedCandidateAsync(
                _treasurySession,
                selectedCandidate,
                _reader,
                _reader,
                desiredValue,
                cancellationToken: token);

            var exportStatus = ExportTreasuryWriteResult(result);
            TreasuryWriteStatusText.Text = BuildTreasuryWriteStatusText(result, exportStatus);

            if (result.Success)
            {
                TreasuryValueTextBox.Text = desiredValue.ToString(CultureInfo.InvariantCulture);
                ConfirmTreasuryWriteCheckBox.IsChecked = false;
            }

            SetStatus($"{result.Status}: {result.Message}", isError: !result.Success);
        }
        catch (OperationCanceledException)
        {
            TreasuryWriteStatusText.Text = "Ecriture annulee avant la fin de la verification.";
            SetStatus("Modification treasury annulee.", isError: false);
        }
        catch (Exception ex)
        {
            TreasuryWriteStatusText.Text = $"Erreur ecriture : {ex.Message}";
            SetStatus($"Error: {ex.Message}", isError: true);
        }
        finally
        {
            _isWritingTreasuryMoney = false;
            _writeCancellation?.Dispose();
            _writeCancellation = null;
            UpdateTreasuryButtons(isBusy: false);
        }
    }

    private async Task RunTreasuryPointerAnalysisAsync()
    {
        if (_reader is null || _snapshot is null || _treasurySession is null)
        {
            SetStatus("Error: attache Rome2 et lance un scan treasury avant d'analyser les pointeurs.", isError: true);
            return;
        }

        var selectedCandidate = GetSelectedTreasuryCandidate();
        if (selectedCandidate is null)
        {
            SetStatus("Error: selectionne une ligne candidate avant d'analyser les pointeurs.", isError: true);
            return;
        }

        _pointerAnalysisCancellation?.Dispose();
        _pointerAnalysisCancellation = new CancellationTokenSource();
        var token = _pointerAnalysisCancellation.Token;

        try
        {
            _isAnalyzingTreasuryPointers = true;
            UpdateTreasuryButtons(isBusy: true);
            BindPointerAnalysisResult(null, $"Analyse read-only en cours pour 0x{selectedCandidate.Address:X} : scan pointeurs x86 borne, profondeur max 2.");
            SetStatus("Analyse structure/pointeurs treasury en cours...", isError: false);

            var snapshot = await Task.Run(() => _reader.CaptureMemoryMap(), token);
            _snapshot = snapshot;
            BindSnapshot(snapshot);

            var result = await Task.Run(() =>
                _treasuryPointerAnalyzer.Analyze(
                    _treasurySession,
                    selectedCandidate,
                    _reader,
                    snapshot,
                    TreasuryPointerAnalysisOptions.Default,
                    token),
                token);

            _lastPointerAnalysisResult = result;
            _lastPointerAnalysisExportPath = null;
            _lastPointerAnalysisCandidates[result.CandidateId] = result;
            BindPointerAnalysisResult(result, null);
            BindTreasurySession(_treasurySession);
            SetStatus($"Analyse pointeurs terminee: {result.OverallVerdict}", isError: false);
        }
        catch (OperationCanceledException)
        {
            BindPointerAnalysisResult(_lastPointerAnalysisResult, "Analyse structure/pointeurs annulee.");
            SetStatus("Analyse structure/pointeurs annulee.", isError: false);
        }
        catch (Exception ex)
        {
            BindPointerAnalysisResult(_lastPointerAnalysisResult, $"Erreur analyse pointeurs : {ex.Message}");
            SetStatus($"Error: {ex.Message}", isError: true);
        }
        finally
        {
            _isAnalyzingTreasuryPointers = false;
            _pointerAnalysisCancellation?.Dispose();
            _pointerAnalysisCancellation = null;
            UpdateTreasuryButtons(isBusy: false);
        }
    }

    private async Task RunTreasuryRestartValidationAsync(string scenario, bool requirePreviousReferences)
    {
        if (_reader is null || _snapshot is null || _treasurySession is null)
        {
            SetStatus("Error: attache Rome2 et lance un scan treasury avant la validation reload.", isError: true);
            return;
        }

        var selectedCandidate = GetSelectedTreasuryCandidate();
        if (selectedCandidate is null)
        {
            SetStatus("Error: selectionne une ligne candidate avant la validation reload.", isError: true);
            return;
        }

        try
        {
            _isValidatingTreasuryRestart = true;
            UpdateTreasuryButtons(isBusy: true);
            BindTreasuryRestartValidationResult(
                _lastTreasuryValidationResult,
                requirePreviousReferences
                    ? "Comparaison reload en cours : je recharge les anciennes preuves, puis je compare avec le candidat courant."
                    : "Export session A en cours : je cree une preuve de depart pour comparer apres reload/redemarrage.");
            SetStatus("Validation treasury reload en cours...", isError: false);

            var root = DonjHackPathResolver.ResolveRoot();
            var snapshot = await Task.Run(() => _reader.CaptureMemoryMap());
            _snapshot = snapshot;
            BindSnapshot(snapshot);

            var pointerForSelected = _lastPointerAnalysisResult is not null
                && string.Equals(_lastPointerAnalysisResult.CandidateId, selectedCandidate.CandidateId, StringComparison.Ordinal)
                    ? _lastPointerAnalysisResult
                    : null;
            var loadResult = await Task.Run(() => _treasuryRestartValidationExporter.LoadReferences(root));

            if (requirePreviousReferences && loadResult.References.Count == 0)
            {
                SetStatus("Validation: aucune preuve precedente trouvee. Clique d'abord Export validation A avant reload.", isError: true);
            }

            var result = await Task.Run(() =>
                _treasuryRestartValidationService.Validate(
                    _treasurySession,
                    selectedCandidate,
                    snapshot,
                    loadResult.References,
                    pointerForSelected,
                    scenario));
            if (loadResult.Warnings.Count > 0)
            {
                result = result with
                {
                    Warnings = result.Warnings.Concat(loadResult.Warnings.Take(5)).Distinct(StringComparer.Ordinal).ToArray()
                };
            }

            var bundle = _treasuryRestartValidationExporter.Export(result, root);
            _lastTreasuryValidationResult = bundle.Result;
            _lastTreasuryValidationBundleDirectory = bundle.BundleDirectory;
            _lastTreasuryValidationCandidates = bundle.Result.RankedCandidates.ToDictionary(candidate => candidate.CandidateId, StringComparer.Ordinal);
            BindTreasuryRestartValidationResult(bundle.Result, null);
            BindTreasurySession(_treasurySession);
            SetStatus($"Validation treasury: {bundle.Result.OverallStatus}", isError: bundle.Result.OverallStatus is TreasuryRestartValidationStatuses.Broken);
        }
        catch (Exception ex)
        {
            BindTreasuryRestartValidationResult(_lastTreasuryValidationResult, $"Erreur validation reload : {ex.Message}");
            SetStatus($"Error: {ex.Message}", isError: true);
        }
        finally
        {
            _isValidatingTreasuryRestart = false;
            UpdateTreasuryButtons(isBusy: false);
        }
    }

    private async Task RunTreasuryScanAsync(bool isRefine)
    {
        if (_reader is null)
        {
            SetStatus("Error: aucun processus attache. Clique d'abord sur Attach Rome2.", isError: true);
            return;
        }

        if (!TryParseTreasuryValue(out var treasuryValue))
        {
            SetStatus("Error: valeur d'argent invalide. Entre un entier Int32, par exemple 4500.", isError: true);
            return;
        }

        if (isRefine && _treasurySession is null)
        {
            SetStatus("Error: lance d'abord Start Scan avant Refine Scan.", isError: true);
            return;
        }

        if (isRefine && _treasurySession?.CurrentValue == treasuryValue)
        {
            var message = "Valeur identique : le refine ne departage rien. Change l'argent en jeu ou clique Departager candidats.";
            BindDiscriminatorResult(_lastDiscriminatorResult, message);
            SetStatus(message, isError: true);
            return;
        }

        _scanCancellation?.Dispose();
        _scanCancellation = new CancellationTokenSource();
        var token = _scanCancellation.Token;

        try
        {
            UpdateTreasuryButtons(isBusy: true);
            SetStatus(isRefine ? "Treasury refine en cours..." : "Treasury scan en cours...", isError: false);

            if (!isRefine)
            {
                _snapshot = _reader.CaptureMemoryMap();
            }

            var snapshot = _snapshot ?? throw new InvalidOperationException("Memory map absente. Relance Attach Rome2.");
            var session = await Task.Run(() =>
            {
                // Le scan tourne hors thread UI : lire plusieurs centaines de Mo peut prendre du temps.
                // Cette etape de scan reste en lecture seule : je compare seulement des octets lus depuis les regions valides.
                return isRefine
                    ? _treasuryScanner.RefineExactInt32Scan(_treasurySession!, _reader, treasuryValue, token)
                    : _treasuryScanner.StartExactInt32Scan(snapshot, _reader, treasuryValue, cancellationToken: token);
            }, token);

            ResetDiscriminatorState();
            ResetPointerAnalysisState();
            ResetTreasuryRestartValidationState();
            ResetStructureComparisonState();
            _treasurySession = session;
            BindTreasurySession(session);
            SetStatus(isRefine ? "Treasury refine termine." : "Treasury scan termine.", isError: false);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Treasury scan annule.", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}", isError: true);
        }
        finally
        {
            _scanCancellation?.Dispose();
            _scanCancellation = null;
            UpdateTreasuryButtons(isBusy: false);
        }
    }

    private void BindSnapshot(MemoryMapSnapshot snapshot)
    {
        ProcessInfoText.Text =
            $"Process: {snapshot.Process.Name}.exe | PID {snapshot.Process.ProcessId} | Arch {snapshot.Process.Architecture} | " +
            $"Modules {snapshot.Summary.ModuleCount} | Regions {snapshot.Summary.RegionCount} | " +
            $"Readable 0x{snapshot.Summary.TotalReadableBytes:X} bytes | Path {snapshot.Process.Path ?? "unknown"}";

        ModulesGrid.ItemsSource = snapshot.Modules
            .Select(module => new ModuleRow(
                module.Name,
                $"0x{module.BaseAddress:X}",
                $"0x{module.Size:X}",
                module.Path ?? string.Empty))
            .ToArray();

        RegionsGrid.ItemsSource = snapshot.Regions
            .Select(region => new RegionRow(
                $"0x{region.BaseAddress:X}",
                $"0x{region.Size:X}",
                region.State,
                region.Protection,
                region.Type,
                region.IsReadable,
                region.IsWritable,
                region.IsExecutable))
            .ToArray();
    }

    private void RefreshRome2Processes(bool updateStatus)
    {
        try
        {
            var rows = _processDiscovery
                .FindRunningProcesses()
                .Select(candidate => new Rome2ProcessRow(
                    candidate.ProcessId,
                    BuildProcessDisplayName(candidate),
                    candidate.IsRecommended,
                    candidate.StartTime?.ToString("HH:mm:ss", CultureInfo.CurrentCulture) ?? "unknown",
                    candidate.Path ?? string.Empty,
                    candidate.MainWindowTitle ?? string.Empty,
                    candidate.RecommendationReason))
                .ToArray();

            Rome2ProcessComboBox.ItemsSource = rows;
            Rome2ProcessComboBox.SelectedItem = rows.FirstOrDefault(row => row.IsRecommended) ?? rows.FirstOrDefault();
            AttachButton.IsEnabled = rows.Length > 0;

            if (updateStatus)
            {
                SetStatus(
                    rows.Length == 0
                        ? "Aucun Rome2.exe detecte. Lance la campagne puis clique Refresh Rome2."
                        : $"{rows.Length} processus Rome2 detecte(s). Verifie le PID recommande avant Attach Selected.",
                    isError: rows.Length == 0);
            }
        }
        catch (Exception ex)
        {
            Rome2ProcessComboBox.ItemsSource = null;
            AttachButton.IsEnabled = false;
            SetStatus($"Error: detection Rome2 impossible: {ex.Message}", isError: true);
        }
    }

    private static string BuildProcessDisplayName(Rome2ProcessCandidate candidate)
    {
        var recommended = candidate.IsRecommended ? " | recommande" : string.Empty;
        var window = candidate.HasMainWindow ? "fenetre oui" : "fenetre non";
        var started = candidate.StartTime?.ToString("HH:mm:ss", CultureInfo.CurrentCulture) ?? "heure inconnue";
        var title = string.IsNullOrWhiteSpace(candidate.MainWindowTitle) ? "titre absent" : candidate.MainWindowTitle;

        return $"PID {candidate.ProcessId} | {started} | {window} | {title}{recommended}";
    }

    private void BindTreasurySession(KnownValueScanSession session)
    {
        const int maxDisplayedCandidates = 5000;
        var displayed = session.Candidates.Take(maxDisplayedCandidates).ToArray();
        var values = string.Join(" -> ", session.ValueHistory);
        var warningText = session.Warnings.Count == 0 ? "none" : string.Join(" | ", session.Warnings.Take(3));

        BindTreasuryGuidance(session);

        TreasurySummaryText.Text =
            $"Feature {session.FeatureId} | Type {session.ValueType} | Values {values} | " +
            $"Candidates {session.Counters.CandidatesAfter} | Displayed {displayed.Length} | " +
            $"Bytes 0x{session.Counters.BytesScanned:X} | ReadFailures {session.Counters.ReadFailures} | Warnings {warningText}";

        var scores = _algorithme
            .ScoreTreasuryCandidates(session, displayed, _snapshot, _savedFindings)
            .GroupBy(score => score.CandidateId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var rows = displayed
            .Select(candidate => BuildTreasuryCandidateRow(session, candidate, scores[candidate.CandidateId]))
            .ToArray();

        TreasuryCandidatesGrid.ItemsSource = rows;
        TreasuryCandidatesGrid.SelectedIndex = rows.Length > 0 ? 0 : -1;
        if (rows.Length == 0)
        {
            TreasuryCandidateDetailsText.Text = "Aucun candidat a analyser pour cette session.";
            SaveTreasuryFindingButton.IsEnabled = false;
        }

        ConfirmTreasuryWriteCheckBox.IsChecked = false;
        UpdateTreasuryWritePreview();
    }

    private void BindTreasuryGuidance(KnownValueScanSession? session)
    {
        // Je separe le guidage utilisateur du resume technique : le resume sert aux preuves,
        // alors que ce panneau explique quoi faire pour eliminer les faux positifs.
        var guidance = TreasuryScanGuidance.Build(session);
        TreasuryGuidanceStepText.Text = guidance.CurrentStep;
        TreasuryGuidanceActionText.Text = guidance.Action;
        TreasuryGuidanceWhyText.Text = guidance.Rationale;
        TreasuryGuidanceExpectedText.Text = guidance.ExpectedResult;
    }

    private bool TryParseTreasuryValue(out int value)
    {
        var raw = TreasuryValueTextBox.Text.Trim();
        return int.TryParse(raw, NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out value)
            || int.TryParse(raw, NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);
    }

    private bool TryParseNewTreasuryValue(out int value)
    {
        var raw = NewTreasuryValueTextBox.Text.Trim();
        return int.TryParse(raw, NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out value)
            || int.TryParse(raw, NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);
    }

    private Candidate? GetSelectedTreasuryCandidate()
    {
        if (_treasurySession is null || TreasuryCandidatesGrid.SelectedItem is not TreasuryCandidateRow row)
        {
            return null;
        }

        return _treasurySession.Candidates.FirstOrDefault(candidate =>
            string.Equals(candidate.CandidateId, row.CandidateId, StringComparison.Ordinal));
    }

    private void ResetTreasuryWriteState(string message)
    {
        ConfirmTreasuryWriteCheckBox.IsChecked = false;
        TreasuryWriteStatusText.Text = message;
    }

    private void UpdateTreasuryWritePreview()
    {
        if (_isWritingTreasuryMoney)
        {
            return;
        }

        if (_reader is null || _snapshot is null)
        {
            TreasuryWriteStatusText.Text = "Pret : attache Rome2 avant d'utiliser la modification argent.";
            return;
        }

        if (_treasurySession is null)
        {
            TreasuryWriteStatusText.Text = "Pret : lance Start Scan puis au moins un Refine avant d'ecrire.";
            return;
        }

        if (TreasuryCandidatesGrid.SelectedItem is not TreasuryCandidateRow row)
        {
            TreasuryWriteStatusText.Text = "Pret : selectionne un candidat dans la table avant d'ecrire.";
            return;
        }

        var selectedText = $"{row.Address} ({row.Status}, algo {row.AlgorithmScore}, departage {row.DiscriminatorVerdict})";
        if (_treasurySession.Candidates.Count > 1)
        {
            TreasuryWriteStatusText.Text =
                $"{TreasuryWriteStatuses.AmbiguousCandidate} : {_treasurySession.Candidates.Count} candidats restent. " +
                $"Je n'ecrirai que la ligne selectionnee {selectedText}. Si le jeu ecrase la valeur, teste l'autre candidat ou lance Analyser pointeurs.";
            return;
        }

        TreasuryWriteStatusText.Text =
            $"{TreasuryWriteStatuses.Ready} : candidat selectionne {selectedText}. " +
            "Coche la confirmation puis ecris la nouvelle valeur.";
    }

    private string ExportTreasuryWriteResult(TreasuryWriteResult result)
    {
        if (_snapshot is null || _treasurySession is null)
        {
            return "Export write ignore : contexte snapshot/session absent.";
        }

        try
        {
            var root = DonjHackPathResolver.ResolveRoot();
            return _treasuryWriteExporter.Export(
                _snapshot.Process,
                _snapshot.Summary,
                _treasurySession,
                result,
                root);
        }
        catch (Exception ex)
        {
            return $"Export write impossible : {ex.Message}";
        }
    }

    private string? ExportLastTreasuryPointerAnalysis(bool updateStatus)
    {
        if (_snapshot is null || _treasurySession is null || _lastPointerAnalysisResult is null)
        {
            if (updateStatus)
            {
                SetStatus("Error: aucune analyse structure/pointeurs a exporter.", isError: true);
            }

            return null;
        }

        try
        {
            var root = DonjHackPathResolver.ResolveRoot();
            var exportPath = _treasuryPointerAnalysisExporter.Export(
                _snapshot.Process,
                _snapshot.Summary,
                _treasurySession,
                _lastPointerAnalysisResult,
                root);

            _lastPointerAnalysisExportPath = exportPath;
            BindPointerAnalysisResult(_lastPointerAnalysisResult, null);
            if (updateStatus)
            {
                SetStatus($"Treasury structure exported: {exportPath}", isError: false);
            }

            return exportPath;
        }
        catch (Exception ex)
        {
            if (updateStatus)
            {
                SetStatus($"Error: export structure impossible: {ex.Message}", isError: true);
            }

            return null;
        }
    }

    private static string BuildTreasuryWriteStatusText(TreasuryWriteResult result, string exportStatus)
    {
        var values =
            $"Avant {FormatNullableInt(result.ValueBefore)} | " +
            $"Apres {FormatNullableInt(result.ValueAfterWrite)} | " +
            $"Stabilite {FormatNullableInt(result.ValueAfterStabilityDelay)}";
        var warnings = result.Warnings.Count == 0
            ? "Warnings none"
            : $"Warnings {string.Join(" | ", result.Warnings.Take(4))}";

        return
            $"{result.Status}: {result.Message}\n" +
            $"Adresse {result.AddressHex ?? "n/a"} | Candidats {result.CandidateCount} | Demande {result.DesiredValue} | {values}\n" +
            $"{warnings}\n" +
            $"Export: {exportStatus}";
    }

    private static string FormatNullableInt(int? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture) ?? "n/a";
    }

    private void UpdateTreasuryButtons(bool isBusy)
    {
        var busy = isBusy || _isWritingTreasuryMoney || _isAnalyzingTreasuryPointers || _isValidatingTreasuryRestart;
        var isAttached = _reader is not null && _snapshot is not null;
        var isWaiting = _isWaitingForMoneyChangeConfirmation;
        var hasSelectedCandidate = TreasuryCandidatesGrid.SelectedItem is TreasuryCandidateRow;
        StartTreasuryScanButton.IsEnabled = isAttached && !busy && !isWaiting;
        RefineTreasuryScanButton.IsEnabled = isAttached && !busy && !isWaiting && _treasurySession is not null && _treasurySession.Candidates.Count > 0;
        ExportTreasuryCandidatesButton.IsEnabled = !busy && !isWaiting && _treasurySession is not null;
        ImportCeCaptureButton.IsEnabled = !busy && !isWaiting && _treasurySession is not null;
        DiscriminateTreasuryCandidatesButton.IsEnabled = isAttached
            && !busy
            && !isWaiting
            && TreasuryCandidateDiscriminator.CanRun(_treasurySession, TreasuryDiscriminatorOptions.Default.MaxCandidates);
        MoneyChangeDoneButton.Visibility = isWaiting ? Visibility.Visible : Visibility.Collapsed;
        MoneyChangeDoneButton.IsEnabled = isWaiting && !busy;
        WriteTreasuryMoneyButton.IsEnabled = isAttached
            && !busy
            && !isWaiting
            && _treasurySession is not null
            && hasSelectedCandidate
            && ConfirmTreasuryWriteCheckBox.IsChecked == true;
        AnalyzeTreasuryPointersButton.IsEnabled = isAttached
            && !busy
            && !isWaiting
            && _treasurySession is not null
            && hasSelectedCandidate;
        ExportTreasuryPointersButton.IsEnabled = !busy
            && !isWaiting
            && _lastPointerAnalysisResult is not null;
        ExportTreasuryValidationButton.IsEnabled = isAttached
            && !busy
            && !isWaiting
            && _treasurySession is not null
            && hasSelectedCandidate;
        CompareTreasuryValidationButton.IsEnabled = isAttached
            && !busy
            && !isWaiting
            && _treasurySession is not null
            && hasSelectedCandidate;
        CancelTreasuryScanButton.IsEnabled = busy || isWaiting;
    }

    private void SetStatus(string status, bool isError)
    {
        StatusText.Text = status;
        StatusBorder.Background = new SolidColorBrush(isError ? Color.FromRgb(255, 232, 232) : Color.FromRgb(232, 235, 239));
        StatusBorder.BorderBrush = new SolidColorBrush(isError ? Color.FromRgb(204, 82, 82) : Color.FromRgb(201, 206, 214));
    }

    private TreasuryCandidateRow BuildTreasuryCandidateRow(
        KnownValueScanSession session,
        Candidate candidate,
        AlgorithmeCandidateScore algorithmScore)
    {
        var snapshot = _snapshot;
        var module = snapshot is null ? null : FindModule(snapshot, candidate.Address);
        _lastDiscriminatorCandidates.TryGetValue(candidate.CandidateId, out var discriminator);
        _lastPointerAnalysisCandidates.TryGetValue(candidate.CandidateId, out var pointerAnalysis);
        _lastTreasuryValidationCandidates.TryGetValue(candidate.CandidateId, out var restartValidation);
        _lastStructureComparisonCandidates.TryGetValue(candidate.CandidateId, out var structureComparison);
        var region = candidate.Region;
        var baseStatus = TreasuryScanGuidance.GetCandidateStatus(session, candidate);
        var status = restartValidation?.Status == TreasuryRestartValidationStatuses.ValidatedAfterRestart
            ? "Validated restart"
            : restartValidation?.Status == TreasuryRestartValidationStatuses.ProbableAfterReload
            ? "Reload probable"
            : pointerAnalysis?.OverallVerdict == TreasuryPointerAnalysisVerdicts.ProbableStructure
            ? "Pointer probable"
            : structureComparison?.Status == "Probable"
            ? "Structure probable"
            : discriminator?.IsFavorite == true
                ? "Favori departage"
                : baseStatus;
        var architecture = snapshot?.Process.Architecture.ToString() ?? "Unknown";
        var regionBase = region is null ? "unknown" : $"0x{region.BaseAddress:X}";
        var regionOffset = region is null ? "unknown" : $"0x{candidate.Address - region.BaseAddress:X}";
        var moduleName = module?.Name ?? "aucun module";
        var moduleBase = module is null ? "n/a" : $"0x{module.BaseAddress:X}";
        var moduleOffset = module is null ? "n/a" : $"0x{candidate.Address - module.BaseAddress:X}";
        var offsetStatus = module is null
            ? $"{regionBase}+{regionOffset} (region dynamique)"
            : $"{module.Name}+{moduleOffset}";
        var hookStatus = "Aucun hook - scan lecture seule, ecriture controlee disponible";
        var pointerStatus = BuildPointerStatus(pointerAnalysis);
        var stabilityStatus = pointerAnalysis?.OverallVerdict == TreasuryPointerAnalysisVerdicts.ProbableStructure
            ? "Pointer probable trouve, validation reload/redemarrage requise"
            : status == TreasuryScanGuidance.VeryLikelyStatus
            ? "Tres probable, a confirmer apres changement d'ecran/redemarrage"
            : "A valider par refine supplementaire";
        var evidence = string.Join(" | ", candidate.Evidence);
        var warnings = candidate.Warnings.Concat(session.Warnings).Distinct().ToArray();
        var algorithmScoreText = algorithmScore.IsAlgorithmAmbiguousTop && session.Candidates.Count > 1
            ? $"{algorithmScore.Score:0.00} sync"
            : algorithmScore.Score.ToString("0.00", CultureInfo.InvariantCulture);
        var discriminatorScore = discriminator is null ? string.Empty : discriminator.Score.ToString("0.00", CultureInfo.InvariantCulture);
        var discriminatorVerdict = discriminator?.Verdict ?? "Non lance";
        var discriminatorDetails = discriminator is null
            ? "Departage: pas encore lance."
            : $"Departage: {discriminator.Verdict} | Score {discriminator.Score:0.00}/100 | Lectures {discriminator.SuccessfulReads} OK/{discriminator.FailedReads} erreur(s) | " +
              $"Changements {discriminator.ChangeCount} | Premier changement sample {discriminator.FirstChangeSampleIndex?.ToString(CultureInfo.InvariantCulture) ?? "n/a"} | " +
              $"Contexte {discriminator.ContextByteCount} octets, {discriminator.ContextNonZeroByteCount} non zero | Raisons: {string.Join(" | ", discriminator.Reasons)}";
        var structureScore = structureComparison is null ? string.Empty : structureComparison.Score.ToString("0.00", CultureInfo.InvariantCulture);
        var structureStatus = structureComparison?.Status ?? "Non importe";
        var structureDetails = BuildStructureComparisonDetails(structureComparison);
        var pointerScore = pointerAnalysis?.BestStructureBase?.Score.ToString("0.00", CultureInfo.InvariantCulture) ?? string.Empty;
        var pointerDetails = BuildPointerAnalysisDetails(pointerAnalysis);
        var validationScore = restartValidation?.Score.ToString("0.00", CultureInfo.InvariantCulture) ?? string.Empty;
        var validationStatus = restartValidation?.Status ?? "Non valide";
        var validationDetails = BuildRestartValidationDetails(restartValidation);
        var details =
            $"Adresse: 0x{candidate.Address:X} | Type: {candidate.Type} | Arch: {architecture} | Statut: {status}\n" +
            $"Algorithme: score {algorithmScore.Score:0.00}/100 | Rang #{algorithmScore.Rank} | Marge {algorithmScore.MarginToNextBest:0.00} | Verdict: {algorithmScore.Verdict}\n" +
            $"Raisons algorithme: {string.Join(" | ", algorithmScore.Reasons)}\n" +
            $"{discriminatorDetails}\n" +
            $"{pointerDetails}\n" +
            $"{validationDetails}\n" +
            $"{structureDetails}\n" +
            $"Valeur observee: {candidate.ObservedValue ?? "unknown"} | Valeur attendue: {candidate.ExpectedValue ?? "unknown"} | Suite: {string.Join(" -> ", session.ValueHistory)}\n" +
            $"Module: {moduleName} | Base module: {moduleBase} | Offset module: {moduleOffset}\n" +
            $"Region: {regionBase} | Offset region: {regionOffset} | Protection: {region?.Protection ?? "unknown"} | State: {region?.State ?? "unknown"} | Type region: {region?.Type ?? "unknown"}\n" +
            $"Hook: {hookStatus} | Offset: {offsetStatus} | Pointer: {pointerStatus}\n" +
            $"Preuves/string: {evidence}";

        return new TreasuryCandidateRow(
            CandidateId: candidate.CandidateId,
            Status: status,
            AlgorithmScore: algorithmScoreText,
            AlgorithmScoreValue: algorithmScore.Score,
            AlgorithmRank: algorithmScore.Rank,
            AlgorithmVerdict: algorithmScore.Verdict,
            AlgorithmReasons: algorithmScore.Reasons.ToArray(),
            IsAlgorithmFavorite: algorithmScore.IsAlgorithmFavorite,
            IsAlgorithmAmbiguousTop: algorithmScore.IsAlgorithmAmbiguousTop,
            DiscriminatorScore: discriminatorScore,
            DiscriminatorScoreValue: discriminator?.Score ?? 0,
            DiscriminatorVerdict: discriminatorVerdict,
            IsDiscriminatorFavorite: discriminator?.IsFavorite == true,
            IsDiscriminatorObserved: discriminator is not null && discriminator.FailedReads == 0 && discriminator.IsFavorite == false,
            IsDiscriminatorFailed: discriminator is not null && discriminator.FailedReads > 0,
            PointerScore: pointerScore,
            PointerScoreValue: pointerAnalysis?.BestStructureBase?.Score ?? 0,
            IsPointerProbable: pointerAnalysis?.OverallVerdict == TreasuryPointerAnalysisVerdicts.ProbableStructure,
            IsPointerAmbiguous: pointerAnalysis?.OverallVerdict == TreasuryPointerAnalysisVerdicts.AmbiguousStructure,
            ValidationScore: validationScore,
            ValidationScoreValue: restartValidation?.Score ?? 0,
            ValidationStatus: validationStatus,
            IsValidationValidated: restartValidation?.Status == TreasuryRestartValidationStatuses.ValidatedAfterRestart,
            IsValidationProbable: restartValidation?.Status == TreasuryRestartValidationStatuses.ProbableAfterReload,
            IsValidationAmbiguous: restartValidation?.Status == TreasuryRestartValidationStatuses.Ambiguous,
            IsValidationBroken: restartValidation?.Status == TreasuryRestartValidationStatuses.Broken,
            StructureScore: structureScore,
            StructureScoreValue: structureComparison?.Score ?? 0,
            StructureStatus: structureStatus,
            IsStructureProbable: structureComparison?.Status == "Probable",
            IsStructureAmbiguous: structureComparison?.Status == "Ambiguous",
            Address: $"0x{candidate.Address:X}",
            AddressValue: candidate.Address,
            ObservedValue: candidate.ObservedValue ?? string.Empty,
            ExpectedValue: candidate.ExpectedValue ?? string.Empty,
            Confidence: candidate.Confidence.ToString("0.00", CultureInfo.InvariantCulture),
            ConfidenceValue: candidate.Confidence,
            RegionBase: regionBase,
            RegionOffset: regionOffset,
            RegionState: region?.State ?? "unknown",
            RegionType: region?.Type ?? "unknown",
            Protection: region?.Protection ?? string.Empty,
            ModuleName: moduleName,
            ModuleBase: moduleBase,
            ModuleOffset: moduleOffset,
            OffsetStatus: offsetStatus,
            HookStatus: hookStatus,
            PointerStatus: pointerStatus,
            StabilityStatus: stabilityStatus,
            Architecture: architecture,
            Evidence: evidence,
            EvidenceItems: candidate.Evidence.ToArray(),
            WarningItems: warnings,
            Details: details);
    }

    private static string BuildPointerStatus(TreasuryPointerAnalysisResult? result)
    {
        if (result is null)
        {
            return "Pointer: non analyse";
        }

        return result.OverallVerdict switch
        {
            TreasuryPointerAnalysisVerdicts.ProbableStructure => "Pointer: probable",
            TreasuryPointerAnalysisVerdicts.AmbiguousStructure => "Pointer: ambigu",
            TreasuryPointerAnalysisVerdicts.NoPointerFound => "Pointer: non trouve",
            TreasuryPointerAnalysisVerdicts.RawCandidateOnly => "Pointer: valeur brute",
            TreasuryPointerAnalysisVerdicts.NeedsRestartValidation => "Pointer: validation reload requise",
            _ => $"Pointer: {result.OverallVerdict}"
        };
    }

    private static string BuildPointerAnalysisDetails(TreasuryPointerAnalysisResult? result)
    {
        if (result is null)
        {
            return "Structure/Pointeurs: aucune analyse lancee.";
        }

        var best = result.BestStructureBase;
        var bestText = best is null
            ? "aucune base probable"
            : $"{best.BaseAddressHex}+{best.TreasuryOffsetHex} score {best.Score:0.00} ({best.Status})";
        var chains = result.PointerChains.Count == 0
            ? "aucune chaine profondeur 2"
            : string.Join(" | ", result.PointerChains.Take(3).Select(chain => $"{chain.RootPointerAddressHex}->{chain.IntermediatePointerAddressHex}->{chain.FinalTargetAddressHex}"));
        var warnings = result.Warnings.Count == 0 ? "none" : string.Join(" | ", result.Warnings.Take(4));

        return
            $"Structure/Pointeurs: {result.OverallVerdict} | Best {bestText}\n" +
            $"Pointeurs {result.PointerHits.Count} | Chaines {result.PointerChains.Count} | Bases testees {result.TestedBaseCount} | Bytes 0x{result.ScannedBytes:X}\n" +
            $"Chaines: {chains}\n" +
            $"Warnings pointeurs: {warnings}";
    }

    private static string BuildRestartValidationDetails(TreasuryValidationCandidateResult? result)
    {
        if (result is null)
        {
            return "Validation reload: aucune comparaison effectuee.";
        }

        var source = string.IsNullOrWhiteSpace(result.StrongestReferenceKind)
            ? "aucune reference"
            : $"{result.StrongestReferenceKind} ({result.StrongestReferencePath})";
        var warnings = result.Warnings.Count == 0 ? "none" : string.Join(" | ", result.Warnings.Take(4));
        var evidence = result.Evidence.Count == 0 ? "none" : string.Join(" | ", result.Evidence.Take(5));

        return
            $"Validation reload: {result.Status} | Score {result.Score:0.00}/100 | Matches {result.MatchedReferenceCount} | Source {source}\n" +
            $"Base structure: {result.StructureBaseHex ?? "n/a"} | Offset treasury: {result.TreasuryOffsetHex ?? "n/a"} | Pointer: {result.PointerStatus}\n" +
            $"Preuves validation: {evidence}\n" +
            $"Warnings validation: {warnings}";
    }

    private static string BuildStructureComparisonDetails(StructureComparisonResult? result)
    {
        if (result is null)
        {
            return "Structure CE/Lua: aucune capture importee.";
        }

        var bestFields = result.FieldOffsets
            .Take(5)
            .Select(field => $"{field.RelativeOffsetHex}={field.Status} score {field.Score:0.00} match {field.MatchKnownValueCount}/{field.ObservationCount}")
            .ToArray();
        var fieldsText = bestFields.Length == 0 ? "aucun offset decode" : string.Join(" | ", bestFields);
        var warnings = result.Warnings.Count == 0 ? "none" : string.Join(" | ", result.Warnings.Take(4));

        return
            $"Structure CE/Lua: {result.Status} | Score {result.Score:0.00}/100 | Captures {result.CaptureCount} | Scenarios {result.ScenarioCount}\n" +
            $"Base suspectee: {result.SuspectedBase.Reason} | Confiance {result.SuspectedBase.Confidence:0.00}\n" +
            $"Offsets: {fieldsText}\n" +
            $"Warnings structure: {warnings}";
    }

    private SavedMemoryFinding BuildSavedFinding(
        TreasuryCandidateRow row,
        MemoryMapSnapshot snapshot,
        KnownValueScanSession session)
    {
        return new SavedMemoryFinding(
            FeatureId: session.FeatureId,
            Label: $"{row.Status} treasury {row.Address}",
            SavedAt: DateTimeOffset.Now,
            ProcessId: snapshot.Process.ProcessId,
            ProcessName: snapshot.Process.Name,
            Architecture: row.Architecture,
            ProcessPath: snapshot.Process.Path,
            Address: row.AddressValue,
            AddressHex: row.Address,
            Type: session.ValueType,
            Status: row.Status,
            ObservedValue: row.ObservedValue,
            ExpectedValue: row.ExpectedValue,
            Confidence: row.ConfidenceValue,
            ModuleName: row.ModuleName == "aucun module" ? null : row.ModuleName,
            ModuleBaseHex: row.ModuleBase == "n/a" ? null : row.ModuleBase,
            ModuleOffsetHex: row.ModuleOffset == "n/a" ? null : row.ModuleOffset,
            RegionBaseHex: row.RegionBase,
            RegionOffsetHex: row.RegionOffset,
            RegionState: row.RegionState,
            RegionProtection: row.Protection,
            RegionType: row.RegionType,
            HookStatus: row.HookStatus,
            OffsetStatus: row.OffsetStatus,
            PointerStatus: row.PointerStatus,
            StabilityStatus: row.StabilityStatus,
            ValueHistory: session.ValueHistory.ToArray(),
            Evidence: row.EvidenceItems,
            Warnings: row.WarningItems);
    }

    private void ResetDiscriminatorState()
    {
        _discriminatorCancellation?.Cancel();
        _isWaitingForMoneyChangeConfirmation = false;
        _lastDiscriminatorResult = null;
        _lastDiscriminatorCandidates = new Dictionary<string, TreasuryDiscriminatorCandidateResult>(StringComparer.Ordinal);
        _discriminatorWorkflowLogEntries.Clear();
        _discriminatorLogRows.Clear();
        BindDiscriminatorResult(null);
    }

    private void ResetPointerAnalysisState()
    {
        _pointerAnalysisCancellation?.Cancel();
        _isAnalyzingTreasuryPointers = false;
        _lastPointerAnalysisResult = null;
        _lastPointerAnalysisExportPath = null;
        _lastPointerAnalysisCandidates.Clear();
        BindPointerAnalysisResult(null, null);
    }

    private void ResetTreasuryRestartValidationState()
    {
        _isValidatingTreasuryRestart = false;
        _lastTreasuryValidationResult = null;
        _lastTreasuryValidationBundleDirectory = null;
        _lastTreasuryValidationCandidates = new Dictionary<string, TreasuryValidationCandidateResult>(StringComparer.Ordinal);
        BindTreasuryRestartValidationResult(null, null);
    }

    private void ResetStructureComparisonState()
    {
        _lastStructureComparisonReport = null;
        _lastStructureComparisonBundleDirectory = null;
        _lastStructureComparisonCandidates = new Dictionary<string, StructureComparisonResult>(StringComparer.Ordinal);
        BindStructureComparisonResult(null, null);
    }

    private void BindDiscriminatorResult(TreasuryDiscriminatorResult? result, string? overrideSummary = null)
    {
        if (!string.IsNullOrWhiteSpace(overrideSummary))
        {
            DiscriminatorSummaryText.Text = overrideSummary;
            DiscriminatorExportPathText.Text = string.Empty;
            return;
        }

        if (result is null)
        {
            DiscriminatorSummaryText.Text =
                "Clique Departager candidats quand 2 a 10 candidats restent apres refine. Change l'argent en jeu, reviens cliquer C'est fait, puis ne touche plus a l'argent pendant l'observation.";
            DiscriminatorExportPathText.Text = string.Empty;
            return;
        }

        var favorite = result.FavoriteAddressHex ?? "aucun";
        var changed = result.Candidates.Count(candidate => candidate.ChangeCount > 0);
        var failed = result.Candidates.Count(candidate => candidate.FailedReads > 0);

        DiscriminatorSummaryText.Text =
            $"Verdict: {result.OverallVerdict} | Candidats {result.CandidateCount} | Synchronises/changes {changed} | " +
            $"Favori {favorite} | Erreurs lecture {failed} | Samples {result.Samples.Count} | Logs {result.Log.Count}";
    }

    private void BindPointerAnalysisResult(TreasuryPointerAnalysisResult? result, string? overrideSummary)
    {
        if (!string.IsNullOrWhiteSpace(overrideSummary))
        {
            TreasuryPointerSummaryText.Text = overrideSummary;
            TreasuryPointerExportPathText.Text = _lastPointerAnalysisExportPath is null ? string.Empty : $"Export: {_lastPointerAnalysisExportPath}";
            return;
        }

        if (result is null)
        {
            TreasuryPointerSummaryText.Text =
                "Selectionne un candidat treasury puis clique Analyser structure/pointeurs. L'objectif est base structure + offset, pas adresse brute.";
            TreasuryPointerExportPathText.Text = string.Empty;
            return;
        }

        var best = result.BestStructureBase;
        var bestText = best is null
            ? "aucune"
            : $"{best.BaseAddressHex}+{best.TreasuryOffsetHex} score {best.Score:0.00}";
        var warnings = result.Warnings.Count == 0 ? "none" : string.Join(" | ", result.Warnings.Take(3));
        TreasuryPointerSummaryText.Text =
            $"Verdict: {result.OverallVerdict} | Best {bestText} | Pointeurs {result.PointerHits.Count} | " +
            $"Chaines {result.PointerChains.Count} | Bases {result.StructureBases.Count}/{result.TestedBaseCount} | Warnings {warnings}";
        TreasuryPointerExportPathText.Text = _lastPointerAnalysisExportPath is null
            ? "Export: non exporte"
            : $"Export: {_lastPointerAnalysisExportPath}";
    }

    private void BindTreasuryRestartValidationResult(TreasuryRestartValidationResult? result, string? overrideSummary)
    {
        if (!string.IsNullOrWhiteSpace(overrideSummary))
        {
            TreasuryValidationSummaryText.Text = overrideSummary;
            TreasuryValidationExportPathText.Text = string.IsNullOrWhiteSpace(_lastTreasuryValidationBundleDirectory)
                ? string.Empty
                : $"Bundle: {_lastTreasuryValidationBundleDirectory}";
            return;
        }

        if (result is null)
        {
            TreasuryValidationSummaryText.Text =
                "Workflow: 1) fais marcher le write, 2) clique Export validation A, 3) reload/redemarre Rome2, 4) rescan/refine, 5) clique Comparer reload.";
            TreasuryValidationExportPathText.Text = string.Empty;
            return;
        }

        var top = result.RankedCandidates.FirstOrDefault();
        var topText = top is null ? "aucun" : $"{top.AddressHex} {top.Status} score {top.Score:0.00}";
        var warnings = result.Warnings.Count == 0 ? "none" : string.Join(" | ", result.Warnings.Take(3));

        TreasuryValidationSummaryText.Text =
            $"Validation: {result.OverallStatus} | {result.OverallVerdict} | References {result.ReferenceCount} | Top {topText} | Warnings {warnings}";
        TreasuryValidationExportPathText.Text = string.IsNullOrWhiteSpace(_lastTreasuryValidationBundleDirectory)
            ? "Bundle: non exporte"
            : $"Bundle: {_lastTreasuryValidationBundleDirectory}";
    }

    private void BindStructureComparisonResult(StructureComparisonReport? report, string? bundleDirectory)
    {
        if (report is null)
        {
            StructureComparisonSummaryText.Text =
                "Importe une ou plusieurs captures CE/Lua pour comparer les fenetres memoire autour des candidats treasury.";
            StructureComparisonExportPathText.Text = string.Empty;
            return;
        }

        var top = report.Results.FirstOrDefault();
        var topText = top is null ? "aucun" : $"{top.AddressHex} {top.Status} score {top.Score:0.00}";
        var warnings = report.Warnings.Count == 0 ? "none" : string.Join(" | ", report.Warnings.Take(3));
        StructureComparisonSummaryText.Text =
            $"Structure: {report.OverallStatus} | {report.OverallVerdict} | Captures {report.CaptureCount} | " +
            $"Scenarios {report.ScenarioCount} | Top {topText} | Warnings {warnings}";
        StructureComparisonExportPathText.Text = string.IsNullOrWhiteSpace(bundleDirectory)
            ? string.Empty
            : $"Bundle: {bundleDirectory}";
    }

    private static DiscriminatorLogRow ToDiscriminatorLogRow(TreasuryDiscriminatorLogEntry entry)
    {
        return new DiscriminatorLogRow(
            Time: entry.Timestamp.ToString("HH:mm:ss.fff", CultureInfo.CurrentCulture),
            EventType: entry.EventType,
            Address: entry.AddressHex ?? string.Empty,
            Message: entry.Message);
    }

    private void AppendDiscriminatorLog(TreasuryDiscriminatorLogEntry entry, bool rememberForExport)
    {
        if (rememberForExport)
        {
            _discriminatorWorkflowLogEntries.Add(entry);
        }

        var row = ToDiscriminatorLogRow(entry);
        _discriminatorLogRows.Add(row);
        if (_discriminatorLogRows.Count > 600)
        {
            _discriminatorLogRows.RemoveAt(0);
        }

        DiscriminatorLogGrid.ScrollIntoView(row);
    }

    private void LoadSavedFindings()
    {
        try
        {
            _savedFindings = _savedFindingStore.Load(DonjHackPathResolver.ResolveRoot());
            SavedFindingsGrid.ItemsSource = _savedFindings
                .Select(finding => new SavedFindingRow(
                    finding.FeatureId,
                    finding.Label,
                    finding.AddressHex,
                    finding.ModuleName ?? "n/a",
                    finding.OffsetStatus,
                    finding.Architecture,
                    finding.HookStatus,
                    finding.PointerStatus,
                    string.Join(" -> ", finding.ValueHistory),
                    finding.SavedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture)))
                .ToArray();
        }
        catch (Exception ex)
        {
            SavedFindingsGrid.ItemsSource = Array.Empty<SavedFindingRow>();
            SetStatus($"Error: chargement known findings impossible: {ex.Message}", isError: true);
        }
    }

    private static ModuleInfo? FindModule(MemoryMapSnapshot snapshot, ulong address)
    {
        return snapshot.Modules.FirstOrDefault(module => IsAddressInsideModule(address, module));
    }

    private static bool IsAddressInsideModule(ulong address, ModuleInfo module)
    {
        if (module.Size <= 0 || address < module.BaseAddress)
        {
            return false;
        }

        // Je compare avec une soustraction pour eviter un overflow si une borne module etait atypique.
        return address - module.BaseAddress < (ulong)module.Size;
    }

    public sealed record ModuleRow(string Name, string BaseAddress, string Size, string Path);

    public sealed record RegionRow(
        string BaseAddress,
        string Size,
        string State,
        string Protection,
        string Type,
        bool IsReadable,
        bool IsWritable,
        bool IsExecutable);

    public sealed record Rome2ProcessRow(
        int ProcessId,
        string DisplayName,
        bool IsRecommended,
        string StartedAt,
        string Path,
        string MainWindowTitle,
        string RecommendationReason);

    public sealed record TreasuryCandidateRow(
        string CandidateId,
        string Status,
        string AlgorithmScore,
        double AlgorithmScoreValue,
        int AlgorithmRank,
        string AlgorithmVerdict,
        string[] AlgorithmReasons,
        bool IsAlgorithmFavorite,
        bool IsAlgorithmAmbiguousTop,
        string DiscriminatorScore,
        double DiscriminatorScoreValue,
        string DiscriminatorVerdict,
        bool IsDiscriminatorFavorite,
        bool IsDiscriminatorObserved,
        bool IsDiscriminatorFailed,
        string PointerScore,
        double PointerScoreValue,
        bool IsPointerProbable,
        bool IsPointerAmbiguous,
        string ValidationScore,
        double ValidationScoreValue,
        string ValidationStatus,
        bool IsValidationValidated,
        bool IsValidationProbable,
        bool IsValidationAmbiguous,
        bool IsValidationBroken,
        string StructureScore,
        double StructureScoreValue,
        string StructureStatus,
        bool IsStructureProbable,
        bool IsStructureAmbiguous,
        string Address,
        ulong AddressValue,
        string ObservedValue,
        string ExpectedValue,
        string Confidence,
        double ConfidenceValue,
        string RegionBase,
        string RegionOffset,
        string RegionState,
        string RegionType,
        string Protection,
        string ModuleName,
        string ModuleBase,
        string ModuleOffset,
        string OffsetStatus,
        string HookStatus,
        string PointerStatus,
        string StabilityStatus,
        string Architecture,
        string Evidence,
        string[] EvidenceItems,
        string[] WarningItems,
        string Details);

    public sealed record SavedFindingRow(
        string FeatureId,
        string Label,
        string AddressHex,
        string ModuleName,
        string OffsetStatus,
        string Architecture,
        string HookStatus,
        string PointerStatus,
        string ValueHistoryText,
        string SavedAt);

    public sealed record DiscriminatorLogRow(
        string Time,
        string EventType,
        string Address,
        string Message);
}
