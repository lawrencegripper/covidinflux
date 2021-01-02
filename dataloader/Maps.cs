using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GeoJSON.Net;
using GeoJSON.Net.Converters;
using GeoJSON.Net.Feature;
using GeoJSON.Net.Geometry;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using Newtonsoft.Json;
using Polly;

namespace phetracker
{

    public class Maps
    {
        private FeatureCollection geoJson;
        private HttpClient client;
        private Dictionary<string, Feature> ltlaBoundaries = new Dictionary<string, Feature>();
        private Dictionary<string, string> ltlaCentriod = new Dictionary<string, string>();
        private Dictionary<string, List<NhsTrust>> ltlaToNhsTrusts = new Dictionary<string, List<NhsTrust>>();
        private Dictionary<string, MsoaInfo> msoaToLa;
        private NhsTrustSearchResponse nhsTrustLocations;

        public Maps()
        {
            var handler = new HttpClientHandler()
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            this.client = new HttpClient(handler);
            client = new HttpClient(handler);

            // Load geojson data
            using (var sr = new StreamReader("./referencedata/ltmsoabounariesandcentriods.geojson"))
            {
                var geoJsonRaw = sr.ReadToEnd();
                geoJson = JsonConvert.DeserializeObject<FeatureCollection>(geoJsonRaw);
            }

            using (var sr = new StreamReader("./referencedata/nhstrusts.json"))
            {
                var nhsTrustsRaw = sr.ReadToEnd();
                var nhsTrustsJson = JsonConvert.DeserializeObject<NhsTrustSearchResponse>(nhsTrustsRaw);
                nhsTrustLocations = nhsTrustsJson;
            }

            GeometryFactory geoFact = new GeometryFactory();
            // Get LTLA Boundaries
            foreach (var polygon in geoJson.Features.Where(x => x.Geometry.Type == GeoJSONObjectType.MultiPolygon || x.Geometry.Type == GeoJSONObjectType.Polygon))
            {
                if (!polygon.Properties.ContainsKey("LAD20NM"))
                {
                    continue;
                }

                var polyJson = JsonConvert.SerializeObject(polygon.Geometry);
                Geometry boundaryGeom = null;
                if (polygon.Geometry.Type == GeoJSONObjectType.Polygon)
                {
                    boundaryGeom = GeometryFromGeoJson<NetTopologySuite.Geometries.Polygon>(polyJson);
                }
                else if (polygon.Geometry.Type == GeoJSONObjectType.MultiPolygon)
                {
                    boundaryGeom = GeometryFromGeoJson<NetTopologySuite.Geometries.MultiPolygon>(polyJson);
                }

                var centre = boundaryGeom.Centroid;

                string laName = polygon.Properties["LAD20NM"].ToString();
                ltlaBoundaries[laName] = polygon;
                string ltlaCentreLatLong = $"{centre.Coordinate.X}, {centre.Coordinate.Y}";
                ltlaCentriod[laName] = ltlaCentreLatLong;

                var hospitalsInLTLA = new List<NhsTrust>();
                foreach (var trust in nhsTrustLocations.value)
                {
                    if (boundaryGeom.Contains(GeometryFromGeoJson<NetTopologySuite.Geometries.Point>(JsonConvert.SerializeObject(trust.Geocode))))
                    {
                        hospitalsInLTLA.Add(trust);
                    }
                }

                ltlaToNhsTrusts[laName] = hospitalsInLTLA;
            }
        }

        private string MsoaNameToLtlaName(Feature x)
        {
            return msoaToLa[x.Properties["msoa11nm"].ToString()].Laname;
        }

        private T GeometryFromGeoJson<T>(string json, JsonSerializerSettings settings = null)
        {
            var serializer = NetTopologySuite.IO.GeoJsonSerializer.CreateDefault(settings);
            serializer.CheckAdditionalContent = false;
            using (var textReader = new StringReader(json))
            using (var jsonReader = new JsonTextReader(textReader))
            {
                return serializer.Deserialize<T>(jsonReader);
            }
        }

        public Dictionary<string, List<NhsTrust>> GetTrustsForLtlas()
        {
            return ltlaToNhsTrusts;
        }
    }
    public class MsoaData
    {
        public int week { get; set; }
        public int value { get; set; }
    }

    public class NewCasesByPublishDate
    {
        public string date { get; set; }
        public double? rollingSum { get; set; }
        public double? rollingRate { get; set; }
    }

    public class Datum
    {
        public string areaType { get; set; }
        public string areaCode { get; set; }
        public string areaName { get; set; }
        public int length { get; set; }
        public List<NewCasesByPublishDate> newCasesByPublishDate { get; set; }
    }

    public class MsoaDataRoot
    {
        public int length { get; set; }
        public List<Datum> data { get; set; }
    }

    // Root myDeserializedClass = JsonConvert.DeserializeObject<Root>(myJsonResponse); 
    public class MsoaInfo
    {
        public string msoa11cd { get; set; }
        public string msoa11nm { get; set; }
        public string msoa11nmw { get; set; }
        public string msoa11hclnm { get; set; }
        public string msoa11hclnmw { get; set; }
        public string Laname { get; set; }
    }

    public class NhsTrust
    {
        [JsonProperty("@search.score")]
        public double SearchScore { get; set; }
        public string OrganisationName { get; set; }
        public string OrganisationType { get; set; }
        public string Address1 { get; set; }
        public string Address2 { get; set; }
        public string Address3 { get; set; }
        public string City { get; set; }
        public string County { get; set; }
        public string OrganisationSubType { get; set; }
        public string Postcode { get; set; }
        public GeoJSON.Net.Geometry.Point Geocode { get; set; }
    }

    public class NhsTrustSearchResponse
    {
        [JsonProperty("@odata.context")]
        public string OdataContext { get; set; }
        [JsonProperty("@odata.count")]
        public int OdataCount { get; set; }
        public List<NhsTrust> value { get; set; }
        public string tracking { get; set; }
    }

}