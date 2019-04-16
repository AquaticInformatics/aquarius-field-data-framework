using System.Linq;
using Aquarius.TimeSeries.Client;
using Aquarius.TimeSeries.Client.ServiceModels.Publish;
using Common;
using FieldVisitHotFolderService.PrivateApis.SiteVisit;
using ServiceStack;

namespace FieldVisitHotFolderService
{
    public class VisitDeleter
    {
        private IAquariusClient Client { get; }
        private IServiceClient SiteVisit { get; }

        public VisitDeleter(IAquariusClient client)
        {
            Client = client;

            SiteVisit = Client.RegisterCustomClient(Root.Endpoint);
        }

        public void DeleteVisit(FieldVisitDescription visit)
        {
            var location = GetSiteVisitLocation(visit.LocationIdentifier);

            var existingVisits = SiteVisit.Get(new GetLocationVisits
            {
                Id = location.Id,
                StartTime = visit.StartTime?.UtcDateTime,
                EndTime = visit.EndTime?.UtcDateTime
            });

            foreach (var existingVisit in existingVisits)
            {
                SiteVisit.Delete(new DeleteVisit {Id = existingVisit.Id});
            }
        }

        private SearchLocation GetSiteVisitLocation(string locationIdentifier)
        {
            var searchResults = SiteVisit.Get(new GetSearchLocations { SearchText = locationIdentifier });

            if (searchResults.LimitExceeded)
                throw new ExpectedException($"Cannot resolve location ID for identifier='{locationIdentifier}'. LimitExceeded=true. Results.Count={searchResults.Results.Count}");

            var location = searchResults.Results
                .SingleOrDefault(l => l.Identifier == locationIdentifier);

            if (location == null)
                throw new ExpectedException($"Cannot resolve locationID for unknown identifier='{locationIdentifier}', even with Results.Count={searchResults.Results.Count}");

            return location;
        }

    }
}
