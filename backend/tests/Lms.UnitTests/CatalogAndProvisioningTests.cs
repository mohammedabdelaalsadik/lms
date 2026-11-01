using Lms.Modules.Catalog.Domain;
using Lms.Modules.Identity.Application;
using Lms.Modules.Identity.Domain;
using Lms.SharedKernel;

namespace Lms.UnitTests;

public class CategoryTests
{
    private static readonly LocalizedText Name = new("اسم", "Name");

    [Fact]
    public void Cannot_sit_inside_itself()
    {
        var category = CourseCategory.Create(Name, null);

        Assert.Equal(CategoryErrors.InvalidParent, category.Update(Name, category.Id.Value, []).Error);
    }

    [Fact]
    public void Cannot_sit_inside_its_own_child()
    {
        var parent = CourseCategory.Create(Name, null);
        var child = CourseCategory.Create(Name, parent.Id.Value);
        var grandchild = CourseCategory.Create(Name, child.Id.Value);
        var below = CourseCategory.Descendants(parent.Id.Value, [parent, child, grandchild]);

        Assert.Equal(2, below.Count);
        Assert.Equal(CategoryErrors.InvalidParent, parent.Update(Name, grandchild.Id.Value, below).Error);
    }

    [Fact]
    public void Can_move_elsewhere()
    {
        var category = CourseCategory.Create(Name, null);
        var other = Guid.NewGuid();

        Assert.True(category.Update(Name, other, []).IsSuccess);
        Assert.Equal(other, category.ParentId);
    }

    [Fact]
    public void Even_a_published_course_can_be_moved()
    {
        var course = Course.Create("C-1", Name, Name, 8, DateTimeOffset.UtcNow).Value;
        var category = Guid.NewGuid();
        course.Publish(DateTimeOffset.UtcNow);

        course.MoveTo(category);

        Assert.Equal(category, course.CategoryId);
    }
}

public class CsvReaderTests
{
    [Fact]
    public void Reads_quotes_commas_and_arabic()
    {
        var rows = CsvReader.Read("a,b\r\n\"x, y\",\"he said \"\"hi\"\"\"\nسارة,  z  \n");

        Assert.Equal(3, rows.Count);
        Assert.Equal(["x, y", "he said \"hi\""], rows[1].Cells);
        Assert.Equal(["سارة", "z"], rows[2].Cells);
    }

    [Fact]
    public void Skips_blank_lines_and_keeps_true_line_numbers()
    {
        var rows = CsvReader.Read("a\n\n,\nb\n");

        Assert.Equal(2, rows.Count);
        Assert.Equal(4, rows[1].Line);
    }
}

public class PasswordChangeRuleTests
{
    private static User New() => User.Register(new ProtectedNationalId("c", "h", "0008"), new LocalizedText("أ", "A"), "a@b.co", "old", Role.Trainee, Language.Ar, DateTimeOffset.UtcNow);

    [Fact]
    public void New_accounts_are_not_forced() => Assert.False(New().MustChangePassword);

    [Fact]
    public void Changing_clears_the_requirement()
    {
        var user = New();
        user.RequirePasswordChange();

        user.ChangePassword("new");

        Assert.False(user.MustChangePassword);
        Assert.Equal("new", user.PasswordHash);
    }
}
