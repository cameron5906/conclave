using Conclave.Abstractions;

namespace Conclave.Extensions.AspNetCore;

public class ProviderRegistry
{
    private readonly Dictionary<string, ILlmProvider> _providers = new(StringComparer.OrdinalIgnoreCase);

    public void Register(string key, ILlmProvider provider)
    {
        _providers[key] = provider;
    }

    public ILlmProvider? Get(string key)
    {
        return _providers.TryGetValue(key, out var provider) ? provider : null;
    }

    public IReadOnlyDictionary<string, ILlmProvider> GetAll() => _providers;

    public bool Contains(string key) => _providers.ContainsKey(key);
}
