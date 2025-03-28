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


        public async Task<Dictionary<string, string>> ObtenerAliasCampos(string layerUrl)
        {
            string token = await GetTokenAsync();
            string Url = $"{layerUrl}?f=json&token={token}";
            using HttpClient client = new();
            string json = await client.GetStringAsync(Url);

            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            Dictionary<string, string> aliases = [];

            if (root.TryGetProperty("fields", out JsonElement fields))
            {
                foreach (JsonElement field in fields.EnumerateArray())
                {
                    string name = field.GetProperty("name").GetString()!;
                    string alias = field.GetProperty("alias").GetString()!;

                    aliases[name] = alias; // Guardar en el diccionario
                }
            }

            return aliases;
        }

        public async Task<Dictionary<int, List<string>>> ObtenerUrlsAdjuntosOLD(string featureServerUrl, List<int> objectIds)
        {
            Dictionary<int, List<string>> imagenesPorObjeto = [];

            foreach (int objectId in objectIds)
            {
                string token = await GetTokenAsync();
                string queryUrl = $"{featureServerUrl}/queryAttachments?objectIds={objectId}&f=json&token={token}";
                using HttpClient client = new();
                string json = await client.GetStringAsync(queryUrl);
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;

                if (root.TryGetProperty("attachmentGroups", out JsonElement attachmentGroups))
                {
                    foreach (JsonElement group in attachmentGroups.EnumerateArray())
                    {
                        if (group.TryGetProperty("attachmentInfos", out JsonElement attachmentInfos))
                        {
                            List<string> urls = new();
                            foreach (JsonElement attachment in attachmentInfos.EnumerateArray())
                            {
                                int attachmentId = attachment.GetProperty("id").GetInt32();
                                string imageUrl = $"{featureServerUrl}/{objectId}/attachments/{attachmentId}?token={token}";
                                urls.Add(imageUrl);
                            }
                            imagenesPorObjeto[objectId] = urls;
                        }
                    }
                }
            }

            return imagenesPorObjeto;
        }

        public async Task<List<ArcGisAttachmentViewModel>> ObtenerDatosAdjuntos(string featureServerUrl, List<int> objectIds)
        {
            List<ArcGisAttachmentViewModel> result = [];
            string token = await GetTokenAsync();

            foreach (int objectId in objectIds)
            {
                // Obtener adjuntos
                string queryAttachmentsUrl = $"{featureServerUrl}/queryAttachments?objectIds={objectId}&f=json&token={token}";
                using HttpClient client = new();
                string jsonAttachments = await client.GetStringAsync(queryAttachmentsUrl);
                using JsonDocument docAttachments = JsonDocument.Parse(jsonAttachments);
                JsonElement rootAttachments = docAttachments.RootElement;

                List<string> imageUrls = [];
                if (rootAttachments.TryGetProperty("attachmentGroups", out JsonElement attachmentGroups))
                {
                    foreach (JsonElement group in attachmentGroups.EnumerateArray())
                    {
                        if (group.TryGetProperty("attachmentInfos", out JsonElement attachmentInfos))
                        {
                            foreach (JsonElement attachment in attachmentInfos.EnumerateArray())
                            {
                                int attachmentId = attachment.GetProperty("id").GetInt32();
                                string keywords = attachment.GetProperty("keywords").ToString();
                                string imageUrl = $"{featureServerUrl}/{objectId}/attachments/{attachmentId}?token={token}";

                                result.Add(new ArcGisAttachmentViewModel
                                {
                                    ObjectId = objectId,
                                    ImageUrl = imageUrl,
                                    Keyword = keywords
                                });
                            }
                        }
                    }
                }
            }

            return result;
        }


        public async Task<bool> AceptarActividad(string layerUrl, int objectId, string key)
        {
            string token = await GetTokenAsync();

            var attributes = new Dictionary<string, object>
            {
                { "objectid", objectId },
                { "g1vala" + key, _aceptar }
            };

            Object objTmp = new { attributes };

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

        public async Task<bool> RechazarActividad(string layerUrl, int objectId, string key)
        {
            string token = await GetTokenAsync();

            var attributes = new Dictionary<string, object>
            {
                { "objectid", objectId },
                { "g1vala" + key, _rechazar }
            };

            Object objTmp = new { attributes };

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
    }
}
