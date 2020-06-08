using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Microsoft.Extensions.Logging;
using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using InfluxDB.Client.Writes;

namespace phetracker
{
    class Program
    {
        private static Dictionary<string, int> populationData;

        static async Task Main(string[] args)
        {
            ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
            {
                builder
                    .AddFilter("Microsoft", LogLevel.Warning)
                    .AddFilter("System", LogLevel.Warning)
                    .AddFilter("phetracker.Program", LogLevel.Debug)
                    .AddConsole(c =>
                    {
                        c.TimestampFormat = "[HH:mm:ss] ";
                    });
            });
            ILogger logger = loggerFactory.CreateLogger<Program>();

            logger.LogInformation("Init services....");

            // Lazy debugging tools
            var localTestMode = true;
            if (System.Environment.GetEnvironmentVariable("environment") == "prod")
            {
                logger.LogInformation("Prod env, message ending enabled");
                localTestMode = false;
            }

            // Service creation
            // populationData = LoadPopulationData();

                logger.LogInformation("Starting...");
                HttpClient client = new HttpClient();

                var caseDataString = await client.GetStringAsync("https://coronavirus.data.gov.uk/downloads/json/coronavirus-cases_latest.json");
                CaseData data = JsonConvert.DeserializeObject<CaseData>(caseDataString);

                var hash = GetHashString(caseDataString);
                data.metadata.hash = hash;

                foreach (var la in data.ltlas)
                {
                    la.kind = "ltlas";
                }

                foreach (var la in data.utlas)
                {
                    la.kind = "utlas";
                }

                var allCaseData = data.ltlas.Concat(data.utlas);


                var Token = "hYf9V7UQge2McZNCTKerUkPwLvqncruS2hczL9jJX3ohSP-zJ7Yt1Za5J33qNcKkn_rI7pU3SiWHbV5waBt4dA==".ToCharArray();
                using (var influxDBClient = InfluxDBClientFactory.Create("http://localhost:9999", Token))
                using (var writeApi = influxDBClient.GetWriteApi())
                {
                    foreach (var record in allCaseData)
                    {
                        var point = PointData.Measurement("confirmedCases")
                           .Tag("localAuth", record.areaName)
                           .Tag("localAuthCode", record.areaCode)
                           .Field("daily", record.dailyLabConfirmedCases ?? 0)
                           .Field("total", record.dailyTotalLabConfirmedCasesRate)
                           .Timestamp(record.specimenDate.ToUniversalTime(), WritePrecision.S);

                        writeApi.WritePoint("pheCaseData", "covid", point);

                    }
                }


                // if (previousMeta?.Resource?.hash == hash && localTestMode != true)
                // {
                //     logger.LogInformation("No update found... waiting 15mins");
                //     await Task.Delay(TimeSpan.FromMinutes(15));
                // }
                // else
                // {


                //     var regionalCharts = new List<RegionalMeta>();

                //     var averagePer100kLast4WeeksByRegion = new List<Tuple<string, double?>>();

                // }

                // if (localTestMode)
                // {
                //     logger.LogInformation("Done. Local testmode existing.");
                //     return;
                // }
                // logger.LogInformation("Done. Looping again.");

            // }
        }

        private static List<CaseRecord> ConvertToPer100kValue(List<CaseRecord> rawData)
        {

            var results = new List<CaseRecord>();
            if (!populationData.ContainsKey(rawData.FirstOrDefault().areaCode))
            {
                return results;
            }
            var population = populationData[rawData.FirstOrDefault().areaCode];
            foreach (var record in rawData)
            {
                try
                {
                    double population100ks = population / 100000d;
                    var per100k = record.dailyLabConfirmedCases / population100ks;

                    record.dailyLabConfirmedCases = (float)per100k;
                    record.label = per100k.Value.ToString("0.00");
                    results.Add(record);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
            return results;
        }

        public static byte[] GetHash(string inputString)
        {
            using (HashAlgorithm algorithm = SHA256.Create())
                return algorithm.ComputeHash(Encoding.UTF8.GetBytes(inputString));
        }

        public static string GetHashString(string inputString)
        {
            StringBuilder sb = new StringBuilder();
            foreach (byte b in GetHash(inputString))
                sb.Append(b.ToString("X2"));

            return sb.ToString();
        }
    }

    public class RegionalMeta
    {
        public string name { get; set; }
        public string[] graphs { get; set; }
        public Graph[] graphsv2 { get; set; }
        public string code { get; set; }
    }

    public class Graph
    {
        public string name { get; set; }
        public string description { get; set; }
        public string dataUrl { get; set; }
        public string graphUrl { get; set; }
        public string kind { get; set; }
    }

    public class Subscription
    {
        public string owner { get; set; }
        public string number { get; set; }
        public string authority { get; set; }
    }

    public class Metadata
    {
        [JsonProperty(PropertyName = "id")]
        public string ID
        {
            get => "metadata";
        }
        [JsonProperty(PropertyName = "partition")]
        public string Partition
        {
            get => "meta";
        }
        public DateTime lastUpdatedAt { get; set; }
        public string disclaimer { get; set; }
        public string hash { get; set; }
    }

    public class DailyRecords
    {
        public string areaName { get; set; }
        public int totalLabConfirmedCases { get; set; }
        public int dailyLabConfirmedCases { get; set; }
    }

    public class CaseRecord
    {
        // Added details
        public string kind;
        public string label;


        public string areaCode { get; set; }
        public string areaName { get; set; }
        public DateTime specimenDate { get; set; }
        public float? dailyLabConfirmedCases { get; set; }
        // public int? previouslyReportedDailyCases { get; set; }
        // public int? changeInDailyCases { get; set; }
        // public int? totalLabConfirmedCases { get; set; }
        // public int? previouslyReportedTotalCases { get; set; }
        // public int? changeInTotalCases { get; set; }
        public double dailyTotalLabConfirmedCasesRate { get; set; }
    }

    public class CaseData
    {
        public Metadata metadata { get; set; }
        public DailyRecords dailyRecords { get; set; }
        public List<CaseRecord> ltlas { get; set; }
        public List<CaseRecord> countries { get; set; }
        public List<CaseRecord> regions { get; set; }
        public List<CaseRecord> utlas { get; set; }
    }

    public class DataVersion
    {
        [JsonProperty(PropertyName = "id")]
        public string ID
        {
            get => this.metadata.hash;
        }
        [JsonProperty(PropertyName = "partition")]
        public string Partition
        {
            get => this.metadata.Partition;
        }

        public string datatype
        {
            get => "casedataversion";
        }

        public DailyRecords dailyRecords { get; set; }

        public Metadata metadata { get; set; }
        public List<CaseRecord> casedata { get; set; }

    }

}
