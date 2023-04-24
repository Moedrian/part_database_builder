using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text;
using IOConsole.Data.Serializable;

namespace IOConsole.Data;

public class LiteAccessor
{
    private readonly string _connectionString;

    public LiteAccessor(string databaseFilename)
    {
        // Avoid file access error in backup/restore by add Pooling=false in the connection string.
        _connectionString = $"Data Source={databaseFilename};Pooling=false";
    }

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

    public void InitializeDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
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

    public int ImportCsvToDatabase(PartColumnConfig columnConfig, string fieldSeparator, IEnumerable<string> inputFiles)
    {
        using var connection = new SqliteConnection(_connectionString);
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

            foreach (var file in inputFiles)
            {
                var lines = File.ReadAllLines(file);

                for (var i = columnConfig.SkippedRow; i < lines.Length; i++)
                {
                    if (lines[i].Length == 0) continue;

                    var elements = lines[i].Split(fieldSeparator, StringSplitOptions.TrimEntries);

                    if (elements[columnConfig.PartNumberColumn - 1].Length == 0)
                        continue;

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
    }

    public void ExportCsv(PartColumnConfig columnConfig, string fieldSeparator, string outputDirectory, string filename)
    {
        var lines = File.ReadAllLines(filename);
        var stringBuilder = new StringBuilder();

        var header = new string?[20];
        header[columnConfig.DrawingReferenceColumn - 1] = "Drawing Reference";
        header[columnConfig.PartNumberColumn - 1] = "Part Number";
        header[columnConfig.DeviceTypeColumn - 1] = "Device Type";
        header[columnConfig.DeviceNameColumn - 1] = "Device Name";
        header[columnConfig.ValueColumn - 1] = "Value";
        header[columnConfig.PositiveToleranceColumn - 1] = "Tol+";
        header[columnConfig.NegativeToleranceColumn - 1] = "Tol-";
        header[columnConfig.CaseColumn - 1] = "Case";
        header[columnConfig.CaseIdentifierColumn - 1] = "CaseIdentifier";
        var headerString = string.Join(fieldSeparator, header.Where(x => x is not null));

        stringBuilder.AppendLine(headerString);

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();

        for (var i = columnConfig.SkippedRow; i < lines.Length; i++)
        {
            if (lines[i].Length == 0) continue;

            var lineElements = lines[i].Split(fieldSeparator, StringSplitOptions.TrimEntries);
            var partNumber = lineElements[columnConfig.PartNumberColumn - 1];
            if (partNumber.Length == 0) continue;

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
                    var partLine = new string?[20];
                    partLine[columnConfig.DrawingReferenceColumn - 1] = drawingReference;
                    partLine[columnConfig.PartNumberColumn - 1] = partNumber;
                    partLine[columnConfig.DeviceTypeColumn - 1] = part.DeviceType;
                    partLine[columnConfig.DeviceNameColumn - 1] = part.DeviceName;
                    partLine[columnConfig.ValueColumn - 1] = part.Value;
                    partLine[columnConfig.PositiveToleranceColumn - 1] = part.PositiveTolerance;
                    partLine[columnConfig.NegativeToleranceColumn - 1] = part.NegativeTolerance;
                    partLine[columnConfig.CaseColumn - 1] = part.Case;
                    partLine[columnConfig.CaseIdentifierColumn - 1] = part.CaseIdentifier;
                    stringBuilder.AppendLine(string.Join(fieldSeparator, partLine.Where(x => x is not null)));
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

    public Part[] QueryParts(string[] partNumberPatterns)
    {

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();

        var likes = partNumberPatterns.Select(x => $"'%{x}%'").ToArray();
        var whereClause = " WHERE partNumber LIKE " + string.Join(" OR partNumber LIKE ", likes);

        command.CommandText = SelectJsonCommandText + whereClause;

        var resultList = new List<Part>();

        using var reader = command.ExecuteReader();

        if (!reader.HasRows)
        {
            return resultList.ToArray();
        }

        while (reader.Read())
        {
            var result = reader.GetString(0);
            var part = JsonSerializer.Deserialize<Part>(result);

            if (part is null) continue;

            resultList.Add(part);
        }

        return resultList.ToArray();
    }

    public int UpdateRecords(IList<Part> originalQueriedParts, IList<Part> modifiedQueriedParts)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var transaction = connection.BeginTransaction();

        try
        {
            var updatedCount = 0;
            foreach (var originalQueriedPart in originalQueriedParts)
            {
                var partNumber = originalQueriedPart.PartNumber;
                var modifiedQueriedPart = (from p in modifiedQueriedParts where p.PartNumber == partNumber select p).First();
                var updated = originalQueriedPart.Equals(modifiedQueriedPart);

                if (!updated) continue;

                using var command = connection.CreateCommand();

                command.CommandText = $@"
                        UPDATE
                            parts
                        SET
                            deviceType = '{modifiedQueriedPart.DeviceType}',
                            deviceName = '{modifiedQueriedPart.DeviceName}',
                            value = '{modifiedQueriedPart.Value}',
                            positiveTolerance = '{modifiedQueriedPart.PositiveTolerance}',
                            negativeTolerance = '{modifiedQueriedPart.NegativeTolerance}',
                            caseName = '{modifiedQueriedPart.Case}',
                            caseIdentifier = '{modifiedQueriedPart.CaseIdentifier}'
                        WHERE
                            partNumber = '{partNumber}'";

                command.ExecuteNonQuery();

                updatedCount++;
            }

            transaction.Commit();

            return updatedCount;
        }
        catch (Exception)
        {
            transaction.Rollback();
            throw;
        }
    }
}