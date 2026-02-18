using BearTrap.Hackathon.Domain;

namespace BearTrap.Hackathon.Services.DataSources;

public interface IFourMemeSource
{
    Task<IReadOnlyList<LatestToken>> GetLatestAsync(int count, CancellationToken ct);
    Task<bool> IsFourMemeLaunchAsync(string tokenAddress, CancellationToken ct);

}
