using Lms.Modules.Learning.Application;
using Lms.Modules.Learning.Domain;
using System.IO.Compression;
using System.Text;

namespace Lms.UnitTests;

public class ScormTests
{
    private const string Manifest = """
        <?xml version="1.0"?>
        <manifest identifier="m" xmlns="http://www.imsproject.org/xsd/imscp_rootv1p1p2">
          <organizations default="org"><organization identifier="org"><item identifier="i" identifierref="res" parameters="?a=1"/></organization></organizations>
          <resources><resource identifier="other" href="other.html"/><resource identifier="res" href="content/start.html"/></resources>
        </manifest>
        """;

    private static MemoryStream Zip(params (string Name, string Text)[] files)
    {
        var stream = new MemoryStream();

        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text) in files)
            {
                using var entry = new StreamWriter(zip.CreateEntry(name).Open());
                entry.Write(text);
            }
        }

        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void Finds_the_launch_page_from_the_manifest() =>
        Assert.Equal("content/start.html?a=1", ScormPackage.Inspect(Zip(("imsmanifest.xml", Manifest), ("content/start.html", "x")))?.EntryPoint);

    [Fact]
    public void Refuses_a_zip_without_a_manifest() => Assert.Null(ScormPackage.Inspect(Zip(("index.html", "x"))));

    [Fact]
    public void Refuses_a_manifest_naming_a_missing_page() => Assert.Null(ScormPackage.Inspect(Zip(("imsmanifest.xml", Manifest))));

    [Fact]
    public void Refuses_files_that_are_not_zips() => Assert.Null(ScormPackage.Inspect(new MemoryStream(Encoding.UTF8.GetBytes("not a zip"))));

    [Fact]
    public void Refuses_manifests_with_document_definitions() =>
        Assert.Null(ScormPackage.Inspect(Zip(("imsmanifest.xml", """<?xml version="1.0"?><!DOCTYPE m [<!ENTITY x SYSTEM "file:///c:/windows/win.ini">]><m>&x;</m>"""))));

    [Theory]
    [InlineData("cmi.core.lesson_status", "completed", true)]
    [InlineData("cmi.core.lesson_status", "passed", true)]
    [InlineData("cmi.core.lesson_status", "failed", false)]
    [InlineData("cmi.core.lesson_status", "incomplete", false)]
    [InlineData("cmi.completion_status", "completed", true)]
    [InlineData("cmi.success_status", "passed", true)]
    [InlineData("cmi.success_status", "failed", false)]
    public void Reads_completion_from_both_scorm_versions(string key, string value, bool expected) =>
        Assert.Equal(expected, ScormResult.IsComplete(new Dictionary<string, string> { [key] = value }));

    [Fact]
    public void Saved_text_cannot_break_out_of_the_runtime_script()
    {
        var script = ScormRuntime.Script("""{"cmi.suspend_data":"</script><script>alert(1)</script>"}""", "/x");

        Assert.DoesNotContain("<script>alert", script);
    }

    [Fact]
    public void Completion_is_never_taken_back()
    {
        var now = DateTimeOffset.UtcNow;
        var attempt = ScormAttempt.Start(new ActivityId(Guid.NewGuid()), Guid.NewGuid(), Guid.NewGuid(), now);

        attempt.Save("{}", true, now);
        attempt.Save("{}", false, now.AddHours(1));

        Assert.Equal(now, attempt.CompletedAt);
    }
}
