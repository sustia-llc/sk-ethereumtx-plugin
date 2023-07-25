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
        private List<string> currencies = new List<string> { "aed", "ars", "aud", "bch", "bdt", "bhd", "bmd", "bnb", "brl", "btc", "cad", "chf", "clp", "cny", "czk", "dkk", "dot", "eos", "eth", "eur", "gbp", "hkd", "huf", "idr", "ils", "inr", "jpy", "krw", "kwd", "lkr", "ltc", "mmk", "mxn", "myr", "ngn", "nok", "nzd", "php", "pkr", "pln", "rub", "sar", "sek", "sgd", "thb", "try", "twd", "uah", "usd", "vef", "vnd", "xag", "xau", "xdr", "xlm", "xrp", "yfi", "zar", "bits", "link", "sats" };
        private readonly string EndpointURL;
        private readonly string EndpointTxListURL;
        private readonly string EndpointETHDailyPriceURL;

        public GetEtherscanData(IEtherscanSettings etherscanSettings, ILoggerFactory loggerFactory)
        {
            this._etherscanSettings = etherscanSettings;
            _logger = loggerFactory.CreateLogger<GetEtherscanData>();

            EndpointURL = "https://api.etherscan.io/";
            // pull the last two transactions defined by offset
            EndpointTxListURL = EndpointURL + "api?module=account&action=txlist&page=1&offset=2&sort=desc&address={address}&apikey=" + etherscanSettings.EtherscanApiKey;
            EndpointETHDailyPriceURL = "https://api.coingecko.com/api/v3/coins/ethereum/history?date={date}&localization=false";
        }

        [OpenApiOperation(operationId: "GetTxList", tags: new[] { "ExecuteFunction" }, Description = "Get a list of transactions by wallet address")]
        [OpenApiParameter(name: "walletAddr", Description = "Etherscan Wallet Address", Required = true, In = ParameterLocation.Query)]
        [OpenApiParameter(name: "currency", Description = "one of: aed,ars,aud,bch,bdt,bhd,bmd,bnb,brl,btc,cad,chf,clp,cny,czk,dkk,dot,eos,eth,eur,gbp,hkd,huf,idr,ils,inr,jpy,krw,kwd,lkr,ltc,mmk,mxn,myr,ngn,nok,nzd,php,pkr,pln,rub,sar,sek,sgd,thb,try,twd,uah,usd,vef,vnd,xag,xau,xdr,xlm,xrp,yfi,zar,bits,link,sats", Required = true, In = ParameterLocation.Query)]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(string), Description = "The list of transactions by wallet address")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.BadRequest, contentType: "application/json", bodyType: typeof(string), Description = "Returns the error of the input.")]
        [Function("GetTxList")]
        public HttpResponseData Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestData req)
        {
            string walletAddr = req.Query["walletAddr"];

            string currency = req.Query["currency"];

            if (!currencies.Contains(currency)) {
                HttpResponseData responseCurrencyNotFound = req.CreateResponse(HttpStatusCode.BadRequest);
                responseCurrencyNotFound.Headers.Add("Content-Type", "application/json");
                //
                string listOfCurrencies = string.Join(",", currencies);
                responseCurrencyNotFound.WriteString("Please pass one of {listOfCurrencies} on the query string or in the request body".Replace("{listOfCurrencies}", listOfCurrencies));

                return responseCurrencyNotFound;
            }

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
                        // convert r["timeStamp"] to date
                        new JProperty("date", DateTimeOffset.FromUnixTimeSeconds(long.Parse(r["timeStamp"].ToString())).DateTime.ToString("dd-MM-yyyy")),
                        new JProperty("transactionIndex", r["transactionIndex"]),
                        new JProperty("from", r["from"]),
                        new JProperty("to", r["to"]),
                        new JProperty("value", r["value"]),
                        new JProperty("gas", r["gasUsed"]),
                        new JProperty("gasPrice", r["gasPrice"]),
                        new JProperty("ethAmt", .0001),
                        new JProperty("ethPriceInCurrency", 1800),
                        new JProperty("currency", currency),
                        new JProperty("currencyAmt", .18)
                    )));

                    // for each transaction, get the ETH price for that day using EndpointETHDailyPriceURL
                    // NOTE: The daily price is the average, so not exactly what you see in the Etherscan UI
                    foreach (JObject tx in txListObjResult["result"])
                    {
                        string ethDailyPriceURL = EndpointETHDailyPriceURL.Replace("{date}", tx["date"].ToString());
                        string ethDailyPrice = client.GetStringAsync(ethDailyPriceURL).Result;

                        // parse ethDailyPrice into json object
                        JObject ethDailyPriceObj = JObject.Parse(ethDailyPrice);

                        // create a new JObject, and add the result from ethDailyPriceObj
                        JObject ethDailyPriceObjResult = new JObject();
                        ethDailyPriceObjResult["result"] = new JArray(ethDailyPriceObj["market_data"]["current_price"][currency]);

                        // add the ethPriceInCurrency to the txListObjResult
                        tx["ethPriceInCurrency"] = ethDailyPriceObjResult["result"][0];

                        // update the ethAmt and currencyAmt
                        tx["ethAmt"] = (double)tx["gas"] * ((double)tx["gasPrice"] / 1e18);
                        tx["currencyAmt"] = (double)tx["ethAmt"] * (double)tx["ethPriceInCurrency"];
                    }

                    _logger.LogInformation("txList with currencyAmt: " + txListObjResult["result"].ToString());
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