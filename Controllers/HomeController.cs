using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System;
using System.Diagnostics;
using System.Security.Claims;
using System.Security.Cryptography.Xml;
using System.Text.Json;
using Transelec.Models;
using Transelec.Servicios;

namespace Transelec.Controllers
{
    public class HomeController(
        ILogger<HomeController> logger, 
        ArcGisService arcGisService, 
        IConfiguration configuration) : Controller
    {
        private readonly ILogger<HomeController> _logger = logger;
        private readonly ArcGisService _arcGisService = arcGisService;
        private readonly IConfiguration _configuration = configuration;
        private readonly string _layerUrlOM = configuration["ArcGIS:Layer0"]!;
        private readonly string _layerUrlTranReac = configuration["ArcGIS:Layer1"]!;
        private readonly string _tableIdRelationTranReac = "0";

        public async Task<IActionResult> Index()
        {
            string layerUrl = $"{_layerUrlOM}/query";
            List<String> outFields = GetOutFields0();
            var featureValues = await _arcGisService.GetLayerFeatureValuesAsync(layerUrl, outFields, "1=1");
            return View(featureValues);
        }

        public async Task<IActionResult> Related(string om, string objectId)
        {
            //Obtengo el registro de la OM
            string where = $"orde_m_id={om}";
            string layerUrlQuery = $"{_layerUrlOM}/query";
            List<String> outFields = GetOutFields0();
            var features = await _arcGisService.GetLayerFeatureValuesAsync(layerUrlQuery, outFields, where);
            ViewBag.Features = features;

            ViewBag.Om = om;
            ViewBag.Objectid = objectId;
            ViewBag.Organizac = GetKeyValue(features, "organizac");
            ViewBag.Start = GetKeyValue(features, "start_time_field");
            ViewBag.End = GetKeyValue(features, "end_time_field");

            //Obtengo los datos relacionados
            string layerUrl = $"{_layerUrlOM}/queryRelatedRecords";
            string relationshipId = _tableIdRelationTranReac;
            var featureValues = await _arcGisService.GetRelatedRecordsAsync(layerUrl, objectId, relationshipId);
            List<int> objectIds = GetObjectIds(featureValues);

            List<ArcGisAttachmentViewModel> attachments = await _arcGisService.ObtenerDatosAdjuntos(_layerUrlTranReac, objectIds);
            ViewBag.Imagenes = attachments; // Pasamos la info a la vista

            Dictionary<string, string> fieldAliases = await _arcGisService.ObtenerAliasCampos(_layerUrlTranReac);
            ViewBag.AliasCampos = fieldAliases;

            return View(featureValues);
        }

        public List<int> GetObjectIds(List<Dictionary<string, object>> featureValues)
        {
            List<int> objectIds = featureValues
            .Select(f => f.TryGetValue("objectid", out var value) ? Convert.ToInt32(value) : (int?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToList();

            return objectIds;
        }

        public string GetKeyValue(List<Dictionary<string, object>> featureValues, string key)
        {
            foreach (var dic in featureValues)
            {
                var claveCoincidente = dic.Keys
                    .FirstOrDefault(k => k.Equals(key, StringComparison.OrdinalIgnoreCase));

                if (claveCoincidente != null)
                {
                    var valor = dic[claveCoincidente];
                    return valor?.ToString() ?? string.Empty;
                }
            }

            return string.Empty;
        }

        public IActionResult Privacy()
        {
            return View();
        }

        public async Task<IActionResult> GetOm(string om, int objectid)
        {
            ViewBag.Om = om;
            ViewBag.Objectid = objectid;
            string layerUrl = $"{_layerUrlOM}/query";
            string where = $"orde_m_id={om}";
            List<String> outFields = GetOutFields0();
            var featureValues = await _arcGisService.GetLayerFeatureValuesAsync(layerUrl, outFields, where);
            return View(featureValues);
        }

        [HttpPost]
        public async Task<IActionResult> AprobarOm([FromBody] OmViewModel data)
        {

            if (data.ObjectId == 0)
            {
                return BadRequest("No se recibió el parámetro OM.");
            }

            //Apruebo la OM
            var result = await _arcGisService.AprobarOm(_layerUrlOM, data.ObjectId);



            //Envío a SAP
            var resultSap = await _arcGisService.EnviarSap(data.Om, data.Organizac, data.Start, data.End);


            if (!result)
            {
                return BadRequest(new { mensaje = $"Error al aprobar la OM" });
            }

            if (!resultSap.Success)
            {
                return BadRequest(new { mensaje = resultSap.Message });
            }

            return Ok(new { mensaje = $"Actualización existosa" });
        }

        [HttpPost]
        public async Task<IActionResult> RechazarOm([FromBody] OmViewModel data)
        {

            if (data.ObjectId == 0)
            {
                return BadRequest("No se recibió el parámetro OM.");
            }

            var result = await _arcGisService.RechazarOm(_layerUrlOM, data.ObjectId, data.Observacion!);

            return result ? Ok(new { mensaje = $"Actualización existosa" }) : BadRequest(new { mensaje = $"Error en la actualización" });
        }

        [HttpPost]
        public async Task<IActionResult> AceptarActividad([FromBody] OmViewModel data)
        {

            if (data.ObjectId == 0)
            {
                return BadRequest(new { mensaje = "No se recibió el parámetro ObjectId." });
            }

            var result = await _arcGisService.AceptarActividad(_layerUrlTranReac, data.ObjectId, data.Key!);

            return result ? Ok(new { mensaje = $"Actualización existosa" }) : BadRequest(new { mensaje = $"Error en la actualización" });
        }

        [HttpPost]
        public async Task<IActionResult> RechazarActividad([FromBody] OmViewModel data)
        {

            if (data.ObjectId == 0)
            {
                return BadRequest(new { mensaje = "No se recibió el parámetro ObjectId." });
            }

            var result = await _arcGisService.RechazarActividad(_layerUrlTranReac, data.ObjectId, data.Key!);

            return result ? Ok(new { mensaje = $"Actualización existosa" }) : BadRequest(new { mensaje = $"Error en la actualización" });
        }

        private static List<String> GetOutFields0()
        {
            return [
                "objectid",
                "uniquerowid",
                "globalid",
                "actividad",
                "tipo_trabaj",
                "created_date",
                "om_text",
                "instalac",
                "responsable",
                "jefe_faen",
                "organizac",
                "zona_name",
                "equipo",
                "jefe_act",
                "aceptar",
                "estado",
                "obs_activ",
                "start_time_field",
                "end_time_field",
                "orde_m_id"
            ];
        }

        private static List<String> GetOutFields1()
        {
            return [
                "objectid",
                "globalid"
            ];
        }

        private static List<String> GetOutFieldsRelated()
        {
            return ["*"];
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
