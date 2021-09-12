using System;
using System.Collections.Generic;
using System.Linq;
using Aquarius.TimeSeries.Client;
using Aquarius.TimeSeries.Client.ServiceModels.Publish;

namespace FieldVisitHotFolderService
{
    public class ReferencePointCache
    {
        private IAquariusClient Client { get; }

        private Dictionary<string, List<ReferencePoint>> ReferencePoints { get; } =
            new Dictionary<string, List<ReferencePoint>>();

        public ReferencePointCache(IAquariusClient client)
        {
            Client = client;
        }

        public ReferencePoint Find(string locationIdentifier, Guid referencePointUniqueId)
        {
            if (!ReferencePoints.TryGetValue(locationIdentifier, out var referencePoints))
            {
                var location = Client.Publish.Get(new LocationDataServiceRequest
                {
                    LocationIdentifier = locationIdentifier
                });

                referencePoints = location.ReferencePoints;

                ReferencePoints.Add(locationIdentifier, referencePoints);
            }

            return referencePoints.FirstOrDefault(rp => rp.UniqueId == referencePointUniqueId);
        }
    }
}
