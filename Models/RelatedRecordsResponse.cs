using System.Text.Json.Serialization;

namespace Transelec.Models
{
    public class RelatedRecordsResponse
    {
        [JsonPropertyName("relatedRecordGroups")]
        public List<RelatedRecordGroup> RelatedRecordGroups { get; set; } = [];
    }

    public class RelatedRecordGroup
    {
        [JsonPropertyName("objectId")]
        public int ObjectId { get; set; }

        [JsonPropertyName("relatedRecords")]
        public List<RelatedRecord> RelatedRecords { get; set; } = [];
    }

    public class RelatedRecord
    {
        [JsonPropertyName("attributes")]
        public Dictionary<string, object> Attributes { get; set; } = [];
    }
}
