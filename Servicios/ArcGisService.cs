using Microsoft.AspNetCore.Http;
using Microsoft.Win32;
using System;
using System.Text;
using System.Text.Json;
using Transelec.Models;

namespace Transelec.Servicios
{
    public class ArcGisService(HttpClient httpClient, IConfiguration configuration)
    {
        private readonly HttpClient _httpClient = httpClient;
        private readonly IConfiguration _configuration = configuration;
        private const int _aceptar = 1;
        private const int _rechazar = 2;
        private const int _estadoEnviarSap = 2;


        public async Task<string> GetTokenAsync()
        {
            var url = _configuration["ArcGIS:UrlToken"];

            var values = new Dictionary<string, string>
            {
                { "username", _configuration["ArcGIS:Username"]! },
                { "password", _configuration["ArcGIS:Password"]! },
                { "client", "referer" },
                { "referer", "https://sigtranselec.maps.arcgis.com" },
                { "expiration", "60" },
                { "f", "json" }
            };

            var content = new FormUrlEncodedContent(values);
            var response = await _httpClient.PostAsync(url, content);
            var responseString = await response.Content.ReadAsStringAsync();

            using var jsonDoc = JsonDocument.Parse(responseString);
            return jsonDoc.RootElement.GetProperty("token").GetString()!;
        }

        public async Task<List<Dictionary<string, object>>> GetLayerFeatureValuesAsync(
            string layerUrl, 
            List<string> outFields,
            string where)
        {
            string token = await GetTokenAsync();

            var queryParams = new Dictionary<string, string>
            {
                { "where", where },
                { "outFields", string.Join(",", outFields) },
                { "returnGeometry", "false" },
                { "f", "json" },
                { "token", token }
            };

            string queryString = string.Join("&", queryParams.Select(kvp => $"{kvp.Key}={kvp.Value}"));
            string Url = $"{layerUrl}/query?{queryString}";
            var response = await _httpClient.GetAsync(Url);

            if (!response.IsSuccessStatusCode)
                throw new Exception("Error al obtener los datos de la capa");

            string responseString = await response.Content.ReadAsStringAsync();
            var featureResponse = JsonSerializer.Deserialize<FeatureResponse>(responseString, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            var featureValues = new List<Dictionary<string, object>>();

            foreach (var feature in featureResponse?.Features ?? [])
            {
                var featureData = new Dictionary<string, object>();

                foreach (var field in outFields)
                {
                    if (feature.Attributes!.TryGetValue(field, out var value))
                    {
                        featureData[field] = value.ValueKind == JsonValueKind.Number
                            ? (object)value.GetDouble()! : value.GetString()!;
                    }
                }

                featureValues.Add(featureData);
            }

            return featureValues;
        }

        public async Task<List<Dictionary<string, object>>> GetRelatedRecordsAsync(
            string layerUrl,
            string objectId,
            string relationshipId
            )
        {
            string token = await GetTokenAsync();

            var queryParams = new Dictionary<string, string>
            {
                { "objectIds", objectId },
                { "relationshipId", relationshipId },  // Asegúrate de usar el ID correcto de relación
                { "outFields", "*" },
                { "f", "json" },
                { "token", token }
            };

            string queryString = string.Join("&", queryParams.Select(kvp => $"{kvp.Key}={kvp.Value}"));
            string Url = $"{layerUrl}?{queryString}";
            var response = await _httpClient.GetAsync(Url);

            if (!response.IsSuccessStatusCode)
                throw new Exception("Error al obtener los registros relacionados");

            string responseString = await response.Content.ReadAsStringAsync();
            var relatedResponse = JsonSerializer.Deserialize<RelatedRecordsResponse>(responseString, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            var relatedValues = new List<Dictionary<string, object>>();

            foreach (var group in relatedResponse?.RelatedRecordGroups ?? [])
            {
                foreach (var record in group.RelatedRecords)
                {
                    var recordData = new Dictionary<string, object>();

                    foreach (var kvp in record.Attributes)
                    {
                        if (kvp.Value is JsonElement jsonElement)
                        {
                            recordData[kvp.Key] = jsonElement.ValueKind switch
                            {
                                JsonValueKind.String => jsonElement.GetString()!,
                                JsonValueKind.Number => jsonElement.TryGetInt64(out long longValue) ? longValue : jsonElement.GetDouble(),
                                JsonValueKind.True => true,
                                JsonValueKind.False => false,
                                _ => jsonElement.ToString()
                            };
                        }
                        else
                        {
                            recordData[kvp.Key] = kvp.Value;
                        }
                    }

                    relatedValues.Add(recordData);
                }
            }

            return relatedValues;
        }



        public async Task<bool> AprobarOm(string layerUrl, int objectId)
        {
            string token = await GetTokenAsync();

            Object objTmp = new
            {
                attributes = new
                {
                    objectid = objectId,
                    aceptar = _aceptar,
                    estado = _estadoEnviarSap,
                }
            };

            string registro = JsonSerializer.Serialize(objTmp);

            var values = new Dictionary<string, string>
            {
                ["updates"] = $"[{registro}]",
                ["token"] = token,
                ["f"] = "json"
            };

            var content = new FormUrlEncodedContent(values);
            
            string Url = $"{layerUrl}/applyEdits";
            var response = await _httpClient.PostAsync(Url, content);
            var responseString = await response.Content.ReadAsStringAsync();
            var res = response.IsSuccessStatusCode && responseString.Contains("\"success\":true");
            return res;
        }

        public async Task<bool> RechazarOm(string layerUrl, int objectId, string observacion)
        {
            string token = await GetTokenAsync();

            Object objTmp = new
            {
                attributes = new
                {
                    objectid = objectId,
                    aceptar = _rechazar,
                    estado = _estadoEnviarSap,
                    obs_activ = observacion
                }
            };

            string registro = JsonSerializer.Serialize(objTmp);

            var values = new Dictionary<string, string>
            {
                ["updates"] = $"[{registro}]",
                ["token"] = token,
                ["f"] = "json"
            };

            var content = new FormUrlEncodedContent(values);

            string Url = $"{layerUrl}/applyEdits";
            var response = await _httpClient.PostAsync(Url, content);
            var responseString = await response.Content.ReadAsStringAsync();
            var res = response.IsSuccessStatusCode && responseString.Contains("\"success\" : true");
            return res;
        }
    }
}
