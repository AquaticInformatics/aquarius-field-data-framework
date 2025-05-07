using FieldDataPluginFramework;
using FieldDataPluginFramework.Context;
using FieldDataPluginFramework.DataModel.CrossSection;
using FieldDataPluginFramework.Results;
using FieldDataPluginFramework.Serialization;
using FieldDataPluginFramework.Validation;
using ServiceStack;
using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;

namespace JsonFieldData
{
    public class Parser
    {
        public Parser(ILog logger, IFieldDataResultsAppender resultsAppender)
        {
            Log = logger;
            ResultsAppender = resultsAppender;
        }

        private ILog Log { get; }
        private IFieldDataResultsAppender ResultsAppender { get; }
        private AppendedResults AppendedResults { get; set; }
        private LocationInfo LocationInfo { get; set; }

        public ParseFileResult Parse(Stream stream, LocationInfo locationInfo = null)
        {
            var jsonText = ReadTextFromStream(stream);

            if (jsonText == null)
                return ParseFileResult.CannotParse();

            try
            {
                LocationInfo = locationInfo;

                AppendedResults = ParseJson(jsonText);

                if (AppendedResults == null)
                    return ParseFileResult.CannotParse();

                return MapToFramework();
            }
            catch (Exception exception)
            {
                return ParseFileResult.SuccessfullyParsedButDataInvalid(exception);
            }
        }

        private AppendedResults ParseJson(string jsonText)
        {
            JsonConfig.Configure();

            try
            {
                var results = jsonText.FromJson<AppendedResults>();

                if (results.FrameworkAssemblyQualifiedName == null && results.PluginAssemblyQualifiedTypeName == null)
                    return null;

                ValidationChecks.CannotBeNull(nameof(results.AppendedVisits), results.AppendedVisits);

                return results;
            }
            catch (SerializationException)
            {
                return null;
            }
        }

        private string ReadTextFromStream(Stream stream)
        {
            try
            {
                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private ParseFileResult MapToFramework()
        {
            if (!AppendedResults.AppendedVisits.Any())
            {
                return ParseFileResult.SuccessfullyParsedButDataInvalid("No visits contained in the JSON document.");
            }

            Log.Info($"Importing {AppendedResults.AppendedVisits.Count} visits parsed by '{AppendedResults.PluginAssemblyQualifiedTypeName}' using '{AppendedResults.FrameworkAssemblyQualifiedName}'");

            foreach (var visit in AppendedResults.AppendedVisits)
            {
                AppendVisit(visit);
            }

            return ParseFileResult.SuccessfullyParsedAndDataValid();
        }

        private void AppendVisit(FieldVisitInfo importedVisit)
        {
            var locationInfo = LocationInfo ?? ResultsAppender.GetLocationByIdentifier(importedVisit.LocationInfo.LocationIdentifier);

            var visit = ResultsAppender.AddFieldVisit(locationInfo, importedVisit.FieldVisitDetails);

            foreach (var controlCondition in importedVisit.ControlConditions)
            {
                ResultsAppender.AddControlCondition(visit, controlCondition);
            }

            foreach (var crossSectionSurvey in importedVisit.CrossSectionSurveys)
            {
                UpgradeCrossSectionSurvey(crossSectionSurvey);
                ResultsAppender.AddCrossSectionSurvey(visit, crossSectionSurvey);
            }

            foreach (var dischargeActivity in importedVisit.DischargeActivities)
            {
                ResultsAppender.AddDischargeActivity(visit, dischargeActivity);
            }

            foreach (var levelSurvey in importedVisit.LevelSurveys)
            {
                ResultsAppender.AddLevelSurvey(visit, levelSurvey);
            }

            foreach (var reading in importedVisit.Readings)
            {
                ResultsAppender.AddReading(visit, reading);
            }

            foreach (var inspection in importedVisit.Inspections)
            {
                ResultsAppender.AddInspection(visit, inspection);
            }

            foreach (var calibration in importedVisit.Calibrations)
            {
                ResultsAppender.AddCalibration(visit, calibration);
            }

            foreach (var gageZeroFlowActivity in importedVisit.GageZeroFlowActivities)
            {
                ResultsAppender.AddGageZeroFlowActivity(visit, gageZeroFlowActivity);
            }

            foreach (var wellIntegrity in importedVisit.WellIntegrity)
            {
                ResultsAppender.AddWellIntegrity(visit, wellIntegrity);
            }
        }

        private static void UpgradeCrossSectionSurvey(CrossSectionSurvey survey)
        {
            if (survey.CrossSectionPoints.Any(p => p.PointOrder != JsonConfig.LegacyPointOrder))
                return;

            survey.CrossSectionPoints = survey
                .CrossSectionPoints
                .Select((point, i) => new CrossSectionPoint(1 + i, point.Distance, point.Elevation)
                {
                    Comments = point.Comments,
                    Depth = point.Depth
                })
                .ToList();
        }
    }
}
