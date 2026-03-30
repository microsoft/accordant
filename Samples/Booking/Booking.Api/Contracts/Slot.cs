// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Booking.Api.Contracts;

/// <summary>
/// Represents a bookable time slot.
/// </summary>
/// <param name="SlotId">The unique identifier for the slot (e.g., "9am", "slot-1")</param>
/// <param name="BookedBy">The customer who booked this slot, or null if available</param>
public record Slot(
    string SlotId,
    string? BookedBy = null)
{
    /// <summary>
    /// Whether this slot is currently available for booking.
    /// </summary>
    public bool IsAvailable => BookedBy == null;
}

/// <summary>
/// Request to book a slot.
/// </summary>
/// <param name="Customer">The customer name to book the slot for</param>
public record BookSlotRequest(string Customer);
