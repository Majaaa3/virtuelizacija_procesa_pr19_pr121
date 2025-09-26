using System;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Linq;
using System.ServiceModel;
using System.Text.RegularExpressions;
using Common;

namespace Client
{
    internal class Program
    {
        static void Main()
        {
            var datasetRoot = ConfigurationManager.AppSettings["DatasetRoot"] ?? ".";
            var logPath = ConfigurationManager.AppSettings["LogPath"] ?? ".\\client-import.log";

            var log = new StreamWriter(logPath, append: true);
            log.WriteLine($"[{DateTime.UtcNow:o}] Start import from: {Path.GetFullPath(datasetRoot)}");

            var factory = new ChannelFactory<IEisIngestService>("EisIngest");
            var proxy = factory.CreateChannel();

            foreach (var csv in Directory.EnumerateFiles(datasetRoot, "*.csv", SearchOption.AllDirectories))
            {
                try
                {
                    
                    var batteryId = ExtractBatteryId(csv);
                    var testId = ExtractTestId(csv);
                    var soc = ExtractSocPercent(Path.GetFileNameWithoutExtension(csv)); 

                    var lines = File.ReadAllLines(csv);
                    var dataLines = lines.Where(l => !string.IsNullOrWhiteSpace(l) && !IsHeader(l)).ToList();

                    var meta = new EisMeta
                    {
                        BatteryId = batteryId,
                        TestId = testId,
                        SoCPercent = soc,
                        FileName = Path.GetFileName(csv),
                        TotalRows = Math.Min(28, dataLines.Count)
                    };

                    var ack = proxy.StartSession(meta);
                    if (!ack.Ok) { log.WriteLine($"StartSession NACK: {csv} - {ack.Message}"); continue; }
                    var sessionId = ack.SessionId;

                    int sent = 0;
                    for (int i = 0; i < dataLines.Count; i++)
                    {
                        if (sent >= 28)
                        {
                            log.WriteLine($"[WARN] Višak reda #{i} u {csv} — ignorišem (zahtev: 28).");
                            continue;
                        }

                        if (!TryParseSample(dataLines[i], i, out var sample, out var reason))
                        {
                            log.WriteLine($"[BAD] {csv} line {i}: {reason}");
                            continue;
                        }

                        var r = proxy.PushSample(sessionId, sample);
                        if (!r.Ok) log.WriteLine($"[REJECT] {csv} line {i}: {r.Message}");
                        else sent++;
                    }

                    var end = proxy.EndSession(sessionId);
                    log.WriteLine($"DONE {csv} - sent={sent}, status={end.Status}");
                }
                catch (FaultException<ValidationFault> vf) { log.WriteLine($"[FAULT-VALID] {csv}: {vf.Detail.Reason}"); }
                catch (FaultException<DataFormatFault> df) { log.WriteLine($"[FAULT-DATA] {csv}: {df.Detail.Reason}"); }
                catch (Exception ex) { log.WriteLine($"[ERROR] {csv}: {ex.Message}"); }
            }

            log.WriteLine($"[{DateTime.UtcNow:o}] Import finished.");
        }

        static bool IsHeader(string line) =>
            line.StartsWith("RowIndex", StringComparison.OrdinalIgnoreCase) ||
            line.IndexOf("Frequency", StringComparison.OrdinalIgnoreCase) >= 0;

        static bool TryParseSample(string line, int rowIndex, out EisSample s, out string reason)
        {
            s = null; reason = null;

            var parts = line.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 6) { reason = "Premalo kolona"; return false; }

            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) { reason = "Bad Frequency"; return false; }
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var r)) { reason = "Bad R"; return false; }
            if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)) { reason = "Bad X"; return false; }
            if (!double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var t)) { reason = "Bad T"; return false; }
            if (!double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var range)) { reason = "Bad Range"; return false; }

            DateTime ts;
            if (!DateTime.TryParse(parts[5], CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out ts))
                ts = DateTime.Now;

            s = new EisSample
            {
                RowIndex = rowIndex,
                FrequencyHz = f,
                R_ohm = r,
                X_ohm = x,
                T_degC = t,
                Range_ohm = range,
                TimestampLocal = ts
            };
            return true;
        }

        static string ExtractBatteryId(string path)
        {
            var m = Regex.Match(path, @"\\(B\d{2})\\", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : "Bxx";
        }
        static string ExtractTestId(string path)
        {
            var m = Regex.Match(path, @"\\(Test_\d)\\", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : "Test_1";
        }
        static int ExtractSocPercent(string fileNameNoExt)
        {
            var m1 = Regex.Match(fileNameNoExt, @"(\d{1,3})\s*%");
            if (m1.Success && int.TryParse(m1.Groups[1].Value, out var p)) return p;
            var m2 = Regex.Match(fileNameNoExt, @"SoC\s*(\d{1,3})", RegexOptions.IgnoreCase);
            if (m2.Success && int.TryParse(m2.Groups[1].Value, out p)) return p;
            return 0;
        }
    }
}
