using kisatsingen.Services;
using Xunit;

namespace kisatsingen.Tests.Services;

public sealed class TextTruncationTests
{
    [Fact]
    public void Text_within_the_limit_is_returned_as_it_is()
    {
        Assert.Equal("kort", TextTruncation.Prefix("kort", 10));
    }

    [Fact]
    public void Text_over_the_limit_is_cut_at_the_limit()
    {
        Assert.Equal("abc", TextTruncation.Prefix("abcdef", 3));
    }

    [Fact]
    public void A_limit_of_zero_gives_an_empty_string_rather_than_an_exception()
    {
        Assert.Equal(string.Empty, TextTruncation.Prefix("tekst", 0));
    }

    // Half an emoji is not valid UTF-16, and Postgres refuses it.
    [Fact]
    public void A_cut_inside_an_emoji_drops_the_whole_emoji_rather_than_half_of_it()
    {
        var prefix = TextTruncation.Prefix("ab😀cd", 3);

        Assert.Equal("ab", prefix);
    }

    // Woman, joiner, laptop make one emoji of five chars: a cut inside it
    // would leave a woman and a dangling joiner.
    [Fact]
    public void A_cut_inside_a_joined_emoji_drops_the_whole_emoji()
    {
        var prefix = TextTruncation.Prefix($"ab👩{HardToSeeCharacters.ZeroWidthJoiner}💻cd", 5);

        Assert.Equal("ab", prefix);
    }

    [Fact]
    public void A_cut_between_a_letter_and_its_combining_accent_drops_both()
    {
        var prefix = TextTruncation.Prefix($"abe{HardToSeeCharacters.CombiningAcuteAccent}cd", 3);

        Assert.Equal("ab", prefix);
    }
}
