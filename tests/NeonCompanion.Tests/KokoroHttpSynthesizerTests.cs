using System.Net;
using NeonCompanion.Speech;
using NeonCompanion.Tests.Fakes;

namespace NeonCompanion.Tests;

public class KokoroHttpSynthesizerTests
{
    private const string Base = "http://127.0.0.1:8880";
    private const string SpeechUrl = Base + "/v1/audio/speech";
    private const string VoicesUrl = Base + "/v1/audio/voices";

    private static (StubHttpMessageHandler Stub, KokoroHttpSynthesizer Synth) Synth(TimeSpan? synthesisTimeout = null, TimeSpan? listTimeout = null)
    {
        var stub = new StubHttpMessageHandler();
        return (stub, new KokoroHttpSynthesizer(new Uri(Base), new HttpClient(stub), listTimeout, synthesisTimeout));
    }

    private static (List<int> Counts, long Total, Action<byte[], int> Sink) Collector()
    {
        var counts = new List<int>();
        long total = 0;
        return (counts, total, (_, count) => { counts.Add(count); Interlocked.Add(ref total, count); });
    }

    // ─── URLs and JSON ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("http://h:8880")]
    [InlineData("http://h:8880/")]
    [InlineData("http://h:8880/v1")]
    [InlineData("http://h:8880/v1/")]
    public void SpeechAndVoicesUrls_AppendToExactlyOneV1(string raw)
    {
        Assert.Equal("http://h:8880/v1/audio/speech", KokoroHttpSynthesizer.SpeechUrl(new Uri(raw)).AbsoluteUri);
        Assert.Equal("http://h:8880/v1/audio/voices", KokoroHttpSynthesizer.VoicesUrl(new Uri(raw)).AbsoluteUri);
    }

    [Fact]
    public void RequestJson_IsPinned_AndInvariant()
    {
        Assert.Equal(
            "{\"model\":\"kokoro\",\"input\":\"Hi.\",\"voice\":\"af_heart\",\"response_format\":\"pcm\",\"speed\":1,\"stream\":true}",
            KokoroHttpSynthesizer.RequestJson("Hi.", "af_heart", 1.0));
        Assert.Contains("\"speed\":1.25,", KokoroHttpSynthesizer.RequestJson("Hi.", "af_heart", 1.25));
    }

    [Fact]
    public void ParseVoices_AcceptsStringsAndObjects_DistinctAndSorted()
    {
        var voices = KokoroHttpSynthesizer.ParseVoices(
            "{\"voices\":[\"bf_emma\",{\"id\":\"af_heart\"},{\"name\":\"am_adam\"},\"af_heart\",\" \",42,{\"other\":1}]}");

        Assert.Equal(new[] { "af_heart", "am_adam", "bf_emma" }, voices);
        Assert.Equal(new[] { "a", "b" }, KokoroHttpSynthesizer.ParseVoices("[\"b\",\"a\"]"));
        Assert.Empty(KokoroHttpSynthesizer.ParseVoices("not json"));
        Assert.Empty(KokoroHttpSynthesizer.ParseVoices("{\"data\":[]}"));
        Assert.Empty(KokoroHttpSynthesizer.ParseVoices(""));
    }

