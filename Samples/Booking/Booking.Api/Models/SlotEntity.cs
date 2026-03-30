// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Booking.Api.Models;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Entity Framework entity for a slot.
/// </summary>
public class SlotEntity
{
    [Key]
    public string SlotId { get; set; } = string.Empty;
    
    /// <summary>
    /// The customer who booked this slot, or null if available.
    /// </summary>
    public string? BookedBy { get; set; }
}
