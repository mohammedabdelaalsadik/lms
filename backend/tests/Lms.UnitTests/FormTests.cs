using Lms.Modules.Learning.Application;
using Lms.Modules.Learning.Domain;

namespace Lms.UnitTests;

public class FormTests
{
    private static readonly FormOption[] TwoOptions = [new("نعم", "Yes"), new("لا", "No")];

    private static FormDefinition Form(bool anonymous = false) => new(anonymous,
    [
        new(FormQuestionKind.Single, "سؤال", "Pick", true, TwoOptions),
        new(FormQuestionKind.Multiple, "سؤال", "Pick many", false, TwoOptions),
        new(FormQuestionKind.Rating, "سؤال", "Rate", false, []),
        new(FormQuestionKind.Text, "سؤال", "Say", false, []),
    ]);

    private static FormAnswer Single(int option) => new(0, [option], null, null);

    [Fact]
    public void A_good_form_is_valid() => Assert.True(Form().IsValid);

    [Fact]
    public void A_form_needs_questions() => Assert.False(new FormDefinition(false, []).IsValid);

    [Fact]
    public void A_choice_needs_two_options() =>
        Assert.False(new FormDefinition(false, [new(FormQuestionKind.Single, "س", "Q", true, [new("أ", "A")])]).IsValid);

    [Fact]
    public void Questions_need_both_languages() =>
        Assert.False(new FormDefinition(false, [new(FormQuestionKind.Text, "س", " ", true, [])]).IsValid);

    [Fact]
    public void A_required_question_must_be_answered() => Assert.Null(Form().Accept([]));

    [Fact]
    public void Optional_questions_can_stay_blank() => Assert.Single(Form().Accept([Single(0), new(3, null, null, "  ")])!);

    [Theory]
    [InlineData(2)]
    [InlineData(-1)]
    public void An_option_must_exist(int option) => Assert.Null(Form().Accept([Single(option)]));

    [Fact]
    public void A_single_choice_takes_one_option() => Assert.Null(Form().Accept([new(0, [0, 1], null, null)]));

    [Fact]
    public void Repeated_options_are_refused() => Assert.Null(Form().Accept([Single(0), new(1, [1, 1], null, null)]));

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Ratings_run_from_one_to_five(int rating) => Assert.Null(Form().Accept([Single(0), new(2, null, rating, null)]));

    [Fact]
    public void Unknown_questions_are_refused() => Assert.Null(Form().Accept([Single(0), new(9, null, null, "x")]));

    [Fact]
    public void A_question_is_answered_once() => Assert.Null(Form().Accept([Single(0), Single(1)]));

    [Fact]
    public void Very_long_text_is_refused() =>
        Assert.Null(Form().Accept([Single(0), new(3, null, null, new string('x', FormDefinition.AnswerMaxLength + 1))]));

    [Fact]
    public void An_anonymous_response_carries_no_trainee_and_a_random_id()
    {
        var response = FormResponse.Create(new ActivityId(Guid.NewGuid()), Guid.NewGuid(), null, "[]");

        Assert.Null(response.TraineeId);
        Assert.Equal(4, response.Id.Version);
    }

    [Fact]
    public void Answers_add_up()
    {
        var responses = new List<(Guid?, IReadOnlyList<FormAnswer>)>
        {
            (null, [Single(0), new(2, null, 5, null), new(3, null, null, "جيد")]),
            (null, [Single(0), new(2, null, 2, null)]),
            (null, [Single(1)]),
        };

        var tally = FormResults.Tally(Form(true), responses, new Dictionary<Guid, TextDto>());

        Assert.Equal([2, 1], tally[0].Options.Select(o => o.Count));
        Assert.Equal(3.5m, tally[2].Average);
        Assert.Equal(2, tally[2].Answered);
        var text = Assert.Single(tally[3].Answers);
        Assert.Null(text.Name);
    }

    [Fact]
    public void Named_answers_show_who_wrote_them()
    {
        var trainee = Guid.NewGuid();
        var names = new Dictionary<Guid, TextDto> { [trainee] = new("سارة", "Sara") };

        var tally = FormResults.Tally(Form(), [(trainee, [Single(0), new(3, null, null, "جيد")])], names);

        Assert.Equal("Sara", tally[3].Answers[0].Name!.En);
    }

    [Fact]
    public void A_form_survives_being_stored()
    {
        var back = FormCodec.ReadForm(FormCodec.Write(Form(true)))!;

        Assert.True(back.IsAnonymous);
        Assert.Equal(FormQuestionKind.Rating, back.Questions[2].Kind);
    }

    [Fact]
    public void Forms_travel_through_the_recycle_bin()
    {
        var content = CourseContent.Create(Guid.NewGuid());
        var section = content.AddSection(Lms.SharedKernel.LocalizedText.Create("و", "U", 100).Value);
        content.AddForm(section.Id, Lms.SharedKernel.LocalizedText.Create("ن", "F", 100).Value, true, "{}");
        var activity = content.OrderedActivities()[0];
        var (snapshot, sectionId) = content.SnapshotActivity(activity.Id)!.Value;

        content.RemoveActivity(activity.Id);
        content.RestoreActivity(snapshot, sectionId);

        Assert.Equal("{}", content.OrderedActivities()[0].FormJson);
    }
}