    [Fact]
    public void Constructor_NormalisesTheBaseUrl_AndOwnsTheTransportOnlyWhenNoneIsGiven()
    {
        using var owned = new KokoroHttpSynthesizer(new Uri("http://localhost:8880/"));
        Assert.Equal("http://localhost:8880/v1", owned.BaseUrl.AbsoluteUri);
        Assert.True(owned.OwnsTransport);
        Assert.Equal(KokoroHttpSynthesizer.DefaultListTimeout, owned.AppliedListTimeout);
        Assert.Equal(KokoroHttpSynthesizer.DefaultSynthesisTimeout, owned.AppliedSynthesisTimeout);

        using var http = new HttpClient(new StubHttpMessageHandler().Map(VoicesUrl, HttpStatusCode.OK, "{}"));
        var borrowed = new KokoroHttpSynthesizer(new Uri(Base), http, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        Assert.False(borrowed.OwnsTransport);
        Assert.Equal(TimeSpan.FromSeconds(1), borrowed.AppliedListTimeout);
        Assert.Equal(TimeSpan.FromSeconds(2), borrowed.AppliedSynthesisTimeout);
        borrowed.Dispose();

        // Still the caller's after the synthesizer that borrowed it is gone.
        Assert.Null(Record.Exception(() => http.GetAsync(VoicesUrl).GetAwaiter().GetResult().Dispose()));

        Assert.Throws<ArgumentException>(() => new KokoroHttpSynthesizer(new Uri("ftp://h/v1")));
        Assert.Throws<ArgumentOutOfRangeException>(() => new KokoroHttpSynthesizer(new Uri(Base), http, TimeSpan.Zero));
    }

    // ─── Synthesis ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Synthesize_PostsKokoroJson_AndStreamsEvenChunksToSink()
    {
        var (stub, synth) = Synth();
        var pcm = new byte[9601];   // two full reads, one byte over: the dangling byte is dropped
        stub.Map(SpeechUrl, (_, _) => Task.FromResult(StubHttpMessageHandler.Bytes(HttpStatusCode.OK, pcm)));
        var (counts, _, sink) = Collector();

        var result = await synth.SynthesizeAsync("Hello there.", "af_heart", 1.25, sink, CancellationToken.None);

        Assert.True(result.Ok, result.Detail);
        Assert.Equal(9600, result.PcmBytes);
        Assert.All(counts, c => Assert.Equal(0, c % 2));
        Assert.Equal(9600, counts.Sum());

        var request = Assert.Single(stub.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(SpeechUrl, request.Uri.AbsoluteUri);
        Assert.Equal(KokoroHttpSynthesizer.RequestJson("Hello there.", "af_heart", 1.25), request.Body);
    }

    [Fact]
    public async Task Synthesize_OddSizedNetworkReads_StillHandOverWholeSamples()
    {
        var (stub, synth) = Synth();
        var pcm = Enumerable.Range(0, 20).Select(i => (byte)i).ToArray();
        stub.Map(SpeechUrl, (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new DribbleStream(pcm, 3)) }));

        var received = new List<byte>();
        var counts = new List<int>();
        var result = await synth.SynthesizeAsync("x", "af_heart", 1.0, (buffer, count) => { counts.Add(count); received.AddRange(buffer.Take(count)); }, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.All(counts, c => Assert.Equal(0, c % 2));
        Assert.Equal(pcm, received);   // nothing lost, nothing reordered, no byte duplicated
    }

    [Fact]
    public async Task Synthesize_ServerError_IsAResultNotAnException()
    {
        var (stub, synth) = Synth();
        stub.Map(SpeechUrl, HttpStatusCode.InternalServerError, "{\"detail\":\"voice not found\"}");

        var result = await synth.SynthesizeAsync("x", "zz_nobody", 1.0, (_, _) => { }, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Contains("HTTP 500 on /v1/audio/speech", result.Detail);
        Assert.Contains("voice not found", result.Detail);
    }

    [Fact]
    public async Task Synthesize_RefusedConnection_IsAResult()
    {
        var (_, synth) = Synth();

        var result = await synth.SynthesizeAsync("x", "af_heart", 1.0, (_, _) => { }, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Contains("refused", result.Detail);
    }

    [Fact]
    public async Task Synthesize_Timeout_IsAResult()
    {
        var (stub, synth) = Synth(synthesisTimeout: TimeSpan.FromMilliseconds(100));
        stub.Map(SpeechUrl, async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return StubHttpMessageHandler.Bytes(HttpStatusCode.OK, new byte[2]);
        });

        var result = await synth.SynthesizeAsync("x", "af_heart", 1.0, (_, _) => { }, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Contains("no answer within", result.Detail);
    }

    [Fact]
    public async Task Synthesize_CallerCancellation_Throws()
    {
        var (stub, synth) = Synth();
        stub.Map(SpeechUrl, async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return StubHttpMessageHandler.Bytes(HttpStatusCode.OK, new byte[2]);
        });
        using var cts = new CancellationTokenSource(50);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => synth.SynthesizeAsync("x", "af_heart", 1.0, (_, _) => { }, cts.Token));
    }

    [Fact]
    public async Task Synthesize_BlankText_SendsNothing()
    {
        var (stub, synth) = Synth();

        var result = await synth.SynthesizeAsync("   ", "af_heart", 1.0, (_, _) => throw new InvalidOperationException("no audio expected"), CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal(0, result.PcmBytes);
        Assert.Empty(stub.Requests);
    }

    // ─── Voices ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListVoices_ParsesTheServerList()
    {
        var (stub, synth) = Synth();
        stub.Map(VoicesUrl, HttpStatusCode.OK, StubHttpMessageHandler.VoicesJson("bf_emma", "af_heart"));

        var result = await synth.ListVoicesAsync(CancellationToken.None);

        Assert.True(result.Exists);
        Assert.Equal(new[] { "af_heart", "bf_emma" }, result.Voices);
        Assert.Equal("2 voices", result.Detail);
        Assert.Equal(HttpMethod.Get, Assert.Single(stub.Requests).Method);
    }

    [Fact]
    public async Task ListVoices_NoServer_IsMissing_AndDoesNotThrow()
    {
        var (_, synth) = Synth();
        var result = await synth.ListVoicesAsync(CancellationToken.None);
        Assert.False(result.Exists);
        Assert.Contains("refused", result.Detail);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task ListVoices_KeyProtected_StillExists(HttpStatusCode status)
    {
        var (stub, synth) = Synth();
        stub.Map(VoicesUrl, status, "{}");
        var result = await synth.ListVoicesAsync(CancellationToken.None);
        Assert.True(result.Exists);
        Assert.Empty(result.Voices);
    }

    [Fact]
    public async Task ListVoices_OtherStatusOrGarbage()
    {
        var (stub, synth) = Synth();
        stub.Map(VoicesUrl, HttpStatusCode.NotFound, "nope", "text/plain");
        var missing = await synth.ListVoicesAsync(CancellationToken.None);
        Assert.False(missing.Exists);
        Assert.Contains("404", missing.Detail);

        var (stub2, synth2) = Synth();
        stub2.Map(VoicesUrl, HttpStatusCode.OK, "<html>", "text/html");
        var garbage = await synth2.ListVoicesAsync(CancellationToken.None);
        Assert.True(garbage.Exists);
        Assert.Empty(garbage.Voices);
    }

    [Fact]
    public async Task ListVoices_Timeout_IsMissing()
    {
        var (stub, synth) = Synth(listTimeout: TimeSpan.FromMilliseconds(100));
        stub.Map(VoicesUrl, async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return StubHttpMessageHandler.Json(HttpStatusCode.OK, "{}");
        });

        var result = await synth.ListVoicesAsync(CancellationToken.None);

        Assert.False(result.Exists);
        Assert.Contains("no answer within", result.Detail);
    }

    /// <summary>A read-only stream that returns at most <c>chunk</c> bytes per read, to fake odd network framing.</summary>
    private sealed class DribbleStream : Stream
    {
        private readonly byte[] _data;
        private readonly int _chunk;
        private int _position;

        public DribbleStream(byte[] data, int chunk)
        {
            _data = data;
            _chunk = chunk;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _data.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = Math.Min(Math.Min(count, _chunk), _data.Length - _position);
            Buffer.BlockCopy(_data, _position, buffer, offset, n);
            _position += n;
            return n;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
