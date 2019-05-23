using System;
using System.Collections.Generic;
using System.IO;

namespace FieldVisitHotFolderService
{
    public class StatusIndicator : IDisposable
    {
        private const string EvidenceFile = "_ServiceIsRunning_.txt";

        public static readonly HashSet<string> FilesToIgnore = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase)
        {
            EvidenceFile,
        };

        private bool Created { get; set; }
        private string RootPath { get; set; }

        public void Dispose()
        {
            if (!Created) return;

            Created = false;

            Deactivate();
        }

        public void Activate(string path)
        {
            RootPath = path;

            using (File.CreateText(Path.Combine(RootPath, EvidenceFile)))
            {
            }

            Created = true;
        }

        private void Deactivate()
        {
            foreach (var file in FilesToIgnore)
            {
                File.Delete(Path.Combine(RootPath, file));
            }
        }
    }
}
