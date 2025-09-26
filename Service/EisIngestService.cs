using Common;
using System;
using System.Collections.Concurrent;
using System.ServiceModel;

namespace Service
{
    public class EisIngestService : IEisIngestService
    {
        private sealed class SessionState
        {
            public int LastRowIndex = -1;
            public readonly EisMeta Meta;
            public SessionState(EisMeta meta) { Meta = meta; }
        }

        private static readonly ConcurrentDictionary<string, SessionState> _sessions =
            new ConcurrentDictionary<string, SessionState>();

        public Ack StartSession(EisMeta meta)
        {
            var reason = ValidateMeta(meta);
            if (reason != null)
                throw new FaultException<ValidationFault>(new ValidationFault { Reason = reason });

            var sessionId = Guid.NewGuid().ToString("N");
            if (!_sessions.TryAdd(sessionId, new SessionState(meta)))
                throw new FaultException<DataFormatFault>(new DataFormatFault { Reason = "Neuspešno otvaranje sesije." });

            return new Ack
            {
                Ok = true,
                Message = "Sesija otvorena.",
                SessionId = sessionId,
                Status = TransferStatus.IN_PROGRESS
            };
        }

        public Ack PushSample(string sessionId, EisSample sample)
        {
            SessionState state;
            if (!_sessions.TryGetValue(sessionId, out state))
                throw new FaultException<ValidationFault>(new ValidationFault { Reason = "Nepoznata sesija." });

            // --- Tačka 3: VALIDACIJE ---
            if (sample == null) ThrowValidation("Sample je null.");
            if (!IsFinite(sample.FrequencyHz) || sample.FrequencyHz <= 0)
                ThrowValidation("FrequencyHz mora biti realan broj > 0.");
            if (!IsFinite(sample.R_ohm)) ThrowValidation("R_ohm nije realan broj.");
            if (!IsFinite(sample.X_ohm)) ThrowValidation("X_ohm nije realan broj.");
            if (!IsFinite(sample.T_degC)) ThrowValidation("T_degC nije realan broj.");
            if (!IsFinite(sample.Range_ohm) || sample.Range_ohm < 0)
                ThrowValidation("Range_ohm mora biti >= 0 i realan broj.");
            if (sample.RowIndex < 0)
                ThrowValidation("RowIndex mora biti >= 0.");
            if (sample.RowIndex != state.LastRowIndex + 1)
                ThrowValidation($"RowIndex mora monoton da raste (očekivano {state.LastRowIndex + 1}, dobio {sample.RowIndex}).");

            state.LastRowIndex = sample.RowIndex;

            return new Ack
            {
                Ok = true,
                Message = "OK",
                Status = TransferStatus.IN_PROGRESS,
                SessionId = sessionId
            };
        }

        public Ack EndSession(string sessionId)
        {
            SessionState _;
            var closed = _sessions.TryRemove(sessionId, out _);
            return new Ack
            {
                Ok = closed,
                Message = closed ? "Završeno." : "Nepoznata sesija.",
                Status = closed ? TransferStatus.COMPLETED : TransferStatus.IN_PROGRESS,
                SessionId = sessionId
            };
        }

        private static string ValidateMeta(EisMeta meta)
        {
            if (meta == null) return "Meta je null.";
            if (string.IsNullOrWhiteSpace(meta.BatteryId)) return "BatteryId je obavezan.";
            if (string.IsNullOrWhiteSpace(meta.TestId)) return "TestId je obavezan.";
            if (meta.SoCPercent < 0 || meta.SoCPercent > 100) return "SoCPercent mora biti [0..100].";
            return null;
        }

        private static bool IsFinite(double d) { return !(double.IsNaN(d) || double.IsInfinity(d)); }

        private static void ThrowValidation(string msg)
        {
            throw new FaultException<ValidationFault>(new ValidationFault { Reason = msg });
        }
    }
}
