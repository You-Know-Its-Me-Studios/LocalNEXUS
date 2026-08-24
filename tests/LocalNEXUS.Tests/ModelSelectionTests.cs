using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using LocalNEXUS.App.Services.Models;
using LocalNEXUS.App.Services.Persistence;
using LocalNEXUS.App.ViewModels;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// Choosing a repository fills the panel beside it.
/// </summary>
/// <remarks>
/// This is the bug that shipped, written down. The list bound its selection one way, so nothing
/// ever wrote the choice back to the view model, and the click was meant to be caught by a mouse
/// binding on the list, which a ListBoxItem handles first so it never fired. The row lit up, the
/// panel beside it went on saying choose a model on the left, and the whole right hand side of
/// the window was dead. Every part of it compiled and every unit test passed, because nothing
/// tested that selecting a thing does anything.
///
/// So this asserts on the view model rather than on the markup: setting the selection is what
/// loads the files, and that is the contract the markup now relies on.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class ModelSelectionTests
{
    private const string Repository = "owner/model-GGUF";

    /// <summary>Answers the tree listing and the model card, and nothing else.</summary>
    private sealed class HubHandler : HttpMessageHandler
    {
        public int TreeRequests { get; private set; }

        public int CardRequests { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct)
        {
            await Task.Yield();

            var url = request.RequestUri!.ToString();

            if (url.Contains("/tree/main", StringComparison.Ordinal))
            {
                TreeRequests++;

                return Json("""
                [
                  { "path": "model-Q4_K_M.gguf", "size": 4500000000, "lfs": { "oid": "aaaa" } },
                  { "path": "model-Q8_0.gguf",   "size": 9000000000 },
                  { "path": "README.md",         "size": 1200 }
                ]
                """);
            }

            if (url.EndsWith("README.md", StringComparison.Ordinal))
            {
                CardRequests++;
                return Text("# Model\n\nA model that does things.\n");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        private static HttpResponseMessage Text(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/plain")
        };
    }

    private static ModelBrowserViewModel Browser(HttpClient http)
    {
        var dialogs = new SilentDialogService();
        var catalogue = new ModelCatalogViewModel(new ModelCatalog(new AppConfig()), dialogs);

        return new ModelBrowserViewModel(http, catalogue, dialogs);
    }

    /// <summary>Waits for something the selection started, rather than assuming it finished.</summary>
    private static async Task<bool> Settles(Func<bool> done)
    {
        for (var waited = 0; waited < 5000; waited += 20)
        {
            if (done())
            {
                return true;
            }

            await Task.Delay(20);
        }

        return false;
    }

    [Fact]
    public async Task SelectingARepositoryListsWhatIsInIt()
    {
        var handler = new HubHandler();
        using var http = new HttpClient(handler);

        var browser = Browser(http);

        Assert.False(browser.HasSelection);
        Assert.Empty(browser.Files);

        // The only thing the list does. Everything else has to follow from it.
        browser.SelectedRepository = new ModelRepository(Repository, 100, 5);

        Assert.True(await Settles(() => browser.VisibleFiles.Count > 0), "the files never arrived");

        Assert.True(browser.HasSelection);
        Assert.Equal(1, handler.TreeRequests);

        // Two GGUF files, and the README is not one of them.
        Assert.Equal(2, browser.Files.Count);
        Assert.Equal(2, browser.VisibleFiles.Count);
        Assert.All(browser.Files, file => Assert.EndsWith(".gguf", file.File.Path, StringComparison.Ordinal));

        // Smallest first, and each one says which quantisation it is and what it weighs.
        Assert.Equal("model-Q4_K_M.gguf", browser.Files[0].File.Path);
        Assert.Contains("Q4_K_M", browser.Files[0].Quantisation, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("4.2 GB", browser.Files[0].SizeLabel);

        // The verdict is on every row, whatever it says on this machine.
        Assert.NotEmpty(browser.Files[0].FitBadge);
        Assert.NotEmpty(browser.Files[0].FitText);

        // And the download button has something to call.
        Assert.True(browser.DownloadCommand.CanExecute(browser.Files[0]));
    }

    [Fact]
    public async Task TheCardIsReadForWhateverWasSelected()
    {
        var handler = new HubHandler();
        using var http = new HttpClient(handler);

        var browser = Browser(http);
        browser.SelectedRepository = new ModelRepository(Repository, 100, 5);

        Assert.True(await Settles(() => browser.Card.Count > 0), "the card never arrived");

        Assert.Equal(1, handler.CardRequests);
        Assert.Equal(CardBlockKind.Heading, browser.Card[0].Kind);
        Assert.Equal("Model", browser.Card[0].Text);
        Assert.False(browser.HasCardNote);
    }

    /// <summary>Choosing a second repository replaces the first rather than adding to it.</summary>
    [Fact]
    public async Task ChoosingAnotherReplacesWhatWasThere()
    {
        var handler = new HubHandler();
        using var http = new HttpClient(handler);

        var browser = Browser(http);

        browser.SelectedRepository = new ModelRepository(Repository, 100, 5);
        Assert.True(await Settles(() => browser.VisibleFiles.Count > 0));

        browser.SelectedRepository = new ModelRepository("owner/other-GGUF", 1, 1);
        Assert.True(await Settles(() => handler.TreeRequests == 2));

        Assert.Equal(2, browser.Files.Count);
        Assert.Equal("owner/other-GGUF", browser.SelectedRepository!.Id);
    }

    /// <summary>The fit filter hides files rather than the panel forgetting them.</summary>
    [Fact]
    public async Task TheFilterHidesWithoutLosing()
    {
        var handler = new HubHandler();
        using var http = new HttpClient(handler);

        var browser = Browser(http);
        browser.SelectedRepository = new ModelRepository(Repository, 100, 5);

        Assert.True(await Settles(() => browser.VisibleFiles.Count > 0));

        var all = browser.Files.Count;
        browser.OnlyWhatFits = true;

        Assert.Equal(all, browser.Files.Count);
        Assert.True(browser.VisibleFiles.Count <= all);

        browser.OnlyWhatFits = false;
        Assert.Equal(all, browser.VisibleFiles.Count);
    }
}
