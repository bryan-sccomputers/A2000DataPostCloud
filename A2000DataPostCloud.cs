using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;

namespace A2000DataPostCloud
{
    public static class A2000DataPostCloud
    {
        /// <summary>
        /// Builds the HTTP Basic Authentication header required
        /// by the A2000 OAuth endpoint.
        /// </summary>
        private static string BuildBasicAuthHeader(string username, string password)
        {
            string rawValue = $"{username}:{password}";
            string encodedValue = Convert.ToBase64String(
                Encoding.UTF8.GetBytes(rawValue)
            );

            return $"Basic {encodedValue}";
        }


        [FunctionName("A2000DataPostCloud")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(
                AuthorizationLevel.Function,
                "get",
                "post",
                Route = null
            )] HttpRequest req,
            ILogger log)
        {
            string runId = Guid.NewGuid().ToString();

            log.LogInformation(
                "A2000DataPostCloud started. RunId: {RunId}",
                runId
            );

            log.LogInformation(
                "Request method: {Method}. Content-Type: {ContentType}",
                req.Method,
                req.ContentType
            );


            /*
             * ---------------------------------------------------------
             * GET
             * ---------------------------------------------------------
             *
             * GET is only used as a health/configuration test.
             *
             * Example:
             *
             * GET
             * https://posta2kdatacloud.azurewebsites.net/
             * api/A2000DataPostCloud?code=FUNCTION_KEY
             *
             */
            if (req.Method.Equals(
                "GET",
                StringComparison.OrdinalIgnoreCase))
            {
                string baseUrl =
                    Environment.GetEnvironmentVariable("A2000_BASE_URL");

                string username =
                    Environment.GetEnvironmentVariable("A2000_AUTH_USER");

                string password =
                    Environment.GetEnvironmentVariable("A2000_AUTH_PASSWORD");


                return new OkObjectResult(new
                {
                    status = "A2000DataPostCloud test successful",
                    function = "A2000DataPostCloud",
                    timestampUtc = DateTime.UtcNow,

                    environment = new
                    {
                        hasBaseUrl =
                            !string.IsNullOrWhiteSpace(baseUrl),

                        hasUsername =
                            !string.IsNullOrWhiteSpace(username),

                        hasPassword =
                            !string.IsNullOrWhiteSpace(password)
                    },

                    method = req.Method,
                    runId
                });
            }


            try
            {
                /*
                 * ---------------------------------------------------------
                 * READ REQUEST BODY
                 * ---------------------------------------------------------
                 *
                 * Expected JSON:
                 *
                 * {
                 *     "table": "style_reclass",
                 *     "payload": {
                 *         "IGNORE_ERRORS": "Y",
                 *         "STYLE_RECLASS": [...]
                 *     }
                 * }
                 *
                 */

                string requestBody =
                    await new StreamReader(req.Body).ReadToEndAsync();


                log.LogInformation(
                    "Request body read. RunId: {RunId}. Length: {Length}",
                    runId,
                    requestBody?.Length ?? 0
                );


                if (string.IsNullOrWhiteSpace(requestBody))
                {
                    log.LogWarning(
                        "Request body was empty. RunId: {RunId}",
                        runId
                    );

                    return new BadRequestObjectResult(new
                    {
                        error = "Request body is required.",
                        expectedFormat = new
                        {
                            table = "style_reclass",
                            payload = new
                            {
                                IGNORE_ERRORS = "Y",
                                STYLE_RECLASS = "..."
                            }
                        },
                        runId
                    });
                }


                /*
                 * ---------------------------------------------------------
                 * PARSE JSON
                 * ---------------------------------------------------------
                 */

                JObject data;

                try
                {
                    data = JObject.Parse(requestBody);
                }
                catch (JsonException jsonEx)
                {
                    log.LogError(
                        jsonEx,
                        "Invalid JSON request. RunId: {RunId}",
                        runId
                    );

                    return new BadRequestObjectResult(new
                    {
                        error = "Invalid JSON in request body.",
                        details = jsonEx.Message,
                        runId
                    });
                }


                /*
                 * ---------------------------------------------------------
                 * GET TABLE
                 * ---------------------------------------------------------
                 */

                string table = data["table"]?.ToString()?.Trim();


                if (string.IsNullOrWhiteSpace(table))
                {
                    log.LogWarning(
                        "Missing table. RunId: {RunId}",
                        runId
                    );

                    return new BadRequestObjectResult(new
                    {
                        error = "Missing required value: table",
                        runId
                    });
                }


                /*
                 * ---------------------------------------------------------
                 * GET PAYLOAD
                 * ---------------------------------------------------------
                 */

                JToken payloadToken = data["payload"];


                if (payloadToken == null ||
                    payloadToken.Type == JTokenType.Null)
                {
                    log.LogWarning(
                        "Missing payload. RunId: {RunId}",
                        runId
                    );

                    return new BadRequestObjectResult(new
                    {
                        error = "Missing required value: payload",
                        runId
                    });
                }


                /*
                 * Serialize ONLY the payload portion.
                 *
                 * No URL decoding is needed.
                 *
                 * Characters such as:
                 *
                 * &
                 * %
                 * +
                 * #
                 * ®
                 *
                 * are safe because this JSON is being transmitted
                 * in the HTTP request BODY, not inside the URL.
                 */

                string postBody =
                    JsonConvert.SerializeObject(
                        payloadToken,
                        Formatting.None
                    );


                log.LogInformation(
                    "Input resolved. RunId: {RunId}. Table: {Table}. PayloadLength: {PayloadLength}",
                    runId,
                    table,
                    postBody.Length
                );


                /*
                 * ---------------------------------------------------------
                 * LOAD ENVIRONMENT VARIABLES
                 * ---------------------------------------------------------
                 */

                string baseUrl =
                    Environment.GetEnvironmentVariable("A2000_BASE_URL");

                string username =
                    Environment.GetEnvironmentVariable("A2000_AUTH_USER");

                string password =
                    Environment.GetEnvironmentVariable("A2000_AUTH_PASSWORD");


                log.LogInformation(
                    "Environment variables checked. RunId: {RunId}. " +
                    "HasBaseUrl: {HasBaseUrl}. " +
                    "HasUsername: {HasUsername}. " +
                    "HasPassword: {HasPassword}",
                    runId,
                    !string.IsNullOrWhiteSpace(baseUrl),
                    !string.IsNullOrWhiteSpace(username),
                    !string.IsNullOrWhiteSpace(password)
                );


                if (string.IsNullOrWhiteSpace(baseUrl))
                {
                    log.LogError(
                        "Missing A2000_BASE_URL. RunId: {RunId}",
                        runId
                    );

                    return new ObjectResult(new
                    {
                        error =
                            "Server configuration error: missing A2000_BASE_URL.",
                        runId
                    })
                    {
                        StatusCode = 500
                    };
                }


                if (string.IsNullOrWhiteSpace(username))
                {
                    log.LogError(
                        "Missing A2000_AUTH_USER. RunId: {RunId}",
                        runId
                    );

                    return new ObjectResult(new
                    {
                        error =
                            "Server configuration error: missing A2000_AUTH_USER.",
                        runId
                    })
                    {
                        StatusCode = 500
                    };
                }


                if (string.IsNullOrWhiteSpace(password))
                {
                    log.LogError(
                        "Missing A2000_AUTH_PASSWORD. RunId: {RunId}",
                        runId
                    );

                    return new ObjectResult(new
                    {
                        error =
                            "Server configuration error: missing A2000_AUTH_PASSWORD.",
                        runId
                    })
                    {
                        StatusCode = 500
                    };
                }


                baseUrl = baseUrl.TrimEnd('/');


                /*
                 * ---------------------------------------------------------
                 * AUTHENTICATE WITH A2000
                 * ---------------------------------------------------------
                 */

                string authUrl = $"{baseUrl}/oauth/token";

                string basicAuthHeader =
                    BuildBasicAuthHeader(username, password);


                log.LogInformation(
                    "Starting A2000 authentication. RunId: {RunId}. AuthUrl: {AuthUrl}",
                    runId,
                    authUrl
                );


                var authClient = new RestClient(authUrl)
                {
                    Timeout = -1,
                    Encoding = Encoding.UTF8
                };


                var authRequest = new RestRequest(Method.POST);

                authRequest.AddHeader(
                    "Authorization",
                    basicAuthHeader
                );

                authRequest.AddHeader(
                    "Content-Type",
                    "application/x-www-form-urlencoded; charset=UTF-8"
                );

                authRequest.AddParameter(
                    "grant_type",
                    "client_credentials"
                );


                IRestResponse authResponse =
                    authClient.Execute(authRequest);


                log.LogInformation(
                    "Auth response received. RunId: {RunId}. " +
                    "StatusCode: {StatusCode}. " +
                    "IsSuccessful: {IsSuccessful}",
                    runId,
                    authResponse.StatusCode,
                    authResponse.IsSuccessful
                );


                /*
                 * ---------------------------------------------------------
                 * HANDLE AUTH FAILURE
                 * ---------------------------------------------------------
                 */

                if (!authResponse.IsSuccessful)
                {
                    log.LogError(
                        "A2000 authentication failed. " +
                        "RunId: {RunId}. " +
                        "StatusCode: {StatusCode}. " +
                        "ErrorMessage: {ErrorMessage}. " +
                        "Response: {Response}",
                        runId,
                        authResponse.StatusCode,
                        authResponse.ErrorMessage,
                        authResponse.Content
                    );


                    return new ObjectResult(new
                    {
                        error = "A2000 authentication failed.",
                        statusCode = (int)authResponse.StatusCode,
                        response = authResponse.Content,
                        runId
                    })
                    {
                        StatusCode =
                            (int)authResponse.StatusCode
                    };
                }


                /*
                 * ---------------------------------------------------------
                 * READ ACCESS TOKEN
                 * ---------------------------------------------------------
                 */

                JObject tokenData;

                try
                {
                    tokenData =
                        JObject.Parse(authResponse.Content);
                }
                catch (JsonException tokenEx)
                {
                    log.LogError(
                        tokenEx,
                        "Unable to parse A2000 auth response. RunId: {RunId}",
                        runId
                    );

                    return new ObjectResult(new
                    {
                        error =
                            "Unable to parse A2000 authentication response.",
                        runId
                    })
                    {
                        StatusCode = 500
                    };
                }


                string token =
                    tokenData["access_token"]?.ToString();


                if (string.IsNullOrWhiteSpace(token))
                {
                    log.LogError(
                        "Authentication succeeded but no access_token was returned. RunId: {RunId}",
                        runId
                    );


                    return new ObjectResult(new
                    {
                        error =
                            "Authentication succeeded but no access token was returned.",
                        runId
                    })
                    {
                        StatusCode = 500
                    };
                }


                log.LogInformation(
                    "A2000 authentication successful. RunId: {RunId}",
                    runId
                );


                /*
                 * ---------------------------------------------------------
                 * BUILD A2000 UPLOAD URL
                 * ---------------------------------------------------------
                 */

                string uploadUrl =
                    $"{baseUrl}/uploads/upload/{table}";


                log.LogInformation(
                    "Starting A2000 upload. RunId: {RunId}. Table: {Table}. URL: {UploadUrl}",
                    runId,
                    table,
                    uploadUrl
                );


                /*
                 * ---------------------------------------------------------
                 * SEND JSON TO A2000
                 * ---------------------------------------------------------
                 */

                var client = new RestClient(uploadUrl)
                {
                    Timeout = -1,
                    Encoding = Encoding.UTF8
                };


                var request = new RestRequest(Method.POST);


                request.AddHeader(
                    "Authorization",
                    $"Bearer {token}"
                );


                request.AddHeader(
                    "Content-Type",
                    "application/json; charset=UTF-8"
                );


                /*
                 * postBody is already valid JSON.
                 *
                 * Do NOT:
                 *
                 * Uri.EscapeDataString()
                 * Replace("%26", "&")
                 * URL encode it
                 *
                 * It is being sent as the HTTP body.
                 */

                request.AddParameter(
                    "application/json",
                    postBody,
                    ParameterType.RequestBody
                );


                IRestResponse response =
                    client.Execute(request);


                /*
                 * ---------------------------------------------------------
                 * LOG A2000 RESPONSE
                 * ---------------------------------------------------------
                 */

                log.LogInformation(
                    "A2000 upload response received. " +
                    "RunId: {RunId}. " +
                    "StatusCode: {StatusCode}. " +
                    "IsSuccessful: {IsSuccessful}. " +
                    "ResponseLength: {ResponseLength}",
                    runId,
                    response.StatusCode,
                    response.IsSuccessful,
                    response.Content?.Length ?? 0
                );


                /*
                 * ---------------------------------------------------------
                 * HANDLE A2000 FAILURE
                 * ---------------------------------------------------------
                 */

                if (!response.IsSuccessful)
                {
                    log.LogError(
                        "A2000 upload failed. " +
                        "RunId: {RunId}. " +
                        "Table: {Table}. " +
                        "StatusCode: {StatusCode}. " +
                        "ErrorMessage: {ErrorMessage}. " +
                        "Response: {Response}",
                        runId,
                        table,
                        response.StatusCode,
                        response.ErrorMessage,
                        response.Content
                    );


                    return new ObjectResult(new
                    {
                        error = "A2000 upload failed.",
                        table,
                        statusCode =
                            (int)response.StatusCode,
                        response =
                            response.Content,
                        runId
                    })
                    {
                        StatusCode =
                            (int)response.StatusCode
                    };
                }


                /*
                 * ---------------------------------------------------------
                 * SUCCESS
                 * ---------------------------------------------------------
                 */

                log.LogInformation(
                    "A2000DataPostCloud completed successfully. " +
                    "RunId: {RunId}. Table: {Table}",
                    runId,
                    table
                );


                /*
                 * Try to return A2000's JSON as actual JSON.
                 *
                 * If A2000 returned plain text, return it as text.
                 */

                return new OkObjectResult(response.Content);
            }
            catch (Exception ex)
            {
                /*
                 * ---------------------------------------------------------
                 * UNHANDLED ERROR
                 * ---------------------------------------------------------
                 */

                log.LogError(
                    ex,
                    "Unhandled exception in A2000DataPostCloud. RunId: {RunId}",
                    runId
                );


                return new ObjectResult(new
                {
                    error =
                        "Unhandled error occurred.",
                    message =
                        ex.Message,
                    runId
                })
                {
                    StatusCode = 500
                };
            }
        }
    }
}