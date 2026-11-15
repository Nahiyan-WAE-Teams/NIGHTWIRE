using System;

namespace Nightwire.Runtime.Installation
{
    /// <summary>
    /// Host-provided durable storage for the highest accepted signed catalogue sequence.
    /// Production implementations should protect this value with platform-backed secure storage.
    /// </summary>
    public interface IReleaseCatalogueSequenceStore
    {
        long LoadHighestAcceptedSequence();
        void SaveHighestAcceptedSequence(long sequence);
    }

    public sealed class ReleaseCatalogueAcceptanceService
    {
        private readonly ReleaseCatalogueVerifier _verifier;
        private readonly IReleaseCatalogueSequenceStore _sequenceStore;
        private static readonly object AcceptanceGate = new object();

        public ReleaseCatalogueAcceptanceService(ReleaseCatalogueVerifier verifier,
            IReleaseCatalogueSequenceStore sequenceStore)
        {
            _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
            _sequenceStore = sequenceStore ?? throw new ArgumentNullException(nameof(sequenceStore));
        }

        /// <summary>
        /// Verifies first, then advances the persisted sequence. A process-wide gate serializes
        /// all acceptance-service instances in this process; the host must coordinate other processes.
        /// </summary>
        public ReleaseCatalogueResult Accept(byte[] envelopeBytes, DateTime utcNow)
        {
            lock (AcceptanceGate)
            {
                try
                {
                    long highest = _sequenceStore.LoadHighestAcceptedSequence();
                    if (highest < 0)
                        return Failure("sequence_store_invalid", "Stored release catalogue sequence cannot be negative.");

                    var result = _verifier.Verify(envelopeBytes, utcNow, highest);
                    if (!result.Success || result.Catalogue == null)
                        return result;

                    if (result.Catalogue.Sequence > highest)
                    {
                        _sequenceStore.SaveHighestAcceptedSequence(result.Catalogue.Sequence);
                        long persisted = _sequenceStore.LoadHighestAcceptedSequence();
                        if (persisted != result.Catalogue.Sequence)
                            return Failure("sequence_store_not_durable",
                                "Persisted release catalogue sequence does not exactly match the accepted sequence.");
                    }
                    return result;
                }
                catch (Exception exception)
                {
                    return Failure("sequence_store_failed", exception.Message);
                }
            }
        }

        private static ReleaseCatalogueResult Failure(string code, string message) =>
            new ReleaseCatalogueResult { Success = false, ErrorCode = code, Error = message };
    }
}
