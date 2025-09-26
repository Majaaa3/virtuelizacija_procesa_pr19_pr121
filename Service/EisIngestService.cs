using Common;
using System;
using System.Collections.Concurrent;
using System.Configuration;
using System.ServiceModel;

namespace Service
{
    public class EisIngestService : IEisIngestService
    {
        private static readonly ConcurrentDictionary<string, SessionWriter> _sessions =
            new ConcurrentDictionary<string, SessionWriter>();

        public Ack StartSession(EisMeta meta)
        {
            if (meta == null) ThrowValidation("Meta je null.");
            if (string.IsNullOrWhiteSpace(meta.BatteryId)) ThrowValidation("BatteryId je obavezan.");
            if (string.IsNullOrWhiteSpace(meta.TestId)) ThrowValidation("TestId je obavezan.");
            if (meta.SoCPercent < 0 || meta.SoCPercent > 100) ThrowValidation("SoCPercent mora biti [0..100].");

            var root = ConfigurationManager.AppSettings["DataRoot"];
            if (string.IsNullOrWhiteSpace(root)) root = "Data";

            var sessionId = Guid.NewGuid().ToString("N");
            var writer = new SessionWriter(root, meta);

            if (!_sessions.TryAdd(sessionId, writer))
                throw new FaultException<DataFormatFault>(new DataFormatFault { Reason = "Neuspešno otvaranje sesije." });

            Console.WriteLine("[StartSession] -> " + writer.Folder);

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
            SessionWriter writer;
            if (!_sessions.TryGetValue(sessionId, out writer))
                ThrowValidation("Nepoznata sesija.");

            // Validacije (tačka 3)
            if (sample == null) ThrowValidation("Sample je null.");
            if (!IsFinite(sample.FrequencyHz) || sample.FrequencyHz <= 0) ThrowValidation("FrequencyHz mora biti > 0 i realan broj.");
            if (!IsFinite(sample.R_ohm)) ThrowValidation("R_ohm nije realan broj.");
            if (!IsFinite(sample.X_ohm)) ThrowValidation("X_ohm nije realan broj.");
            if (!IsFinite(sample.T_degC)) ThrowValidation("T_degC nije realan broj.");
            if (!IsFinite(sample.Range_ohm) || sample.Range_ohm < 0) ThrowValidation("Range_ohm mora biti >= 0 i realan broj.");
            if (sample.RowIndex < 0) ThrowValidation("RowIndex mora biti >= 0.");
            if (!writer.IsNextRowIndex(sample.RowIndex)) ThrowValidation("RowIndex mora monoton da raste.");

            string rejectReason;
            var ok = writer.AppendSample(sample, out rejectReason);
            if (!ok)
            {
                return new Ack
                {
                    Ok = false,
                    Message = rejectReason,
                    Status = TransferStatus.IN_PROGRESS,
                    SessionId = sessionId
                };
            }

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
            SessionWriter writer;
            if (_sessions.TryRemove(sessionId, out writer))
            {
                writer.Dispose();
                return new Ack { Ok = true, Message = "Završeno.", Status = TransferStatus.COMPLETED, SessionId = sessionId };
            }
            return new Ack { Ok = false, Message = "Nepoznata sesija.", Status = TransferStatus.IN_PROGRESS, SessionId = sessionId };
        }

        private static void ThrowValidation(string msg)
        {
            throw new FaultException<ValidationFault>(new ValidationFault { Reason = msg });
        }
        private static bool IsFinite(double d) { return !(double.IsNaN(d) || double.IsInfinity(d)); }
    }
}
