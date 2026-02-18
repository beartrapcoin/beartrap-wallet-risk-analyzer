using BearTrap.Hackathon.Domain;

namespace BearTrap.Hackathon.Services.DataSources;

public sealed class MockFourMemeSource : IFourMemeSource
{
    public Task<IReadOnlyList<LatestToken>> GetLatestAsync(int count, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // 2 creators repeat to trigger CREATOR_SPAM once DB has history
        var creatorA = "0x7b2f8b8d9a4c1c2d3e4f5a6b7c8d9e0f1a2b3c4d";
        var creatorB = "0x1a2b3c4d5e6f708192a3b4c5d6e7f8091a2b3c4d";

        var tokens = new List<LatestToken>
        {
            new("0x1111111111111111111111111111111111111111", "BearTrap", "BT", creatorA, now.AddMinutes(-5)),
            new("0x2222222222222222222222222222222222222222", "BinanceRewards", "BNB", creatorA, now.AddMinutes(-8)),
            new("0x3333333333333333333333333333333333333333", "TetherMoon", "USDT2", creatorB, now.AddMinutes(-12)),
            new("0x4444444444444444444444444444444444444444", "EtherDrop", "ETH", creatorB, now.AddMinutes(-15)),
            new("0x5555555555555555555555555555555555555555", "CutePepe", "PEPE1", creatorA, now.AddMinutes(-20)),
        };

        return Task.FromResult<IReadOnlyList<LatestToken>>(tokens);
    }
    public Task<bool> IsFourMemeLaunchAsync(string tokenAddress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(true);
    }
    public Task<IReadOnlyList<FourMemeListedToken>> GetMainListAsync(int pageSize, CancellationToken ct)
    {
        return Task.FromResult<IReadOnlyList<FourMemeListedToken>>(new List<FourMemeListedToken>());
    }
}
