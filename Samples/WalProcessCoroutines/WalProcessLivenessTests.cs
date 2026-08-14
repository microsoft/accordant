// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace WalProcessCoroutines.Tests;

using Microsoft.Accordant.ModelChecking;
using NUnit.Framework;

/// <summary>
/// The temporal refinement ladder: which concrete fairness assumptions make the
/// process WAL refine the atomic store's liveness. The crash loop keeps the
/// obligation from holding for free; strong fairness on the recovery/report step
/// is what closes it.
///
/// <para>Because recovery completes and publishes the owed reply in one atomic
/// step, that single transition is at once the server returning to
/// <see cref="ServerMode.Running"/> and the slot being cleared, so strong
/// fairness on either characterization closes the crash loop. The implementation
/// bundle asks for both to mirror the hand-written WAL's separate recover and
/// report obligations.</para>
/// </summary>
[TestFixture]
public class WalProcessLivenessTests
{
    private static readonly WalConfig Config = WalConfig.Default;

    private static RefinementCheckingResult CheckTemporal(Fairness concrete)
        => StoreRefinement.Build(Config).CheckTemporal(
            concreteFairness: concrete,
            abstractFairness: WalFairness.StoreLiveness);

    [Test]
    public void NoFairnessLeavesTheCrashLoopAndFailsTemporalRefinement()
    {
        var result = CheckTemporal(Fairness.None);
        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.DoesNotRefine));
        Assert.That(
            result.FailureKind,
            Is.EqualTo(RefinementFailureKind.TemporalFairnessMismatch),
            result.GetTraceString());
    }

    [Test]
    public void WeakRecoveryAndWeakReportingStillFailBecauseTheCrashInterruptsBoth()
    {
        // A crash resets the server before either recovery or a report is
        // continuously enabled, so weak fairness never fires.
        var result = CheckTemporal(WalFairness.WeakBoth);
        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.DoesNotRefine));
    }

    [Test]
    public void StrongRecoveryAndReportingRefinesTheStoreLiveness()
    {
        var result = CheckTemporal(WalFairness.Implementation);
        Assert.That(
            result.Status,
            Is.EqualTo(RefinementCheckingStatus.Refines),
            result.GetTraceString());
    }

    [Test]
    public void StrongRecoveryAloneAlreadyClosesTheCrashLoop()
    {
        // The atomic recovery step is a Recovering -> Running transition, so
        // strong fairness on recovery alone forces it out of the crash loop.
        var result = CheckTemporal(WalFairness.Recovers);
        Assert.That(
            result.Status,
            Is.EqualTo(RefinementCheckingStatus.Refines),
            result.GetTraceString());
    }

    [Test]
    public void StrongReportingAloneAlreadyClosesTheCrashLoop()
    {
        // The same atomic step also clears the slot, so strong fairness on the
        // report alone is likewise enough.
        var result = CheckTemporal(WalFairness.Reports);
        Assert.That(
            result.Status,
            Is.EqualTo(RefinementCheckingStatus.Refines),
            result.GetTraceString());
    }
}
