using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace FileReport.Models
{
    public class SearchParameters
    {
        public string SearchPath { get; set; } = string.Empty;
        public string OutputPath { get; set; } = string.Empty;

        [JsonInclude]
        public ObservableCollection<string> FileFilters { get; private set; } = new();
    }
}
