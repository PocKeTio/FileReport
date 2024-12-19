using System;

namespace FileReport.Models
{
    public class FileInformation
    {
        public string Name { get; set; }
        public string FullPath { get; set; }
        public long Size { get; set; }
        public DateTime CreationTime { get; set; }
        public DateTime LastModifiedTime { get; set; }
    }
}
