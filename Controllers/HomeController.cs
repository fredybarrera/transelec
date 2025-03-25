using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;
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

        public async Task<IActionResult> Index()
        {
            string layerUrl = $"{_layerUrl}/query";
            List<String> outFields = GetOutFields0();
            var featureValues = await _arcGisService.GetLayerFeatureValuesAsync(layerUrl, outFields, "1=1");
            return View(featureValues);
        }

        public async Task<IActionResult> Related(string om, string objectId)
        {
            string layerUrl = $"{_layerUrl}/queryRelatedRecords";
            ViewBag.Om = om;
            string relationshipId = "7";
            var featureValues = await _arcGisService.GetRelatedRecordsAsync(layerUrl, objectId, relationshipId);
            return View(featureValues);
        }

        public IActionResult Privacy()
        {
            return View();
        }

        public async Task<IActionResult> GetOm(string om)
        {
            ViewBag.Om = om;
            string layerUrl = $"{_layerUrl}/query";
            //string where = "orde_m_id=8262538";
            string where = $"orde_m_id={om}";
            List<String> outFields = GetOutFields0();
            var featureValues = await _arcGisService.GetLayerFeatureValuesAsync(layerUrl, outFields, where);
            return View(featureValues);
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
                "jefe_act"
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
