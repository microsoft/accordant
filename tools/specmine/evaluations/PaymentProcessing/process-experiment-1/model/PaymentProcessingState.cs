using System.Globalization;
using System.Text;
using Microsoft.Accordant;

namespace PaymentProcessing.ProcessExperiment1.Model;

internal sealed class PaymentProcessingState : State
{
    private readonly SortedDictionary<string, PaymentSnapshot> paymentsByKey;

    public PaymentProcessingState()
        : this(new SortedDictionary<string, PaymentSnapshot>(StringComparer.Ordinal))
    {
    }

    private PaymentProcessingState(SortedDictionary<string, PaymentSnapshot> paymentsByKey)
    {
        this.paymentsByKey = paymentsByKey;
    }

    public bool TryGetPaymentByKey(string idempotencyKey, out PaymentSnapshot payment) =>
        paymentsByKey.TryGetValue(idempotencyKey, out payment!);

    public bool TryGetPaymentById(string id, out PaymentSnapshot payment)
    {
        foreach (var candidate in paymentsByKey.Values)
        {
            if (string.Equals(candidate.Id, id, StringComparison.Ordinal))
            {
                payment = candidate;
                return true;
            }
        }

        payment = default!;
        return false;
    }

    public bool ContainsPaymentId(string id) =>
        paymentsByKey.Values.Any(payment => string.Equals(payment.Id, id, StringComparison.Ordinal));

    public void AddOrUpdate(PaymentSnapshot payment)
    {
        if (IsFrozen)
        {
            throw new StateFrozenException("PaymentProcessingState is frozen and cannot be mutated.");
        }

        paymentsByKey[payment.IdempotencyKey] = payment;
    }

    protected override void CloneInternal(Dictionary<object, object> clonedMap)
    {
        clonedMap[this] = new PaymentProcessingState(
            new SortedDictionary<string, PaymentSnapshot>(paymentsByKey, StringComparer.Ordinal));
    }

    protected override void FreezeComponents(HashSet<object> visited)
    {
    }

    protected override string StringRepresentationInternal(
        Dictionary<object, string> objectPaths,
        string path,
        bool forceRecompute)
    {
        if (paymentsByKey.Count == 0)
        {
            return "payments=[]";
        }

        var builder = new StringBuilder("payments=[");
        var first = true;

        foreach (var (idempotencyKey, payment) in paymentsByKey)
        {
            if (!first)
            {
                builder.Append(';');
            }

            first = false;
            builder.Append(idempotencyKey.Replace("|", "||", StringComparison.Ordinal));
            builder.Append('|');
            builder.Append(payment.Id.Replace("|", "||", StringComparison.Ordinal));
            builder.Append('|');
            builder.Append(payment.Amount.ToString(CultureInfo.InvariantCulture));
            builder.Append('|');
            builder.Append(payment.Currency.Replace("|", "||", StringComparison.Ordinal));
            builder.Append('|');
            builder.Append(payment.Status.Replace("|", "||", StringComparison.Ordinal));
        }

        builder.Append(']');
        return builder.ToString();
    }
}
