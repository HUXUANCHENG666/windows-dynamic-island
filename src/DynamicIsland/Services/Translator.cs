using DynamicIsland.Core.Translation;
namespace DynamicIsland.Services;
sealed class Translator : IDisposable
{
    readonly TranslationClient _client = new();
    public Task<string> TranslateAsync(TranslateRequest request, CancellationToken token) => TranslateAsync(request, new(TranslateApiKind.Default, null, null, null), token);
    public Task<string> TranslateAsync(TranslateRequest request, TranslateApiOptions api, CancellationToken token) => _client.TranslateAsync(request, api, token);
    public void Dispose() => _client.Dispose();
}
