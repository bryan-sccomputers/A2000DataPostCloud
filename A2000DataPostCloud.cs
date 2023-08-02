using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Text;
using RestSharp;

namespace A2000DataPostCloud
{
    public static class A2000DataPostCloud
    {
        public static string DecodeBody(string body)
        {
            return body.Replace("%26", "&");
        }

        [FunctionName("A2000DataPostCloud")]
        public static async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req, ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");
            string table = req.Query["table"];
            string post_body = req.Query["post_body"];
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            table ??= data?.table;
            post_body ??= data?.post_body;

            //Post for auth token
            RestClient authClient = new("http://spirit.a2000cloud.com:8890/ords/spirit/api/oauth/token")
            {
                Timeout = -1
            };
            RestRequest authRequest = new(Method.POST);
            authRequest.AddHeader("Authorization", "Basic MHFUc2syX1FJRVJIaXIwb1dEM2ZvZy4uOldIdzlHSlZLTFBnQjNETjJjSkt4YncuLg==");
            authRequest.AddHeader("Content-Type", "application/x-www-form-urlencoded; charset=UTF-8");
            authRequest.AddParameter("grant_type", "client_credentials");
            IRestResponse authResponse = authClient.Execute(authRequest);
            //Extract auth token from response body
            dynamic tokenData = JsonConvert.DeserializeObject(authResponse.Content);
            string token = tokenData.access_token;

            //Use auth token and make Post Upload to A2000 Cloud
            RestClient client = new("http://spirit.a2000cloud.com:8890/ords/spirit/api/uploads/upload/" + table)
            {
                Timeout = -1,
                Encoding = Encoding.Latin1
            };
            RestRequest request = new(Method.POST);
            request.AddHeader("Authorization", "Bearer " + token);
            request.AddHeader("Content-Type", "application/json; charset=UTF-8");
            request.AddParameter("application/json", DecodeBody(post_body), ParameterType.RequestBody);
            IRestResponse response = client.Execute(request);

            //Display status to page
            return new OkObjectResult(response.Content);
        }
    }
}
