using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Polly;

namespace phetracker
{

    public class APIClient
    {
        public DateTimeOffset? LastModified { get; private set; }

        private ILogger logger;
        private HttpClient client;
        private const string apiEndpoint = "https://api.coronavirus.data.gov.uk";
        private const string BaseUrl = apiEndpoint + @"/v1/data?filters=areaType=ltla&structure=%7B%22date%22:%20%22date%22,%22areaCode%22:%20%22areaCode%22,%20%22areaName%22:%20%22areaName%22,%20%22newCasesBySpecimenDate%22:%20%22newCasesBySpecimenDate%22%7D&page=1";

        public APIClient(ILogger logger)
        {
            var handler = new HttpRetryMessageHandler(new HttpClientHandler()
            {
                AutomaticDecompression = DecompressionMethods.GZip
            }, logger);

            this.logger = logger;
            client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromMinutes(10);
            client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        }

        public async Task<List<NhsTrustDataPoint>> GetNHSTrustData()
        {
            using var response = await (new HttpClient()).GetAsync("https://api.coronavirus.data.gov.uk/v2/data?areaType=nhsTrust&metric=hospitalCases&metric=newAdmissions&metric=covidOccupiedMVBeds&format=json");
            var contentStream = await response.Content.ReadAsStreamAsync();

            using var streamReader = new StreamReader(contentStream);
            using var jsonReader = new JsonTextReader(streamReader);

            JsonSerializer serializer = new JsonSerializer();

            var data = serializer.Deserialize<NhsTrustData>(jsonReader);
            return data.body;
        }

        public async Task<List<CaseDataV1>> GetLTLAData(ILogger logger)
        {

            try
            {
                var caseData = new List<CaseDataV1>();

                string url = BaseUrl;
                logger.LogInformation("Fetching case data page:" + url);
                var apiResponse = await client.GetStringAsync(BaseUrl);
                var data = JsonConvert.DeserializeObject<Root>(apiResponse);
                caseData.AddRange(data.data);

                while (!string.IsNullOrEmpty(data.pagination.next))
                {
                    url = apiEndpoint + data.pagination.next;
                    logger.LogInformation("Fetching case data page:" + url);
                    apiResponse = await client.GetStringAsync(url);
                    data = JsonConvert.DeserializeObject<Root>(apiResponse);
                    caseData.AddRange(data.data);
                }

                return caseData;
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, "Failed retrieving data from api");

                throw;
            }

        }

        public async Task<bool> HasChanged()
        {
            var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, BaseUrl));
            response.EnsureSuccessStatusCode();
            var val = response.Content.Headers.LastModified;

            if (val == null)
            {
                throw new Exception("Fail to get lat mupdated header");
            }

            if (val.Equals(LastModified))
            {
                return false;
            }

            LastModified = val;
            return true;


        }

    }

    public class CaseDataV1
    {
        public DateTime date { get; set; }
        public string areaCode { get; set; }
        public string areaName { get; set; }
        public int newCasesBySpecimenDate { get; set; }
    }

    public class Pagination
    {
        public string current { get; set; }
        public string? next { get; set; }
        public string previous { get; set; }
        public string first { get; set; }
        public string last { get; set; }
    }

    public class Root
    {
        public int length { get; set; }
        public int maxPageLimit { get; set; }
        public List<CaseDataV1> data { get; set; }
        public Pagination pagination { get; set; }
    }

    public class HttpRetryMessageHandler : DelegatingHandler
    {
        private ILogger logger;

        public HttpRetryMessageHandler(HttpClientHandler handler, ILogger logger) : base(handler)
        {
            this.logger = logger;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Policy
                .Handle<HttpRequestException>()
                .Or<TaskCanceledException>()
                .OrResult<HttpResponseMessage>(x => !x.IsSuccessStatusCode && x.StatusCode != HttpStatusCode.TooManyRequests)
                .WaitAndRetryAsync(8, retryAttempt =>
                {
                    logger.LogError("Request failed retrying");
                    return TimeSpan.FromSeconds(Math.Pow(3, retryAttempt));
                })
                .ExecuteAsync(() => base.SendAsync(request, cancellationToken));
    }

    public class NhsTrustDataPoint
    {
        public DateTime date { get; set; }
        public string areaType { get; set; }
        public string areaCode { get; set; }
        public string areaName { get; set; }
        public int? hospitalCases { get; set; }
        public int? newAdmissions { get; set; }
        public int? covidOccupiedMVBeds { get; set; }
        public string ltlaName { get; set; }
    }

    public class NhsTrustData
    {
        public int length { get; set; }
        public List<NhsTrustDataPoint> body { get; set; }
    }

}
