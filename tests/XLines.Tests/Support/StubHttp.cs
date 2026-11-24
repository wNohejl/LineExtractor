using System.Net;

namespace XLines.Tests.Support;

/// <summary>
/// A provider that answers from the test instead of the network: a body for each request, chosen
/// by its URL, and a log of every address asked, which is how a test proves what went on the wire
/// and that nothing did. It holds no other state and checks nothing itself; a fake that builds its
/// answers from what it holds, or asserts inside the handler, stays a class of its own.
/// </summary>
public sealed class StubHttp : HttpMessageHandler
{
    private readonly Func<string, (HttpStatusCode Status, string Body)> respond;

    /// <summary>Answers every request with the one body.</summary>
    public StubHttp(string body) : this(_ => (HttpStatusCode.OK, body)) { }

    /// <summary>Answers each request with the body for its URL, as <see cref="Uri.ToString"/> writes it.</summary>
    public StubHttp(Func<string, string> respond) : this(url => (HttpStatusCode.OK, respond(url))) { }

    /// <summary>Answers each request with the status and body for its URL.</summary>
    public StubHttp(Func<string, (HttpStatusCode Status, string Body)> respond) => this.respond = respond;

    /// <summary>The <c>x-requests-last</c> header The Odds API bills with, or none.</summary>
    public int? RequestsLast { get; init; }

    /// <summary>The <c>x-requests-remaining</c> header The Odds API reports the key's month with, or none.</summary>
    public int? RequestsRemaining { get; init; }

    /// <summary>Every address asked, in order.</summary>
    public List<Uri> Asked { get; } = [];

    /// <summary>The last address asked, or null if nothing was.</summary>
    public Uri? Last => Asked.Count == 0 ? null : Asked[^1];

    /// <summary>Every address asked, as the strings the ESPN tests compare against.</summary>
    public List<string> Urls => Asked.ConvertAll(u => u.ToString());

    /// <summary>A client on this handler, addressed as the adapter's registration addresses it.</summary>
    public HttpClient Client(string baseAddress) => new(this) { BaseAddress = new Uri(baseAddress) };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;

        // A host's jobs may ask from more than one thread; the log must not lose a request.
        lock (Asked)
            Asked.Add(uri);

        var (status, body) = respond(uri.ToString());
        var response = new HttpResponseMessage(status) { Content = new StringContent(body) };
        if (RequestsLast is { } last)
            response.Headers.Add("x-requests-last", last.ToString());
        if (RequestsRemaining is { } left)
            response.Headers.Add("x-requests-remaining", left.ToString());

        return Task.FromResult(response);
    }
}
