using Microsoft.AspNetCore.Mvc;
using Polly;
using Polly.Retry;
using Polly.Timeout;
using RestSharp;
using System.Net;

namespace TestApi.Controllers;

[ApiController]
[Route("api/v1/tests")]
public class TestController : ControllerBase
{
    private readonly ILogger<TestController> _logger;

    public TestController(ILogger<TestController> logger)
    {
        _logger = logger;
    }

    [HttpGet("retry")]
    public async Task<IActionResult> RetryTest()
    {
        var restClient = new RestClient("https://jsonplaceholder.typicode.com/");
        var restRequest = new RestRequest("todos/1111");
        var restResponse = await restClient.GetAsync(restRequest);

        await Policy.HandleResult<RestResponse>(x => !x.IsSuccessful)
            .WaitAndRetryAsync(5, x => TimeSpan.FromSeconds(Math.Pow(2, x)), async (response, time, retryCount, ctx) =>
            {
                _logger.LogInformation($"The request failed. HttpStatusCode={response.Result.StatusCode}. Waiting {time} seconds before retry. Number attempt {retryCount}. Uri={response.Result.ResponseUri}; RequestResponse={response.Result.Content}");
            })
          .ExecuteAsync(async () =>
          {
              return await restClient.GetAsync(restRequest);
          });

        if (restResponse.IsSuccessStatusCode)
            _logger.LogInformation("OK");
        else
            _logger.LogInformation("All Error");

        return Ok();
    }

    [HttpGet("circuit-creaker")]
    public async Task<IActionResult> CircuitBreaker()
    {
        var restClient = new RestClient("https://jsonplaceholder.typicode.com/");
        var restRequest = new RestRequest("todos/1111");
        var restResponse = await restClient.GetAsync(restRequest);

        var circuitBreakerPolicy = Policy
               .HandleResult<RestResponse>(x => !x.IsSuccessStatusCode)
               .CircuitBreaker(1, TimeSpan.FromSeconds(10), onBreak: async (response, time, ctx) =>
               {
                   _logger.LogInformation($"Service down. Uri={response.Result.ResponseUri};");
               },
               onReset: async (context) =>
               {
                   _logger.LogInformation($"Circuit breaker release");
               });

        var retryPolicy = Policy.HandleResult<RestResponse>(x => !x.IsSuccessStatusCode)
                        .WaitAndRetry(3, x => TimeSpan.FromSeconds(3), (response, time, retryCount, ctx) =>
                        {
                            _logger.LogInformation($"The request failed. HttpStatusCode={response.Result.StatusCode}. Waiting {time} seconds before retry. Number attempt {retryCount}. Uri={response.Result.ResponseUri}; RequestResponse={response.Result.Content}");
                            _logger.LogInformation($"CircuitBreaker State: {circuitBreakerPolicy.CircuitState}");
                        });

        retryPolicy.Execute(() =>
        {
            return restClient.Get(restRequest);
        });

        var policy = retryPolicy.Wrap(circuitBreakerPolicy)
                                .Execute(() => restClient.Execute(restRequest));

        return Ok();
    }

    [HttpGet("fallback")]
    public async Task<IActionResult> Fallback()
    {
        var restClient = new RestClient("https://jsonplaceholder.typicode.com/");
        var restRequest = new RestRequest("todos/1111");
        var restResponse = await restClient.GetAsync(restRequest);
 
        Policy
              .HandleResult<RestResponse>(x => !x.IsSuccessStatusCode)
              .Fallback(
                fallbackAction: () =>
                {
                    return restClient.Get(new RestRequest("todos/1"));
                },
                onFallback: (resp) =>
                {
                    _logger.LogInformation("Fallback triggered");
                })
              .Execute(() =>
                  {
                      return restClient.Get(restRequest);
                  });

        return Ok();
    }

    [HttpGet("timeout")]
    public IActionResult Timeout()
    {
        var timeoutPolicy = Policy.Timeout(3, TimeoutStrategy.Optimistic,
            onTimeout: (ctx, tmp, tsk) =>
            {
                _logger.LogInformation("Timeout triggered");
            });

        timeoutPolicy.Execute(() =>
        {
            Thread.Sleep(TimeSpan.FromSeconds(10));
        });

        return Ok();
    }
}