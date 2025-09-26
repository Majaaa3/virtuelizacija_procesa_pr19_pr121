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
        private readonly int _plannedRows; // npr. 28
        private bool _disposed;
        private int _lastRowIndex = -1;

        public string Folder { get { return _folder; } }

        public SessionWriter(string root, EisMeta meta)
        {
            _plannedRows = meta.TotalRows;

            _folder = Path.Combine(root, meta.BatteryId, meta.TestId, meta.SoCPercent + "%");
            Directory.CreateDirectory(_folder);

            _sessionCsv = Path.Combine(_folder, "session.csv");
            _rejectCsv = Path.Combine(_folder, "rejects.csv");

            // Svaki StartSession kreće od nule – obriši stare fajlove
            TryDelete(_sessionCsv);
            TryDelete(_rejectCsv);

            _sessionSw = new StreamWriter(new FileStream(_sessionCsv, FileMode.Create, FileAccess.Write, FileShare.Read))
            { AutoFlush = true };
            _sessionSw.WriteLine("RowIndex,FrequencyHz,R_ohm,X_ohm,T_degC,Range_ohm,TimestampLocal");

            _rejectSw = new StreamWriter(new FileStream(_rejectCsv, FileMode.Create, FileAccess.Write, FileShare.Read))
            { AutoFlush = true };
            _rejectSw.WriteLine("UtcTime,Reason,RowIndex,FrequencyHz,R_ohm,X_ohm,T_degC,Range_ohm");
        }

        public bool IsNextRowIndex(int rowIndex) { return rowIndex == _lastRowIndex + 1; }

        // true = snimljen u session.csv; false = upisan u rejects.csv
        public bool AppendSample(EisSample s, out string rejectReason)
        {
            // Višak preko plana -> REJECT (ali napreduj index da sledeći red prođe monotoni check)
            if (s.RowIndex >= _plannedRows)
            {
                rejectReason = $"Over planned rows ({_plannedRows}).";
                WriteReject(rejectReason, s);
                _lastRowIndex = s.RowIndex; // važno: napreduj i kad rejectujemo višak
                return false;
            }

            // Primer poslovnog pravila
            if (double.IsNaN(s.Range_ohm) || s.Range_ohm < 0)
            {
                rejectReason = "Invalid Range_ohm";
                WriteReject(rejectReason, s);
                // ovde NE napredujemo _lastRowIndex, da klijent može da pošalje ispravku sa istim RowIndex
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
            if (_sessionSw != null) _sessionSw.Dispose();
            if (_rejectSw != null) _rejectSw.Dispose();
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { /* ignore */ }
        }
    }
}
