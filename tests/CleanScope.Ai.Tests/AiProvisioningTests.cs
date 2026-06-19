using System.Net;
using System.Text;
using CleanScope.Ai.Chat;

namespace CleanScope.Ai.Tests;

// D: 模型检索 (GET /models) 的解析 —— OpenAI 标准 data[].id、兼容裸数组; 非成功状态抛错。
public sealed class AiProvisioningTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _code;
        private readonly string _body;
        public Uri? Requested;
        public StubHandler(string body, HttpStatusCode code = HttpStatusCode.OK) { _body = body; _code = code; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requested = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(_code)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            });
        }
    }

    [Fact]
    public async Task Parses_openai_data_id_list()
    {
        var stub = new StubHandler("""{ "data": [ { "id": "deepseek-chat" }, { "id": "gpt-4o" } ] }""");
        using var http = new HttpClient(stub);

        var models = await AiProvisioning.ListModelsAsync(http, "https://relay/v1", "sk-x");

        Assert.Equal(new[] { "deepseek-chat", "gpt-4o" }, models);
        Assert.Equal("https://relay/v1/models", stub.Requested!.ToString());
    }

    [Fact]
    public async Task Parses_bare_array_and_dedupes()
    {
        var stub = new StubHandler("""[ "a", { "id": "b" }, "a" ]""");
        using var http = new HttpClient(stub);

        var models = await AiProvisioning.ListModelsAsync(http, "https://relay/v1/", "sk-x");

        Assert.Equal(new[] { "a", "b" }, models);
    }

    [Fact]
    public async Task Non_success_status_throws_readable_error()
    {
        var stub = new StubHandler("unauthorized", HttpStatusCode.Unauthorized);
        using var http = new HttpClient(stub);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => AiProvisioning.ListModelsAsync(http, "https://relay/v1", "bad"));
        Assert.Contains("401", ex.Message);
    }

    [Fact]
    public async Task Empty_base_url_rejected()
        => await Assert.ThrowsAsync<ArgumentException>(
            () => AiProvisioning.ListModelsAsync(new HttpClient(new StubHandler("{}")), "", "k"));
}
