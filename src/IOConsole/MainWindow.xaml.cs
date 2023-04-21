using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using IOConsole.Data;
using IOConsole.Data.Serializable;
using DataFormats = System.Windows.DataFormats;
using DragEventArgs = System.Windows.DragEventArgs;
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
    private static readonly string TempCopyDir = Path.Combine(AppDir, "temp");

    private const string DatabaseFilename = "part_library.db";

    private static readonly string CurrentDatabaseFile = Path.Combine(DataDir, DatabaseFilename);
    private static readonly string BackupDatabaseFile = Path.Combine(DataBackupDir, DatabaseFilename);

    private static readonly LiteAccessor Accessor = new($"data/{DatabaseFilename}");

    private static readonly ObservableCollection<Part> QueryResultsCollection = new();
    private static readonly List<Part> OriginalQueryResults = new();

    private static readonly List<string> SelectedFiles = new();

    public MainWindow()
    {
        InitializeComponent();

        InitializeApplication();

        AddEvents();
    }

    private static void InitializeApplication()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            Directory.CreateDirectory(DataBackupDir);
            Directory.CreateDirectory(TempCopyDir);

            Accessor.InitializeDatabase();
        }
        catch (Exception e)
        {
            MessageBox.Show(e.Message, "Error During Initialization", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AddEvents()
    {
        QueryResultsDataGrid.ItemsSource = QueryResultsCollection;

        IOGroupBox.Drop += DropFilesIntoBox;
        CsvFilenameBox.Drop += DropFilesIntoBox;

        SelectCsvButton.Click += SelectCsvButtonOnClick;

        ImportCsvButton.Click += ImportCsvButtonOnClick;
        RollbackButton.Click += RollbackButtonOnClick;
        ExportCsvButton.Click += ExportCsvButtonOnClick;

        QueryPartNumberButton.Click += (_, _) => { QueryPartNumber(); };
        PartNumberInputBox.KeyDown += (_, keyEventArgs) =>
        {
            if (keyEventArgs.Key == Key.Return)
                QueryPartNumber();
        };

        UpdateRecordButton.Click += UpdateRecordButtonOnClick;
    }

    private void DropFilesIntoBox(object? sender, DragEventArgs e)
    {
        try
        {
            var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
            if (files is not null)
                CopyAndDisplayFiles(files);
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Select Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CopyAndDisplayFiles(string[] files)
    {
        if (files.Any(file => !file.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)))
            throw new Exception("Please select only csv files.");

        // avoid file access error
        foreach (var file in files)
        {
            var fi = new FileInfo(file);
            var copiedFile = Path.Combine(TempCopyDir, fi.Name);
            fi.CopyTo(Path.Combine(TempCopyDir, fi.Name));
            SelectedFiles.Add(copiedFile);
        }

        CsvFilenameBox.Text = string.Join(';', files);
    }

    private void SelectCsvButtonOnClick(object? sender, RoutedEventArgs args)
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Multiselect = true
            };

            var result = dialog.ShowDialog();

            if (result is not true) return;

            var files = dialog.FileNames;

            CopyAndDisplayFiles(files);
        }
        catch (Exception e)
        {
            MessageBox.Show(e.Message, "Select Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ClearCsvFiles()
    {
        CsvFilenameBox.Text = string.Empty;
        SelectedFiles.Clear();
        var di = new DirectoryInfo(TempCopyDir);
        foreach (var fi in di.GetFiles())
            fi.Delete();
    }

    private static PartColumnConfig GetColumnConfig(string file)
    {
        var firstLine = string.Empty;
        foreach (var line in File.ReadLines(file))
        {
            if (file.Length <= 0) continue;
            firstLine = line;
            break;
        }
        var columnSettingsWindow = new ColumnSettings(firstLine);
        var result = columnSettingsWindow.ShowDialog();

        if (result is not true)
        {
            throw new Exception("Column Settings not set properly.");
        }

        var columnConfig = (PartColumnConfig)columnSettingsWindow.DataContext;

        return columnConfig;
    }

    private void ImportCsvButtonOnClick(object? sender, RoutedEventArgs args)
    {
        try
        {
            if (SelectedFiles.Count == 0)
                throw new Exception("Please select filename first");

            // backup current database file, enabling overwrite
            File.Copy(CurrentDatabaseFile, BackupDatabaseFile, true);

            ImportCsvButton.Content = "Importing";
            ImportCsvButton.IsEnabled = false;

            var columnConfig = GetColumnConfig(SelectedFiles[0]);
            var affectedRows = Accessor.ImportCsvToDatabase(columnConfig, SelectedFiles);

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

            ClearCsvFiles();
        }
    }

    private static void RollbackButtonOnClick(object? sender, RoutedEventArgs args)
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

    private void ExportCsvButtonOnClick(object? sender, RoutedEventArgs args)
    {
        if (SelectedFiles.Count == 0)
        {
            MessageBox.Show("Please select one file to export.", "Oops",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (SelectedFiles.Count > 1)
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

            if (dr != System.Windows.Forms.DialogResult.OK) return;

            var partColumnConfig = GetColumnConfig(SelectedFiles[0]);
            Accessor.ExportCsv(partColumnConfig, dialog.SelectedPath, SelectedFiles[0]);

            result = MessageBox.Show("Exported successfully, open the output directory?", "Congratulations",
                MessageBoxButton.YesNo, MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
                Process.Start("explorer", dialog.SelectedPath);
        }
        catch (Exception e)
        {
            MessageBox.Show(e.Message, "Export Error", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally
        {
            ClearCsvFiles();
        }
    }

    private void QueryPartNumber()
    {
        try
        {
            var partNumberPattern = PartNumberInputBox.Text;
            if (partNumberPattern.Length == 0)
                throw new Exception("Search pattern cannot be empty!");

            QueryResultsCollection.Clear();
            OriginalQueryResults.Clear();

            var patterns = partNumberPattern.Split(',', StringSplitOptions.TrimEntries);
            var parts = Accessor.QueryParts(patterns);

            if (parts.Length == 0)
            {
                MessageBox.Show("No records found.", "Empty Result Collection", MessageBoxButton.OK, MessageBoxImage.Asterisk);
                return;
            }

            foreach (var part in parts)
            {
                QueryResultsCollection.Add(part);
                OriginalQueryResults.Add(part.Copy());  // avoid passing by reference
            }

            Status.Text = $"Found {parts.Length} record(s).";
        }
        catch (Exception e)
        {
            MessageBox.Show(e.Message, "Search Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateRecordButtonOnClick(object? sender, RoutedEventArgs args)
    {
        try
        {
            var updatedRecords = Accessor.UpdateRecords(OriginalQueryResults, QueryResultsCollection);

            if (updatedRecords > 0)
            {
                OriginalQueryResults.Clear();
                foreach (var part in QueryResultsCollection)
                    // avoid passing by reference, again
                    OriginalQueryResults.Add(part.Copy());
            }

            Status.Text = $"{updatedRecords} records updated.";
        }
        catch (Exception e)
        {
            MessageBox.Show(e.Message, "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
