// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Booking.Tests;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Booking.Api.Contracts;
using Booking.Api.Controllers;
using Microsoft.Accordant;
using NUnit.Framework;

// ============================================================
// State Definition
// ============================================================

/// <summary>
/// State tracks slots and their booking status.
/// Compare this to the implementation: Controller + Entity + EF DbContext + SQLite + WriteLock!
/// The spec is much simpler.
/// </summary>
[State]
public partial class BookingState
{
    /// <summary>
    /// Dictionary of slots. Key = slotId, Value = customer name (null if available).
    /// </summary>
    public Dictionary<string, string?> Slots { get; set; } = new();
}

/// <summary>
/// Accordant tests for the Booking REST API.
/// 
/// This sample demonstrates CONCURRENCY TESTING:
/// - The "double-booking" scenario: two customers trying to book the same slot
/// - Accordant automatically tests all interleavings
/// - Validates that exactly one booking succeeds (linearizability)
/// </summary>
[TestFixture]
public class BookingTests
{
    // ============================================================
    // Spec Creation
    // ============================================================

    private static Spec<BookingState> CreateSpec()
    {
        var spec = new Spec<BookingState>()
            .WithJsonPrinters();

        // ---------------------------------------------------------
        // CREATE SLOT: PUT /api/slots/{slotId}
        // Creates a new available slot
        // ---------------------------------------------------------
        spec.Operation<string, ApiResult<Slot>>("CreateSlot", (slotId, state) =>
        {
            if (state.Slots.ContainsKey(slotId))
            {
                return Expect.That<ApiResult<Slot>>(r => r.IsConflict,
                           $"Should return 409 Conflict because slot '{slotId}' already exists")
                       .SameState();
            }

            return Expect.That<ApiResult<Slot>>(
                       r => r.IsSuccess &&
                            r.Data != null &&
                            r.Data.SlotId == slotId &&
                            r.Data.BookedBy == null &&
                            r.Data.IsAvailable == true,
                       $"Should return 200 OK with available slot '{slotId}'")
                   .ThenState<BookingState>(nextState => nextState.Slots[slotId] = null);
        });

        // ---------------------------------------------------------
        // GET SLOT: GET /api/slots/{slotId}
        // ---------------------------------------------------------
        spec.Operation<string, ApiResult<Slot>>("GetSlot", (slotId, state) =>
        {
            if (!state.Slots.TryGetValue(slotId, out var bookedBy))
            {
                return Expect.That<ApiResult<Slot>>(r => r.IsNotFound,
                           $"Should return 404 Not Found because slot '{slotId}' doesn't exist")
                       .SameState();
            }

            return Expect.That<ApiResult<Slot>>(
                       r => r.IsSuccess &&
                            r.Data != null &&
                            r.Data.SlotId == slotId &&
                            r.Data.BookedBy == bookedBy,
                       $"Should return 200 OK with slot '{slotId}'")
                   .SameState();
        });

        // ---------------------------------------------------------
        // DELETE SLOT: DELETE /api/slots/{slotId}
        // ---------------------------------------------------------
        spec.Operation<string, int>("DeleteSlot", (slotId, state) =>
        {
            if (!state.Slots.ContainsKey(slotId))
            {
                return Expect.That<int>(s => s == 404,
                           $"Should return 404 Not Found because slot '{slotId}' doesn't exist")
                       .SameState();
            }

            return Expect.That<int>(s => s == 204,
                       $"Should return 204 No Content after deleting slot '{slotId}'")
                   .ThenState<BookingState>(nextState => nextState.Slots.Remove(slotId));
        });

        // ---------------------------------------------------------
        // BOOK SLOT: POST /api/slots/{slotId}/book
        // This is where the concurrency magic happens!
        // Two concurrent BookSlot calls should result in exactly one success.
        // ---------------------------------------------------------
        spec.Operation<(string SlotId, string Customer), ApiResult<Slot>>("BookSlot", (request, state) =>
        {
            var (slotId, customer) = request;

            if (!state.Slots.TryGetValue(slotId, out var currentBookedBy))
            {
                return Expect.That<ApiResult<Slot>>(r => r.IsNotFound,
                           $"Should return 404 Not Found because slot '{slotId}' doesn't exist")
                       .SameState();
            }

            if (currentBookedBy != null)
            {
                return Expect.That<ApiResult<Slot>>(r => r.IsConflict,
                           $"Should return 409 Conflict because slot '{slotId}' is already booked by '{currentBookedBy}'")
                       .SameState();
            }

            // Slot is available - book it!
            return Expect.That<ApiResult<Slot>>(
                       r => r.IsSuccess &&
                            r.Data != null &&
                            r.Data.SlotId == slotId &&
                            r.Data.BookedBy == customer &&
                            r.Data.IsAvailable == false,
                       $"Should return 200 OK with slot booked by '{customer}'")
                   .ThenState<BookingState>(nextState => nextState.Slots[slotId] = customer);
        });

        // ---------------------------------------------------------
        // CANCEL BOOKING: POST /api/slots/{slotId}/cancel
        // ---------------------------------------------------------
        spec.Operation<string, ApiResult<Slot>>("CancelBooking", (slotId, state) =>
        {
            if (!state.Slots.TryGetValue(slotId, out var bookedBy))
            {
                return Expect.That<ApiResult<Slot>>(r => r.IsNotFound,
                           $"Should return 404 Not Found because slot '{slotId}' doesn't exist")
                       .SameState();
            }

            if (bookedBy == null)
            {
                return Expect.That<ApiResult<Slot>>(r => r.IsBadRequest,
                           $"Should return 400 Bad Request because slot '{slotId}' is not currently booked")
                       .SameState();
            }

            return Expect.That<ApiResult<Slot>>(
                       r => r.IsSuccess &&
                            r.Data != null &&
                            r.Data.SlotId == slotId &&
                            r.Data.BookedBy == null &&
                            r.Data.IsAvailable == true,
                       $"Should return 200 OK with slot now available")
                   .ThenState<BookingState>(nextState => nextState.Slots[slotId] = null);
        });

        // ==========================================================
        // Bind to HTTP API
        // ==========================================================
        spec.ExecuteWith<BookingApiClient>()
            .BindAsync<string, ApiResult<Slot>>("CreateSlot",
                (client, slotId) => client.CreateSlotAsync(slotId))
            .BindAsync<string, ApiResult<Slot>>("GetSlot",
                (client, slotId) => client.GetSlotAsync(slotId))
            .BindAsync<string, int>("DeleteSlot",
                (client, slotId) => client.DeleteSlotAsync(slotId))
            .BindAsync<(string SlotId, string Customer), ApiResult<Slot>>("BookSlot",
                (client, req) => client.BookSlotAsync(req.SlotId, req.Customer))
            .BindAsync<string, ApiResult<Slot>>("CancelBooking",
                (client, slotId) => client.CancelBookingAsync(slotId));

        return spec;
    }

