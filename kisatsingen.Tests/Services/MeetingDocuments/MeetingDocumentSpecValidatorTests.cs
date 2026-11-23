using kisatsingen.Services.MeetingDocuments;
using Xunit;

namespace kisatsingen.Tests.Services.MeetingDocuments;

public sealed class MeetingDocumentSpecValidatorTests
{
    private static readonly AgendaItemSpec Item = new("Budsjett", "1");

    private static MeetingDocumentSpec Spec(params AgendaItemSpec[] items) => new("Referat", MeetingDocumentKind.Minutes, "Styremøte", items);

    [Fact]
    public void A_spec_with_only_the_required_fields_has_no_problems()
    {
        Assert.Empty(MeetingDocumentSpecValidator.Validate(Spec(Item)));
    }

    [Fact]
    public void A_meeting_without_agenda_items_has_no_problems()
    {
        Assert.Empty(MeetingDocumentSpecValidator.Validate(Spec()));
    }

    [Fact]
    public void A_meeting_with_more_agenda_items_than_the_limit_is_refused()
    {
        var problems = MeetingDocumentSpecValidator.Validate(Spec([.. Enumerable.Repeat(Item, MeetingDocumentSpecValidator.MaxAgendaItems + 1)]));

        Assert.Equal(["Sakslisten har 41 saker; den kan ha opptil 40. Del møtet på flere dokumenter, eller slå sammen saker."], problems);
    }

    [Fact]
    public void A_meeting_without_a_name_and_an_item_without_a_number_have_no_problems()
    {
        Assert.Empty(MeetingDocumentSpecValidator.Validate(Spec(new AgendaItemSpec("Budsjett")) with { Meeting = null }));
    }

    [Fact]
    public void An_agenda_item_without_a_description_is_refused_by_position()
    {
        var problems = MeetingDocumentSpecValidator.Validate(Spec(Item, new AgendaItemSpec("", "2")));

        Assert.Equal(["Sak 2: description er tom. Skriv inn tekst, eller utelat feltet der det er valgfritt."], problems);
    }

    [Fact]
    public void An_unknown_kind_is_refused_naming_the_kinds()
    {
        var problems = MeetingDocumentSpecValidator.Validate(Spec(Item) with { Kind = (MeetingDocumentKind)9 });

        Assert.Equal(["Typen 9 finnes ikke. Velg en av: Notice, Minutes."], problems);
    }

    [Fact]
    public void The_structure_check_accepts_a_spec_that_is_only_over_the_size_limits()
    {
        var spec = Spec([.. Enumerable.Repeat(new AgendaItemSpec(new string('x', MeetingDocumentSpecValidator.MaxDescriptionLength + 1), "1"), MeetingDocumentSpecValidator.MaxAgendaItems + 1)]);

        Assert.Empty(MeetingDocumentSpecValidator.ValidateStructure(spec));
    }
}
