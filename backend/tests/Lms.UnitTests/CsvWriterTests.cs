using Lms.BuildingBlocks.Application;
using System.Text;

namespace Lms.UnitTests;

public class CsvWriterTests
{
    [Theory]
    [InlineData("=SUM(A1)", "\"'=SUM(A1)\"")]
    [InlineData("+966500", "\"+966500\"")]
    [InlineData("@cmd", "\"'@cmd\"")]
    [InlineData("-5", "\"-5\"")]
    [InlineData("-5+cmd", "\"'-5+cmd\"")]
    [InlineData("عادي", "\"عادي\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData(null, "\"\"")]
    public void Cells_are_quoted_and_cannot_run_as_formulas(string? value, string expected) =>
        Assert.Equal(expected, CsvWriter.Cell(value));

    [Fact]
    public void Starts_with_a_marker_so_excel_reads_arabic()
    {
        var file = CsvWriter.Write("a.csv", [["سارة"]]);

        Assert.Equal([0xEF, 0xBB, 0xBF], file.Content[..3]);
        Assert.Contains("سارة", Encoding.UTF8.GetString(file.Content));
    }
}
