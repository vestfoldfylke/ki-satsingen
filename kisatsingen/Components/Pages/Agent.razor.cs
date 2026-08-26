using Microsoft.AspNetCore.Components;

namespace kisatsingen.Components.Pages;

public partial class Agent : ComponentBase
{
    public string Something = "Heisann";

    protected override async Task OnInitializedAsync()
    {
        // Simulate asynchronous loading to demonstrate a loading indicator
        await Task.Delay(500);
        Something = "Agent kommer snart";
    }
}