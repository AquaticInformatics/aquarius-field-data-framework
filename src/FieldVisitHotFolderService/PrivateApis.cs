using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using ServiceStack;

// ReSharper disable once CheckNamespace
namespace FieldVisitHotFolderService.PrivateApis
{
    // These private API endpoints and operations are not supported for external integrations, and are subject to change without notice.
    // They have been hand-curated to work across multiple versions of AQTS software.
    // May contain traces of peanuts.
    namespace SiteVisit
    {
        public static class Root
        {
            public const string Endpoint = Aquarius.TimeSeries.Client.EndPoints.Root.EndPoint + "/apps/v1";
        }

        // Stolen from 3.10 SiteVisit endpoint 
        [DataContract]
        [Route("/locations/search", HttpMethods.Get)]
        public class GetSearchLocations : IReturn<SearchLocationsResponse>
        {
            [DataMember(Name = "q")]
            public string SearchText { get; set; }

            [DataMember(Name = "n")]
            public int MaxResults { get; set; }

            [DataMember(Name = "h")]
            public bool HideTruncatedResults { get; set; }
        }

        public class SearchLocationsResponse
        {
            public List<SearchLocation> Results { get; set; }
            public bool LimitExceeded { get; set; }
        }

        public class SearchLocation
        {
            public long Id { get; set; }
            public string Identifier { get; set; }
            public string Name { get; set; }
        }

        // Stolen from 17.3 SiteVisit
        [Route("/locations/{Id}/visits", HttpMethods.Get)]
        public class GetLocationVisits : IReturn<List<Visit>>
        {
            public long Id { get; set; }
            public DateTime? StartTime { get; set; }
            public DateTime? EndTime { get; set; }
        }

        public class Visit
        {
            public long Id { get; set; }
            public DateTime StartDate { get; set; }
            public DateTime? EndDate { get; set; }
            public bool IsLocked { get; set; }
        }

        [Route("/visits/{Id}", HttpMethods.Delete)]
        public class DeleteVisit : IReturnVoid
        {
            public long Id { get; set; }
        }
    }
}
