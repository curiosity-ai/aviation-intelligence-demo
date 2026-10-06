using System.Threading.Tasks;

namespace AviationIntel.FrontEnd.Api
{
    /// <summary>One method per workspace endpoint.</summary>
    internal static class Api
    {
        internal static Task<DashboardDto> DashboardAsync()
            => Mosaik.API.Endpoints.CallAsync<DashboardDto>(Endpoints.Dashboard, readOnlyCompatible: true);
    }
}
