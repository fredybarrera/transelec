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
        private readonly string _layerUrl = configuration["ArcGIS:Layer0"]!;
        private readonly string _layerUrl1 = configuration["ArcGIS:Layer1"]!;

        public async Task<IActionResult> Index()
        {
            string layerUrl = $"{_layerUrl}/query";
            List<String> outFields = GetOutFields0();
            var featureValues = await _arcGisService.GetLayerFeatureValuesAsync(layerUrl, outFields, "1=1");
            return View(featureValues);
        }

        public async Task<IActionResult> Related(string om,string objectId)
        {
            string layerUrl = $"{_layerUrl}/queryRelatedRecords";
            string relationshipId = "8";
            var featureValues = await _arcGisService.GetRelatedRecordsAsync(layerUrl, objectId, relationshipId);
            List<int> objectIds = GetObjectIds(featureValues);

            ViewBag.Om = om;
            ViewBag.Objectid = objectId;

            string where = $"orde_m_id={om}";
            string layerUrlQuery = $"{_layerUrl}/query";
            List<String> outFields = GetOutFields0();
            var features = await _arcGisService.GetLayerFeatureValuesAsync(layerUrlQuery, outFields, where);
            ViewBag.Features = features;

            List<ArcGisAttachmentViewModel> attachments = await _arcGisService.ObtenerDatosAdjuntos(_layerUrl1, objectIds);
            ViewBag.Imagenes = attachments; // Pasamos la info a la vista

            Dictionary<string, string> fieldAliases = await _arcGisService.ObtenerAliasCampos(_layerUrl1);
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

        public IActionResult Privacy()
        {
            return View();
        }

        public async Task<IActionResult> GetOm(string om, int objectid)
        {
            ViewBag.Om = om;
            ViewBag.Objectid = objectid;
            string layerUrl = $"{_layerUrl}/query";
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

            var result = await _arcGisService.AprobarOm(_layerUrl, data.ObjectId);

            return result ? Ok(new { mensaje = $"Actualización existosa" }) : BadRequest(new { mensaje = $"Error en la actualización" });
        }

        [HttpPost]
        public async Task<IActionResult> RechazarOm([FromBody] OmViewModel data)
        {

            if (data.ObjectId == 0)
            {
                return BadRequest("No se recibió el parámetro OM.");
            }

            var result = await _arcGisService.RechazarOm(_layerUrl, data.ObjectId, data.Observacion!);

            return result ? Ok(new { mensaje = $"Actualización existosa" }) : BadRequest(new { mensaje = $"Error en la actualización" });
        }

        [HttpPost]
        public async Task<IActionResult> AceptarActividad([FromBody] OmViewModel data)
        {

            if (data.ObjectId == 0)
            {
                return BadRequest(new { mensaje = "No se recibió el parámetro ObjectId." });
            }

            var result = await _arcGisService.AceptarActividad(_layerUrl1, data.ObjectId, data.Key!);

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
                "obs_activ"
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
