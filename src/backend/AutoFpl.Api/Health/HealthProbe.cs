namespace AutoFpl.Api.Health;

public static class HealthProbe
{
    private static readonly Uri HealthEndpoint = new("http://127.0.0.1:8080/healthz");

    public static async Task<int> CheckAsync()
    {
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5),
        };

        try
        {
            using HttpResponseMessage response = await client.GetAsync(
                HealthEndpoint,
                CancellationToken.None);
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (HttpRequestException)
        {
            return 1;
        }
        catch (TaskCanceledException)
        {
            return 1;
        }
    }
}