    // ============================================================
    // Test 1: Sequential Tests - Basic CRUD
    // ============================================================

    [Test]
    public async Task SequentialTests_BasicSlotOperations()
    {
        using var factory = new BookingServiceFactory();
        var spec = CreateSpec();
        var initialState = new BookingState();

        var createSlot = spec.GetOperation<string, ApiResult<Slot>>("CreateSlot");
        var getSlot = spec.GetOperation<string, ApiResult<Slot>>("GetSlot");
        var deleteSlot = spec.GetOperation<string, int>("DeleteSlot");
        var bookSlot = spec.GetOperation<(string, string), ApiResult<Slot>>("BookSlot");
        var cancelBooking = spec.GetOperation<string, ApiResult<Slot>>("CancelBooking");

        var inputs = new InputSet()
        {
            createSlot.With("9am", "Create 9am slot"),
            createSlot.With("10am", "Create 10am slot"),
            getSlot.With("9am", "Get 9am slot"),
            getSlot.With("unknown", "Get unknown slot"),
            bookSlot.With(("9am", "Alice"), "Alice books 9am"),
            bookSlot.With(("9am", "Bob"), "Bob tries to book 9am"),
            cancelBooking.With("9am", "Cancel 9am booking"),
            deleteSlot.With("9am", "Delete 9am slot"),
        };

        var testCases = spec.GenerateTests(
            initialState,
            inputs,
            new TestGenerationOptions
            {
                MaxDepth = 4,
                StateConstraint = state =>
                {
                    var s = (BookingState)state;
                    return s.Slots.Count <= 2; // Keep state space manageable
                }
            });

        var context = spec.CreateTestingContext();
        context.Register(new BookingApiClient(factory.CreateTestClient()));

        var results = await spec.RunTests(
            context,
            initialState,
            testCases,
            new TestExecutionOptions
            {
                BeforeEachAsync = async ctx =>
                {
                    var client = ctx.Context.Get<BookingApiClient>();
                    await client.DeleteSlotAsync("9am");
                    await client.DeleteSlotAsync("10am");
                }
            });

        var failures = results.Where(r => !r.Success).ToList();
        Assert.IsEmpty(failures, $"{failures.Count} failed. First: {failures.FirstOrDefault()?.LastFailureMessage}");
    }

