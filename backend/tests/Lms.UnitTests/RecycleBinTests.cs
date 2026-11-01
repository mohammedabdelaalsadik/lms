using Lms.Modules.Learning.Application;
using Lms.Modules.Learning.Domain;
using Lms.SharedKernel;
using System.Text;

namespace Lms.UnitTests;

public class RecycleBinTests
{
    private static LocalizedText Text() => LocalizedText.Create("نص", "Text", 100).Value;

    private static CourseContent WithFile()
    {
        var content = CourseContent.Create(Guid.NewGuid());
        var section = content.AddSection(Text());
        content.AddFile(section.Id, Text(), true, new FileAsset("key", "a.pdf", "application/pdf", 10));
        return content;
    }

    [Fact]
    public void A_restored_activity_keeps_its_id_and_file()
    {
        var content = WithFile();
        var activity = content.OrderedActivities()[0];
        var (snapshot, section) = content.SnapshotActivity(activity.Id)!.Value;

        content.RemoveActivity(activity.Id);
        Assert.True(content.RestoreActivity(snapshot, section).IsSuccess);

        var back = Assert.Single(content.OrderedActivities());
        Assert.Equal(activity.Id, back.Id);
        Assert.Equal("key", back.FileKey);
        Assert.True(back.IsRequired);
    }

    [Fact]
    public void An_activity_returns_to_the_last_section_when_its_own_is_gone()
    {
        var content = WithFile();
        var (snapshot, section) = content.SnapshotActivity(content.OrderedActivities()[0].Id)!.Value;
        content.RemoveSection(section);
        var other = content.AddSection(Text());

        content.RestoreActivity(snapshot, section);

        Assert.Single(other.Activities);
    }

    [Fact]
    public void An_activity_needs_some_section_to_return_to()
    {
        var content = WithFile();
        var (snapshot, section) = content.SnapshotActivity(content.OrderedActivities()[0].Id)!.Value;
        content.RemoveSection(section);

        Assert.Equal(BinErrors.NoSection, content.RestoreActivity(snapshot, section).Error);
    }

    [Fact]
    public void A_section_returns_with_its_activities_at_the_end()
    {
        var content = WithFile();
        var first = content.OrderedSections()[0];
        var activity = content.OrderedActivities()[0].Id;
        var snapshot = content.SnapshotSection(first.Id)!;
        content.RemoveSection(first.Id);
        var other = content.AddSection(Text());

        content.RestoreSection(snapshot);

        Assert.Equal(other.Id, content.OrderedSections()[0].Id);
        Assert.Equal(first.Id, content.OrderedSections()[1].Id);
        Assert.Equal(activity, Assert.Single(content.OrderedActivities()).Id);
    }

    [Fact]
    public void The_bin_finds_every_file_of_a_section()
    {
        var content = WithFile();
        var snapshot = content.SnapshotSection(content.OrderedSections()[0].Id)!;
        var item = BinItem.Create(content.CourseId, BinItemKind.Section, "أ", "A", null, System.Text.Json.JsonSerializer.Serialize(snapshot), Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Equal(["key"], RecycleBin.FilesOf(item));
    }

    [Fact]
    public void Items_expire_after_the_retention_period()
    {
        var now = DateTimeOffset.UtcNow;
        var item = BinItem.Create(Guid.NewGuid(), BinItemKind.Activity, "أ", "A", Guid.NewGuid(), "{}", Guid.NewGuid(), now);

        Assert.Equal(now.AddDays(BinItem.RetentionDays), item.ExpiresAt);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"version\":99}")]
    [InlineData("{\"version\":1}")]
    public void Bad_manifests_are_refused(string text) =>
        Assert.Null(BackupFormat.Parse(new MemoryStream(Encoding.UTF8.GetBytes(text))));

    [Fact]
    public void A_manifest_survives_the_round_trip()
    {
        var manifest = new BackupManifest(
            BackupFormat.CurrentVersion,
            new("C-1", "أ", "A", "و", "D", 2),
            true,
            50,
            4,
            60,
            [new BackupSection("و", "U", [new BackupActivity(ActivityKind.Page, "ص", "P", true, "ن", "T", null, null, null)])],
            []);

        var back = BackupFormat.Parse(new MemoryStream(BackupFormat.Serialize(manifest)))!;

        Assert.Equal(ActivityKind.Page, back.Sections[0].Activities[0].Kind);
        Assert.Equal("C-1", back.Course.Code);
        Assert.Equal(60, back.PassMarkPercent);
    }
}
