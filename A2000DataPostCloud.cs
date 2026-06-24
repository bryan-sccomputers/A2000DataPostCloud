using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using RestSharp;
using System.Text;

namespace A2000DataPostCloud
{
    public static class A2000DataPostCloud
    {
        public static string DecodeBody(string body)
        {
            return body?.Replace("%26", "&");
        }

        private static string BuildBasicAuthHeader(string username, string password)
        {
            string rawValue = $"{username}:{password}";
            string encodedValue = Convert.ToBase64String(Encoding.UTF8.GetBytes(rawValue));
            return $"Basic {encodedValue}";
        }

        [FunctionName("A2000DataPostCloud")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            string runId = Guid.NewGuid().ToString();

            log.LogInformation("A2000DataPost started. RunId: {RunId}", runId);
            log.LogInformation("Request method: {Method}. Content-Type: {ContentType}", req.Method, req.ContentType);
            if (req.Method.Equals("GET", StringComparison.OrdinalIgnoreCase))
            {
                string baseUrl = Environment.GetEnvironmentVariable("A2000_BASE_URL");
                string username = Environment.GetEnvironmentVariable("A2000_AUTH_USER");
                string password = Environment.GetEnvironmentVariable("A2000_AUTH_PASSWORD");

                return new OkObjectResult(new
                {
                    status = "SCM test successful",
                    function = "A2000DataPostCloud",
                    timestampUtc = DateTime.UtcNow,
                    hasBaseUrl = !string.IsNullOrWhiteSpace(baseUrl),
                    hasUsername = !string.IsNullOrWhiteSpace(username),
                    hasPassword = !string.IsNullOrWhiteSpace(password),
                    method = req.Method
                });
            }

            try
            {
                string table = req.Query["table"];
                string post_body = req.Query["post_body"];

                log.LogInformation("Query values received. RunId: {RunId}. Table from query exists: {HasTable}. Post body from query exists: {HasPostBody}",
                    runId,
                    !string.IsNullOrWhiteSpace(table),
                    !string.IsNullOrWhiteSpace(post_body));

                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

                log.LogInformation("Request body read. RunId: {RunId}. Body length: {BodyLength}",
                    runId,
                    requestBody?.Length ?? 0);

                dynamic data = null;

                if (!string.IsNullOrWhiteSpace(requestBody))
                {
                    try
                    {
                        data = JsonConvert.DeserializeObject(requestBody);
                        log.LogInformation("Request body JSON parsed successfully. RunId: {RunId}", runId);
                    }
                    catch (Exception jsonEx)
                    {
                        log.LogError(jsonEx, "Failed to parse request body JSON. RunId: {RunId}. Body: {Body}",
                            runId,
                            requestBody);

                        return new BadRequestObjectResult("Invalid JSON in request body.");
                    }
                }
                else
                {
                    log.LogWarning("Request body was empty. RunId: {RunId}", runId);
                }

                table = table ?? data?.table;
                post_body = post_body ?? data?.post_body;

                log.LogInformation("Final input values resolved. RunId: {RunId}. Table: {Table}. Has post_body: {HasPostBody}",
                    runId,
                    table,
                    !string.IsNullOrWhiteSpace(post_body));

                if (string.IsNullOrWhiteSpace(table))
                {
                    log.LogWarning("Missing required value: table. RunId: {RunId}", runId);
                    return new BadRequestObjectResult("Missing required value: table");
                }

                if (string.IsNullOrWhiteSpace(post_body))
                {
                    log.LogWarning("Missing required value: post_body. RunId: {RunId}", runId);
                    return new BadRequestObjectResult("Missing required value: post_body");
                }

                string baseUrl = Environment.GetEnvironmentVariable("A2000_BASE_URL");
                string username = Environment.GetEnvironmentVariable("A2000_AUTH_USER");
                string password = Environment.GetEnvironmentVariable("A2000_AUTH_PASSWORD");

                log.LogInformation("Environment variables checked. RunId: {RunId}. HasBaseUrl: {HasBaseUrl}. HasUsername: {HasUsername}. HasPassword: {HasPassword}",
                    runId,
                    !string.IsNullOrWhiteSpace(baseUrl),
                    !string.IsNullOrWhiteSpace(username),
                    !string.IsNullOrWhiteSpace(password));

                if (string.IsNullOrWhiteSpace(baseUrl))
                {
                    log.LogError("Missing environment variable: A2000_BASE_URL. RunId: {RunId}", runId);
                    return new BadRequestObjectResult("Missing environment variable: A2000_BASE_URL");
                }

                if (string.IsNullOrWhiteSpace(username))
                {
                    log.LogError("Missing environment variable: A2000_AUTH_USER. RunId: {RunId}", runId);
                    return new BadRequestObjectResult("Missing environment variable: A2000_AUTH_USER");
                }

                if (string.IsNullOrWhiteSpace(password))
                {
                    log.LogError("Missing environment variable: A2000_AUTH_PASSWORD. RunId: {RunId}", runId);
                    return new BadRequestObjectResult("Missing environment variable: A2000_AUTH_PASSWORD");
                }

                baseUrl = baseUrl.TrimEnd('/');

                log.LogInformation("Base URL prepared. RunId: {RunId}. BaseUrl: {BaseUrl}",
                    runId,
                    baseUrl);

                string basicAuthHeader = BuildBasicAuthHeader(username, password);

                // Post for auth token
                string authUrl = $"{baseUrl}/oauth/token";

                log.LogInformation("Starting auth request. RunId: {RunId}. AuthUrl: {AuthUrl}",
                    runId,
                    authUrl);

                var authClient = new RestClient(authUrl);
                authClient.Timeout = -1;

                var authRequest = new RestRequest(Method.POST);
                authRequest.AddHeader("Authorization", basicAuthHeader);
                authRequest.AddHeader("Content-Type", "application/x-www-form-urlencoded; charset=UTF-8");
                authRequest.AddParameter("grant_type", "client_credentials");

                IRestResponse authResponse = authClient.Execute(authRequest);

                log.LogInformation("Auth response received. RunId: {RunId}. StatusCode: {StatusCode}. IsSuccessful: {IsSuccessful}. ResponseLength: {ResponseLength}",
                    runId,
                    authResponse.StatusCode,
                    authResponse.IsSuccessful,
                    authResponse.Content?.Length ?? 0);

                if (!authResponse.IsSuccessful)
                {
                    log.LogError("Auth request failed. RunId: {RunId}. Status: {StatusCode}. ErrorMessage: {ErrorMessage}. Content: {Content}",
                        runId,
                        authResponse.StatusCode,
                        authResponse.ErrorMessage,
                        authResponse.Content);

                    return new ObjectResult(authResponse.Content)
                    {
                        StatusCode = (int)authResponse.StatusCode
                    };
                }

                dynamic tokenData = JsonConvert.DeserializeObject(authResponse.Content);
                string token = tokenData?.access_token;

                if (string.IsNullOrWhiteSpace(token))
                {
                    log.LogError("Auth succeeded but no access_token was returned. RunId: {RunId}. Content: {Content}",
                        runId,
                        authResponse.Content);

                    return new BadRequestObjectResult("Auth succeeded but no access_token was returned.");
                }

                log.LogInformation("Auth token received successfully. RunId: {RunId}", runId);

                // Use auth token and make POST upload to A2000
                string uploadUrl = $"{baseUrl}/uploads/upload/{table}";

                log.LogInformation("Starting upload request. RunId: {RunId}. UploadUrl: {UploadUrl}. Table: {Table}",
                    runId,
                    uploadUrl,
                    table);

                string decodedBody = DecodeBody(post_body);

                log.LogInformation("Post body decoded. RunId: {RunId}. OriginalLength: {OriginalLength}. DecodedLength: {DecodedLength}",
                    runId,
                    post_body?.Length ?? 0,
                    decodedBody?.Length ?? 0);

                log.LogInformation("Final A2000 upload payload. RunId: {RunId}. Payload: {Payload}",
                    runId,
                    decodedBody);

                var client = new RestClient(uploadUrl);
                client.Timeout = -1;
                client.Encoding = Encoding.Latin1;

                var request = new RestRequest(Method.POST);
                request.AddHeader("Authorization", $"Bearer {token}");
                request.AddHeader("Content-Type", "application/json; charset=UTF-8");
                request.AddParameter("application/json", decodedBody, ParameterType.RequestBody);

                IRestResponse response = client.Execute(request);

                log.LogInformation("Upload response received. RunId: {RunId}. StatusCode: {StatusCode}. IsSuccessful: {IsSuccessful}. ResponseLength: {ResponseLength}",
                    runId,
                    response.StatusCode,
                    response.IsSuccessful,
                    response.Content?.Length ?? 0);

                if (!response.IsSuccessful)
                {
                    log.LogError("Upload request failed. RunId: {RunId}. Status: {StatusCode}. ErrorMessage: {ErrorMessage}. Content: {Content}",
                        runId,
                        response.StatusCode,
                        response.ErrorMessage,
                        response.Content);

                    return new ObjectResult(response.Content)
                    {
                        StatusCode = (int)response.StatusCode
                    };
                }

                log.LogInformation("A2000DataPost completed successfully. RunId: {RunId}. Table: {Table}",
                    runId,
                    table);

                return new OkObjectResult(response.Content);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Unhandled exception in A2000DataPost. RunId: {RunId}", runId);

                return new ObjectResult($"Unhandled error occurred. RunId: {runId}. Error: {ex.Message}")
                {
                    StatusCode = 500
                };
            }
        }
    }
}