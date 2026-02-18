using BearTrap.Hackathon.Domain;

namespace BearTrap.Hackathon.Services.DataSources
{
    public interface IFourMemeMainListSource
    {
        Task<IReadOnlyList<FourMemeListedToken>> GetMainListAsync(int pageSize, CancellationToken ct);
    }
}
