using System.Text;

namespace Lms.BuildingBlocks.Application;

public sealed record CsvFile(string FileName, byte[] Content);

/// <summary>Builds exports that open correctly in Excel, Arabic included.</summary>
public static class CsvWriter
{
    private const string Formula = "=+-@\t\r";

    public static CsvFile Write(string fileName, IEnumerable<IReadOnlyList<string?>> rows)
    {
        var text = new StringBuilder("﻿");

        foreach (var row in rows)
        {
            text.AppendJoin(',', row.Select(Cell)).Append("\r\n");
        }

        return new CsvFile(fileName, Encoding.UTF8.GetBytes(text.ToString()));
    }

    /// <summary>A leading apostrophe keeps a typed name or reason from running as a spreadsheet formula.</summary>
    public static string Cell(string? value)
    {
        var text = value ?? "";

        if (text.Length > 0 && Formula.Contains(text[0]) && !decimal.TryParse(text, out _))
        {
            text = $"'{text}";
        }

        return $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
