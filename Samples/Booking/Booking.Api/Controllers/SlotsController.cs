// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Booking.Api.Controllers;

using Booking.Api.Contracts;
using Booking.Api.Data;
using Booking.Api.Models;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public class SlotsController : ControllerBase
{
    private readonly BookingDbContext _context;
    private static readonly SemaphoreSlim _writeLock = new(1, 1);

    /// <summary>
    /// Set to true to disable locking in BookSlot, introducing a race condition bug.
    /// This allows demonstrating how Accordant concurrent tests catch double-booking.
    /// </summary>
    public static bool DisableBookingLock { get; set; } = false;

    /// <summary>
    /// Artificial delay (ms) added when DisableBookingLock is true to make race more likely.
    /// </summary>
    public static int BuggyDelayMs { get; set; } = 10;

    public SlotsController(BookingDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Create a new slot.
    /// PUT /api/slots/{slotId}
    /// </summary>
    [HttpPut("{slotId}")]
    public async Task<IActionResult> CreateSlot(string slotId)
    {
        await _writeLock.WaitAsync();
        try
        {
            var existing = await _context.Slots.FindAsync(slotId);
            if (existing != null)
            {
                return Conflict(new { error = $"Slot '{slotId}' already exists" });
            }

            var entity = new SlotEntity
            {
                SlotId = slotId,
                BookedBy = null
            };

            _context.Slots.Add(entity);
            await _context.SaveChangesAsync();

            return Ok(new Slot(entity.SlotId, entity.BookedBy));
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Get a slot by ID.
    /// GET /api/slots/{slotId}
    /// </summary>
    [HttpGet("{slotId}")]
    public async Task<IActionResult> GetSlot(string slotId)
    {
        var entity = await _context.Slots.FindAsync(slotId);
        if (entity == null)
        {
            return NotFound(new { error = $"Slot '{slotId}' not found" });
        }

        return Ok(new Slot(entity.SlotId, entity.BookedBy));
    }

    /// <summary>
    /// Delete a slot.
    /// DELETE /api/slots/{slotId}
    /// </summary>
    [HttpDelete("{slotId}")]
    public async Task<IActionResult> DeleteSlot(string slotId)
    {
        await _writeLock.WaitAsync();
        try
        {
            var entity = await _context.Slots.FindAsync(slotId);
            if (entity == null)
            {
                return NotFound(new { error = $"Slot '{slotId}' not found" });
            }

            _context.Slots.Remove(entity);
            await _context.SaveChangesAsync();

            return NoContent();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Book a slot for a customer.
    /// POST /api/slots/{slotId}/book
    /// Returns 409 Conflict if already booked.
    /// </summary>
    [HttpPost("{slotId}/book")]
    public async Task<IActionResult> BookSlot(string slotId, [FromBody] BookSlotRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Customer))
        {
            return BadRequest(new { error = "Customer name is required" });
        }

        // When DisableBookingLock is true, skip the lock to demonstrate race condition
        var useLock = !DisableBookingLock;

        if (useLock)
        {
            await _writeLock.WaitAsync();
        }

        try
        {
            var entity = await _context.Slots.FindAsync(slotId);
            if (entity == null)
            {
                return NotFound(new { error = $"Slot '{slotId}' not found" });
            }

            // Add delay when buggy mode is enabled to make race condition more likely
            if (DisableBookingLock && BuggyDelayMs > 0)
            {
                await Task.Delay(BuggyDelayMs);
            }

            if (entity.BookedBy != null)
            {
                return Conflict(new { error = $"Slot '{slotId}' is already booked by '{entity.BookedBy}'" });
            }

            entity.BookedBy = request.Customer;
            await _context.SaveChangesAsync();

            return Ok(new Slot(entity.SlotId, entity.BookedBy));
        }
        finally
        {
            if (useLock)
            {
                _writeLock.Release();
            }
        }
    }

    /// <summary>
    /// Cancel a booking, making the slot available again.
    /// POST /api/slots/{slotId}/cancel
    /// Returns 400 Bad Request if not currently booked.
    /// </summary>
    [HttpPost("{slotId}/cancel")]
    public async Task<IActionResult> CancelBooking(string slotId)
    {
        await _writeLock.WaitAsync();
        try
        {
            var entity = await _context.Slots.FindAsync(slotId);
            if (entity == null)
            {
                return NotFound(new { error = $"Slot '{slotId}' not found" });
            }

            if (entity.BookedBy == null)
            {
                return BadRequest(new { error = $"Slot '{slotId}' is not currently booked" });
            }

            entity.BookedBy = null;
            await _context.SaveChangesAsync();

            return Ok(new Slot(entity.SlotId, entity.BookedBy));
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