    // ============================================================
    // Test 2: Concurrent Tests - The Double-Booking Scenario!
    // ============================================================

    /// <summary>
    /// This is the KEY test demonstrating concurrency testing.
    /// 
    /// Scenario: Slot "9am" exists and is available.
    /// Alice and Bob both try to book it concurrently.
    /// 
    /// Valid outcomes (linearizable):
    /// - Alice succeeds, Bob gets 409 Conflict
    /// - Bob succeeds, Alice gets 409 Conflict
    /// 
    /// Invalid outcome (bug!):
    /// - Both succeed (double-booking)
    /// 
    /// Accordant will test both interleavings and verify exactly one succeeds.
    /// </summary>
    [Test]
    public async Task ConcurrentTests_DoubleBookingPrevented()
    {
        using var factory = new BookingServiceFactory();
        var spec = CreateSpec();
        var initialState = new BookingState();

        var createSlot = spec.GetOperation<string, ApiResult<Slot>>("CreateSlot");
        var bookSlot = spec.GetOperation<(string, string), ApiResult<Slot>>("BookSlot");
        var getSlot = spec.GetOperation<string, ApiResult<Slot>>("GetSlot");

        var inputs = new InputSet()
        {
            // Setup: create a slot
            createSlot.With("9am", "Create 9am slot"),
        
            // The concurrent operations: Alice and Bob both try to book
            bookSlot.With(("9am", "Alice"), "Alice books 9am"),
            bookSlot.With(("9am", "Bob"), "Bob books 9am"),
        
            // Verify final state
            getSlot.With("9am", "Check who got the slot"),
        };

        var testCases = spec.GenerateConcurrentTests(
            initialState,
            inputs,
            new TestGenerationOptions
            {
                MaxDepth = 4,
                StateConstraint = state =>
                {
                    var s = (BookingState)state;
                    return s.Slots.Count <= 1;
                }
            });

        var context = spec.CreateTestingContext();
        context.Register(new BookingApiClient(factory.CreateTestClient()));

        var results = await spec.RunTests(
            context,
            initialState,
            testCases,
            new TestExecutionOptions
            {
                BeforeEachAsync = async info =>
                {
                    var client = info.Context.Get<BookingApiClient>();
                    await client.DeleteSlotAsync("9am");
                }
            });

        var failures = results.Where(r => !r.Success).ToList();
        Assert.IsEmpty(failures, $"{failures.Count} failed. First: {failures.FirstOrDefault()?.LastFailureMessage}");
    }

    // ============================================================
    // Test 3: More Complex Concurrent Scenarios
    // ============================================================

    /// <summary>
    /// More complex concurrency: multiple slots, multiple customers.
    /// </summary>
    [Test]
    public async Task ConcurrentTests_MultipleSlots()
    {
        using var factory = new BookingServiceFactory();
        var spec = CreateSpec();
        var initialState = new BookingState();

        var createSlot = spec.GetOperation<string, ApiResult<Slot>>("CreateSlot");
        var bookSlot = spec.GetOperation<(string, string), ApiResult<Slot>>("BookSlot");
        var cancelBooking = spec.GetOperation<string, ApiResult<Slot>>("CancelBooking");

        var inputs = new InputSet()
        {
            createSlot.With("9am", "Create 9am"),
            createSlot.With("10am", "Create 10am"),
            bookSlot.With(("9am", "Alice"), "Alice books 9am"),
            bookSlot.With(("9am", "Bob"), "Bob books 9am"),
            bookSlot.With(("10am", "Alice"), "Alice books 10am"),
            bookSlot.With(("10am", "Bob"), "Bob books 10am"),
            cancelBooking.With("9am", "Cancel 9am"),
        };

        var testCases = spec.GenerateConcurrentTests(
            initialState,
            inputs,
            new TestGenerationOptions
            {
                MaxDepth = 4,
                StateConstraint = state =>
                {
                    var s = (BookingState)state;
                    return s.Slots.Count <= 2;
                }
            });

        var context = spec.CreateTestingContext();
        context.Register(new BookingApiClient(factory.CreateTestClient()));

        var results = await spec.RunTests(
            context,
            initialState,
            testCases,
            new TestExecutionOptions
            {
                BeforeEachAsync = async info =>
                {
                    var client = info.Context.Get<BookingApiClient>();
                    await client.DeleteSlotAsync("9am");
                    await client.DeleteSlotAsync("10am");
                }
            });

        var failures = results.Where(r => !r.Success).ToList();
        Assert.IsEmpty(failures, $"{failures.Count} failed. First: {failures.FirstOrDefault()?.LastFailureMessage}");
    }

