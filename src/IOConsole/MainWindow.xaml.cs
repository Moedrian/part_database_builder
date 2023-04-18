using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using IOConsole.Serializable;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;

namespace IOConsole;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private static readonly string AppDir = AppDomain.CurrentDomain.BaseDirectory;
    private static readonly string DataDir = Path.Combine(AppDir, "data");
    private static readonly string PartColumnConfigFile = Path.Combine(DataDir, "part_column_config.json");
    private const string ConnectionString = "Data Source=data/part_library.db";

    private static readonly JsonSerializerOptions Jso = new() { WriteIndented = true };

    private static readonly ObservableCollection<Part> QueryResultsCollection = new();
    private static readonly List<Part> OriginalQueryResults = new();

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

        SetBindings();
        AddEvents();
    }

    private void Initialize()
    {
        Directory.CreateDirectory(DataDir);

        if (!File.Exists(PartColumnConfigFile))
        {
            var json = JsonSerializer.Serialize(new PartColumnConfig(), Jso);
            File.WriteAllText(PartColumnConfigFile, json);
        }

        var cfg = JsonSerializer.Deserialize<PartColumnConfig>(File.ReadAllText(PartColumnConfigFile));
        if (cfg is not null)
            DataContext = cfg;

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
    }

    private string[] _selectedFiles = Array.Empty<string>();

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
        {
            var ithChild = VisualTreeHelper.GetChild(depObj, i);
            if (ithChild is T t)
                yield return t;

            foreach (var childOfChild in FindVisualChildren<T>(ithChild))
                yield return childOfChild;
        }
    }

    private void SetBindings()
    {
        var dp = TextBox.TextProperty;
        var columnConfig = (PartColumnConfig)DataContext;
        var textBoxes = FindVisualChildren<TextBox>(ColumnSettingsGrid);
        foreach (var tb in textBoxes)
        {
            tb.SetBinding(dp, new Binding(tb.Name.Replace("NumberBox", string.Empty)) { Source = columnConfig });
        }
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

            if (result is true)
            {
                var files = dialog.FileNames;
                _selectedFiles = files;
                CsvFilenameBox.Text = string.Join(';', files);
            }
        };

        SaveColumnNumberConfigButton.Click += (_, _) =>
        {
            var cfg = (PartColumnConfig)DataContext;
            File.WriteAllText(PartColumnConfigFile, JsonSerializer.Serialize(cfg, Jso));
        };

        ImportCsvButton.Click += (_, _) =>
        {
            try
            {
                if (CsvFilenameBox.Text.Length == 0)
                    throw new Exception("Please select filename first");

                ImportCsvButton.Content = "Importing";
                ImportCsvButton.IsEnabled = false;
                ImportCsvToDatabase();

                MessageBox.Show("Successfully imported!", "SUCCESS", MessageBoxButton.OK, MessageBoxImage.Information);
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
        };

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

    private void ImportCsvToDatabase()
    {
        var columnConfig = (PartColumnConfig)DataContext;

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
            foreach (var file in _selectedFiles)
            {
                var lines = File.ReadAllLines(file);

                for (var i = columnConfig.SkippedRow; i < lines.Length; i++)
                {
                    if (lines[i].Length == 0) continue;

                    var elements = lines[i].Split(',');

                    partNumberParameter.Value = elements[columnConfig.PartNumberColumn - 1];
                    deviceTypeParameter.Value = elements[columnConfig.DeviceTypeColumn - 1];
                    deviceNameParameter.Value = elements[columnConfig.DeviceNameColumn - 1];
                    valueParameter.Value = elements[columnConfig.ValueColumn - 1];
                    positiveTolParameter.Value = elements[columnConfig.PositiveToleranceColumn - 1];
                    negativeTolParameter.Value = elements[columnConfig.NegativeToleranceColumn - 1];
                    caseParameter.Value = elements[columnConfig.CaseColumn - 1];
                    caseIdentifierParameter.Value = elements[columnConfig.CaseIdentifierColumn - 1];

                    command.ExecuteNonQuery();
                }
            }

            transaction.Commit();
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

    private void QueryParts(string[] partNumberPatterns)
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();

        const string commandText = @"
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
                    parts
                WHERE 
                    partNumber LIKE ";

        var likes = partNumberPatterns.Select(x => $"'%{x}%'").ToArray();
        var whereClause = string.Join(" OR partNumber LIKE ", likes);

        command.CommandText = commandText + whereClause;

        using var reader = command.ExecuteReader();

        if (!reader.HasRows)
        {
            MessageBox.Show("No records found.", "Empty Result Collection", MessageBoxButton.OK, MessageBoxImage.Asterisk);
            return;
        }

        var recordNumber = 0;

        QueryResultsCollection.Clear();
        OriginalQueryResults.Clear();

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
