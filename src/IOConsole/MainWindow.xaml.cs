using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Forms;
using IOConsole.Serializable;
using Microsoft.Data.Sqlite;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace IOConsole;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private static readonly string AppDir = AppDomain.CurrentDomain.BaseDirectory;
    public static readonly string DataDir = Path.Combine(AppDir, "data");
    private static readonly string DataBackupDir = Path.Combine(DataDir, "backup");

    private const string DatabaseFilename = "part_library.db";

    private static readonly string CurrentDatabaseFile = Path.Combine(DataDir, DatabaseFilename);
    private static readonly string BackupDatabaseFile = Path.Combine(DataBackupDir, DatabaseFilename);

    // Avoid file access error in backup/restore by add Pooling=false in the connection string.
    private const string ConnectionString = "Data Source=data/part_library.db;Pooling=false";

    private static readonly ObservableCollection<Part> QueryResultsCollection = new();
    private static readonly List<Part> OriginalQueryResults = new();

    private string[] _selectedFiles = Array.Empty<string>();

    private const string SelectJsonCommandText = @"
                SELECT 
                    json_object(
                        'partNumber', partNumber,
                        'deviceType', deviceType,
                        'deviceName', deviceName,
                        'value', value,
                        'positiveTolerance', positiveTolerance,
                        'negativeTolerance', negativeTolerance,
                        'caseName', caseName,
                        'caseIdentifier', caseIdentifier
                    )
                FROM
                    parts";

    public MainWindow()
    {
        InitializeComponent();

        try
        {
            Initialize();
        }
        catch (Exception e)
        {
            MessageBox.Show(e.Message, "Error During Initialization", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        AddEvents();
    }

    private static void Initialize()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(DataBackupDir);

        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        const string createCommand = @"
                CREATE TABLE IF NOT EXISTS
                    parts
                        (partNumber TEXT PRIMARY KEY,
                        deviceName TEXT,
                        deviceType TEXT,
                        value TEXT,
                        positiveTolerance TEXT,
                        negativeTolerance TEXT,
                        caseName TEXT,
                        caseIdentifier TEXT)";

        using var command = connection.CreateCommand();
        command.CommandText = createCommand;
        command.ExecuteNonQuery();

        connection.Close();
    }

    private void AddEvents()
    {
        QueryResultsDataGrid.ItemsSource = QueryResultsCollection;

        SelectCsvButton.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog
            {
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Multiselect = true
            };

            var result = dialog.ShowDialog();

            if (result is not true) return;

            var files = dialog.FileNames;
            _selectedFiles = files;
            CsvFilenameBox.Text = string.Join(';', files);
        };

        ImportCsvButton.Click += ImportCsvAction;

        RollbackButton.Click += Rollback;

        ExportCsvButton.Click += ExportCsvAction;

        QueryPartNumberButton.Click += (_, _) =>
        {
            try
            {
                var partNumberPattern = PartNumberInputBox.Text;
                if (partNumberPattern.Length == 0)
                    throw new Exception("Search pattern cannot be empty!");

                var patterns = partNumberPattern.Split(',', StringSplitOptions.TrimEntries);
                QueryParts(patterns);
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message, "Search Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };

        UpdateRecordButton.Click += (_, _) =>
        {
            try
            {
                UpdateRecords();
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message, "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };
    }

    private PartColumnConfig GetColumnConfig()
    {
        var columnSettingsWindow = new ColumnSettings();
        var result = columnSettingsWindow.ShowDialog();

        if (result is not true)
        {
            throw new Exception("Column Settings not set properly.");
        }

        var columnConfig = (PartColumnConfig)columnSettingsWindow.DataContext;

        return columnConfig;
    }

    private void ImportCsvAction(object? sender, RoutedEventArgs args)
    {
        try
        {
            if (_selectedFiles.Length == 0)
                throw new Exception("Please select filename first");

            // backup current database file, enabling overwrite
            File.Copy(CurrentDatabaseFile, BackupDatabaseFile, true);

            ImportCsvButton.Content = "Importing";
            ImportCsvButton.IsEnabled = false;

            var columnConfig = GetColumnConfig();
            var affectedRows = ImportCsvToDatabase(columnConfig);

            var message = affectedRows == 0
                ? "No records imported from your csv file..."
                : $"Successfully imported {affectedRows} records.";

            MessageBox.Show(message, "Congratulations", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception e)
        {
            MessageBox.Show(e.Message, "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ImportCsvButton.Content = "Import";
            ImportCsvButton.IsEnabled = true;
        }
    }

    private int ImportCsvToDatabase(PartColumnConfig columnConfig)
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.CommandText = @"
                INSERT OR IGNORE INTO
                    parts
                        (partNumber, deviceName, deviceType, value, positiveTolerance, negativeTolerance, caseName, caseIdentifier)
                VALUES
                    ($partNumber, $deviceName, $deviceType, $value, $positiveTolerance, $negativeTolerance, $caseName, $caseIdentifier)";

        var partNumberParameter = command.CreateParameter();
        partNumberParameter.ParameterName = "$partNumber";
        command.Parameters.Add(partNumberParameter);

        var deviceNameParameter = command.CreateParameter();
        deviceNameParameter.ParameterName = "$deviceName";
        command.Parameters.Add(deviceNameParameter);

        var deviceTypeParameter = command.CreateParameter();
        deviceTypeParameter.ParameterName = "$deviceType";
        command.Parameters.Add(deviceTypeParameter);

        var valueParameter = command.CreateParameter();
        valueParameter.ParameterName = "$value";
        command.Parameters.Add(valueParameter);

        var positiveTolParameter = command.CreateParameter();
        positiveTolParameter.ParameterName = "$positiveTolerance";
        command.Parameters.Add(positiveTolParameter);

        var negativeTolParameter = command.CreateParameter();
        negativeTolParameter.ParameterName = "$negativeTolerance";
        command.Parameters.Add(negativeTolParameter);

        var caseParameter = command.CreateParameter();
        caseParameter.ParameterName = "$caseName";
        command.Parameters.Add(caseParameter);

        var caseIdentifierParameter = command.CreateParameter();
        caseIdentifierParameter.ParameterName = "$caseIdentifier";
        command.Parameters.Add(caseIdentifierParameter);

        try
        {
            var affectedRows = 0;

            foreach (var file in _selectedFiles)
            {
                var lines = File.ReadAllLines(file);

                for (var i = columnConfig.SkippedRow; i < lines.Length; i++)
                {
                    if (lines[i].Length == 0) continue;

                    var elements = lines[i].Split(',', StringSplitOptions.TrimEntries);

                    partNumberParameter.Value = elements[columnConfig.PartNumberColumn - 1];
                    deviceTypeParameter.Value = elements[columnConfig.DeviceTypeColumn - 1];
                    deviceNameParameter.Value = elements[columnConfig.DeviceNameColumn - 1];
                    valueParameter.Value = elements[columnConfig.ValueColumn - 1];
                    positiveTolParameter.Value = elements[columnConfig.PositiveToleranceColumn - 1];
                    negativeTolParameter.Value = elements[columnConfig.NegativeToleranceColumn - 1];
                    caseParameter.Value = elements[columnConfig.CaseColumn - 1];
                    caseIdentifierParameter.Value = elements[columnConfig.CaseIdentifierColumn - 1];

                    affectedRows += command.ExecuteNonQuery();
                }
            }

            transaction.Commit();

            return affectedRows;
        }
        catch (Exception)
        {
            transaction.Rollback();

            throw;
        }
        finally
        {
            CsvFilenameBox.Text = string.Empty;
            _selectedFiles = Array.Empty<string>();
        }
    }

    private static void Rollback(object? sender, RoutedEventArgs args)
    {
        static string CalculateMD5(string filename)
        {
            using var md5 = MD5.Create();
            using var stream = File.OpenRead(filename);
            var hash = md5.ComputeHash(stream);
            return BitConverter.ToString(hash);
        }

        try
        {
            const string message = "Restore the database from backup? Please notice that this operation is not recoverable.";
            var result = MessageBox.Show(message, "Warning", MessageBoxButton.YesNo, MessageBoxImage.Exclamation);

            if (result != MessageBoxResult.Yes) return;

            if (!File.Exists(BackupDatabaseFile))
            {
                MessageBox.Show("There is no backup available...", "Oops", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var currentMD5 = CalculateMD5(CurrentDatabaseFile);
            var backupMD5 = CalculateMD5(BackupDatabaseFile);

            if (string.Equals(currentMD5, backupMD5, StringComparison.Ordinal))
            {
                MessageBox.Show("Current database is the same as the backup. No need to restore...", "Oops", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            File.Copy(BackupDatabaseFile, CurrentDatabaseFile, true);

            MessageBox.Show("Restored successfully.", "Congratulations", MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception e)
        {
            MessageBox.Show(e.Message, "Rollback Error", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void ExportCsvAction(object? sender, RoutedEventArgs args)
    {
        if (_selectedFiles.Length == 0)
        {
            MessageBox.Show("Please select one file to export.", "Oops",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_selectedFiles.Length > 1)
        {
            MessageBox.Show("Export operation only supports one file.", "Oops",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var result = MessageBox.Show("Please select output directory.", "Step 1", MessageBoxButton.OK,
                MessageBoxImage.Information);

            if (result != MessageBoxResult.OK) return;

            var dialog = new FolderBrowserDialog
            {
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };

            var dr = dialog.ShowDialog();

            if (dr == System.Windows.Forms.DialogResult.OK)
            {
                ExportCsv(dialog.SelectedPath, _selectedFiles[0]);
                result = MessageBox.Show("Exported successfully, open output directory?", "Congratulations",
                    MessageBoxButton.YesNo, MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                    Process.Start("explorer", dialog.SelectedPath);
            }
        }
        catch (Exception e)
        {
            MessageBox.Show(e.Message, "Export Error", MessageBoxButton.OK, MessageBoxImage.Information);
        }

    }

    private void ExportCsv(string outputDirectory, string filename)
    {
        var lines = File.ReadAllLines(filename);
        var columnConfig = GetColumnConfig();
        var stringBuilder = new StringBuilder();

        var header = new string?[10];
        header[columnConfig.DrawingReferenceColumn - 1] = "Drawing Reference";
        header[columnConfig.PartNumberColumn - 1] = "Part Number";
        header[columnConfig.DeviceTypeColumn - 1] = "Device Type";
        header[columnConfig.DeviceNameColumn - 1] = "Device Name";
        header[columnConfig.ValueColumn - 1] = "Value";
        header[columnConfig.PositiveToleranceColumn - 1] = "Tol+";
        header[columnConfig.NegativeToleranceColumn - 1] = "Tol-";
        header[columnConfig.CaseColumn - 1] = "Case";
        header[columnConfig.CaseIdentifierColumn - 1] = "CaseIdentifier";
        var headerString = string.Join(",", header.Where(x => x is not null));

        stringBuilder.AppendLine(headerString);

        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();

        for (var i = columnConfig.SkippedRow; i < lines.Length; i++)
        {
            if (lines[i].Length == 0) continue;
            var lineElements = lines[i].Split(',', StringSplitOptions.TrimEntries);
            var partNumber = lineElements[columnConfig.PartNumberColumn - 1];
            var drawingReference = lineElements[columnConfig.DrawingReferenceColumn - 1];

            command.CommandText = SelectJsonCommandText + $" WHERE partNumber='{partNumber}'";
            using var reader = command.ExecuteReader();
            if (reader.HasRows)
            {
                Part? part = null;
                while (reader.Read())
                {
                    var result = reader.GetString(0);
                    part = JsonSerializer.Deserialize<Part>(result);
                    break;
                }

                if (part != null)
                {
                    var partLine = new string?[10];
                    partLine[columnConfig.DrawingReferenceColumn - 1] = drawingReference;
                    partLine[columnConfig.PartNumberColumn - 1] = partNumber;
                    partLine[columnConfig.DeviceTypeColumn - 1] = part.DeviceType;
                    partLine[columnConfig.DeviceNameColumn - 1] = part.DeviceName;
                    partLine[columnConfig.ValueColumn - 1] = part.Value;
                    partLine[columnConfig.PositiveToleranceColumn - 1] = part.PositiveTolerance;
                    partLine[columnConfig.NegativeToleranceColumn - 1] = part.NegativeTolerance;
                    partLine[columnConfig.CaseColumn - 1] = part.Case;
                    partLine[columnConfig.CaseIdentifierColumn - 1] = part.CaseIdentifier;
                    stringBuilder.AppendLine(string.Join(',', partLine.Where(x => x is not null)));
                }
                else
                {
                    stringBuilder.AppendLine(lines[i]);
                }
            }
            else
            {
                stringBuilder.AppendLine(lines[i]);
            }
        }

        var fi = new FileInfo(filename);
        var outputFilename = Path.Combine(outputDirectory, fi.Name + ".bom.csv");
        File.WriteAllText(outputFilename, stringBuilder.ToString());
    }

    private void QueryParts(string[] partNumberPatterns)
    {
        QueryResultsCollection.Clear();
        OriginalQueryResults.Clear();

        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();

        var likes = partNumberPatterns.Select(x => $"'%{x}%'").ToArray();
        var whereClause = " WHERE partNumber LIKE " + string.Join(" OR partNumber LIKE ", likes);

        command.CommandText = SelectJsonCommandText + whereClause;

        using var reader = command.ExecuteReader();

        if (!reader.HasRows)
        {
            MessageBox.Show("No records found.", "Empty Result Collection", MessageBoxButton.OK, MessageBoxImage.Asterisk);
            return;
        }

        var recordNumber = 0;

        while (reader.Read())
        {
            var result = reader.GetString(0);
            var part = JsonSerializer.Deserialize<Part>(result);

            if (part is null) continue;

            QueryResultsCollection.Add(part);
            OriginalQueryResults.Add(Part.Copy(part));
            recordNumber++;
        }

        Status.Text = $"Found {recordNumber} record(s).";
    }

    private void UpdateRecords()
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        var transaction = connection.BeginTransaction();

        try
        {
            var updatedCount = 0;
            for (var i = 0; i < OriginalQueryResults.Count; i++)
            {
                var partNumber = OriginalQueryResults[i].PartNumber;
                var queryResultPart = (from p in QueryResultsCollection where p.PartNumber == partNumber select p).First();
                var updated = Part.Updated(OriginalQueryResults[i], queryResultPart);

                if (!updated) continue;

                OriginalQueryResults[i] = Part.Copy(queryResultPart);

                using var command = connection.CreateCommand();

                command.CommandText = $@"
                        UPDATE
                            parts
                        SET
                            deviceType = '{queryResultPart.DeviceType}',
                            deviceName = '{queryResultPart.DeviceName}',
                            value = '{queryResultPart.Value}',
                            positiveTolerance = '{queryResultPart.PositiveTolerance}',
                            negativeTolerance = '{queryResultPart.NegativeTolerance}',
                            caseName = '{queryResultPart.Case}',
                            caseIdentifier = '{queryResultPart.CaseIdentifier}'
                        WHERE
                            partNumber = '{partNumber}'";

                command.ExecuteNonQuery();

                updatedCount++;
            }

            transaction.Commit();

            Status.Text = $"{updatedCount} record(s) updated.";
        }
        catch (Exception)
        {
            transaction.Rollback();
            throw;
        }
    }
}
