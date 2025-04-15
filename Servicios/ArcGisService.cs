using Microsoft.AspNetCore.Http;
using Microsoft.Win32;
using System;
using System.Net.Http.Headers;
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

        private readonly string _VORNR = "0010";
        private readonly string _GRUND = "TRFI";
        private readonly string _PLANT = "0060";

        private string? _cachedToken;
        private DateTime _tokenExpiration = DateTime.MinValue;

        private async Task<string> GetTokenAsync()
        {
            if (_cachedToken != null && DateTime.UtcNow < _tokenExpiration)
            {
                return _cachedToken; // Retornar token en caché si aún es válido
            }

            var url = _configuration["ArcGIS:UrlToken"];
            var values = new Dictionary<string, string>
            {
                { "username", _configuration["ArcGIS:Username"]! },
                { "password", _configuration["ArcGIS:Password"]! },
                { "client", "referer" },
                { "referer", "https://sigtranselec.maps.arcgis.com" },
                { "expiration", "60" }, // Expira en 60 minutos
                { "f", "json" }
            };

            var content = new FormUrlEncodedContent(values);
            var response = await _httpClient.PostAsync(url, content);
            response.EnsureSuccessStatusCode();

            var responseString = await response.Content.ReadAsStringAsync();
            using var jsonDoc = JsonDocument.Parse(responseString);
            _cachedToken = jsonDoc.RootElement.GetProperty("token").GetString();
            _tokenExpiration = DateTime.UtcNow.AddMinutes(55); // Guardar con margen antes de expirar

            return _cachedToken!;
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
            var res = response.IsSuccessStatusCode && responseString.Contains("\"success\":true");
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

        public async Task<RespuestaHttp> EnviarSap(string orde_m_id, string organizac, string start_time_field, string end_time_field)
        {
            var resultado = new RespuestaHttp();

            try
            {
                if (string.IsNullOrWhiteSpace(orde_m_id) || string.IsNullOrWhiteSpace(organizac))
                    throw new ArgumentException("Parámetros requeridos no pueden ser nulos o vacíos.");

                var startTime = ConvertUnixMillisecondsToDateTime(start_time_field);
                var endTime = ConvertUnixMillisecondsToDateTime(end_time_field);

                var url = _configuration["SAP:ApiSAP"];
                var username = _configuration["SAP:User"];
                var password = _configuration["SAP:Password"];

                var authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));

                using var requestMessage = new HttpRequestMessage(HttpMethod.Get, url);
                requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);

                var requestData = new
                {
                    VORNR = _VORNR,
                    AUFNR = orde_m_id,
                    BUDAT = DateTime.Now.ToString("yyyyMMdd"),
                    ARBPL = organizac,
                    ISDD = startTime.ToString("yyyyMMdd"),
                    ISDZ = startTime.ToString("HHmmss"),
                    IEDD = endTime.ToString("yyyyMMdd"),
                    IEDZ = endTime.ToString("HHmmss"),
                    GRUND = _GRUND,
                    PLANT = _PLANT
                };

                requestMessage.Content = new StringContent(JsonSerializer.Serialize(requestData), Encoding.UTF8, "application/json");

                using var response = await _httpClient.SendAsync(requestMessage);
                response.EnsureSuccessStatusCode();

                var responseText = await response.Content.ReadAsStringAsync();
                Console.WriteLine(responseText);

                resultado.Success = true;
                resultado.Message = "La operación fue exitosa. SAP respondió correctamente.";
            }
            catch (HttpRequestException ex)
            {
                resultado.Success = false;
                resultado.Message = $"Error al enviar a SAP: {ex.Message}";
            }
            catch (Exception ex)
            {
                resultado.Success = false;
                resultado.Message = $"Error inesperado: {ex.Message}";
            }

            return resultado;
        }


        private static DateTime ConvertUnixMillisecondsToDateTime(string timestamp)
        {
            if (long.TryParse(timestamp, out long milliseconds))
            {
                var dateTimeOffset = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
                return dateTimeOffset.DateTime;
            }
            throw new ArgumentException("El valor no es un timestamp válido.");
        }

        //public async Task<bool> EnviarSapOLD2(string orde_m_id, string organizac, string start_time_field, string end_time_field)
        //{
        //    try
        //    {
        //        var url = _configuration["SAP:ApiSAP"];
        //        var username = _configuration["SAP:User"];
        //        var password = _configuration["SAP:Password"];

        //        // Autenticación Basic Auth
        //        var authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        //        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);

        //        // Crear JSON dinámico
        //        var requestData = new
        //        {
        //            "VORNR": _VORNR,
        //            "AUFNR": orde_m_id,
        //            "BUDAT": DateTime.Now,
        //            "ARBPL": organizac,
        //            "ISDD": start_time_field,
        //            "ISDZ": start_time_field,
        //            "IEDD": end_time_field,
        //            "IEDZ": end_time_field,
        //            "GRUND": _GRUND,
        //            "PLANT": _PLANT
        //        };

        //        var jsonContent = new StringContent(JsonSerializer.Serialize(requestData), Encoding.UTF8, "application/json");

        //        // Enviar petición
        //        using var response = await _httpClient.PostAsync(url, jsonContent);
        //        response.EnsureSuccessStatusCode();

        //        var responseText = await response.Content.ReadAsStringAsync();
        //        Console.WriteLine(responseText);
        //        return true;
        //    }
        //    catch (HttpRequestException ex)
        //    {
        //        Console.WriteLine($"Error en la solicitud HTTP: {ex.Message}");
        //        return false;
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine($"Error inesperado: {ex.Message}");
        //        return false;
        //    }
        //}

        public async Task<bool> EnviarSapOLD(string orde_m_id, string organizac, string start_time_field, string end_time_field)
        {
            try
            {
                var url = _configuration["SAP:ApiSAP"];
                var username = _configuration["SAP:User"];
                var password = _configuration["SAP:Password"];

                var client = new HttpClient();
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Authorization", "••••••");
                request.Headers.Add("Cookie", "JSESSIONID=8C1A7F3D0BDE07B4902A91F1DB187793; __VCAP_ID__=c4686882-3d99-42ab-439f-83bf");
                var content = new StringContent("{\r\n    \"aufnr\": \"5079612\",\r\n    \"gewrk\": \"PTENG\",\r\n    \"gstpr\": \"2026-01-01\",\r\n    \"gltpr\": \"2026-01-01\"\r\n }", null, "application/json");
                request.Content = content;
                var response = await client.SendAsync(request);
                response.EnsureSuccessStatusCode();
                Console.WriteLine(await response.Content.ReadAsStringAsync());
                return true;

            }
            catch (Exception ex)
            { 
                Console.WriteLine(ex.Message);
                return false;
            }
        }
    }
}
