using BearTrap.Hackathon.Application.Abstractions;
using BearTrap.Hackathon.Domain;

namespace BearTrap.Hackathon.Infrastructure.DataSources;

/// <summary>
/// Mock implementation of IChainDataSource for offline/demo mode.
/// Returns deterministic mock data without making any HTTP calls.
/// </summary>
public class OffchainChainDataSource : IChainDataSource
{
    private static readonly IReadOnlyList<LatestToken> MockTokens = new List<LatestToken>
    {
        new LatestToken(
            Address: "0x1234567890123456789012345678901234567890",
            Name: "Mock Token Alpha",
            Symbol: "MTA",
            Creator: "0x0000000000000000000000000000000000000001",
            CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-30),
            ImageUrl: "/mock-images/token-1.svg"
        ),
        new LatestToken(
            Address: "0x0987654321098765432109876543210987654321",
            Name: "Mock Token Beta",
            Symbol: "MTB",
            Creator: "0x0000000000000000000000000000000000000002",
            CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-15),
            ImageUrl: "/mock-images/token-2.svg"
        ),
        new LatestToken(
            Address: "0xabcdefabcdefabcdefabcdefabcdefabcdefabcd",
            Name: "Mock Token Gamma",
            Symbol: "MTG",
            Creator: "0x0000000000000000000000000000000000000003",
            CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-5),
            ImageUrl: "/mock-images/token-3.svg"
        ),
        new LatestToken(
            Address: "0xfedcbafedcbafedcbafedcbafedcbafedcbafed",
            Name: "Mock Four.Meme Token",
            Symbol: "MFM",
            Creator: "0x0000000000000000000000000000000000000004",
            CreatedAt: DateTimeOffset.UtcNow.AddSeconds(-30),
            ImageUrl: "/mock-images/token-4.svg"
        ),
    };

    // Mock tokens that are considered to be from Four.Meme
    private static readonly HashSet<string> FourMemeTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "0xfedcbafedcbafedcbafedcbafedcbafedcbafed", // Mock Four.Meme Token
    };

    public Task<IReadOnlyList<LatestToken>> GetLatestTokensAsync(int count, CancellationToken ct)
    {
        // Return mock tokens up to the requested count, most recent first
        var result = MockTokens.Take(Math.Max(0, count)).ToList();
        return Task.FromResult<IReadOnlyList<LatestToken>>(result);
    }

    public Task<bool> IsTokenFromFourMemeAsync(string tokenAddress, CancellationToken ct)
    {
        // Check if token address is in the mock four.meme list
        var isFromFourMeme = FourMemeTokens.Contains(tokenAddress);
        return Task.FromResult(isFromFourMeme);
    }
}
