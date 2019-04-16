using Aquarius.TimeSeries.Client.ServiceModels.Publish;

namespace FieldVisitHotFolderService
{
    public class ArchivedVisit
    {
        public FieldVisitDescription Summary { get; set; }
        public FieldVisitDataServiceResponse Activities { get; set; }
    }
}
