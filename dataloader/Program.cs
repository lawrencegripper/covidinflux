﻿using System;
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
using System.IO;

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
            populationData = LoadPopulationData();
            Maps maps = new Maps();
            var trustsData = maps.GetTrustsForLtlas();
            logger.LogInformation("Starting...");
            string dataFilePath = "datacache.json";

            logger.LogInformation("Downloading data from API");
            APIClient client = new APIClient(logger);
            CaseData data = new CaseData();
            var trustData = await client.GetNHSTrustData();
            var processedTrustData = new List<NhsTrustDataPoint>();
            if (System.IO.File.Exists(dataFilePath))
            {
                logger.LogInformation("Using Cached data delete data.json to load fresh data");
                data = JsonConvert.DeserializeObject<CaseData>(System.IO.File.ReadAllText(dataFilePath));
            }
            else
            {
                var caseDataV1 = await client.GetLTLAData(logger);

                data.ltlas = new List<CaseRecord>();
                data.metadata = new Metadata();
                foreach (var item in caseDataV1)
                {
                    data.ltlas.Add(new CaseRecord()
                    {
                        kind = "localAuthorityCases",
                        areaCode = item.areaCode,
                        areaName = item.areaName,
                        dailyLabConfirmedCases = item.newCasesBySpecimenDate,
                        specimenDate = item.date,
                    });
                }

                var caseDataString = JsonConvert.SerializeObject(data);
                System.IO.File.WriteAllText(dataFilePath, caseDataString);
            }

            // always process the hospital data
            // Build trust data structure
            var trustDataDictionary = trustData.GroupBy(y => y.areaName).ToDictionary(y => y.Key, z =>
            {
                return z.ToDictionary(v => v.date);
            });
            foreach (var ltla in data.ltlas.GroupBy(y => y.areaName))
            {
                try
                {
                    var hospitals = trustsData[ltla.Key];
                    foreach (var hospital in hospitals)
                    {
                        var hospitalData = trustDataDictionary[hospital.OrganisationName];
                        foreach (var dataPoint in hospitalData)
                        {
                            Console.WriteLine("Adding datapoint for ltal" + ltla.Key);
                            dataPoint.Value.ltlaName = ltla.Key;
                            processedTrustData.Add(dataPoint.Value);
                        }
                    }
                }
                catch (System.Exception)
                {
                    Console.WriteLine("Failed to find hospitals for ltla:" + ltla.Key);
                }
            }

            // Combine and dedupe (for regions) case data
            var allCaseData = data.ltlas;

            var pheBucketName = "pheCovidData";
            var orgId = "05d0f71967e52000";
            var Token = "hYf9V7UQge2McZNCTKerUkPwLvqncruS2hczL9jJX3ohSP-zJ7Yt1Za5J33qNcKkn_rI7pU3SiWHbV5waBt4dA==".ToCharArray();
            using (var influxDBClient = InfluxDBClientFactory.Create("http://localhost:9999", Token))
            using (var writeApi = influxDBClient.GetWriteApi())
            {
                BucketsApi bucketsApi = influxDBClient.GetBucketsApi();
                try
                {
                    var pheBucketInstance = await bucketsApi.FindBucketByNameAsync(pheBucketName);
                    await bucketsApi.DeleteBucketAsync(pheBucketInstance);
                }
                catch (Exception e)
                {
                    logger.LogError("Failed to delete bucket, may not exist" + e.ToString(), e);
                }
                var bucket = await bucketsApi.CreateBucketAsync(pheBucketName, orgId);
                var points = new List<PointData>(allCaseData.Count);

                foreach (var record in allCaseData)
                {
                    var recordPer100k = ConvertToPer100kValue(record);
                    var point = PointData.Measurement("confirmedCases")
                       .Tag("localAuth", record.areaName)
                       .Tag("localAuthKind", record.kind)
                       .Tag("localAuthCode", record.areaCode)
                       .Field("daily", record.dailyLabConfirmedCases ?? 0)
                       .Field("daily100k", recordPer100k?.dailyLabConfirmedCases ?? 0)
                       .Field("total", record.dailyTotalLabConfirmedCasesRate)
                       .Field("total100k", recordPer100k?.dailyTotalLabConfirmedCasesRate ?? 0)
                       .Timestamp(record.specimenDate.ToUniversalTime(), WritePrecision.S);
                    points.Add(point);

                }

                foreach (var record in processedTrustData)
                {
                    var point = PointData.Measurement("trustData")
                        .Tag("localAuth", record.ltlaName)
                        .Tag("trust", record.areaName)
                        .Field("newAdmissions", record.newAdmissions ?? 0)
                        .Field("cases", record.hospitalCases ?? 0)
                        .Field("mvBeds", record.covidOccupiedMVBeds ?? 0)
                        .Timestamp(record.date.ToUniversalTime(), WritePrecision.S);
                    points.Add(point);

                }
                writeApi.WritePoints(pheBucketName, orgId, points);


            }
        }

        private static Dictionary<string, int> LoadPopulationData()
        {
            var result = new Dictionary<string, int>();
            using (var rd = new StreamReader("../referencedata/populationData.csv"))
            {
                while (!rd.EndOfStream)
                {
                    // Track Code to population value
                    //E08000002,190990
                    var splits = rd.ReadLine().Split(',');
                    result[splits[0]] = int.Parse(splits[1]);
                }
            }
            return result;
        }

        private static CaseRecord ConvertToPer100kValue(CaseRecord record)
        {
            if (record == null)
            {
                return null;
            }
            if (!populationData.ContainsKey(record.areaCode))
            {
                return null;
            }
            var population = populationData[record.areaCode];
            try
            {
                double population100ks = population / 100000d;
                var newRecord = new CaseRecord
                {
                    areaCode = record.areaCode,
                    areaName = record.areaName,
                    specimenDate = record.specimenDate,
                    kind = record.kind,
                    dailyLabConfirmedCases = record.dailyLabConfirmedCases / population100ks,
                    dailyTotalLabConfirmedCasesRate = record.dailyTotalLabConfirmedCasesRate / population100ks,
                };
                return newRecord;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return null;
            }
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
        public string areaCode { get; set; }
        public string areaName { get; set; }
        public DateTime specimenDate { get; set; }
        public double? dailyLabConfirmedCases { get; set; }
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