    // ============================================================
    // Test 4: Manual Test - Explicit Flow
    // ============================================================

    [Test]
    public async Task ManualTest_BookingFlow()
    {
        using var factory = new BookingServiceFactory();
        var spec = CreateSpec();
        var initialState = new BookingState();

        var createSlot = spec.GetOperation<string, ApiResult<Slot>>("CreateSlot");
        var getSlot = spec.GetOperation<string, ApiResult<Slot>>("GetSlot");
        var bookSlot = spec.GetOperation<(string, string), ApiResult<Slot>>("BookSlot");
        var cancelBooking = spec.GetOperation<string, ApiResult<Slot>>("CancelBooking");

        var context = spec.CreateTestingContext();
        context.Register(new BookingApiClient(factory.CreateTestClient()));
        var stateProfile = new StateProfile(initialState);

        async Task<TResp> Allows<TReq, TResp>(
            Operation<TReq, TResp, BookingState> op, TReq request)
        {
            var response = await op.ExecuteAsync(context, request);
            var (isValid, message, nextProfile) = spec.Allows(op, request, response, stateProfile);
            Assert.IsTrue(isValid, message);
            stateProfile = nextProfile;
            return response;
        }

        // 1. Create a slot
        var slot = await Allows(createSlot, "9am");
        Assert.IsTrue(slot.Data!.IsAvailable);

        // 2. Book it
        var booked = await Allows(bookSlot, ("9am", "Alice"));
        Assert.AreEqual("Alice", booked.Data!.BookedBy);

        // 3. Try to book again - should fail
        var conflict = await Allows(bookSlot, ("9am", "Bob"));
        Assert.IsTrue(conflict.IsConflict);

        // 4. Cancel the booking
        var cancelled = await Allows(cancelBooking, "9am");
        Assert.IsTrue(cancelled.Data!.IsAvailable);

        // 5. Now Bob can book
        var bobBooked = await Allows(bookSlot, ("9am", "Bob"));
        Assert.AreEqual("Bob", bobBooked.Data!.BookedBy);
    }

    // ============================================================
    // Test 5: Demonstrate the Bug (Toggle Lock Off)
    // ============================================================

    /// <summary>
    /// This test demonstrates how to reproduce the double-booking bug.
    /// Set SlotsController.DisableBookingLock = true to introduce the race condition.
    /// 
    /// To see the bug in action:
    /// 1. Change the [Ignore] attribute to [Test]
    /// 2. Run the test - it will fail showing double-booking was detected
    /// </summary>
    [Ignore("Enable this test to demonstrate the double-booking bug")]
    [Test]
    public async Task ConcurrentTests_DemonstrateBug_DoubleBookingAllowed()
    {
        // Enable the buggy behavior
        SlotsController.DisableBookingLock = true;
        SlotsController.BuggyDelayMs = 10; // Add delay to make race more likely

        try
        {
            using var factory = new BookingServiceFactory();
            var spec = CreateSpec();
            var initialState = new BookingState();

            var createSlot = spec.GetOperation<string, ApiResult<Slot>>("CreateSlot");
            var bookSlot = spec.GetOperation<(string, string), ApiResult<Slot>>("BookSlot");

            var inputs = new InputSet()
            {
                createSlot.With("9am", "Create 9am slot"),
                bookSlot.With(("9am", "Alice"), "Alice books 9am"),
                bookSlot.With(("9am", "Bob"), "Bob books 9am"),
            };

            var testCases = spec.GenerateConcurrentTests(
                initialState,
                inputs,
                new TestGenerationOptions
                {
                    MaxDepth = 4,
                    StateConstraint = state => ((BookingState)state).Slots.Count <= 1
                });

            var context = spec.CreateTestingContext();
            context.Register(new BookingApiClient(factory.CreateTestClient()));

            var results = await spec.RunTests(
                context,
                initialState,
                testCases,
                new TestExecutionOptions
                {
                    BeforeEachAsync = async info =>
                    {
                        var client = info.Context.Get<BookingApiClient>();
                        await client.DeleteSlotAsync("9am");
                    }
                });

            // This assertion will FAIL when the bug is enabled - that's the point!
            var failures = results.Where(r => !r.Success).ToList();
            Assert.IsEmpty(failures,
                $"BUG DETECTED! {failures.Count} test(s) failed due to double-booking. " +
                $"First failure: {failures.FirstOrDefault()?.LastFailureMessage}");
        }
        finally
        {
            // Always restore correct behavior
            SlotsController.DisableBookingLock = false;
        }
    }
}
