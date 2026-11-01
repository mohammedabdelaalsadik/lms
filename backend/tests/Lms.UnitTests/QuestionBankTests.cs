using Lms.Modules.Assessment.Application;
using Lms.Modules.Assessment.Domain;
using Lms.SharedKernel;
using System.Text;

namespace Lms.UnitTests;

public class AnswerTextTests
{
    [Theory]
    [InlineData("  Hello   World ", "hello world")]
    [InlineData("أحمد", "احمد")]
    [InlineData("إسلام", "اسلام")]
    [InlineData("مدرسة", "مدرسه")]
    [InlineData("موسى", "موسي")]
    [InlineData("كَتَبَ", "كتب")]
    [InlineData("١٢٣", "123")]
    public void Normalize_ignores_spelling_variants(string input, string expected) =>
        Assert.Equal(expected, AnswerText.Normalize(input));

    [Theory]
    [InlineData("3.14", 3.14)]
    [InlineData("٣٫١٤", 3.14)]
    [InlineData(" 42 ", 42)]
    [InlineData("-5", -5)]
    public void Numbers_read_in_either_script(string input, double expected) =>
        Assert.Equal((decimal)expected, AnswerText.ParseNumber(input));

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    public void Non_numbers_are_refused(string input) => Assert.Null(AnswerText.ParseNumber(input));
}

