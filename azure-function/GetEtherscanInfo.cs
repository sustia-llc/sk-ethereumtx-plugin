using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json.Linq;

namespace Plugins.EtherscanPlugin
{
    public class GetEtherscanData
    {
        private static readonly HttpClient client = new HttpClient();
        private readonly IEtherscanSettings _etherscanSettings;
        private readonly ILogger<GetEtherscanData> _logger;

        private readonly string EndpointURL;
        private readonly string EndpointTxListURL;

        public GetEtherscanData(IEtherscanSettings etherscanSettings, ILoggerFactory loggerFactory)
        {
            this._etherscanSettings = etherscanSettings;
            _logger = loggerFactory.CreateLogger<GetEtherscanData>();

            EndpointURL = "https://api.etherscan.io/";
            // pull the last two transactions defined by offset
            EndpointTxListURL = EndpointURL + "api?module=account&action=txlist&page=1&offset=2&address={address}&apikey=" + etherscanSettings.EtherscanApiKey;
        }

        [OpenApiOperation(operationId: "GetTxList", tags: new[] { "ExecuteFunction" }, Description = "Get a list of transactions by wallet address")]
        [OpenApiParameter(name: "walletAddr", Description = "Etherscan Wallet Address", Required = true, In = ParameterLocation.Query)]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "text/plain", bodyType: typeof(string), Description = "The list of transactions by wallet address")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json", bodyType: typeof(string), Description = "Returns the error of the input.")]
        [Function("GetTxList")]
        public HttpResponseData Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestData req)
        {
            string walletAddr = req.Query["walletAddr"];

            if (!string.IsNullOrEmpty(walletAddr))
            {
                string txListURL = EndpointTxListURL.Replace("{address}", walletAddr.ToString());
                string txList = client.GetStringAsync(txListURL).Result;

                if (txList.Contains("error"))
                {
                    HttpResponseData responseError = req.CreateResponse(HttpStatusCode.BadRequest);
                    responseError.Headers.Add("Content-Type", "application/json");
                    responseError.WriteString(txList);

                    return responseError;
                } else {
                    HttpResponseData response = req.CreateResponse(HttpStatusCode.OK);
                    response.Headers.Add("Content-Type", "application/json");
                    // parse txList into json object
                    JObject txListObj = JObject.Parse(txList);

                    // create a new JObject, and add the result from txListObj
                    JObject txListObjResult = new JObject();
                    txListObjResult["result"] = new JArray(txListObj["result"].Select(r => new JObject(
                        new JProperty("blockNumber", r["blockNumber"]),
                        new JProperty("timeStamp", r["timeStamp"]),
                        new JProperty("transactionIndex", r["transactionIndex"]),
                        new JProperty("from", r["from"]),
                        new JProperty("to", r["to"]),
                        new JProperty("value", r["value"]),
                        new JProperty("gas", r["gas"]),
                        new JProperty("gasPrice", r["gasPrice"])
                    )));
                    _logger.LogInformation("txList: " + txListObjResult["result"].ToString());
                    response.WriteString(txListObjResult["result"].ToString());

                    return response;
                }
            }
            else
            {
                HttpResponseData responseWalletAddrNotFound = req.CreateResponse(HttpStatusCode.BadRequest);
                responseWalletAddrNotFound.Headers.Add("Content-Type", "application/json");
                responseWalletAddrNotFound.WriteString("Please pass the wallet address on the query string or in the request body");

                return responseWalletAddrNotFound;
            }
        }
    }
}