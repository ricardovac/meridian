using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Application.Common;
using Meridian.Application.Dtos;

namespace Meridian.Tests.Api;

public static class ApiClientExtensions
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task<HttpClient> RegisterUserAsync(this MeridianApiFactory factory, string? email = null)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = email ?? $"user-{Guid.NewGuid():N}@meridian.dev",
            password = "s3cret-password",
        });

        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadFromJsonAsync<AuthResult>(Json);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);
        return client;
    }

    public static async Task<AccountDto> GetMainAccountAsync(this HttpClient client)
    {
        var accounts = await client.GetFromJsonAsync<List<AccountDto>>("/api/accounts", Json);
        return Assert.Single(accounts!, a => a.Name == "Main");
    }

    public static async Task<HttpResponseMessage> PostTransferAsync(
        this HttpClient client, string idempotencyKey, object payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/transfers")
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(request);
    }

    public static async Task<HttpResponseMessage> PostDepositAsync(
        this HttpClient client, Guid accountId, decimal amount, string idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/accounts/{accountId}/deposit")
        {
            Content = JsonContent.Create(new { amount }),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(request);
    }

    public static async Task<PagedResult<LedgerEntryDto>> ReadEntriesAsync(
        this HttpClient client, Guid accountId, int? page = null, int? pageSize = null)
    {
        var query = page is null ? "" : $"?page={page}&pageSize={pageSize}";
        var result = await client.GetFromJsonAsync<PagedResult<LedgerEntryDto>>(
            $"/api/accounts/{accountId}/entries{query}", Json);
        return result!;
    }

    public static Task<T?> ReadAsAsync<T>(this HttpResponseMessage response) =>
        response.Content.ReadFromJsonAsync<T>(Json);
}
