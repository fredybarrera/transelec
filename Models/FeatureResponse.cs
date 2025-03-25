using System.Text.Json;
using System.Text.Json.Serialization;

namespace Transelec.Models
{
    public class FeatureResponse
    {
        [JsonPropertyName("features")]
        public List<Feature>? Features { get; set; }
    }

    public class Feature
    {
        [JsonPropertyName("attributes")]
        public Dictionary<string, JsonElement>? Attributes { get; set; }
    }
}
