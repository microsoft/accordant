// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Booking.Tests;

using System.Net;
using System.Net.Http.Json;
using Booking.Api.Contracts; // For Slot, BookSlotRequest

/// <summary>
/// HTTP client wrapper for the Booking API.
/// Converts HTTP responses to ApiResult for easy handling in specs.
/// </summary>
public class BookingApiClient
{
    private readonly HttpClient _client;

    public BookingApiClient(HttpClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Create a new slot.
    /// PUT /api/slots/{slotId}
    /// </summary>
    public async Task<ApiResult<Slot>> CreateSlotAsync(string slotId)
    {
        var response = await _client.PutAsync($"/api/slots/{slotId}", null);
        return await ToApiResult<Slot>(response);
    }

    /// <summary>
    /// Get a slot by ID.
    /// GET /api/slots/{slotId}
    /// </summary>
    public async Task<ApiResult<Slot>> GetSlotAsync(string slotId)
    {
        var response = await _client.GetAsync($"/api/slots/{slotId}");
        return await ToApiResult<Slot>(response);
    }

    /// <summary>
    /// Delete a slot.
    /// DELETE /api/slots/{slotId}
    /// </summary>
    public async Task<int> DeleteSlotAsync(string slotId)
    {
        var response = await _client.DeleteAsync($"/api/slots/{slotId}");
        return (int)response.StatusCode;
    }

    /// <summary>
    /// Book a slot for a customer.
    /// POST /api/slots/{slotId}/book
    /// </summary>
    public async Task<ApiResult<Slot>> BookSlotAsync(string slotId, string customer)
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/slots/{slotId}/book",
            new BookSlotRequest(customer));
        return await ToApiResult<Slot>(response);
    }

    /// <summary>
    /// Cancel a booking.
    /// POST /api/slots/{slotId}/cancel
    /// </summary>
    public async Task<ApiResult<Slot>> CancelBookingAsync(string slotId)
    {
        var response = await _client.PostAsync($"/api/slots/{slotId}/cancel", null);
        return await ToApiResult<Slot>(response);
    }

    private static async Task<ApiResult<T>> ToApiResult<T>(HttpResponseMessage response)
    {
        var result = new ApiResult<T>
        {
            StatusCode = (int)response.StatusCode
        };

        if (response.IsSuccessStatusCode)
        {
            result.Data = await response.Content.ReadFromJsonAsync<T>();
        }
        else
        {
            result.Error = await response.Content.ReadAsStringAsync();
        }

        return result;
    }
}