public class QuestionKindTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly LocalizedText Title = new("اختبار", "Quiz");
    private static readonly Guid Seed = Guid.NewGuid();

    private static QuestionDraft Multi() => new(QuestionKind.MultipleResponse, "س", "Q", 2,
        [new OptionDraft("أ", "A", true), new OptionDraft("ب", "B", true), new OptionDraft("ج", "C", false)]);

    private static QuestionDraft Short() => new(QuestionKind.ShortAnswer, "س", "Q", 1, [new OptionDraft("الرياض", "Riyadh", true)]);

    private static QuestionDraft Numeric(decimal? tolerance = 0.5m) => new(QuestionKind.Numeric, "س", "Q", 1, [new OptionDraft("10", "10", true)], tolerance);

    private static QuestionDraft Matching() => new(QuestionKind.Matching, "س", "Q", 1,
        [new OptionDraft("مصر", "Egypt", true, "القاهرة", "Cairo"), new OptionDraft("الأردن", "Jordan", true, "عمّان", "Amman")]);

    private static QuizQuestion Only(QuestionDraft draft) =>
        Quiz.Create(Guid.NewGuid(), Title, 10, 1, null, null, 1, [draft]).Value.OrderedQuestions()[0];

    [Fact]
    public void All_correct_options_are_needed_and_no_wrong_ones()
    {
        var q = Only(Multi());
        var ids = q.Options.OrderBy(o => o.Position).Select(o => o.Id.Value.ToString()).ToList();

        Assert.True(q.IsCorrect($"{ids[1]},{ids[0]}", Seed));
        Assert.False(q.IsCorrect(ids[0], Seed));
        Assert.False(q.IsCorrect(string.Join(',', ids), Seed));
    }

    [Theory]
    [InlineData("الرياض", true)]
    [InlineData(" riyadh ", true)]
    [InlineData("جدة", false)]
    [InlineData("", false)]
    public void Short_answer_accepts_either_language(string response, bool expected) =>
        Assert.Equal(expected, Only(Short()).IsCorrect(response, Seed));

    [Theory]
    [InlineData("10", true)]
    [InlineData("10.5", true)]
    [InlineData("9.5", true)]
    [InlineData("10.6", false)]
    [InlineData("١٠", true)]
    [InlineData("ten", false)]
    public void Numeric_allows_the_tolerance(string response, bool expected) =>
        Assert.Equal(expected, Only(Numeric()).IsCorrect(response, Seed));

    [Fact]
    public void Matching_needs_every_pair_right()
    {
        var q = Only(Matching());
        var left = q.Options.OrderBy(o => o.Position).Select(o => o.Id.Value).ToList();
        string Key(Guid id) => AnswerText.SecretKey(Seed, id);

        Assert.True(q.IsCorrect($"{left[0]}:{Key(left[0])},{left[1]}:{Key(left[1])}", Seed));
        Assert.False(q.IsCorrect($"{left[0]}:{Key(left[1])},{left[1]}:{Key(left[0])}", Seed));
        Assert.False(q.IsCorrect($"{left[0]}:{Key(left[0])}", Seed));
    }

    [Fact]
    public void Matching_keys_differ_by_attempt_and_do_not_show_the_option() =>
        Assert.NotEqual(AnswerText.SecretKey(Guid.NewGuid(), Seed), AnswerText.SecretKey(Guid.NewGuid(), Seed));

    [Fact]
    public void A_malformed_response_is_simply_wrong()
    {
        Assert.False(Only(Multi()).IsCorrect("not-a-guid", Seed));
        Assert.False(Only(Matching()).IsCorrect("nonsense", Seed));
    }

    public static TheoryData<QuestionDraft> Incomplete() => new()
    {
        new QuestionDraft(QuestionKind.MultipleResponse, "س", "Q", 1, [new OptionDraft("أ", "A", false), new OptionDraft("ب", "B", false)]),
        new QuestionDraft(QuestionKind.ShortAnswer, "س", "Q", 1, []),
        new QuestionDraft(QuestionKind.Numeric, "س", "Q", 1, [new OptionDraft("x", "x", true)]),
        new QuestionDraft(QuestionKind.Numeric, "س", "Q", 1, [new OptionDraft("1", "1", true)], -1),
        new QuestionDraft(QuestionKind.Matching, "س", "Q", 1, [new OptionDraft("أ", "A", true), new OptionDraft("ب", "B", true)]),
    };

    [Theory]
    [MemberData(nameof(Incomplete))]
    public void Incomplete_questions_cannot_be_published(QuestionDraft draft) =>
        Assert.Equal(AssessmentErrors.QuizNotReady, Quiz.Create(Guid.NewGuid(), Title, 10, 1, null, null, 1, [draft]).Value.Publish().Error);

    [Fact]
    public void Each_attempt_draws_the_set_number_and_is_scored_on_those_only()
    {
        var quiz = Quiz.Create(Guid.NewGuid(), Title, 10, 3, null, null, 1, [Short(), Short(), Short(), Short(), Short()], 2, true).Value;
        quiz.Publish();

        var attempt = QuizAttempt.Start(quiz, Guid.NewGuid(), 1, Now);
        var drawn = attempt.QuestionsFor(quiz);

        Assert.Equal(2, drawn.Count);
        Assert.Equal(2, attempt.QuestionsFor(quiz).Intersect(drawn).Count());

        var outside = quiz.OrderedQuestions().First(q => !drawn.Contains(q));
        Assert.Equal(AssessmentErrors.InvalidAnswer, attempt.SaveAnswer(quiz, outside.Id, "Riyadh", Now).Error);

        attempt.SaveAnswer(quiz, drawn.First().Id, "Riyadh", Now);
        attempt.Finish(quiz, Now.AddMinutes(1));

        Assert.Equal(50m, attempt.ScorePercent);
    }

    [Fact]
    public void Drawing_more_than_exist_blocks_publishing() =>
        Assert.Equal(AssessmentErrors.QuizNotReady, Quiz.Create(Guid.NewGuid(), Title, 10, 1, null, null, 1, [Short()], 2).Value.Publish().Error);

    [Fact]
    public void Clearing_an_answer_removes_it()
    {
        var quiz = Quiz.Create(Guid.NewGuid(), Title, 10, 1, null, null, 1, [Short()]).Value;
        quiz.Publish();
        var attempt = QuizAttempt.Start(quiz, Guid.NewGuid(), 1, Now);
        var question = quiz.OrderedQuestions()[0];

        attempt.SaveAnswer(quiz, question.Id, "Riyadh", Now);
        attempt.SaveAnswer(quiz, question.Id, "", Now);

        Assert.Empty(attempt.Answers);
    }

    [Fact]
    public void Questions_keep_their_order_within_one_attempt()
    {
        var quiz = Quiz.Create(Guid.NewGuid(), Title, 10, 1, null, null, 1, [Short(), Short(), Short(), Short()], null, true).Value;
        var attempt = QuizAttempt.Start(quiz, Guid.NewGuid(), 1, Now);

        Assert.Equal(attempt.QuestionsFor(quiz), attempt.QuestionsFor(quiz));
    }
}

