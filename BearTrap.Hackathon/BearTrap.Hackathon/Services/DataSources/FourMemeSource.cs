using BearTrap.Hackathon.Application.Abstractions;
using BearTrap.Hackathon.Domain;

namespace BearTrap.Hackathon.Services.DataSources;

/// <summary>
/// Adapter that implements IFourMemeMainListSource using the Four.Meme API client.
/// Provides access to the main list of Four.Meme tokens.
/// </summary>
public sealed class FourMemeSource : IFourMemeMainListSource
{
    private readonly IFourMemeClient _fourMemeClient;

    public FourMemeSource(IFourMemeClient fourMemeClient)
    {
        _fourMemeClient = fourMemeClient ?? throw new ArgumentNullException(nameof(fourMemeClient));
    }

    public async Task<bool> IsFourMemeLaunchAsync(string tokenAddress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(tokenAddress))
            return false;

        return await _fourMemeClient.IsTokenOnMainListAsync(tokenAddress, ct);
    }

    public async Task<IReadOnlyList<FourMemeListedToken>> GetMainListAsync(int pageSize, CancellationToken ct)
    {
        return await _fourMemeClient.GetMainListAsync(pageSize, ct);
    }
}
