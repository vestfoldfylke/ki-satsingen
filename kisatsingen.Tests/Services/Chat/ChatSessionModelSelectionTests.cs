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

    // The key comes from the browser; the check must hold before any model is gated.
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

    // A notification re-renders the whole transcript.
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