public class BankQuestionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    private static QuestionDraft Good() => new(QuestionKind.TrueFalse, "س", "Q", 1, [new OptionDraft("صحيح", "True", true), new OptionDraft("خطأ", "False", false)]);

    [Fact]
    public void Refuses_an_incomplete_question() =>
        Assert.Equal(AssessmentErrors.QuestionIncomplete, BankQuestion.Create(Guid.NewGuid(), "", Good() with { Points = 0 }, Guid.NewGuid(), Now).Error);

    [Fact]
    public void Failed_edit_changes_nothing()
    {
        var question = BankQuestion.Create(Guid.NewGuid(), "الأول", Good(), Guid.NewGuid(), Now).Value;

        question.Update("الثاني", Good() with { Options = [] });

        Assert.Equal("الأول", question.Category);
    }

    [Fact]
    public void Archive_keeps_the_first_date()
    {
        var question = BankQuestion.Create(Guid.NewGuid(), "", Good(), Guid.NewGuid(), Now).Value;

        question.Archive(Now);
        question.Archive(Now.AddDays(1));

        Assert.Equal(Now, question.ArchivedAt);
    }
}

public class MoodleXmlTests
{
    private const string Sample = """
        <?xml version="1.0" encoding="UTF-8"?>
        <quiz>
          <question type="category"><category><text>$course$/top/الفصل الأول</text></category></question>
          <question type="multichoice">
            <questiontext format="html"><text><![CDATA[<p>عاصمة <b>مصر</b>؟</p>]]></text></questiontext>
            <defaultgrade>2.0000000</defaultgrade><single>true</single>
            <answer fraction="100"><text>القاهرة</text></answer>
            <answer fraction="0"><text>جدة</text></answer>
          </question>
          <question type="multichoice">
            <questiontext><text>Pick two</text></questiontext><single>false</single>
            <answer fraction="50"><text>A</text></answer>
            <answer fraction="50"><text>B</text></answer>
            <answer fraction="-100"><text>C</text></answer>
          </question>
          <question type="truefalse">
            <questiontext><text>Sky is blue</text></questiontext>
            <answer fraction="100"><text>true</text></answer>
            <answer fraction="0"><text>false</text></answer>
          </question>
          <question type="shortanswer">
            <questiontext><text>Capital of KSA</text></questiontext>
            <answer fraction="100"><text>Riyadh</text></answer>
          </question>
          <question type="numerical">
            <questiontext><text>2 + 2</text></questiontext>
            <answer fraction="100"><text>4</text><tolerance>0.1</tolerance></answer>
          </question>
          <question type="matching">
            <questiontext><text>Match</text></questiontext>
            <subquestion><text>Egypt</text><answer><text>Cairo</text></answer></subquestion>
            <subquestion><text>Jordan</text><answer><text>Amman</text></answer></subquestion>
          </question>
          <question type="essay"><questiontext><text>Discuss</text></questiontext></question>
          <question type="multichoice">
            <questiontext><text><![CDATA[<img src="@@PLUGINFILE@@/a.png"/> What is this?]]></text></questiontext>
            <answer fraction="100"><text>A</text></answer><answer fraction="0"><text>B</text></answer>
          </question>
        </quiz>
        """;

    private static MoodleImport? Read(string xml) => MoodleQuestionXml.Read(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    [Fact]
    public void Reads_every_supported_kind_and_counts_the_rest()
    {
        var result = Read(Sample)!;

        Assert.Equal(
            [QuestionKind.MultipleChoice, QuestionKind.MultipleResponse, QuestionKind.TrueFalse, QuestionKind.ShortAnswer, QuestionKind.Numeric, QuestionKind.Matching, QuestionKind.Essay],
            result.Questions.Select(q => q.Draft.Kind));
        Assert.Equal(1, result.Unsupported);
    }

    [Fact]
    public void Keeps_category_points_and_plain_text()
    {
        var first = Read(Sample)!.Questions[0];

        Assert.Equal("الفصل الأول", first.Category);
        Assert.Equal(2, first.Draft.Points);
        Assert.Equal("عاصمة مصر ؟", first.Draft.TextAr);
    }

    [Fact]
    public void Negative_options_stay_wrong_in_multiple_answer() =>
        Assert.Equal([true, true, false], Read(Sample)!.Questions[1].Draft.Options.Select(o => o.IsCorrect));

    [Fact]
    public void Numeric_keeps_its_tolerance() => Assert.Equal(0.1m, Read(Sample)!.Questions[4].Draft.Tolerance);

    [Theory]
    [InlineData("not xml")]
    [InlineData("<other/>")]
    public void Refuses_other_files(string text) => Assert.Null(Read(text));

    [Fact]
    public void Refuses_files_with_document_definitions() =>
        Assert.Null(Read("""<?xml version="1.0"?><!DOCTYPE quiz [<!ENTITY x SYSTEM "file:///c:/windows/win.ini">]><quiz>&x;</quiz>"""));
}
