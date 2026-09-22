using kisatsingen.Services.Chat;
using Xunit;

namespace kisatsingen.Tests.Services.Chat;

public sealed class ChatSessionModelSelectionTests
{
    [Fact]
    public async Task A_session_starts_on_the_catalogue_default()
    {
        await using var harness = new ChatSessionHarness();

        Assert.Equal(FakeChatModelCatalog.DefaultKey, harness.Session.SelectedModel.Key);
    }

    [Fact]
    public async Task Selecting_a_model_the_user_may_use_changes_the_selection()
    {
        await using var harness = new ChatSessionHarness();

        await harness.Session.SelectModelAsync(FakeChatModelCatalog.AlternativeKey);

        Assert.Equal(FakeChatModelCatalog.AlternativeKey, harness.Session.SelectedModel.Key);
    }

    // The key comes from the browser, so a key naming a model this user cannot use
    // must not select it. Today nothing is gated, and the check still has to hold:
    // the moment one model becomes role-gated, a path that skipped this is the way
    // around it.
    [Fact]
    public async Task A_key_that_is_not_in_the_users_available_models_is_refused()
    {
        await using var harness = new ChatSessionHarness();

        await harness.Session.SelectModelAsync(FakeChatModelCatalog.UnknownKey);

        Assert.Equal(FakeChatModelCatalog.DefaultKey, harness.Session.SelectedModel.Key);
    }

    [Fact]
    public async Task Selecting_a_model_notifies_the_ui()
    {
        await using var harness = new ChatSessionHarness();
        var notifications = 0;
        harness.Session.StateChanged += () => notifications++;

        await harness.Session.SelectModelAsync(FakeChatModelCatalog.AlternativeKey);

        Assert.Equal(1, notifications);
    }

    // Re-picking what is already selected is not a change, and a notification for
    // it would re-render the whole transcript for nothing.
    [Fact]
    public async Task Reselecting_the_current_model_notifies_nothing()
    {
        await using var harness = new ChatSessionHarness();
        var notifications = 0;
        harness.Session.StateChanged += () => notifications++;

        await harness.Session.SelectModelAsync(FakeChatModelCatalog.DefaultKey);

        Assert.Equal(0, notifications);
    }

    [Fact]
    public async Task A_turn_runs_on_the_model_that_was_selected_when_it_started()
    {
        await using var harness = new ChatSessionHarness();
        await harness.Session.SelectModelAsync(FakeChatModelCatalog.AlternativeKey);

        await harness.Session.SendAsync("hei");

        Assert.Equal(FakeChatModelCatalog.AlternativeKey, Assert.Single(harness.Catalog.ResolvedKeys));
    }

    // The whole point of capturing the model at the top of the turn: switching
    // while a response streams must not move the turn in flight onto a different
    // provider partway through.
    [Fact]
    public async Task Switching_during_a_turn_leaves_that_turn_on_its_original_model()
    {
        await using var harness = new ChatSessionHarness();
        var reachedTheModel = new TaskCompletionSource();
        harness.Client.OnStream = ct => ModelStream.Stalling(reachedTheModel, ct);

        var turn = harness.Session.SendAsync("hei");
        await reachedTheModel.Task;

        await harness.Session.SelectModelAsync(FakeChatModelCatalog.AlternativeKey);

        harness.Session.Cancel();
        await turn;

        // The turn resolved once, before the switch, and the switch did not send it
        // back to the catalogue for a different client.
        Assert.Equal(FakeChatModelCatalog.DefaultKey, Assert.Single(harness.Catalog.ResolvedKeys));
        Assert.Equal(FakeChatModelCatalog.AlternativeKey, harness.Session.SelectedModel.Key);
    }

    [Fact]
    public async Task Opening_another_chat_returns_to_the_default_model()
    {
        await using var harness = new ChatSessionHarness();
        await harness.Session.SelectModelAsync(FakeChatModelCatalog.AlternativeKey);

        await harness.Session.LoadAsync(Guid.NewGuid());

        Assert.Equal(FakeChatModelCatalog.DefaultKey, harness.Session.SelectedModel.Key);
    }
}
