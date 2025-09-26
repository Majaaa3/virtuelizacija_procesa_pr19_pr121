using Common;
using System;
using System.Globalization;
using System.IO;

namespace Service
{
    internal sealed class SessionWriter : IDisposable
    {
        private readonly string _folder;
        private readonly string _sessionCsv;
        private readonly string _rejectCsv;
        private readonly StreamWriter _sessionSw;
        private readonly StreamWriter _rejectSw;
        private bool _disposed;
        private int _lastRowIndex = -1;

        public SessionWriter(string root, EisMeta meta)
        {
            _folder = Path.Combine(root, meta.BatteryId, meta.TestId, $"{meta.SoCPercent}%");
            Directory.CreateDirectory(_folder);

            _sessionCsv = Path.Combine(_folder, "session.csv");
            _rejectCsv = Path.Combine(_folder, "rejects.csv");

            _sessionSw = new StreamWriter(new FileStream(_sessionCsv, File.Exists(_sessionCsv) ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read))
            { AutoFlush = true };

            if (new FileInfo(_sessionCsv).Length == 0)
                _sessionSw.WriteLine("RowIndex,FrequencyHz,R_ohm,X_ohm,T_degC,Range_ohm,TimestampLocal");

            _rejectSw = new StreamWriter(new FileStream(_rejectCsv, File.Exists(_rejectCsv) ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read))
            { AutoFlush = true };
            if (new FileInfo(_rejectCsv).Length == 0)
                _rejectSw.WriteLine("UtcTime,Reason,RowIndex,FrequencyHz,R_ohm,X_ohm,T_degC,Range_ohm");
        }

        public bool IsNextRowIndex(int rowIndex) => rowIndex == _lastRowIndex + 1;

        public bool AppendSample(EisSample s, out string rejectReason)
        {
            if (double.IsNaN(s.Range_ohm) || s.Range_ohm < 0)
            {
                rejectReason = "Range_ohm nije validan; uzorak upisan u rejects.csv.";
                WriteReject("Invalid Range_ohm", s);
                return false;
            }

            _sessionSw.WriteLine(string.Join(",",
                s.RowIndex,
                s.FrequencyHz.ToString("G", CultureInfo.InvariantCulture),
                s.R_ohm.ToString("G", CultureInfo.InvariantCulture),
                s.X_ohm.ToString("G", CultureInfo.InvariantCulture),
                s.T_degC.ToString("G", CultureInfo.InvariantCulture),
                s.Range_ohm.ToString("G", CultureInfo.InvariantCulture),
                s.TimestampLocal.ToString("o")));

            _lastRowIndex = s.RowIndex;
            rejectReason = null;
            return true;
        }

        private void WriteReject(string reason, EisSample s)
        {
            _rejectSw.WriteLine(string.Join(",",
                DateTime.UtcNow.ToString("o"),
                reason,
                s.RowIndex,
                s.FrequencyHz,
                s.R_ohm,
                s.X_ohm,
                s.T_degC,
                s.Range_ohm));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _sessionSw?.Dispose();
            _rejectSw?.Dispose();
        }
    }
}
