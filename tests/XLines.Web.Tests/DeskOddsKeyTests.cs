using System.Net;
using XLines.Ingestion.Services;
using XLines.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace XLines.Web.Tests;

/// <summary>
/// The key typed into Ops: asked of the provider before it is kept, kept sealed and outside the
/// database, and in force again after a restart.
/// </summary>
public sealed class DeskOddsKeyTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "xlines-oddskey-" + Guid.NewGuid().ToString("N"));
    private readonly IDataProtectionProvider _protection = new EphemeralDataProtectionProvider();

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private sealed class Provider(HttpStatusCode status, bool throws = false) : HttpMessageHandler
    {
        public Uri? Asked { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Asked = request.RequestUri;
            if (throws)
                throw new HttpRequestException("no route");

            var response = new HttpResponseMessage(status) { Content = new StringContent("[]") };
            response.Headers.Add("x-requests-remaining", "431");
            return Task.FromResult(response);
        }
    }

    private sealed class Clients(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => new(handler, disposeHandler: false) { BaseAddress = new Uri("https://api.the-odds-api.com/v4/") };
    }

    private (DeskOddsKey Desk, RuntimeOddsKey Runtime, Provider Provider, List<string> Reconciled) Create(
        HttpStatusCode status = HttpStatusCode.OK, bool throws = false)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [DeskOddsKey.StorePathKey] = _directory })
            .Build();

        var runtime = new RuntimeOddsKey();
        var provider = new Provider(status, throws);
        var reconciled = new List<string>();

        var desk = new DeskOddsKey(
            _protection, runtime,
            _ => { reconciled.Add(runtime.TheOddsApi ?? ""); return Task.CompletedTask; },
            new Clients(provider), configuration, NullLogger<DeskOddsKey>.Instance);

        return (desk, runtime, provider, reconciled);
    }

    private string StoredFile => Path.Combine(_directory, "the-odds-api.key");

    [Fact]
    public async Task An_accepted_key_is_used_at_once_kept_sealed_and_reconciled()
    {
        var (desk, runtime, provider, reconciled) = Create();

        var check = await desk.SaveAsync("  abcd1234wxyz  ");

        Assert.Equal(OddsKeyVerdict.Accepted, check.Verdict);
        Assert.Equal(431, check.CreditsRemaining);
        Assert.Equal("abcd1234wxyz", runtime.TheOddsApi);
        Assert.Equal("wxyz", desk.Hint);
        Assert.Equal(["abcd1234wxyz"], reconciled);

        // The free list, never a priced call; and what is on disk is not the key.
        Assert.StartsWith("/v4/sports", provider.Asked!.AbsolutePath);
        Assert.DoesNotContain("odds", provider.Asked.AbsolutePath);
        Assert.DoesNotContain("abcd1234wxyz", File.ReadAllText(StoredFile));
    }

    [Fact]
    public async Task A_stored_key_is_in_force_again_after_a_restart()
    {
        var (first, _, _, _) = Create();
        await first.SaveAsync("abcd1234wxyz");

        var (second, runtime, _, _) = Create();
        second.Load();

        Assert.Equal("abcd1234wxyz", runtime.TheOddsApi);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, false, OddsKeyVerdict.Rejected)]
    [InlineData(HttpStatusCode.ServiceUnavailable, false, OddsKeyVerdict.Unreachable)]
    [InlineData(HttpStatusCode.OK, true, OddsKeyVerdict.Unreachable)]
    public async Task A_key_the_provider_did_not_accept_is_neither_kept_nor_used(
        HttpStatusCode status, bool throws, OddsKeyVerdict expected)
    {
        var (desk, runtime, _, reconciled) = Create(status, throws);

        var check = await desk.SaveAsync("abcd1234wxyz");

        Assert.Equal(expected, check.Verdict);
        Assert.Null(runtime.TheOddsApi);
        Assert.Null(desk.Hint);
        Assert.Empty(reconciled);
        Assert.False(File.Exists(StoredFile));
    }

    [Fact]
    public async Task Removing_the_key_forgets_it_here_and_on_disk()
    {
        var (desk, runtime, _, reconciled) = Create();
        await desk.SaveAsync("abcd1234wxyz");

        await desk.RemoveAsync();

        Assert.Null(runtime.TheOddsApi);
        Assert.False(File.Exists(StoredFile));
        Assert.Equal(["abcd1234wxyz", ""], reconciled);
    }

    [Fact]
    public void A_file_sealed_with_keys_that_are_gone_is_no_key_and_does_not_stop_the_host()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(StoredFile, "not-something-this-host-sealed");

        var (desk, runtime, _, _) = Create();
        desk.Load();

        Assert.Null(runtime.TheOddsApi);
    }
}
