// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace WalProcessCoroutines.Tests;

using Microsoft.Accordant.ModelChecking;
using NUnit.Framework;

/// <summary>
/// The temporal refinement ladder: which concrete fairness assumptions make the
/// process WAL refine the atomic store's liveness. Strong recovery and strong
/// reporting are both required, exactly as in the hand-written WAL sample,
/// because a crash can repeatedly disable either one.
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
    public void WeakRecoveryStillFailsBecauseCrashInterruptsRecovery()
    {
        var result = CheckTemporal(WalFairness.WithWeakRecovery);
        Assert.That(result.Status, Is.EqualTo(RefinementCheckingStatus.DoesNotRefine));
    }

    [Test]
    public void WeakReportingStillFailsBecauseTheOutcomeIsLostToTheNextCrash()
    {
        var result = CheckTemporal(WalFairness.WithWeakReports);
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
}
